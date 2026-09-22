// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Veriqa.Core.TransactionEngine.Configuration;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.TransactionEngine.Store;

/// <summary>
/// In-memory implementation of the transaction store.
/// Used for development and testing.
/// Data is kept in a ConcurrentDictionary and does not survive a process restart.
/// Stores copies of transactions (CloneTransaction): primitive/value-type fields are copied by value,
/// AllowedChannelTypes — via ToFrozenSet (guaranteed immutable).
/// ClientContext is stored as a JSON string (string, immutable per the .NET contract).
/// Snapshot and context objects (sealed, init-only) are shared by reference; their mutable
/// collections (Claims, SlotValues, Expectations) must contain FrozenDictionary — this is ensured
/// by TransactionService when accepting data from external code.
/// Optimistic concurrency is implemented via an atomic compare-and-swap (TryUpdate).
/// </summary>
internal sealed class InMemoryTransactionStore : ITransactionStore
{
    /// <summary>
    /// Storage: key — string representation of TransactionId, value — (Transaction, ConcurrencyToken).
    /// ConcurrencyToken is stored alongside the transaction copy for atomic compare-and-swap.
    /// </summary>
    private readonly ConcurrentDictionary<string, (Transaction Snapshot, string ConcurrencyToken)> _entries = new();

    /// <summary>
    /// Idempotency index: tuple key (IdempotencyScope, IdempotencyKey) → TransactionId.
    /// The tuple key guarantees the absence of collisions, unlike string concatenation
    /// with a reserved separator (caller-controlled values may contain it).
    /// Provides an atomic uniqueness check in AddAsync via TryAdd,
    /// eliminating the TOCTOU race condition between GetByIdempotencyKeyAsync and AddAsync.
    /// Analogous to the UNIQUE INDEX (IdempotencyScope, IdempotencyKey) in EF Core / the Lua script in Redis.
    /// </summary>
    private readonly ConcurrentDictionary<(string Scope, string Key), string> _idempotencyIndex = new();

    /// <summary>
    /// Atomic counter of the number of entries.
    /// Used to check the limit without a race condition between Count and TryAdd.
    /// </summary>
    private int _count;

    /// <summary>
    /// Transaction Engine configuration monitor.
    /// </summary>
    private readonly IOptionsMonitor<TransactionEngineOptions> _options;

    /// <summary>
    /// Maximum batch size for batch operations (in-memory store).
    /// Matches the upper bound of CleanupBatchSize in TransactionEngineOptionsValidator.
    /// </summary>
    private const int MaxBatchSize = 1000;

