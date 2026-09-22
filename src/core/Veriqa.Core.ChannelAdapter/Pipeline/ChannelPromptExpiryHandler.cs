// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Events;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Lifecycle handler that, on transaction TTL expiry, shows the user the terminal "expired" status
/// for the confirmation prompt already sent to the channel (SPEC-003 §4.5). It states the same
/// intent as the live confirm/decline path — the adapter picks the platform operations — but is
/// driven by <see cref="TransactionExpiredEvent"/> instead of an inbound callback.
///
/// If no prompt was sent for the transaction (no in-channel surface, or a different instance sent it
/// in a multi-instance in-memory deployment) the handler is a graceful no-op. All failures are logged,
/// never thrown — a background lifecycle handler must not break event processing (a throw here is
/// isolated by the publisher, but we degrade explicitly).
/// </summary>
internal sealed class ChannelPromptExpiryHandler : ITransactionEventHandler
{
    /// <summary>
    /// Store of sent-prompt coordinates keyed by transaction.
    /// </summary>
    private readonly IChannelPromptMessageStore _promptMessageStore;

    /// <summary>
    /// Registered channel adapters (resolved by <see cref="ChannelPromptMessageRef.ChannelType"/>).
    /// </summary>
    private readonly IEnumerable<IChannelAdapter> _adapters;

    /// <summary>
    /// Terminal text of the transaction: the handler states the address of the receipt and the core
    /// answers with the wording, already localized (SPEC-036 TPL-123).
    /// </summary>
    private readonly IOutcomeReceiptText _receiptText;

    /// <summary>
    /// How the deployment wants the receipt to appear in the conversation. The background path states
    /// the very same intent the live one does — an expiry is shown the way every other terminal
    /// outcome of the same deployment is.
    /// </summary>
    private readonly IOutcomeNoticeDisplayIntentSource _displayIntentSource;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<ChannelPromptExpiryHandler> _logger;

    /// <summary>
    /// Component metrics: an expiry notice the channel refused is counted by the outbound seam.
    /// This background path is the very reason the instrument travels as a parameter rather than
    /// being taken from a request's service provider — there is no request here.
    /// </summary>
    private readonly ChannelAdapterMetrics _metrics;

