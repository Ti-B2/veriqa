// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.Configuration;
using Veriqa.Core.Contracts;
using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Configuration;
using Veriqa.Core.TransactionEngine.Constants;
using Veriqa.Core.TransactionEngine.Diagnostics;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Events;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.TransactionEngine.Services;

/// <summary>
/// Transaction management service implementation.
/// Coordinates creation, storage, state transitions and event publishing.
/// </summary>
internal sealed class TransactionService : ITransactionService
{
    /// <summary>
    /// Transaction store.
    /// </summary>
    private readonly ITransactionStore _store;

    /// <summary>
    /// Event publisher.
    /// </summary>
    private readonly ITransactionEventPublisher _eventPublisher;

    /// <summary>
    /// Transaction Engine configuration.
    /// </summary>
    private readonly IOptionsMonitor<TransactionEngineOptions> _options;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<TransactionService> _logger;

    /// <summary>
    /// Configuration resolver: reads protective settings across levels.
    /// </summary>
    private readonly IConfigurationResolver _configResolver;

    /// <summary>
    /// Clock behind every TTL, timestamp and event moment produced by the service.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Lifecycle metrics: the transaction entering the lifecycle and the terminal transitions this
    /// service is the sole writer of.
    /// </summary>
    private readonly TransactionMetrics _metrics;

    /// <summary>
    /// Identity-match axis of the engine: the verdict written where the resolved identity is, and the
    /// expectations term of the idempotent-repeat comparison (SPEC-039 R40).
    /// </summary>
    private readonly IdentityMatchService _identityMatch;

    /// <summary>
    /// Maximum ClientContext size in bytes (4 KB).
    /// </summary>
    private const int MaxClientContextSize = 4096;

    /// <summary>
    /// Maximum IdempotencyKey length.
    /// </summary>
    private const int MaxIdempotencyKeyLength = 128;

    /// <summary>
    /// Maximum snapshot data size in bytes (128 KB).
    /// Applies to ConfirmationSnapshot, ChannelIdentitySnapshot, ResolvedIdentitySnapshot.
    /// </summary>
    private const int MaxSnapshotSize = 128 * 1024;

    /// <summary>
    /// Upper bound of read-check-write attempts of the identity token issuance mark. On a Completed
    /// transaction a lost lock is settled by the next read, so the bound only keeps a store whose
    /// update never applies from turning the method into a spin.
    /// </summary>
    private const int MaxIdentityTokenMarkAttempts = 3;

    /// <summary>
    /// What the caller is told when the transaction store fails. Deliberately free of any provider
    /// detail: the message travels outward with the Result, while the exception stays in the log.
    /// </summary>
    private const string StoreUnavailableMessage = "The transaction store is unavailable.";

    /// <summary>
    /// Creates a transaction service instance.
    /// </summary>
    public TransactionService(
        ITransactionStore store,
        ITransactionEventPublisher eventPublisher,
        IOptionsMonitor<TransactionEngineOptions> options,
        ILogger<TransactionService> logger,
        IConfigurationResolver configResolver,
        TimeProvider timeProvider,
        TransactionMetrics metrics,
        IdentityMatchService identityMatch)
    {
        _store = store;
        _eventPublisher = eventPublisher;
        _options = options;
        _logger = logger;
        _configResolver = configResolver;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _identityMatch = identityMatch;
    }

    /// <inheritdoc />
    public async Task<Result<Transaction>> CreateTransactionAsync(
        CreateTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Root span of the sign-in trace. No null branching below: a host that subscribed to nothing
        // gets null here, and `activity?.` keeps every tag a no-op instead of a special case.
        using var activity = TransactionActivitySource.Source.StartActivity(TransactionTelemetry.CreateActivityName);
        activity?.SetTag(TransactionTelemetry.TransactionTypeTag, request.Type);

        // Every refusal of this method leaves the span through one funnel, so a creation that failed
        // is visible as an errored span with its code instead of a silently successful one.
        Result<Transaction> Fail(
            string code,
            string message,
            TransactionErrorCategory category = TransactionErrorCategory.BusinessRule)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(ChannelTelemetry.ErrorCodeTag, code);

            return Result<Transaction>.Failure(code, message, category);
        }

        // The same funnel for a storage failure: one place decides what the caller is told about a
        // dependency that did not answer, and the details stay in the log rather than in a Result
        // that travels outward.
        Result<Transaction> FailStoreUnavailable(Exception exception)
        {
            _logger.LogError(exception, "Transaction store call failed while creating a transaction");

            return Fail(
                TransactionErrorCodes.StoreUnavailable,
                StoreUnavailableMessage,
                TransactionErrorCategory.Infrastructure);
        }

        // Validate the transaction type
        if (!TransactionTypes.IsValid(request.Type))
        {
            return Fail(
                TransactionErrorCodes.InvalidTransactionType,
                $"Unknown transaction type: '{request.Type}'");
        }

        // A preferred channel is a choice INSIDE the allowed set (SPEC-001 §10.4), and a request that
        // names one outside a stated set contradicts itself. Accepting it would create a transaction
        // whose only offered way in leads to a channel whose answer this very service refuses later
        // (channel_not_allowed on every write of channel data, §10.1) — a dead end silent until the
        // TTL. It is refused here instead, at the acceptance, with the code that already names this
        // fact. An empty (or absent) allowed set states no restriction and restricts nothing.
        if (!string.IsNullOrWhiteSpace(request.RequestedChannelType)
            && request.AllowedChannelTypes is { Count: > 0 }
            && !request.AllowedChannelTypes.Contains(request.RequestedChannelType, StringComparer.Ordinal))
        {
            return Fail(
                TransactionErrorCodes.ChannelNotAllowed,
                $"Channel '{request.RequestedChannelType}' is not among the allowed channel types of this transaction");
        }

