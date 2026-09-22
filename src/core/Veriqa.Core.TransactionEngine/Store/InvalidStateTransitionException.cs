// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.Store;

/// <summary>
/// Thrown when a write would move a transaction along a transition the state machine does not
/// allow. Signals a broken invariant, not a business outcome: no supported path can produce it.
/// </summary>
/// <remarks>
/// Deliberately distinct from the <see langword="false"/> that
/// <see cref="ITransactionStore.UpdateAsync"/> returns on a lost optimistic lock. A caller answers
/// that <see langword="false"/> by re-reading the transaction and retrying the write, which for a
/// forbidden transition would repeat forever: re-reading changes nothing about what the state
/// machine allows.
/// </remarks>
public sealed class InvalidStateTransitionException : Exception
{
    /// <summary>
    /// Creates an exception instance naming the transaction and both ends of the refused transition.
    /// </summary>
    /// <param name="transactionId">Identifier of the transaction that was not written.</param>
    /// <param name="from">State currently held in the store.</param>
    /// <param name="to">State the refused write carried.</param>
    public InvalidStateTransitionException(
        TransactionId transactionId,
        TransactionState from,
        TransactionState to)
        : base(
            $"Transition from '{from}' to '{to}' is not allowed by the transaction state machine. " +
            $"Transaction '{transactionId}' was left as it was stored.")
    {
        TransactionId = transactionId;
        From = from;
        To = to;
    }

    /// <summary>
    /// Identifier of the transaction whose write was refused.
    /// </summary>
    public TransactionId TransactionId { get; }

    /// <summary>
    /// State the store currently holds for that transaction.
    /// </summary>
    public TransactionState From { get; }

    /// <summary>
    /// State the refused write would have stored.
    /// </summary>
    public TransactionState To { get; }
}
