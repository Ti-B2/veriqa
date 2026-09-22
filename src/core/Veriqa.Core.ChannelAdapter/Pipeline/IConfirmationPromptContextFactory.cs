// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Confirmation context factory (SPEC-017 §7.1, ICC-041):
/// builds the ConfirmationPromptContext from the transaction's InitiatorContextSnapshot —
/// in the pipeline (orchestrator), not in the adapter. The adapter is unaware of the transaction structure.
/// </summary>
public interface IConfirmationPromptContextFactory
{
    /// <summary>
    /// Builds the confirmation context from the transaction: filters fields by the DisplayFields
    /// configuration (ICC-015) and applies the anomaly heuristic (SPEC-017 §9).
    /// The result is never null: when there are no initiator details to show, the context says so
    /// explicitly and names the reason — the adapter then uses its default text (ICC-042).
    /// </summary>
    /// <param name="transaction">Transaction (source of the InitiatorContextSnapshot).</param>
    /// <param name="channelType">Confirmation channel type.</param>
    /// <param name="channelUserId">User identifier within the channel.</param>
    /// <param name="recipientLocale">Recipient locale from channel data (null — from the transaction's ChannelIdentitySnapshot or the base language).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Confirmation context, always populated.</returns>
    Task<ConfirmationPromptContext> CreateAsync(
        Transaction transaction,
        string channelType,
        string channelUserId,
        string? recipientLocale,
        CancellationToken cancellationToken = default);
}