        // Validate the IdempotencyKey
        if (request.IdempotencyKey is not null && request.IdempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            return Fail(
                TransactionErrorCodes.InvalidIdempotencyKey,
                $"IdempotencyKey length exceeds the maximum ({MaxIdempotencyKeyLength} characters)");
        }

        // Validate that IdempotencyKey and IdempotencyScope are paired: both set or both absent
        var hasKey = request.IdempotencyKey is not null;
        var hasScope = request.IdempotencyScope is not null;
        if (hasKey != hasScope)
        {
            return Fail(
                TransactionErrorCodes.IdempotencyParamsMismatch,
                "IdempotencyKey and IdempotencyScope must be set as a pair (both or neither)");
        }

        // Validate the ClientContext (maximum 4 KB in UTF-8)
        string? clientContextJson = null;
        if (request.ClientContext is not null)
        {
            clientContextJson = request.ClientContext;
            var byteCount = Encoding.UTF8.GetByteCount(clientContextJson);
            if (byteCount > MaxClientContextSize)
            {
                return Fail(
                    TransactionErrorCodes.ClientContextTooLarge,
                    $"ClientContext size exceeds the limit ({MaxClientContextSize} bytes)");
            }
        }

        // Freeze ConfirmationSnapshot.SlotValues to guarantee immutability —
        // before the size validation, so the same instance is measured and stored
        var confirmationSnapshot = request.ConfirmationSnapshot;
        if (confirmationSnapshot?.SlotValues is not null)
        {
            confirmationSnapshot = new ConfirmationSnapshot
            {
                ActionType = confirmationSnapshot.ActionType,
                SlotValues = confirmationSnapshot.SlotValues.ToFrozenDictionary(StringComparer.Ordinal)
            };
        }

        // Freeze IdentityMatchState.Expectations to guarantee immutability — the store shares the
        // container by reference (InMemoryTransactionStore.CloneTransaction) and relies on its
        // collections being already immutable, exactly as for ConfirmationSnapshot.SlotValues
        var identityMatch = request.IdentityMatch;
        if (identityMatch?.Expectations is not null)
        {
            identityMatch = new IdentityMatchState
            {
                Expectations = identityMatch.Expectations.ToFrozenDictionary(StringComparer.Ordinal),
                MatchedType = identityMatch.MatchedType,
                IdentityTokenIssuedAt = identityMatch.IdentityTokenIssuedAt
            };
        }

        // Validate the ConfirmationSnapshot size (maximum 128 KB).
        // The serialization is memoized per instance and reused by the store on
        // persist (TransactionEntityMapper.SerializeImmutableSnapshot) — exactly
        // the JSON that will be written is measured, without re-serialization.
        if (confirmationSnapshot is not null)
        {
            var snapshotJson = TransactionEntityMapper.SerializeImmutableSnapshot(confirmationSnapshot);
            if (Encoding.UTF8.GetByteCount(snapshotJson) > MaxSnapshotSize)
            {
                return Fail(
                    TransactionErrorCodes.SnapshotTooLarge,
                    $"ConfirmationSnapshot size exceeds the limit ({MaxSnapshotSize} bytes)");
            }
        }

        // Validate the InitiatorContextSnapshot size (shared 128 KB snapshot limit, ICC-031) —
        // with the same memoized JSON that will go to the store
        if (request.InitiatorContext is not null)
        {
            var initiatorContextJson = TransactionEntityMapper.SerializeImmutableSnapshot(request.InitiatorContext);
            if (Encoding.UTF8.GetByteCount(initiatorContextJson) > MaxSnapshotSize)
            {
                return Fail(
                    TransactionErrorCodes.SnapshotTooLarge,
                    $"InitiatorContextSnapshot size exceeds the limit ({MaxSnapshotSize} bytes)");
            }
        }

        // Check idempotency
        if (request.IdempotencyKey is not null && request.IdempotencyScope is not null)
        {
            var lookup = await CallStoreAsync(() => _store.GetByIdempotencyKeyAsync(
                request.IdempotencyScope, request.IdempotencyKey, cancellationToken));

            if (lookup.IsFailure)
            {
                return Fail(lookup.Error.Code, lookup.Error.Message, lookup.Error.Category);
            }

            var existing = lookup.Value;

            if (existing is not null)
            {
                // Compare the immutable creation parameters. If the key is reused with different
                // parameters — conflict (SPEC §6, §9.1).
                if (!CreationParametersMatch(existing, request))
                {
                    _logger.LogWarning(
                        "IdempotencyKey conflict — creation parameters do not match. TransactionId: {TransactionId}, ExistingType: {ExistingType}, RequestType: {RequestType}",
                        existing.Id.ToString(),
                        existing.Type,
                        request.Type);

                    return Fail(
                        TransactionErrorCodes.IdempotencyKeyConflict,
                        "IdempotencyKey has already been used with different creation parameters");
                }

                _logger.LogInformation(
                    "Existing transaction found by IdempotencyKey. TransactionId: {TransactionId}",
                    existing.Id.ToString());

                return Result<Transaction>.Success(existing);
            }
        }

        // Compute the TTL
        var engineOptions = _options.CurrentValue;
        var ttlSeconds = request.TtlSeconds ?? engineOptions.TransactionTtlSeconds;
        ttlSeconds = Math.Clamp(
            ttlSeconds,
            TransactionEngineOptions.MinTransactionTtlSeconds,
            TransactionEngineOptions.MaxTransactionTtlSeconds);

        var now = _timeProvider.GetUtcNow();