    /// <summary>
    /// Creates an instance of the in-memory transaction store.
    /// </summary>
    /// <param name="options">Transaction Engine configuration monitor.</param>
    public InMemoryTransactionStore(IOptionsMonitor<TransactionEngineOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        // Validate the input BEFORE incrementing the counter to avoid polluting _count
        ArgumentNullException.ThrowIfNull(transaction);

        // Atomic uniqueness check of the idempotency key.
        // Eliminates a TOCTOU race condition: without this check, two parallel requests
        // with the same (IdempotencyScope, IdempotencyKey) could both pass
        // GetByIdempotencyKeyAsync → AddAsync, since the linear scan over _entries
        // does not guarantee insertion atomicity.
        // Analogous to the UNIQUE INDEX in EF Core and the idem_exists Lua script in Redis.
        if (transaction.IdempotencyScope is not null && transaction.IdempotencyKey is not null)
        {
            var idempotencyCompositeKey = (transaction.IdempotencyScope, transaction.IdempotencyKey);
            if (!_idempotencyIndex.TryAdd(idempotencyCompositeKey, transaction.Id.ToString()))
            {
                throw new DuplicateIdempotencyKeyException(
                    transaction.IdempotencyScope,
                    transaction.IdempotencyKey);
            }
        }

        // Atomically reserve a slot via Interlocked.Increment —
        // eliminates the race condition between the limit check and TryAdd.
        var maxEntries = _options.CurrentValue.MaxInMemoryEntries;
        var newCount = Interlocked.Increment(ref _count);
        if (newCount > maxEntries)
        {
            Interlocked.Decrement(ref _count);
            // Roll back the entry in the idempotency index
            if (transaction.IdempotencyScope is not null && transaction.IdempotencyKey is not null)
            {
                _idempotencyIndex.TryRemove((transaction.IdempotencyScope, transaction.IdempotencyKey), out _);
            }
            throw new StoreCapacityExceededException(maxEntries);
        }

        // Store a copy of the transaction.
        // Use TryAdd to honor add semantics — do not overwrite an already existing entry.
        var key = transaction.Id.ToString();
        var snapshot = CloneTransaction(transaction);

        if (!_entries.TryAdd(key, (snapshot, snapshot.ConcurrencyToken)))
        {
            Interlocked.Decrement(ref _count);
            // Roll back the entry in the idempotency index
            if (transaction.IdempotencyScope is not null && transaction.IdempotencyKey is not null)
            {
                _idempotencyIndex.TryRemove((transaction.IdempotencyScope, transaction.IdempotencyKey), out _);
            }
            throw new InvalidOperationException(
                $"Transaction with ID '{key}' already exists in the store");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Transaction?> GetByIdAsync(TransactionId id, CancellationToken cancellationToken = default)
    {
        // Return a copy so that external mutation does not affect the store
        var key = id.ToString();
        if (_entries.TryGetValue(key, out var entry))
        {
            return Task.FromResult<Transaction?>(CloneTransaction(entry.Snapshot));
        }

        return Task.FromResult<Transaction?>(null);
    }

    /// <inheritdoc />
    public async Task<Transaction?> GetByIdempotencyKeyAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        // First look up the index — it is reserved atomically in AddAsync BEFORE the insert into _entries.
        // This eliminates the TOCTOU: if a concurrent thread's AddAsync has already reserved the key,
        // we will see the index entry and wait for the transaction to appear in _entries.
        if (_idempotencyIndex.TryGetValue((scope, key), out var transactionId)
            && _entries.TryGetValue(transactionId, out var indexedEntry))
        {
            return CloneTransaction(indexedEntry.Snapshot);
        }

        if (transactionId is not null)
        {
            // The index already contains an entry, but _entries is still empty —
            // a concurrent AddAsync is between the index TryAdd and the _entries TryAdd.
            // Briefly wait for the transaction to appear (bounded spin-wait).
            // At most 50 attempts × 1ms = 50ms — comfortably more than the synchronous
            // code between the two TryAdd calls inside AddAsync needs.
            for (var i = 0; i < 50; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                if (_entries.TryGetValue(transactionId, out var entry))
                {
                    return CloneTransaction(entry.Snapshot);
                }
            }
            // The entry is missing — the concurrent AddAsync failed between the reservation and the insert
            // (e.g., StoreCapacityExceededException). The index rollback has already happened
            // or will happen; return null here — the caller will get ConcurrencyConflict.
        }

        return null;
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(Transaction transaction, string expectedConcurrencyToken, CancellationToken cancellationToken = default)
    {
        // Atomic compare-and-swap via ConcurrentDictionary.TryUpdate
        var key = transaction.Id.ToString();

        if (!_entries.TryGetValue(key, out var existing))
        {
            return Task.FromResult(false);
        }

        // Check optimistic concurrency
        if (!string.Equals(existing.ConcurrencyToken, expectedConcurrencyToken, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        // Create a new entry with a copy of the transaction
        var newSnapshot = CloneTransaction(transaction);
        var newEntry = (newSnapshot, newSnapshot.ConcurrencyToken);

        // Atomic CAS: update only if the current value matches the expected one
        var swapped = _entries.TryUpdate(key, newEntry, existing);

        return Task.FromResult(swapped);
    }

    /// <inheritdoc />
    public Task DeleteAsync(TransactionId id, CancellationToken cancellationToken = default)
    {
        // Remove the transaction from the store and the idempotency index
        var key = id.ToString();
        if (_entries.TryRemove(key, out var removed))
        {
            Interlocked.Decrement(ref _count);

            // Clear the idempotency index for the removed transaction
            if (removed.Snapshot.IdempotencyScope is not null && removed.Snapshot.IdempotencyKey is not null)
            {
                _idempotencyIndex.TryRemove((removed.Snapshot.IdempotencyScope, removed.Snapshot.IdempotencyKey), out _);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Transaction>> GetExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        // Look for the transactions the passing of their deadline ends. Asking the transaction
        // itself rather than "not terminal": a confirmed one is neither terminal nor ended by its
        // deadline, and selecting it here would spend a slot of the batch on a record the caller
        // cannot move.
        var effectiveBatchSize = Math.Min(batchSize, MaxBatchSize);

        var expired = _entries.Values
            .Where(e => e.Snapshot.IsSubjectToExpiry() && e.Snapshot.IsExpired(now))
            .Take(effectiveBatchSize)
            .Select(e => CloneTransaction(e.Snapshot))
            .ToList();

        return Task.FromResult<IReadOnlyList<Transaction>>(expired);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Transaction>> GetStaleTerminalAsync(DateTimeOffset olderThan, int batchSize, CancellationToken cancellationToken = default)
    {
        // Look for terminal transactions updated earlier than olderThan
        var effectiveBatchSize = Math.Min(batchSize, MaxBatchSize);

        var stale = _entries.Values
            .Where(e => e.Snapshot.IsTerminal() && e.Snapshot.UpdatedAt < olderThan)
            .Take(effectiveBatchSize)
            .Select(e => CloneTransaction(e.Snapshot))
            .ToList();

        return Task.FromResult<IReadOnlyList<Transaction>>(stale);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Transaction>> GetStalledConfirmedAsync(DateTimeOffset confirmedBefore, int batchSize, CancellationToken cancellationToken = default)
    {
        // Look for Confirmed transactions confirmed earlier than confirmedBefore
        var effectiveBatchSize = Math.Min(batchSize, MaxBatchSize);

        var stalled = _entries.Values
            .Where(e => e.Snapshot.State is TransactionState.Confirmed && e.Snapshot.UpdatedAt < confirmedBefore)
            .Take(effectiveBatchSize)
            .Select(e => CloneTransaction(e.Snapshot))
            .ToList();

        return Task.FromResult<IReadOnlyList<Transaction>>(stalled);
    }

    /// <summary>
    /// Creates a copy of the transaction to isolate the store from external mutation.
    /// Primitive/value-type fields are copied by value. AllowedChannelTypes — via ToFrozenSet
    /// (inside <see cref="Transaction.Restore"/>). ClientContext is a string (immutable).
    /// Snapshot and context objects (sealed, init-only) are shared by reference; their collections
    /// (Claims, SlotValues, Expectations) are guaranteed immutable (FrozenDictionary, ensured by
    /// TransactionService).
    /// </summary>
    /// <remarks>
    /// The copy goes through the published snapshot path — the same one a store written outside the
    /// Veriqa assemblies uses — instead of a hand-written list of assignments. That list was the one
    /// place in the engine where a field newly added to <see cref="Transaction"/> was lost without a
    /// compilation error and without a failure at run time: this store calls no mapper, so nothing
    /// else covered it. With <c>required</c> members on the snapshot the same omission now stops the
    /// build.
    /// </remarks>
    /// <param name="source">Source transaction.</param>
    /// <returns>Copy of the transaction.</returns>
    private static Transaction CloneTransaction(Transaction source)
        => Transaction.Restore(source.ToSnapshot());
}
