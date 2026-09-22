// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Abstractions;

/// <summary>
/// Store of in-channel confirmation-prompt coordinates keyed by transaction (SPEC-003 §4.5).
/// Lets a background lifecycle handler (TTL expiry) edit the exact prompt message that a
/// channel adapter previously sent — removing the stale buttons and showing "expired".
///
/// Only channels that send an in-channel prompt with buttons (Telegram, Max) write here;
/// channels without such a surface (WhatsApp/Email) do not. A missing entry means "no prompt
/// was sent for this transaction" and the handler is a graceful no-op.
///
/// The default implementation is in-process (single instance / self-hosted / demo). In a
/// multi-instance deployment the expiry event may be handled on an instance other than the one
/// that sent the prompt; then the edit is skipped (graceful). A distributed implementation
/// (e.g. Redis) can be layered on later without changing this contract.
/// </summary>
public interface IChannelPromptMessageStore
{
    /// <summary>
    /// Records the coordinates of a sent prompt for the transaction. Overwrites a prior entry
    /// for the same transaction (a resent prompt supersedes the previous message).
    /// </summary>
    /// <param name="transactionId">Transaction the prompt belongs to.</param>
    /// <param name="reference">Coordinates of the sent prompt message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(TransactionId transactionId, ChannelPromptMessageRef reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically retrieves and removes the stored prompt coordinates for the transaction.
    /// Returns null when nothing was stored (no in-channel prompt was sent, the entry already
    /// consumed, or it was evicted). Remove-on-read prevents a second edit of the same message.
    /// </summary>
    /// <param name="transactionId">Transaction to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored coordinates, or null.</returns>
    Task<ChannelPromptMessageRef?> TakeAsync(TransactionId transactionId, CancellationToken cancellationToken = default);
}