        // Determine the allowed channels (an immutable collection to prevent external mutation)
        var allowedChannels = request.AllowedChannelTypes is { Count: > 0 }
            ? request.AllowedChannelTypes.ToFrozenSet(StringComparer.Ordinal)
            : FrozenSet<string>.Empty;

        // Create the transaction
        var transaction = new Transaction
        {
            Id = TransactionId.NewId(),
            Type = request.Type,
            State = TransactionState.Created,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddSeconds(ttlSeconds),
            CorrelationId = request.CorrelationId,
            IdempotencyKey = request.IdempotencyKey,
            IdempotencyScope = request.IdempotencyScope,
            RequestedChannelType = request.RequestedChannelType,
            AllowedChannelTypes = allowedChannels,
            ClientContext = clientContextJson,
            ConfirmationSnapshot = confirmationSnapshot,
            OidcContext = request.OidcContext,
            RequestContext = request.RequestContext,
            IdentityMatch = identityMatch,
            InitiatorContextSnapshot = request.InitiatorContext
        };

        // The identifier of the operation the trace follows is the point of a trace, and it is the
        // very value the log records of this method already carry. Metric tags stay free of it.
        activity?.SetTag(TransactionTelemetry.TransactionIdTag, transaction.Id.ToString());

        // Save the transaction.
        // StoreCapacityExceededException is converted into Result.Failure so that
        // the calling code gets a controlled business error instead of a 500.
        // DuplicateIdempotencyKeyException arises on a race condition:
        // two requests with the same key arrived simultaneously — look up by key again
        // and return the transaction successfully saved by the first thread.
        try
        {
            await _store.AddAsync(transaction, cancellationToken);
        }
        catch (StoreCapacityExceededException ex)
        {
            _logger.LogError(
                ex,
                "Transaction store is full. Limit: {MaxEntries}",
                ex.MaxEntries);

            return Fail(
                TransactionErrorCodes.StoreCapacityExceeded,
                ex.Message);
        }
        catch (DuplicateIdempotencyKeyException ex)
        {
            _logger.LogInformation(
                ex,
                "Idempotency race while creating the transaction. Returning the existing one. Scope: {Scope}",
                ex.Scope);

            var lookup = await CallStoreAsync(() => _store.GetByIdempotencyKeyAsync(
                ex.Scope,
                ex.Key,
                cancellationToken));

            if (lookup.IsFailure)
            {
                return Fail(lookup.Error.Code, lookup.Error.Message, lookup.Error.Category);
            }

            var existing = lookup.Value;

            if (existing is not null)
            {
                return Result<Transaction>.Success(existing);
            }

            // Extremely unlikely case: the transaction has already expired while
            // we were waiting. Return a concurrency error.
            return Fail(
                TransactionErrorCodes.ConcurrencyConflict,
                "Idempotency conflict: the concurrent transaction is no longer available");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Everything the engine does NOT define itself: a provider failure of the store
            // (a database, a cache) reaches here as its own exception type, which this assembly
            // neither sees nor is entitled to name. It must not leave a method that declares
            // Result<Transaction> — the caller would get a 500 where the contract promises a
            // verdict. Cancellation is excluded above: it is a shutdown, not a failure.
            return FailStoreUnavailable(ex);
        }

        _logger.LogInformation(
            "Transaction created. TransactionId: {TransactionId}, Type: {TransactionType}, ExpiresAt: {ExpiresAt}",
            transaction.Id.ToString(),
            transaction.Type,
            transaction.ExpiresAt);

        // Publish the creation event
        await _eventPublisher.PublishAsync(
            new TransactionCreatedEvent
            {
                TransactionId = transaction.Id,
                OccurredAt = _timeProvider.GetUtcNow(),
                Type = transaction.Type,
                ExpiresAt = transaction.ExpiresAt,
                Context = TransactionEventContext.FromTransaction(transaction)
            },
            cancellationToken);

        // Activate the transaction (Created → Pending)
        var previousToken = transaction.ConcurrencyToken;
        var activationResult = TransactionStateMachine.TryTransition(transaction, TransactionState.Pending, _timeProvider.GetUtcNow());
        if (activationResult.IsFailure)
        {
            // The state machine already built the error, so the refusal is not rebuilt — only marked
            // on the span, the way the funnel above marks the ones this method builds itself.
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(ChannelTelemetry.ErrorCodeTag, activationResult.Error.Code);

            return activationResult;
        }

        // Update the store after activation, checking the previous token
        var activation = await CallStoreAsync(
            () => _store.UpdateAsync(transaction, previousToken, cancellationToken));
        if (activation.IsFailure)
        {
            return Fail(activation.Error.Code, activation.Error.Message, activation.Error.Category);
        }

        if (!activation.Value)
        {
            return Fail(
                TransactionErrorCodes.ConcurrencyConflict,
                "Concurrency conflict while activating the transaction");
        }

        _logger.LogInformation(
            "Transaction activated. TransactionId: {TransactionId}, State: {TransactionState}",
            transaction.Id.ToString(),
            transaction.State);

        // Counted here and not on entry: the idempotent branches above return an already existing
        // transaction, and counting them would report one sign-in attempt as several.
        _metrics.RecordInitiated(transaction.Type);

        // Publish the activation event
        await _eventPublisher.PublishAsync(
            new TransactionActivatedEvent
            {
                TransactionId = transaction.Id,
                OccurredAt = _timeProvider.GetUtcNow(),
                Context = TransactionEventContext.FromTransaction(transaction)
            },
            cancellationToken);

