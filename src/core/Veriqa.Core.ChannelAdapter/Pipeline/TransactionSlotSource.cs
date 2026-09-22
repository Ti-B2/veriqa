// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Events;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// What a render point knows about the transaction a message reports: the attributes the slot values
/// of that message are assembled from — the caller values of the transaction and the server values the
/// core states about it (SPEC-036 TPL-124).
/// <para>
/// Public because it is SPI: it is the carrier <see cref="IOutcomeReceiptText"/> takes, and a host
/// composing a render point of its own states it the same way the core does — from a live transaction,
/// or from the attribution of a transaction event when the transaction is already gone.
/// </para>
/// </summary>
public sealed record TransactionSlotSource
{
    /// <summary>
    /// Type of the transaction (login, confirmation); null when the render point does not know it. It
    /// decides what the application slot is filled with.
    /// </summary>
    public required string? TransactionType { get; init; }

    /// <summary>
    /// Application attribution of the transaction (the calling client's identifier); null when the
    /// transaction states none.
    /// </summary>
    public string? ApplicationId { get; init; }

    /// <summary>
    /// Initiator context of the transaction, exactly as it was collected; null when there is none — a
    /// confirmation collects none (SPEC-039 N32), and the attribution of an event does not carry it.
    /// </summary>
    public InitiatorContextSnapshot? InitiatorContext { get; init; }

    /// <summary>
    /// Caller slot values of the transaction, as the confirmation snapshot stores them; null when the
    /// transaction carries none, or when the render point may not show them (SPEC-039 E41 × SPEC-003
    /// CA-192). They fill only the slots a contract declares as caller slots.
    /// </summary>
    public IReadOnlyDictionary<string, string>? CallerSlotValues { get; init; }

    /// <summary>
    /// The source of a live transaction.
    /// </summary>
    /// <param name="transaction">Transaction the message reports.</param>
    /// <returns>The source.</returns>
    public static TransactionSlotSource From(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        return new TransactionSlotSource
        {
            TransactionType = transaction.Type,
            ApplicationId = transaction.GetApplicationId(),
            InitiatorContext = transaction.InitiatorContextSnapshot,
            CallerSlotValues = transaction.ConfirmationSnapshot?.SlotValues
        };
    }

    /// <summary>
    /// The source of a transaction that is already gone, from the attribution its event carries. The
    /// attribution states no initiator context, so none is there.
    /// </summary>
    /// <param name="context">Attribution of the transaction event.</param>
    /// <returns>The source.</returns>
    public static TransactionSlotSource From(TransactionEventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TransactionSlotSource
        {
            TransactionType = context.TransactionType,
            ApplicationId = context.ClientId,
            InitiatorContext = null,
            CallerSlotValues = context.ConfirmationParameters?.SlotValues
        };
    }

    /// <summary>
    /// The same source without the caller values — for a render point that may state what the core
    /// knows about the transaction but not what the calling party supplied.
    /// </summary>
    /// <returns>The source without caller values.</returns>
    public TransactionSlotSource WithoutCallerValues() => this with { CallerSlotValues = null };
}
