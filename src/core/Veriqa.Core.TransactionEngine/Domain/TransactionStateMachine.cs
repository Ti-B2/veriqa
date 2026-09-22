// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Transaction state machine.
/// Defines the allowed transitions and validates their correctness.
/// All transitions update UpdatedAt and ConcurrencyToken.
/// </summary>
public static class TransactionStateMachine
{
    /// <summary>
    /// Table of allowed state transitions.
    /// Key — source state, value — set of allowed target states.
    /// </summary>
    private static readonly FrozenDictionary<TransactionState, FrozenSet<TransactionState>> AllowedTransitions =
        new Dictionary<TransactionState, FrozenSet<TransactionState>>
        {
            // Created → Pending, Failed, Expired
            [TransactionState.Created] = new HashSet<TransactionState>
            {
                TransactionState.Pending,
                TransactionState.Failed,
                TransactionState.Expired
            }.ToFrozenSet(),
            // Pending → Confirmed, Failed, Expired
            [TransactionState.Pending] = new HashSet<TransactionState>
            {
                TransactionState.Confirmed,
                TransactionState.Failed,
                TransactionState.Expired
            }.ToFrozenSet(),
            // Confirmed → Completed, Failed
            [TransactionState.Confirmed] = new HashSet<TransactionState>
            {
                TransactionState.Completed,
                TransactionState.Failed
            }.ToFrozenSet(),
            // Terminal states — no transitions possible
            [TransactionState.Completed] = FrozenSet<TransactionState>.Empty,
            [TransactionState.Expired] = FrozenSet<TransactionState>.Empty,
            [TransactionState.Failed] = FrozenSet<TransactionState>.Empty
        }.ToFrozenDictionary();

    /// <summary>
    /// Backing set of <see cref="ExpirableStates"/>.
    /// </summary>
    private static readonly FrozenSet<TransactionState> ExpirableStatesSet =
        AllowedTransitions
            .Where(transitions => transitions.Value.Contains(TransactionState.Expired))
            .Select(transitions => transitions.Key)
            .ToFrozenSet();

    /// <summary>
    /// The states running out of time may end a transaction from — Created and Pending, and no
    /// others. A confirmed transaction is past the part its TTL guards: the user has answered, and
    /// what is left is finalization — a wait the TTL does not govern.
    /// </summary>
    /// <remarks>
    /// Read off the transition table rather than written out a second time, so that the two cannot
    /// disagree: this set IS the answer to "from where does <see cref="CanTransition"/> permit a move
    /// into <see cref="TransactionState.Expired"/>". Both forms are needed — a store choosing which
    /// records the deadline still decides has no transaction to ask and asks this set, while code
    /// about to move one asks <see cref="CanTransition"/>. A reader answering the question its own
    /// way (most tempting: "not terminal") calls transactions expired that the state machine then
    /// refuses to expire.
    /// </remarks>
    public static IReadOnlySet<TransactionState> ExpirableStates => ExpirableStatesSet;

    /// <summary>
    /// Checks whether a transition from the current state to the target state is allowed.
    /// </summary>
    /// <param name="from">Current state.</param>
    /// <param name="to">Target state.</param>
    /// <returns>true if the transition is allowed.</returns>
    public static bool CanTransition(TransactionState from, TransactionState to)
    {
        // Check that the transition exists in the table
        if (!AllowedTransitions.TryGetValue(from, out var allowed))
        {
            return false;
        }

        return allowed.Contains(to);
    }

    /// <summary>
    /// Performs a transaction state transition.
    /// Validates that the transition is allowed and updates the metadata.
    /// </summary>
    /// <remarks>
    /// Internal on purpose: moving a transaction is the business of the engine alone, and this method
    /// writes the lifecycle fields (<c>State</c>, <c>StateReasonCode</c>, <c>UpdatedAt</c>,
    /// <c>ConcurrencyToken</c>) that carry internal setters for the same reason. Outside code that
    /// needs to know whether a transition is allowed asks <see cref="CanTransition"/>, which decides
    /// nothing and mutates nothing.
    /// </remarks>
    /// <param name="transaction">Transaction to update.</param>
    /// <param name="targetState">Target state.</param>
    /// <param name="now">Moment of the transition (UTC), supplied by the caller's time provider.</param>
    /// <param name="reasonCode">Reason code (for terminal states).</param>
    /// <returns>Result with the updated transaction or an error.</returns>
    internal static Result<Transaction> TryTransition(
        Transaction transaction,
        TransactionState targetState,
        DateTimeOffset now,
        string? reasonCode = null)
    {
        // Validate that the transition is allowed
        if (!CanTransition(transaction.State, targetState))
        {
            return Result<Transaction>.Failure(
                TransactionErrorCodes.InvalidStateTransition,
                $"Transition from '{transaction.State}' to '{targetState}' is not allowed");
        }

        // Perform the transition
        transaction.State = targetState;
        transaction.StateReasonCode = reasonCode;
        transaction.UpdatedAt = now;
        transaction.ConcurrencyToken = Guid.NewGuid().ToString("N");

        EraseExpectationsOnTerminal(transaction);

        return Result<Transaction>.Success(transaction);
    }

    /// <summary>
    /// Drops the relying party's expectations from a transaction that has just become terminal
    /// (SPEC-039 C23, L41), keeping the verdict.
    /// </summary>
    /// <remarks>
    /// The erasure is WIDER than the computation of the verdict and therefore hangs here, on the one
    /// point every transition of the engine and of the cleanup service goes through, rather than on
    /// the completion alone: a declined or an expired transaction has nothing left to compare, and
    /// keeping personal data of the relying party on it until the retention sweep would be a stored
    /// "person ↔ channel" link the product has none of by design. No sweep of its own is introduced
    /// for this — the transition IS the moment.
    /// <para>
    /// Every caller of <see cref="TryTransition"/> writes the transaction to the store right after,
    /// so the erasure travels with the transition it belongs to.
    /// </para>
    /// </remarks>
    /// <param name="transaction">Transaction that has just been moved.</param>
    private static void EraseExpectationsOnTerminal(Transaction transaction)
    {
        if (!transaction.IsTerminal() || transaction.IdentityMatch?.Expectations is null)
        {
            return;
        }

        transaction.IdentityMatch = new IdentityMatchState
        {
            MatchedType = transaction.IdentityMatch.MatchedType,
            IdentityTokenIssuedAt = transaction.IdentityMatch.IdentityTokenIssuedAt
        };
    }
}