        return Result<Transaction>.Success(transaction);
    }

    /// <summary>
    /// Decides whether a repeat under an already used idempotency key describes the SAME creation.
    /// </summary>
    /// <remarks>
    /// The terms are the creation parameters that decide what is being confirmed and how it is
    /// presented: the type and the allowed channels, the snapshot (the action type and the caller
    /// values), the request-context container (the language and the <c>ui_config</c> record) and the
    /// expectations about who confirms. A repeat that changed any of them is not a retry of the first
    /// request but a different request wearing its key, and returning the first transaction would hand
    /// the caller an operation about something else (SPEC-039 E30).
    /// <para>
    /// Values are compared as strings, ordinally; a map matches when its keys and values match; and a
    /// value absent on both sides is a match. The two containers are read directly rather than through
    /// the accessor that merges them, and deliberately: what is compared here is what a REQUEST
    /// states, and the OIDC container is not among the terms — the sign-in path states no idempotency
    /// key at all, and pulling its language into the comparison would add a term nobody asked for.
    /// </para>
    /// <para>
    /// The expectations of the relying party about who confirms are a term of this same list, and the
    /// only one that cannot be compared in the form it is stored in: the protection of a value is
    /// randomized, so two protections of one and the same value differ. Both sides are restored
    /// through the protection port and the restored values are compared, which is what keeps an
    /// honest retry from reading as a conflict.
    /// </para>
    /// <para>
    /// That same term is also the only one a transaction STOPS carrying: the transition to a terminal
    /// state drops the expectations and keeps the verdict (SPEC-039 C23, L41). A repeat that finds a
    /// terminal transaction therefore has nothing left to compare against, and holding the emptied
    /// side against a stated one would turn every honest repeat after the outcome into a conflict —
    /// while the repeat is owed the same transaction. The term is dropped exactly where its evidence
    /// is: on a terminal transaction, and only there. Every other term stays comparable, so a repeat
    /// that changed the action, the values, the language, the time zone or the record is still a
    /// conflict.
    /// </para>
    /// </remarks>
    /// <param name="existing">Transaction found by the idempotency key.</param>
    /// <param name="request">Creation request of the repeat.</param>
    /// <returns><see langword="true"/> when the parameters describe the same creation.</returns>
    private bool CreationParametersMatch(Transaction existing, CreateTransactionRequest request)
    {
        if (!string.Equals(existing.Type, request.Type, StringComparison.Ordinal))
        {
            return false;
        }

        var requestedChannels = request.AllowedChannelTypes is { Count: > 0 }
            ? request.AllowedChannelTypes
            : Array.Empty<string>();
        var existingChannels = existing.AllowedChannelTypes ?? FrozenSet<string>.Empty;

        if (!existingChannels.SetEquals(requestedChannels))
        {
            return false;
        }

        if (!SameText(existing.ConfirmationSnapshot?.ActionType, request.ConfirmationSnapshot?.ActionType)
            || !SameEntries(existing.ConfirmationSnapshot?.SlotValues, request.ConfirmationSnapshot?.SlotValues))
        {
            return false;
        }

        if (!SameText(existing.RequestContext?.UiLocale, request.RequestContext?.UiLocale)
            || !SameText(existing.RequestContext?.UiTimeZone, request.RequestContext?.UiTimeZone)
            || !SameText(existing.RequestContext?.UiConfigCode, request.RequestContext?.UiConfigCode))
        {
            return false;
        }

        // A terminal transaction no longer carries the expectations — the transition erased them
        // together with writing the verdict. There is nothing left to hold the repeat against, and
        // an emptied side compared with a stated one would answer a conflict to a repeat that
        // changed nothing. Every other term above has already matched, so the repeat gets the same
        // transaction, as the semantics of a repeat after the outcome require.
        if (existing.IsTerminal())
        {
            return true;
        }

        // The expectations of the relying party are a creation parameter like any other, and they are
        // the one term that cannot be compared as it is stored: the protection is randomized, so two
        // protections of the same value differ. The axis restores both sides and compares those.
        return _identityMatch.ExpectationsMatch(
            existing.IdentityMatch?.Expectations,
            request.IdentityMatch?.Expectations);
    }

    /// <summary>
    /// Ordinal equality of two optional values, where "absent on both sides" is equality.
    /// </summary>
    /// <param name="left">First value.</param>
    /// <param name="right">Second value.</param>
    /// <returns><see langword="true"/> when the two are the same value.</returns>
    private static bool SameText(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    /// <summary>
    /// Equality of two optional maps: same set of keys, same value under each. An absent map and an
    /// empty one are the same thing here — neither states a value.
    /// </summary>
    /// <param name="left">First map.</param>
    /// <param name="right">Second map.</param>
    /// <returns><see langword="true"/> when the two state the same entries.</returns>
    private static bool SameEntries(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        var leftCount = left?.Count ?? 0;
        var rightCount = right?.Count ?? 0;

        if (leftCount != rightCount)
        {
            return false;
        }

        if (leftCount is 0)
        {
            return true;
        }

        foreach (var entry in left!)
        {
            if (!right!.TryGetValue(entry.Key, out var value)
                || !string.Equals(entry.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<Result<Transaction>> GetTransactionAsync(
        TransactionId id,
        CancellationToken cancellationToken = default)
    {
        // Get the transaction from the store
        var transaction = await _store.GetByIdAsync(id, cancellationToken);

        if (transaction is null)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.TransactionNotFound,
                "Transaction not found");
        }

        // A transaction whose deadline has passed must not be handed back as a live one — but only
        // where the deadline is what decides its fate. Expiry ends a transaction out of Created and
        // Pending and out of nothing else (SPEC-001 §4.4 item 4), so a confirmed one awaiting
        // finalization is returned with the state it is actually in: its wait ends on the
        // finalization timeout, and the sweep that enforces it writes Failed, never Expired — how
        // long the wait can run is therefore a fact about the store this deployment runs, not about
        // the engine (ITransactionStore.GetStalledConfirmedAsync names the store that ends it
        // never). Asking "not terminal" instead would answer "expired" about a transaction the
        // state machine cannot expire, and every reader of this method would carry that answer —
        // the status surface among them, which maps any refusal to "no such transaction".
        if (transaction.IsSubjectToExpiry() && transaction.IsExpired(_timeProvider.GetUtcNow()))
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.TransactionExpired,
                "Transaction expired by TTL");
        }

        return Result<Transaction>.Success(transaction);
    }

    /// <inheritdoc />
    public async Task<Result<Transaction>> ConfirmTransactionAsync(
        TransactionId id,
        ChannelIdentitySnapshot channelIdentity,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        // Get the transaction
        var transaction = await _store.GetByIdAsync(id, cancellationToken);
        if (transaction is null)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.TransactionNotFound,
                "Transaction not found");
        }

        // Idempotency: if already Confirmed or Completed — return the current state.
        // Checked before the TTL, so idempotency is not lost for already completed transactions.
        if (transaction.State is TransactionState.Confirmed or TransactionState.Completed)
        {
            _logger.LogInformation(
                "Repeated confirmation — the transaction is already in state {TransactionState}. TransactionId: {TransactionId}",
                transaction.State,
                transaction.Id.ToString());

            return Result<Transaction>.Success(transaction);
        }

        // Check the TTL (only for non-terminal, not yet confirmed transactions) — asked in the ONE
        // reading of a closed window that everything answering about one uses, and not as a bare
        // deadline. A bare deadline says "expired" about a transaction a decision already ended: the
        // sender who declined and then pressed the other button would be told their sign-in ran out
        // of time instead of what they themselves decided, while the same press on the refusal path
        // (which has no deadline gate at all) is answered with that decision. The refusal below is
        // what the caller branches on, so the two buttons have to reach it by the same reading.
        if (transaction.HasRunOutOfTime(_timeProvider.GetUtcNow()))
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.TransactionExpired,
                "Transaction expired by TTL");
        }

        // Check that the sender may act on this transaction at all (allow-list, bot policy)
        var admissibility = await ValidateChannelAdmissibilityAsync(
            transaction, channelIdentity, cancellationToken);
        if (admissibility.IsFailure)
        {
            return Result<Transaction>.Failure(admissibility.Error);
        }

        // Validate the ChannelIdentitySnapshot size (maximum 128 KB).
        // The serialization is memoized per instance and reused by the store on
        // persist (TransactionEntityMapper.SerializeImmutableSnapshot) — exactly
        // the JSON that will be written is measured, without re-serialization.
        // The instance already carries frozen AdditionalClaims: the snapshot freezes them in its
        // init accessor, so an unfrozen state cannot reach the cache no matter who constructed it —
        // the order "frozen → memoized → validated → stored" holds by construction.
        var channelSnapshotJson = TransactionEntityMapper.SerializeImmutableSnapshot(channelIdentity);
        if (Encoding.UTF8.GetByteCount(channelSnapshotJson) > MaxSnapshotSize)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.SnapshotTooLarge,
                $"ChannelIdentitySnapshot size exceeds the limit ({MaxSnapshotSize} bytes)");
        }

        // Check the ConcurrencyToken
        if (!string.Equals(transaction.ConcurrencyToken, concurrencyToken, StringComparison.Ordinal))
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.ConcurrencyConflict,
                "Concurrency conflict — ConcurrencyToken does not match");
        }

        // Save the current token for the update check
        var previousToken = transaction.ConcurrencyToken;

        // Perform the Pending → Confirmed transition
        var transitionResult = TransactionStateMachine.TryTransition(transaction, TransactionState.Confirmed, _timeProvider.GetUtcNow());
        if (transitionResult.IsFailure)
        {
            return transitionResult;
        }

        // Store the channel data — the same instance whose JSON was measured above
        transaction.ChannelIdentitySnapshot = channelIdentity;

        // Update the store
        var updated = await _store.UpdateAsync(transaction, previousToken, cancellationToken);
        if (!updated)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.ConcurrencyConflict,
                "Concurrency conflict while updating the store");
        }

        _logger.LogInformation(
            "Transaction confirmed. TransactionId: {TransactionId}, ChannelType: {ChannelType}",
            transaction.Id.ToString(),
            channelIdentity.ChannelType);

        // Publish the confirmation event
        await _eventPublisher.PublishAsync(
            new TransactionConfirmedEvent
            {
                TransactionId = transaction.Id,
                OccurredAt = _timeProvider.GetUtcNow(),
                ChannelType = channelIdentity.ChannelType,
                Context = TransactionEventContext.FromTransaction(transaction)
            },
            cancellationToken);

        return Result<Transaction>.Success(transaction);
    }

    /// <inheritdoc />
    public async Task<Result<Transaction>> AttachChannelIdentityAsync(
        TransactionId id,
        ChannelIdentitySnapshot identity,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        // Get the transaction
        var transaction = await _store.GetByIdAsync(id, cancellationToken);
        if (transaction is null)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.TransactionNotFound,
                "Transaction not found");
        }

        // The operation is defined only while the transaction is still awaiting an answer. Any other
        // state (already confirmed, declined, or promoted to Expired by the cleanup pass) is a late
        // delivery of the channel event and must not silently rewrite the identity of a decided
        // transaction.
        if (transaction.State is not TransactionState.Pending)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.InvalidStateTransition,
                $"Channel identity can only be attached to a Pending transaction; current state: {transaction.State}");
        }

        // The TTL is a second gate, because the state alone does not close it: a transaction that ran
        // out of time stays Pending until the cleanup pass promotes it, and on every store that window
        // is observable. Late delivery is late on either side of that promotion — the answer this
        // identity would sign can no longer be given — so the deadline is checked here, in the same
        // place in the order the confirmation path checks it. It is read directly, and not through
        // Transaction.HasRunOutOfTime, because the state gate above has already narrowed this call to
        // Pending, and on Pending the two readings reduce to the same deadline check in today's
        // delivery: Pending is expirable by the transition table, so the wider reading adds nothing.
        if (transaction.IsExpired(_timeProvider.GetUtcNow()))
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.TransactionExpired,
                "Transaction expired by TTL");
        }

        // Check that the sender may act on this transaction at all (allow-list, bot policy). The same
        // gate the confirmation applies: the attached identity is what the web answer later signs in,
        // so a sender rejected at confirmation time must not get that far in the first place.
        var admissibility = await ValidateChannelAdmissibilityAsync(
            transaction, identity, cancellationToken);
        if (admissibility.IsFailure)
        {
            return Result<Transaction>.Failure(admissibility.Error);
        }

        // Validate the ChannelIdentitySnapshot size — the same 128 KB limit the confirmation applies,
        // measured on the memoized JSON that the store will persist.
        var channelSnapshotJson = TransactionEntityMapper.SerializeImmutableSnapshot(identity);
        if (Encoding.UTF8.GetByteCount(channelSnapshotJson) > MaxSnapshotSize)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.SnapshotTooLarge,
                $"ChannelIdentitySnapshot size exceeds the limit ({MaxSnapshotSize} bytes)");
        }

        // Check the ConcurrencyToken
        if (!string.Equals(transaction.ConcurrencyToken, concurrencyToken, StringComparison.Ordinal))
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.ConcurrencyConflict,
                "Concurrency conflict — ConcurrencyToken does not match");
        }

        // Save the current token for the update check
        var previousToken = transaction.ConcurrencyToken;

        // No state transition here: the transaction keeps waiting. Only the snapshot and the
        // concurrency metadata move, so the metadata is refreshed the same way a transition would.
        transaction.ChannelIdentitySnapshot = identity;
        transaction.UpdatedAt = _timeProvider.GetUtcNow();
        transaction.ConcurrencyToken = Guid.NewGuid().ToString("N");

        // Update the store
        var updated = await _store.UpdateAsync(transaction, previousToken, cancellationToken);
        if (!updated)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.ConcurrencyConflict,
                "Concurrency conflict while updating the store");
        }

        _logger.LogInformation(
            "Channel identity attached to a pending transaction. TransactionId: {TransactionId}, ChannelType: {ChannelType}",
            transaction.Id.ToString(),
            identity.ChannelType);

        // Publish the attachment event
        await _eventPublisher.PublishAsync(
            new TransactionChannelIdentityAttachedEvent
            {
                TransactionId = transaction.Id,
                OccurredAt = _timeProvider.GetUtcNow(),
                ChannelType = identity.ChannelType,
                Context = TransactionEventContext.FromTransaction(transaction)
            },
            cancellationToken);

        return Result<Transaction>.Success(transaction);
    }

    /// <inheritdoc />
    public async Task<Result<Transaction>> CompleteTransactionAsync(
        TransactionId id,
        ResolvedIdentitySnapshot resolvedIdentity,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        // Get the transaction
        var transaction = await _store.GetByIdAsync(id, cancellationToken);
        if (transaction is null)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.TransactionNotFound,
                "Transaction not found");
        }

        // Idempotency: if already Completed — return it
        if (transaction.State is TransactionState.Completed)
        {
            return Result<Transaction>.Success(transaction);
        }

        // Check the ConcurrencyToken
        if (!string.Equals(transaction.ConcurrencyToken, concurrencyToken, StringComparison.Ordinal))
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.ConcurrencyConflict,
                "Concurrency conflict — ConcurrencyToken does not match");
        }

        // Save the current token for the update check
        var previousToken = transaction.ConcurrencyToken;

        // Invariant: ChannelIdentitySnapshot must be populated on transition to Completed
        // (populated at Confirmed, and Completed is only reachable from Confirmed).
        // Checked BEFORE the state mutation, so the transaction is not left in a corrupted state.
        if (transaction.ChannelIdentitySnapshot is null)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.InvalidStateTransition,
                "ChannelIdentitySnapshot is not populated — cannot complete the transaction without channel data");
        }

        // Freeze the Claims to guarantee immutability — before the size validation,
        // so the same instance is measured and stored
        var frozenResolvedIdentity = resolvedIdentity.WithFrozenClaims();

        // Validate the ResolvedIdentitySnapshot size (maximum 128 KB).
        // Checked BEFORE the state mutation, so the transaction is not left in a corrupted state.
        // The serialization is memoized per instance and reused by the store on
        // persist (TransactionEntityMapper.SerializeImmutableSnapshot) — exactly
        // the JSON that will be written is measured, without re-serialization
        var resolvedSnapshotJson = TransactionEntityMapper.SerializeImmutableSnapshot(frozenResolvedIdentity);
        if (Encoding.UTF8.GetByteCount(resolvedSnapshotJson) > MaxSnapshotSize)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.SnapshotTooLarge,
                $"ResolvedIdentitySnapshot size exceeds the limit ({MaxSnapshotSize} bytes)");
        }

        // The verdict of the identity match (SPEC-039 L41) is computed HERE — at the point the resolved
        // identity is written, and not at the surface that finalized the transaction: there are several
        // such surfaces (the channel, the core page) and the verdict must be one and the same wherever
        // the person pressed "yes". Computed BEFORE the transition, because the transition is what
        // erases the expectations it reads. It refuses nothing: a verdict is not a gate (E43).
        var matchedType = await _identityMatch.ComputeVerdictAsync(
            transaction,
            frozenResolvedIdentity,
            cancellationToken);

        // Perform the Confirmed → Completed transition
        var transitionResult = TransactionStateMachine.TryTransition(transaction, TransactionState.Completed, _timeProvider.GetUtcNow());
        if (transitionResult.IsFailure)
        {
            return transitionResult;
        }

        // The transition above erased the expectations; what survives the transaction is the name of
        // the matched type alone. A transaction that stated no expectations keeps its empty container
        // (null) rather than growing one that says nothing.
        if (transaction.IdentityMatch is not null)
        {
            transaction.IdentityMatch = new IdentityMatchState
            {
                MatchedType = matchedType,
                IdentityTokenIssuedAt = transaction.IdentityMatch.IdentityTokenIssuedAt
            };
        }

        // Store the resolved identity and the completion snapshot
        transaction.ResolvedIdentitySnapshot = frozenResolvedIdentity;
        transaction.CompletionSnapshot = new CompletionSnapshot
        {
            CompletedAt = _timeProvider.GetUtcNow(),
            ChannelType = transaction.ChannelIdentitySnapshot.ChannelType,
            Claims = frozenResolvedIdentity.Claims
        };

        // Update the store
        var updated = await _store.UpdateAsync(transaction, previousToken, cancellationToken);
        if (!updated)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.ConcurrencyConflict,
                "Concurrency conflict while updating the store");
        }

        // The point of the actual state transition — the idempotent branch at the top of the method
        // returns before it, so a repeated delivery of the same channel event counts once.
        RecordTerminated(transaction, TransactionOutcomes.Completed);

        _logger.LogInformation(
            "Transaction completed. TransactionId: {TransactionId}, SubjectHash: {SubjectHash}",
            transaction.Id.ToString(),
            LogMasking.Fingerprint(resolvedIdentity.Subject));

        // Publish the completion event
        await _eventPublisher.PublishAsync(
            new TransactionCompletedEvent
            {
                TransactionId = transaction.Id,
                OccurredAt = _timeProvider.GetUtcNow(),
                Subject = resolvedIdentity.Subject,
                Context = TransactionEventContext.FromTransaction(transaction)
            },
            cancellationToken);

        return Result<Transaction>.Success(transaction);
    }

    /// <inheritdoc />
    public async Task<bool> TryMarkIdentityTokenIssuedAsync(
        TransactionId id,
        CancellationToken cancellationToken = default)
    {
        // The method writes the one-time issuance mark by a compare-and-swap on the concurrency token.
        // A Completed transaction is terminal, so the only writers competing for it are another mark and
        // the retention sweep: a lost lock is settled by the next read, which sees either the winner's
        // mark or no transaction at all. The attempts are bounded all the same, so a store whose update
        // never applies ends in a refusal rather than a spin — and a refusal issues nothing.
        for (var attempt = 0; attempt < MaxIdentityTokenMarkAttempts; attempt++)
        {
            var transaction = await _store.GetByIdAsync(id, cancellationToken);
            if (transaction is null
                || transaction.State is not TransactionState.Completed
                || transaction.IdentityMatch?.IdentityTokenIssuedAt is not null)
            {
                return false;
            }

            var previousToken = transaction.ConcurrencyToken;

            // A transaction that stated no expectations has no container yet, so one is created. The
            // verdict is kept; the expectations are not carried — the completion is what erased them.
            transaction.IdentityMatch = new IdentityMatchState
            {
                MatchedType = transaction.IdentityMatch?.MatchedType,
                IdentityTokenIssuedAt = _timeProvider.GetUtcNow()
            };

            // Only the concurrency token moves with the mark. UpdatedAt stays as the completion wrote it:
            // the retention of a terminal transaction counts from it, and redeeming the transaction must
            // not keep the resolved identity stored any longer than it already would be.
            transaction.ConcurrencyToken = Guid.NewGuid().ToString("N");

            if (await _store.UpdateAsync(transaction, previousToken, cancellationToken))
            {
                _logger.LogDebug(
                    "Identity token issuance marked. TransactionId: {TransactionId}",
                    transaction.Id.ToString());

                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<Result<Transaction>> FailTransactionAsync(
        TransactionId id,
        string reasonCode,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        // Get the transaction
        var transaction = await _store.GetByIdAsync(id, cancellationToken);
        if (transaction is null)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.TransactionNotFound,
                "Transaction not found");
        }

        // If already in a terminal state — error
        if (transaction.IsTerminal())
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.InvalidStateTransition,
                $"Transaction is already in a terminal state: {transaction.State}");
        }

        // Check the ConcurrencyToken
        if (!string.Equals(transaction.ConcurrencyToken, concurrencyToken, StringComparison.Ordinal))
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.ConcurrencyConflict,
                "Concurrency conflict — ConcurrencyToken does not match");
        }

        // Save the current token for the update check
        var previousToken = transaction.ConcurrencyToken;

        // Perform the → Failed transition
        var transitionResult = TransactionStateMachine.TryTransition(
            transaction, TransactionState.Failed, _timeProvider.GetUtcNow(), reasonCode);
        if (transitionResult.IsFailure)
        {
            return transitionResult;
        }

        // Update the store
        var updated = await _store.UpdateAsync(transaction, previousToken, cancellationToken);
        if (!updated)
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.ConcurrencyConflict,
                "Concurrency conflict while updating the store");
        }

        // The user's refusal and a system failure arrive at the same method — it is the single writer
        // of the Failed state — so the outcome is told from the reason code rather than from the call
        // site: giving "declined" a method of its own would mean duplicating the state transition.
        RecordTerminated(
            transaction,
            string.Equals(reasonCode, TransactionErrorCodes.DeclinedByUser, StringComparison.Ordinal)
                ? TransactionOutcomes.Declined
                : TransactionOutcomes.Failed);

        _logger.LogWarning(
            "Transaction failed. TransactionId: {TransactionId}, ReasonCode: {ReasonCode}",
            transaction.Id.ToString(),
            reasonCode);

        // Publish the event
        await _eventPublisher.PublishAsync(
            new TransactionFailedEvent
            {
                TransactionId = transaction.Id,
                OccurredAt = _timeProvider.GetUtcNow(),
                ReasonCode = reasonCode,
                Context = TransactionEventContext.FromTransaction(transaction)
            },
            cancellationToken);

        return Result<Transaction>.Success(transaction);
    }

    /// <inheritdoc />
    public Task<Result<Transaction>> CancelTransactionAsync(
        TransactionId id,
        string concurrencyToken,
        CancellationToken cancellationToken = default)
    {
        // Cancellation is a Fail with the cancelled_by_client code
        return FailTransactionAsync(id, TransactionErrorCodes.CancelledByClient, concurrencyToken, cancellationToken);
    }

    /// <summary>
    /// Records a terminal transition of the transaction: the outcome counter and the lifetime
    /// histogram, measured from <c>CreatedAt</c> to this moment.
    /// </summary>
    /// <remarks>
    /// The lifetime is a difference of two stored moments rather than an elapsed-time reading:
    /// the transaction may well have been created on another replica, so there is no local start
    /// timestamp to subtract from.
    /// The channel tag falls back from the channel that actually answered to the channel that was
    /// asked, and then to the declared "nobody answered" value — which is the honest state of a
    /// transaction that expired before any channel replied.
    /// </remarks>
    /// <param name="transaction">Transaction that has just reached its terminal state.</param>
    /// <param name="outcome">Terminal outcome to report.</param>
    private void RecordTerminated(Transaction transaction, string outcome)
    {
        var channelType = transaction.ChannelIdentitySnapshot?.ChannelType
            ?? transaction.RequestedChannelType
            ?? TransactionTelemetry.UnknownChannelType;

        _metrics.RecordTerminated(
            outcome,
            channelType,
            _timeProvider.GetUtcNow() - transaction.CreatedAt);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every operation that puts channel data onto a transaction applies this gate — the confirmation
    /// and the attachment that keeps the transaction Pending until the answer comes from the core web
    /// page. They are one decision made at two moments, so a single implementation keeps them from
    /// drifting apart: an identity accepted by one of them is signed in through the other. Since it is
    /// stated on the contract, a surface that only discloses an already recorded outcome applies that
    /// same implementation rather than a copy of it.
    /// </remarks>
    public async ValueTask<Result> ValidateChannelAdmissibilityAsync(
        Transaction transaction,
        ChannelIdentitySnapshot channelIdentity,
        CancellationToken cancellationToken = default)
    {
        // Check that the channel is allowed
        if (transaction.AllowedChannelTypes.Count > 0
            && !transaction.AllowedChannelTypes.Contains(channelIdentity.ChannelType))
        {
            return Result.Failure(
                TransactionErrorCodes.ChannelNotAllowed,
                $"Channel '{channelIdentity.ChannelType}' is not allowed for this transaction");
        }

        // Reject requests from bots (security policy).
        // RejectBots is read via the resolver with the transaction's ApplicationId (CFG-222):
        // a protective setting (ProtectiveCeiling). Self-hosted = core value 1:1 (criterion met).
        // The application level is bound over the OIDC client entry (per-client OidcClientOptions.RejectBots):
        // a per-app relaxation takes effect only when the core/tenant gate RejectBotsAllowApplicationOverride is
        // open (CFG-212), otherwise the stricter upper-level value wins. Extending this to per-tenant is a future track.
        // The tenant override is written out as null by hand: this assembly has no ambient tenant
        // scope to read (it lives in the channel contour), so the tenant of the transaction is the
        // only one there is.
        var rejectBotsContext = transaction.ToResolutionContext(tenantId: null);
        var rejectBots = (await _configResolver.ResolveAsync(
            TransactionEngineConfigKeys.RejectBots,
            rejectBotsContext,
            ConfigDimensionValues.None,
            cancellationToken)).Value;

        if (channelIdentity.IsBot && rejectBots)
        {
            _logger.LogWarning(
                "Channel event rejected: the sender is a bot. TransactionId: {TransactionId}, ChannelType: {ChannelType}",
                transaction.Id.ToString(),
                channelIdentity.ChannelType);

            return Result.Failure(
                TransactionErrorCodes.BotRejected,
                "Bot authentication is forbidden by the security policy");
        }

        return Result.Success();
    }

    /// <summary>
    /// Runs one call into the transaction store, keeping a failure of the storage inside the
    /// <see cref="Result{T}"/> boundary of the operation.
    /// </summary>
    /// <remarks>
    /// The engine classifies only the two failures it defines itself
    /// (<see cref="StoreCapacityExceededException"/>, <see cref="DuplicateIdempotencyKeyException"/>),
    /// and those are handled where they arise, as business outcomes. Everything else is a provider
    /// type this assembly neither references nor is entitled to name — a Npgsql or a Redis failure —
    /// so it is reported by category rather than by type. Cancellation is not a storage failure and
    /// is left to propagate as the shutdown it is.
    /// </remarks>
    /// <typeparam name="T">Value the store call produces.</typeparam>
    /// <param name="storeCall">The store call to run.</param>
    /// <returns>The value of the call, or an infrastructure error.</returns>
    private async Task<Result<T>> CallStoreAsync<T>(Func<Task<T>> storeCall)
    {
        try
        {
            return Result<T>.Success(await storeCall());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Transaction store call failed");

            return Result<T>.Failure(
                TransactionErrorCodes.StoreUnavailable,
                StoreUnavailableMessage,
                TransactionErrorCategory.Infrastructure);
        }
    }
}
