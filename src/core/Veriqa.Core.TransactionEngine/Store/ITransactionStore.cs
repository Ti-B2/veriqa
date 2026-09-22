// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.Store;

/// <summary>
/// Transaction store abstraction.
/// Defines the contract for all storage implementations (in-memory, EF Core, Redis).
/// Only one implementation may be registered at a time.
/// </summary>
public interface ITransactionStore
{
    /// <summary>
    /// Adds a new transaction to the store.
    /// </summary>
    /// <param name="transaction">Transaction to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the completion of the operation.</returns>
    /// <exception cref="DuplicateIdempotencyKeyException">
    /// A transaction with the same (<see cref="Transaction.IdempotencyScope"/>,
    /// <see cref="Transaction.IdempotencyKey"/>) pair is already stored. Throwing is an obligation of
    /// the implementation, not an option: idempotency in the engine rests on this exception, and the
    /// caller answers it by re-reading the stored transaction. An implementation that instead returns
    /// the existing transaction, or silently overwrites it, breaks idempotency without any failure to
    /// observe. The check must be atomic with the insert (a unique index, a conditional write), not a
    /// read followed by a write — otherwise two concurrent requests both pass it.
    /// </exception>
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a transaction by its identifier.
    /// </summary>
    /// <param name="id">Transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transaction, or null if not found.</returns>
    Task<Transaction?> GetByIdAsync(TransactionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a transaction by the IdempotencyScope + IdempotencyKey pair.
    /// </summary>
    /// <remarks>
    /// An implementation is required even though no shipped entry point fills the pair in: the
    /// creation path calls this method whenever a caller supplies one, and the s2s entry of
    /// SPEC-039 is where that becomes the normal case. Returning null unconditionally would turn a
    /// repeated request into a second transaction silently.
    /// </remarks>
    /// <param name="scope">Idempotency scope.</param>
    /// <param name="key">Idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transaction, or null if not found.</returns>
    Task<Transaction?> GetByIdempotencyKeyAsync(string scope, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a transaction with a ConcurrencyToken check.
    /// Returns true on a successful update, false on a conflict.
    /// </summary>
    /// <remarks>
    /// A state the stored transaction cannot reach must not be written. The state machine decides
    /// what it can reach — <see cref="TransactionStateMachine.CanTransition"/> over the state
    /// currently stored and the state the passed transaction carries — and a state equal to the
    /// stored one is not a transition at all, so a write that changes only other fields passes.
    /// Refuse the forbidden write by throwing <see cref="InvalidStateTransitionException"/>, never by
    /// returning <see langword="false"/>: <see langword="false"/> means, and keeps meaning, only a
    /// lost optimistic lock or a transaction that is no longer stored, and a caller answers it by
    /// re-reading and retrying — which for a forbidden transition never terminates.
    /// <para>
    /// The two refusals do not compete: throw only when <paramref name="expectedConcurrencyToken"/>
    /// still matches the stored token, that is, when the write would otherwise have been applied. A
    /// caller working off a stale copy has already lost the optimistic lock, and the transition its
    /// copy appears to make is an artefact of that copy rather than a broken invariant — answer it
    /// with <see langword="false"/>, the lost-lock answer. Retrying terminates in that case: the re-read copy
    /// carries the state actually stored, over which the state machine refuses an impossible move up
    /// front.
    /// </para>
    /// <para>
    /// A store composed through <c>AddVeriqaTransactionEngine</c> is given this check by the engine
    /// and need not repeat it. The obligation is stated here for a store reached by any other path.
    /// </para>
    /// </remarks>
    /// <param name="transaction">Updated transaction.</param>
    /// <param name="expectedConcurrencyToken">Expected ConcurrencyToken.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>true if the update was applied; false on a concurrency conflict.</returns>
    /// <exception cref="InvalidStateTransitionException">
    /// The write would move the transaction along a transition the state machine does not allow.
    /// </exception>
    Task<bool> UpdateAsync(Transaction transaction, string expectedConcurrencyToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a transaction (for background cleanup).
    /// </summary>
    /// <param name="id">Transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the completion of the operation.</returns>
    Task DeleteAsync(TransactionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a batch of transactions the passing of <c>ExpiresAt</c> ends: those whose state is one
    /// <see cref="TransactionStateMachine.ExpirableStates"/> holds, with a deadline at or before
    /// <paramref name="now"/>.
    /// </summary>
    /// <remarks>
    /// "Not terminal" is a wider condition and the wrong one: a confirmed transaction is neither
    /// terminal nor ended by its deadline (SPEC-001 §4.4 item 4 — the answer is in, and only
    /// finalization is left, a wait the TTL does not govern). A store selecting by it hands back
    /// records the caller can do nothing with, and they take the slots of the batch away from the
    /// transactions that do have to be expired. A store that cannot ask a rehydrated transaction
    /// (<c>Transaction.IsSubjectToExpiry</c>) — one selecting over stored state names — asks
    /// <see cref="TransactionStateMachine.ExpirableStates"/> instead of writing the set out a second
    /// time.
    /// </remarks>
    /// <param name="now">Current point in time.</param>
    /// <param name="batchSize">Maximum batch size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of expired transactions.</returns>
    Task<IReadOnlyList<Transaction>> GetExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets terminal transactions ready for cleanup.
    /// </summary>
    /// <param name="olderThan">Transactions updated earlier than this moment.</param>
    /// <param name="batchSize">Maximum batch size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of transactions to delete.</returns>
    Task<IReadOnlyList<Transaction>> GetStaleTerminalAsync(DateTimeOffset olderThan, int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets transactions in the Confirmed state that have exceeded the finalization timeout.
    /// </summary>
    /// <remarks>
    /// This selection is the ONLY thing that bounds a confirmed transaction in time: its TTL no
    /// longer decides anything about it (SPEC-001 §4.4 item 4), so what ends the wait is the sweep
    /// reading this member and writing Failed with <c>finalization_timeout</c> — a selection every
    /// store owes (SPEC-001 §8.3 item 3). The Redis store of this delivery does not implement it
    /// and answers with an empty list: a gap against that norm, not a licensed variant. There
    /// nothing bounds a confirmed transaction — it stays readable, and answerable, until the
    /// store drops the record. Callers stating that finalization "has a timeout of its
    /// own" are therefore stating a fact about the store in use, not a property of the engine.
    /// </remarks>
    /// <param name="confirmedBefore">Transactions confirmed earlier than this moment.</param>
    /// <param name="batchSize">Maximum batch size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of stalled Confirmed transactions.</returns>
    Task<IReadOnlyList<Transaction>> GetStalledConfirmedAsync(DateTimeOffset confirmedBefore, int batchSize, CancellationToken cancellationToken = default);
}
