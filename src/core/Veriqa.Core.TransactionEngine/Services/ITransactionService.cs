// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.Services;

/// <summary>
/// Core transaction management service.
/// Defines the contract for creating, retrieving, and managing transaction states.
/// </summary>
public interface ITransactionService
{
    /// <summary>
    /// Creates and activates a new transaction.
    /// Steps 2–3 of the algorithm: ID generation, State = Created → Pending.
    /// </summary>
    /// <param name="request">Transaction creation parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the created transaction or an error.</returns>
    Task<Result<Transaction>> CreateTransactionAsync(CreateTransactionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of a transaction.
    /// </summary>
    /// <param name="id">Transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the transaction or an error.</returns>
    Task<Result<Transaction>> GetTransactionAsync(TransactionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms a transaction via a Channel Adapter (State → Confirmed).
    /// </summary>
    /// <remarks>
    /// <see cref="TransactionErrorCodes.TransactionExpired"/> is refused to a transaction that running
    /// out of time ENDED: one already in Expired, or one still Created/Pending past the deadline that
    /// the cleanup pass has not promoted yet. A transaction some decision already ended is refused
    /// with the code of that state instead, and never with expiry.
    /// </remarks>
    /// <param name="id">Transaction identifier.</param>
    /// <param name="channelIdentity">User data from the channel.</param>
    /// <param name="concurrencyToken">Optimistic concurrency token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the updated transaction or an error.</returns>
    Task<Result<Transaction>> ConfirmTransactionAsync(
        TransactionId id,
        ChannelIdentitySnapshot channelIdentity,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches the channel identity to a transaction that stays Pending: the core has accepted the
    /// user's action in the channel, but the confirming question is asked elsewhere (the core web
    /// page), so the transaction must NOT advance along the state machine yet.
    /// </summary>
    /// <remarks>
    /// The pair "State == Pending + a populated ChannelIdentitySnapshot" is what tells a later reader
    /// (the status endpoint, the authorize callback) that the core is waiting for the answer on its
    /// own page. Only the snapshot and the concurrency metadata change; the state machine is not
    /// touched. A repeated call with a current token overwrites the snapshot (the last delivery of the
    /// same inbound event wins), which keeps provider retries idempotent.
    /// The sender is admitted under the same rules the confirmation applies — the transaction's
    /// channel allow-list and the bot policy — because the attached identity is the one the answer on
    /// the web page signs in: a sender that confirmation would reject must not reach that page.
    /// <para>
    /// A transaction past its TTL is refused with <see cref="TransactionErrorCodes.TransactionExpired"/>
    /// even while it is still Pending: the state is promoted only by the cleanup pass, so "Pending"
    /// alone does not mean the answer can still be given.
    /// </para>
    /// </remarks>
    /// <param name="id">Transaction identifier.</param>
    /// <param name="identity">User data from the channel.</param>
    /// <param name="concurrencyToken">Optimistic concurrency token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the updated transaction or an error.</returns>
    Task<Result<Transaction>> AttachChannelIdentityAsync(
        TransactionId id,
        ChannelIdentitySnapshot identity,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the sender of a channel event may act on this transaction: the channel is in
    /// the transaction's allow-list and the sender is not a bot when bots are rejected.
    /// </summary>
    /// <remarks>
    /// The same gate <see cref="ConfirmTransactionAsync"/> and <see cref="AttachChannelIdentityAsync"/>
    /// apply before they write anything of the channel onto the transaction. It is stated on the
    /// contract so that a surface which does not write — one that only tells the sender what the
    /// transaction ended in — can apply the very same decision instead of carrying a second copy of
    /// the rule. The transaction is taken as a value rather than by identifier: such a caller has
    /// already read it, and a second read here would be a second answer to the same question.
    /// <para>
    /// A refusal carries <see cref="TransactionErrorCodes.ChannelNotAllowed"/> or
    /// <see cref="TransactionErrorCodes.BotRejected"/>. An empty allow-list means "no restriction by
    /// channel", not "no channel allowed".
    /// </para>
    /// </remarks>
    /// <param name="transaction">Transaction the event acts on.</param>
    /// <param name="channelIdentity">Identity of the sender in the channel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the sender is admissible; otherwise the error to answer with.</returns>
    ValueTask<Result> ValidateChannelAdmissibilityAsync(
        Transaction transaction,
        ChannelIdentitySnapshot channelIdentity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes a transaction (State → Completed).
    /// </summary>
    /// <param name="id">Transaction identifier.</param>
    /// <param name="resolvedIdentity">Final resolved identity.</param>
    /// <param name="concurrencyToken">Optimistic concurrency token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the updated transaction or an error.</returns>
    Task<Result<Transaction>> CompleteTransactionAsync(
        TransactionId id,
        ResolvedIdentitySnapshot resolvedIdentity,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly transitions a transaction to Failed.
    /// </summary>
    /// <param name="id">Transaction identifier.</param>
    /// <param name="reasonCode">Failure reason code.</param>
    /// <param name="concurrencyToken">Optimistic concurrency token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the updated transaction or an error.</returns>
    Task<Result<Transaction>> FailTransactionAsync(
        TransactionId id,
        string reasonCode,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a transaction on behalf of the client (→ Failed + cancelled_by_client).
    /// </summary>
    /// <param name="id">Transaction identifier.</param>
    /// <param name="concurrencyToken">Optimistic concurrency token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with the updated transaction or an error.</returns>
    Task<Result<Transaction>> CancelTransactionAsync(
        TransactionId id,
        string concurrencyToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a completed transaction as having handed the identity of the confirming party to its
    /// client (SPEC-039 C51). The mark is written once per transaction.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="true"/> to exactly one call per transaction. <see langword="false"/> —
    /// the transaction is not stored (including one swept by retention), is not Completed, or already
    /// carries the mark; the reasons are deliberately not told apart. The state does not change.
    /// Atomicity rests on the optimistic concurrency of the store: a lost lock is answered inside the
    /// method by re-reading the transaction and checking again. Infrastructure exceptions of the store
    /// propagate.
    /// </remarks>
    /// <param name="id">Transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when this call wrote the mark.</returns>
    Task<bool> TryMarkIdentityTokenIssuedAsync(
        TransactionId id,
        CancellationToken cancellationToken = default);
}
