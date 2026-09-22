// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Authentication / confirmation transaction model.
/// The central entity of the Transaction Engine. Contains all lifecycle fields,
/// snapshot data and metadata for optimistic concurrency.
/// </summary>
public sealed class Transaction
{
    /// <summary>
    /// Unique transaction identifier. Base62, 43 characters.
    /// Generated at creation. Immutable.
    /// </summary>
    public required TransactionId Id { get; init; }

    /// <summary>
    /// Transaction type (login, confirmation). Immutable after creation.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Current state of the state machine.
    /// </summary>
    public TransactionState State { get; internal set; }

    /// <summary>
    /// Reason code for terminal states (Failed, Expired).
    /// Null for non-terminal states.
    /// </summary>
    public string? StateReasonCode { get; internal set; }

    /// <summary>
    /// Transaction creation time (UTC). Immutable.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Time of the last state update (UTC).
    /// Updated on every transition.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>
    /// TTL expiration time (UTC). Computed as CreatedAt + TTL. Immutable.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Client request identifier for end-to-end tracing.
    /// Provided by the client at creation.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Idempotency key. Provided by the client at creation.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Idempotency scope — a stable identifier of the calling client.
    /// Determined by the Engine from the authentication context.
    /// </summary>
    public string? IdempotencyScope { get; init; }

    /// <summary>
    /// Channel type explicitly requested by the client. Null — all allowed ones are available.
    /// </summary>
    public string? RequestedChannelType { get; init; }

    /// <summary>
    /// Allowed channel types for this transaction.
    /// Default — all registered channels.
    /// Stored as a truly immutable collection (FrozenSet).
    /// </summary>
    public required IReadOnlySet<string> AllowedChannelTypes { get; init; }

    /// <summary>
    /// Arbitrary client data associated with the transaction (raw JSON string).
    /// Not interpreted by the Transaction Engine. Maximum size: 4 KB.
    /// Stored as a string for clear ownership (no IDisposable dependency on JsonDocument).
    /// </summary>
    public string? ClientContext { get; init; }

    /// <summary>
    /// Action data for confirmation (confirmation type).
    /// Populated at creation.
    /// </summary>
    public ConfirmationSnapshot? ConfirmationSnapshot { get; init; }

    /// <summary>
    /// User data received from the channel on confirmation.
    /// Populated on transition to Confirmed.
    /// </summary>
    public ChannelIdentitySnapshot? ChannelIdentitySnapshot { get; internal set; }

    /// <summary>
    /// Final resolved identity. Populated on transition to Completed.
    /// </summary>
    public ResolvedIdentitySnapshot? ResolvedIdentitySnapshot { get; internal set; }

    /// <summary>
    /// Final transaction completion data. Populated at Completed.
    /// </summary>
    public CompletionSnapshot? CompletionSnapshot { get; internal set; }