    /// <summary>
    /// Creates the expiry handler.
    /// </summary>
    /// <param name="promptMessageStore">Store of sent-prompt coordinates.</param>
    /// <param name="adapters">Registered channel adapters.</param>
    /// <param name="receiptText">Terminal text of the transaction.</param>
    /// <param name="displayIntentSource">Port the desired display intent of the receipt comes from.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics.</param>
    public ChannelPromptExpiryHandler(
        IChannelPromptMessageStore promptMessageStore,
        IEnumerable<IChannelAdapter> adapters,
        IOutcomeReceiptText receiptText,
        IOutcomeNoticeDisplayIntentSource displayIntentSource,
        ILogger<ChannelPromptExpiryHandler> logger,
        ChannelAdapterMetrics metrics)
    {
        _promptMessageStore = promptMessageStore ?? throw new ArgumentNullException(nameof(promptMessageStore));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _receiptText = receiptText ?? throw new ArgumentNullException(nameof(receiptText));
        _displayIntentSource = displayIntentSource ?? throw new ArgumentNullException(nameof(displayIntentSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <inheritdoc />
    public async Task HandleAsync(TransactionEvent transactionEvent, CancellationToken cancellationToken = default)
    {
        // Only TTL expiry is relevant here: confirm/decline are already handled by the webhook pipeline.
        if (transactionEvent is not TransactionExpiredEvent expired)
        {
            return;
        }

        // Take (get + remove) the sent-prompt record. Null — no in-channel prompt to report on.
        var reference = await _promptMessageStore.TakeAsync(expired.TransactionId, cancellationToken);
        if (reference is null)
        {
            return;
        }

        var adapter = FindAdapter(reference.ChannelType);
        if (adapter is null)
        {
            _logger.LogWarning(
                "No adapter for channel {ChannelType} to report the expired prompt. TransactionId: {TransactionId}",
                reference.ChannelType,
                expired.TransactionId.ToString());
            return;
        }

        // Restore the tenant captured at send time: a background handler has no ambient request
        // context, and the adapter resolves its per-tenant client/credentials from ChannelTenantContext.
        // The scope is for the ADAPTER's credentials only — it is no longer the source of the ownership
        // the receipt is worded over: that comes from the stored record below. Tenant is the level the
        // prompt store contracts for (ChannelPromptMessageRef.TenantId), and this receipt is worded at
        // that level on purpose: the expiry event does carry an application id, but it is deliberately
        // not promoted into the receipt's ownership, so the single record captured at send time stays
        // the whole answer here. The ui_config record of an expired transaction cannot be read back
        // at all.
        using (ChannelTenantContext.BeginScope(reference.TenantId))
        {
            // The type of the transaction comes from the event: the publisher of the expiry states it
            // in the context of the event, and the transaction itself is already gone by now. The
            // surface is the channel the prompt was shown in, the locale is the one captured at send
            // time; a missing translation falls back to the base text of the wording.
            var address = new OutcomeReceiptAddress(
                TransactionOutcome.Expired,
                OutcomeReceiptSurfaces.ForChannel(adapter),
                expired.Context?.TransactionType,
                expired.Context?.ConfirmationParameters?.ActionType,
                reference.ChannelType);

            // The ownership the receipt is worded over is the ownership its display is resolved for:
            // the tenant captured on the stored record, exactly as the wording below states it.
            var displayIntent = await _displayIntentSource.ResolveAsync(
                ResolutionContext.ForTenant(reference.TenantId),
                cancellationToken);

            var notice = new TransactionOutcomeNotice
            {
                TransactionId = expired.TransactionId,
                Outcome = TransactionOutcome.Expired,
                ChannelUserId = reference.ChannelUserId,
                StatusText = await _receiptText.RenderAsync(
                    address,
                    reference.RecipientLocale,
                    // Zone off the SAME record the locale is: the two are one answer to how this
                    // recipient reads the receipt, and reading one half from the send-time snapshot
                    // and the other from the event would be two sources for one thing. The moment of
                    // the outcome is the TTL the transaction ended at, which the expiry event states
                    // beside the moment of its own publication — the same moment the pages of the
                    // core report for an expiry, and never the clock of the sweep that noticed it.
                    reference.RecipientTimeZone,
                    expired.ExpiresAt,
                    // The prompt this notice replaces IS the question this recipient was shown, so the
                    // receipt may name its subject. The values come off the event beside the action type
                    // of the address, for the same reason: the transaction itself is already gone. The
                    // attribution of the event carries no initiator context, so a sign-in names its
                    // application by the client identifier here.
                    expired.Context is { } eventContext ? TransactionSlotSource.From(eventContext) : null,
                    ResolutionContext.ForTenant(reference.TenantId),
                    cancellationToken),
                // No inbound token on the background path: nobody pressed anything.
                PromptMessage = reference.Message,
                DisplayIntent = displayIntent
            };

            await ChannelOutbound.NotifyOutcomeAsync(adapter, notice, _logger, _metrics, cancellationToken);
        }
    }

    /// <summary>
    /// Finds the adapter serving the given channel type via a linear scan (there are only a few).
    /// </summary>
    /// <param name="channelType">Channel type to resolve.</param>
    /// <returns>Adapter of the channel type, or null when not registered.</returns>
    private IChannelAdapter? FindAdapter(string channelType)
    {
        foreach (var adapter in _adapters)
        {
            if (string.Equals(adapter.ChannelType, channelType, StringComparison.Ordinal))
            {
                return adapter;
            }
        }

        return null;
    }
}