    /// <summary>
    /// Optimistic concurrency token. Updated on every change.
    /// </summary>
    public string ConcurrencyToken { get; internal set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// OIDC request context. Populated for transactions initiated by the OIDC flow.
    /// </summary>
    public OidcContext? OidcContext { get; init; }

    /// <summary>
    /// Request context of a transaction created outside the OIDC flow (SPEC-039 C19).
    /// Populated once at creation; null — the transaction states no such context.
    /// Read it through <c>TransactionContextExtensions</c>, never directly.
    /// </summary>
    public TransactionRequestContext? RequestContext { get; init; }

    /// <summary>
    /// Container of the check between the confirming party and the relying party's expectations
    /// (SPEC-039 C23). Populated at creation; null — no expectations were stated.
    /// </summary>
    /// <remarks>
    /// The setter is internal for the same reason the lifecycle fields above carry one: the container
    /// changes exactly twice after creation, and both moves are the engine's own — the verdict is
    /// written where the resolved identity is (L41), and the expectations are erased by the transition
    /// into a terminal state, whichever one it is.
    /// </remarks>
    public IdentityMatchState? IdentityMatch { get; internal set; }

    /// <summary>
    /// Transaction initiator context (SPEC-017, field #19 of SPEC-001 §3.1).
    /// Populated once at creation when Initiator Context Check is enabled;
    /// null — the context was not collected. Immutable after creation (ICC-030).
    /// </summary>
    public InitiatorContextSnapshot? InitiatorContextSnapshot { get; init; }

    /// <summary>
    /// Restores a transaction from its stored state — the published path for a store implementation,
    /// including one written outside the Veriqa assemblies.
    /// </summary>
    /// <remarks>
    /// Restoring is not a transition: the state, the timestamps and the concurrency token are taken
    /// from the snapshot exactly as they were stored, and no new token is generated. Moving a
    /// transaction remains the sole business of <see cref="TransactionStateMachine"/>.
    /// <see cref="AllowedChannelTypes"/> is frozen here, so the caller may pass any read-only set and
    /// a later mutation of it cannot leak into a transaction that is immutable by contract.
    /// <para>
    /// The members the snapshot declares non-nullable are checked, not assumed. <c>required</c> makes
    /// the compiler demand that a member be <i>assigned</i>, and nullable reference types are a
    /// compile-time analysis only — neither survives a deserializer. A store rehydrating a document
    /// whose field is missing or explicitly null therefore reaches this method with a null in a
    /// non-nullable member, and the guards turn that into a named
    /// <see cref="ArgumentNullException"/> at the boundary instead of an opaque failure later.
    /// </para>
    /// </remarks>
    /// <param name="snapshot">Complete stored state of the transaction.</param>
    /// <returns>Transaction carrying exactly the state the snapshot states.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="snapshot"/> is null, or one of its non-nullable members
    /// (<see cref="TransactionSnapshot.Type"/>, <see cref="TransactionSnapshot.AllowedChannelTypes"/>,
    /// <see cref="TransactionSnapshot.ConcurrencyToken"/>) carries null.
    /// </exception>
    public static Transaction Restore(TransactionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(
            snapshot.Type, $"{nameof(snapshot)}.{nameof(snapshot.Type)}");
        ArgumentNullException.ThrowIfNull(
            snapshot.AllowedChannelTypes,
            $"{nameof(snapshot)}.{nameof(snapshot.AllowedChannelTypes)}");
        ArgumentNullException.ThrowIfNull(
            snapshot.ConcurrencyToken,
            $"{nameof(snapshot)}.{nameof(snapshot.ConcurrencyToken)}");

        return new Transaction
        {
            Id = snapshot.Id,
            Type = snapshot.Type,
            State = snapshot.State,
            StateReasonCode = snapshot.StateReasonCode,
            CreatedAt = snapshot.CreatedAt,
            UpdatedAt = snapshot.UpdatedAt,
            ExpiresAt = snapshot.ExpiresAt,
            CorrelationId = snapshot.CorrelationId,
            IdempotencyKey = snapshot.IdempotencyKey,
            IdempotencyScope = snapshot.IdempotencyScope,
            RequestedChannelType = snapshot.RequestedChannelType,
            AllowedChannelTypes = snapshot.AllowedChannelTypes.ToFrozenSet(StringComparer.Ordinal),
            ClientContext = snapshot.ClientContext,
            ConfirmationSnapshot = snapshot.ConfirmationSnapshot,
            ChannelIdentitySnapshot = snapshot.ChannelIdentitySnapshot,
            ResolvedIdentitySnapshot = snapshot.ResolvedIdentitySnapshot,
            CompletionSnapshot = snapshot.CompletionSnapshot,
            ConcurrencyToken = snapshot.ConcurrencyToken,
            OidcContext = snapshot.OidcContext,
            RequestContext = snapshot.RequestContext,
            IdentityMatch = snapshot.IdentityMatch,
            InitiatorContextSnapshot = snapshot.InitiatorContextSnapshot
        };
    }

    /// <summary>
    /// Takes the complete state of the transaction as one object — the symmetric half of
    /// <see cref="Restore"/>, so a store writes a transaction out without reflecting over properties.
    /// </summary>
    /// <returns>Snapshot carrying every field of the transaction.</returns>
    public TransactionSnapshot ToSnapshot() => new()
    {
        Id = Id,
        Type = Type,
        State = State,
        StateReasonCode = StateReasonCode,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        ExpiresAt = ExpiresAt,
        CorrelationId = CorrelationId,
        IdempotencyKey = IdempotencyKey,
        IdempotencyScope = IdempotencyScope,
        RequestedChannelType = RequestedChannelType,
        AllowedChannelTypes = AllowedChannelTypes,
        ClientContext = ClientContext,
        ConfirmationSnapshot = ConfirmationSnapshot,
        ChannelIdentitySnapshot = ChannelIdentitySnapshot,
        ResolvedIdentitySnapshot = ResolvedIdentitySnapshot,
        CompletionSnapshot = CompletionSnapshot,
        ConcurrencyToken = ConcurrencyToken,
        OidcContext = OidcContext,
        RequestContext = RequestContext,
        IdentityMatch = IdentityMatch,
        InitiatorContextSnapshot = InitiatorContextSnapshot
    };

    /// <summary>
    /// Checks whether the transaction is in a terminal state.
    /// </summary>
    /// <returns>true if the state is terminal (Completed, Expired, Failed).</returns>
    public bool IsTerminal() => State is TransactionState.Completed
        or TransactionState.Expired
        or TransactionState.Failed;

    /// <summary>
    /// Checks whether the core is waiting for the user's answer on its own confirmation page.
    /// </summary>
    /// <remarks>
    /// The single expression of that state, used by every reader (the status endpoint, the authorize
    /// callback). The channel snapshot is written before the state machine moves only by
    /// <c>ITransactionService.AttachChannelIdentityAsync</c>; every other path populates it together
    /// with the transition to Confirmed. So "still Pending, yet the channel data is already here"
    /// unambiguously means the question has been handed over to the core page.
    /// </remarks>
    /// <returns>true when the transaction waits for a web answer.</returns>
    public bool IsAwaitingWebConfirmation() =>
        State is TransactionState.Pending && ChannelIdentitySnapshot is not null;

    /// <summary>
    /// Checks whether the transaction TTL has expired.
    /// </summary>
    /// <param name="now">Current point in time.</param>
    /// <returns>true if the TTL has expired.</returns>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>
    /// Checks whether running out of time is still what ends this transaction.
    /// </summary>
    /// <remarks>
    /// True in Created and Pending only. A transaction the user has already confirmed is past the
    /// part its TTL guards — the answer is in, and only finalization is left, a wait the TTL does
    /// not govern (SPEC-001 §4.4 item 4: expiry never happens out of Confirmed).
    /// So its deadline passing states nothing about it, and a reader must not read it as an ending.
    /// What does end that wait is the finalization timeout, and it is the STORE that enforces it —
    /// see the remarks on <c>ITransactionStore.GetStalledConfirmedAsync</c> for the store that does
    /// not, where a confirmed transaction is bounded by nothing.
    /// <para>
    /// This is NOT the complement of <see cref="IsTerminal"/>, and the difference is the whole point:
    /// Confirmed is neither terminal nor expirable. Pair the answer with <see cref="IsExpired"/> —
    /// this one says whether the deadline decides anything, that one whether it has passed.
    /// </para>
    /// </remarks>
    /// <returns>true when the TTL of this transaction may still end it.</returns>
    public bool IsSubjectToExpiry() => TransactionStateMachine.ExpirableStates.Contains(State);

    /// <summary>
    /// Checks whether running out of time is what ENDED this transaction — in either of the two
    /// forms that one fact takes on the two sides of the cleanup pass.
    /// </summary>
    /// <remarks>
    /// Before the pass the transaction is still subject to expiry and its deadline is simply behind;
    /// after it, the state itself is the record of the expiry. Any other state is not an expiry,
    /// whatever its deadline says: a decision was recorded on it, and that is a different answer.
    /// That includes Confirmed, which is not terminal yet is past the part the TTL guards
    /// (SPEC-001 §4.4 item 4: expiry never happens out of Confirmed), and Failed, whose deadline
    /// passing afterwards changes nothing about how it ended.
    /// <para>
    /// This is the question asked by everything that ANSWERS about a closed window — the channel
    /// pipeline before it writes anything, and the confirmation write itself — so it is asked in one
    /// place. Two spellings of it answer one question with two different things: a decision already
    /// recorded is told as a decision by the button that races the write and as an expiry by the
    /// button that arrives after the deadline.
    /// </para>
    /// <para>
    /// It is NOT the question <c>ITransactionService.GetTransactionAsync</c> asks. That one decides
    /// whether to withhold a transaction whose fate the deadline still decides, and hands back a
    /// transaction already IN Expired with the state it is in — pairing
    /// <see cref="IsSubjectToExpiry"/> with <see cref="IsExpired"/> and nothing else.
    /// </para>
    /// </remarks>
    /// <param name="now">Current point in time.</param>
    /// <returns>true when running out of time is how this transaction ended.</returns>
    public bool HasRunOutOfTime(DateTimeOffset now)
        => State is TransactionState.Expired || (IsSubjectToExpiry() && IsExpired(now));
}
