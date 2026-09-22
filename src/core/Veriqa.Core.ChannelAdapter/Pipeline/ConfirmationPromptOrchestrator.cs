// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Implementation of the confirmation-prompt orchestrator (SPEC-003 §13.2, SPEC-017 §7).
/// The confirmation context is built by <see cref="IConfirmationPromptContextFactory"/> (ICC-041).
/// </summary>
internal sealed class ConfirmationPromptOrchestrator : IConfirmationPromptOrchestrator
{
    /// <summary>
    /// Resolver of the effective confirmation surface (SPEC-012 §4.4.2).
    /// </summary>
    private readonly IEffectiveConfirmationSurfaceResolver _surfaceResolver;

    /// <summary>
    /// Confirmation context factory.
    /// </summary>
    private readonly IConfirmationPromptContextFactory _promptContextFactory;

    /// <summary>
    /// Store of sent-prompt coordinates keyed by transaction (SPEC-003 §4.5). The core writes it:
    /// the adapter only reports the reference it got back from the platform.
    /// </summary>
    private readonly IChannelPromptMessageStore _promptMessageStore;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<ConfirmationPromptOrchestrator> _logger;

    /// <summary>
    /// Component metrics: a prompt the channel refused is counted by the outbound seam.
    /// </summary>
    private readonly ChannelAdapterMetrics _metrics;

    /// <summary>
    /// Creates the confirmation-prompt orchestrator.
    /// </summary>
    /// <param name="surfaceResolver">Resolver of the effective confirmation surface.</param>
    /// <param name="promptContextFactory">Confirmation context factory.</param>
    /// <param name="promptMessageStore">Store of sent-prompt coordinates.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics.</param>
    public ConfirmationPromptOrchestrator(
        IEffectiveConfirmationSurfaceResolver surfaceResolver,
        IConfirmationPromptContextFactory promptContextFactory,
        IChannelPromptMessageStore promptMessageStore,
        ILogger<ConfirmationPromptOrchestrator> logger,
        ChannelAdapterMetrics metrics)
    {
        _surfaceResolver = surfaceResolver;
        _promptContextFactory = promptContextFactory;
        _promptMessageStore = promptMessageStore;
        _logger = logger;
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task<AuthStartConfirmationOutcome> HandleAuthStartConfirmationAsync(
        Transaction transaction,
        IChannelAdapter adapter,
        string channelUserId,
        string? recipientLocale,
        CancellationToken cancellationToken = default)
    {
        // The method applies the prompt flow only when the effective surface is InChannel; the other
        // two surfaces are decisions of the core and are handed back to the caller as an outcome.
        // The surface is resolved by a single resolver (SPEC-012 §4.4.2): the levels resolver →
        // substitution of the channel default → clamping by the channel's supported surfaces.
        var surface = await _surfaceResolver.ResolveEffectiveSurfaceAsync(transaction, adapter, cancellationToken);

        if (surface is not ConfirmationSurface.InChannel)
        {
            return surface is ConfirmationSurface.OnWebPage
                ? AuthStartConfirmationOutcome.AwaitingWebConfirmation
                : AuthStartConfirmationOutcome.AutoConfirm;
        }

        // Repeat delivery of the same inbound event (a provider retry, a second tap on the deep
        // link) must not reach the adapter at all: the outbound seam reports and counts a refusal
        // before the state machine gets a chance to reject the transition, so without this gate one
        // decision would produce as many loud reports as the provider makes attempts. A terminal
        // state IS "the decision is already recorded" — for every terminal state, not just the
        // failure this signal produces: a prompt on a finished transaction is pointless in any case.
        if (transaction.IsTerminal())
        {
            _logger.LogDebug(
                "The transaction decision is already recorded — no confirmation prompt is sent. ChannelType: {ChannelType}, TransactionId: {TransactionId}, State: {State}",
                adapter.ChannelType,
                transaction.Id.ToString(),
                transaction.State);

            return AuthStartConfirmationOutcome.HandledInChannel;
        }

        var promptContext = await _promptContextFactory.CreateAsync(
            transaction,
            adapter.ChannelType,
            channelUserId,
            recipientLocale,
            cancellationToken);

        LogContextDiagnostics(adapter, promptContext);

        // The one thing that may go into a channel on a transaction whose subject IS the confirmation
        // is that subject, worded (SPEC-039 E28). Any other context — a suppressed one above all —
        // would make the adapter fall back on its own sign-in text, and the user would be asked to
        // confirm signing in instead of the action the relying party named. The question is not asked
        // at all; the caller ends the transaction. Judged by the VARIANT of the context and not by the
        // reason of a suppression: the reason stays diagnostics, the decision rests on the fact that
        // there is no subject to show.
        if (string.Equals(transaction.Type, TransactionTypes.Confirmation, StringComparison.Ordinal)
            && promptContext is not SubjectConfirmationPromptContext)
        {
            _logger.LogWarning(
                "The subject of the confirmation is unavailable — the prompt is not sent. ChannelType: {ChannelType}, TransactionId: {TransactionId}",
                adapter.ChannelType,
                transaction.Id.ToString());

            return AuthStartConfirmationOutcome.ConfirmationSubjectUnavailable;
        }

        var sendResult = await ChannelOutbound.SendConfirmationPromptAsync(
            adapter,
            transaction.Id,
            channelUserId,
            promptContext,
            _logger,
            _metrics,
            cancellationToken);

        if (sendResult.IsFailure)
        {
            // Prompt not sent. An ordinary refusal leaves the transaction Pending until a retry or
            // TTL; auto-confirmation is NOT performed — a fallback would weaken the chosen
            // confirmation mode. The runtime signal (SPEC-003 CA-191) is the one exception: the
            // channel said it cannot carry this confirmation out, so waiting for it is pointless and
            // the caller ends the transaction. The failure has already been logged and counted by
            // the outbound seam.
            return ChannelOutbound.IsCannotContinueSignal(sendResult.Error.Code)
                ? AuthStartConfirmationOutcome.ChannelCannotContinue
                : AuthStartConfirmationOutcome.HandledInChannel;
        }

        await StorePromptReferenceAsync(transaction, adapter, channelUserId, promptContext, sendResult.Value);

        return AuthStartConfirmationOutcome.HandledInChannel;
    }

    /// <inheritdoc />
    public async ValueTask<bool> AcceptsChannelDecisionAsync(
        Transaction transaction,
        IChannelAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(adapter);

        // A sign-in transaction is out of scope entirely: its surface is resolved by the existing
        // mechanism and nothing about its paths changes here.
        if (!string.Equals(transaction.Type, TransactionTypes.Confirmation, StringComparison.Ordinal))
        {
            return true;
        }

        // The same resolver that decides WHERE the question is asked answers whether this channel was
        // the one asked to ask it. Any surface other than the channel itself means the subject never
        // travelled through this channel, so a decision coming back from it answers for a question the
        // user was never shown.
        var surface = await _surfaceResolver.ResolveEffectiveSurfaceAsync(transaction, adapter, cancellationToken);

        if (surface is ConfirmationSurface.InChannel)
        {
            return true;
        }

        _logger.LogWarning(
            "A channel reported a decision on a confirmation whose question it was not asked to show — the decision is dropped. ChannelType: {ChannelType}, TransactionId: {TransactionId}, Surface: {Surface}",
            adapter.ChannelType,
            transaction.Id.ToString(),
            surface);

        return false;
    }

    /// <summary>
    /// Records the sent-prompt coordinates so a later lifecycle event (TTL expiry) can address that
    /// exact message. Only channels that declared they can update their own message are stored —
    /// for the rest the record would never be usable.
    /// </summary>
    /// <remarks>
    /// Best-effort with <see cref="CancellationToken.None"/>: the prompt is already delivered, so a
    /// cancelling request must not skip the record (there would be no expiry notice otherwise), and
    /// a store failure never turns a delivered prompt into a failure.
    /// </remarks>
    /// <param name="transaction">Transaction the prompt belongs to.</param>
    /// <param name="adapter">Channel adapter that sent the prompt.</param>
    /// <param name="channelUserId">Recipient within the channel.</param>
    /// <param name="promptContext">Confirmation context (carries the recipient locale and zone).</param>
    /// <param name="messageRef">Reference reported by the adapter (null — nothing addressable).</param>
    private async Task StorePromptReferenceAsync(
        Transaction transaction,
        IChannelAdapter adapter,
        string channelUserId,
        ConfirmationPromptContext promptContext,
        ChannelMessageRef? messageRef)
    {
        if (!adapter.Capabilities.SupportsMessageUpdate)
        {
            // A channel that cannot update its own message may still return a reference — the core
            // simply does not keep it. That is not a refusal.
            return;
        }

        if (messageRef is null)
        {
            // The contract invariant used to rest on the adapter promising to call the store itself;
            // now it rests on the signature, and a breach is visible instead of silent.
            _logger.LogWarning(
                "Channel declares message updates but returned no prompt reference. ChannelType: {ChannelType}, TransactionId: {TransactionId}",
                adapter.ChannelType,
                transaction.Id.ToString());
            return;
        }

        try
        {
            await _promptMessageStore.SaveAsync(
                transaction.Id,
                new ChannelPromptMessageRef
                {
                    ChannelType = adapter.ChannelType,
                    Message = messageRef,
                    ChannelUserId = channelUserId,
                    TenantId = ChannelTenantContext.CurrentTenantId,
                    RecipientLocale = promptContext.RecipientLocale,
                    RecipientTimeZone = promptContext.RecipientTimeZone
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to record the sent prompt coordinates. ChannelType: {ChannelType}, TransactionId: {TransactionId}",
                adapter.ChannelType,
                transaction.Id.ToString());
        }
    }

    /// <summary>
    /// Diagnostics of the prompt about to be sent: why the initiator details are absent, and whether
    /// the channel's declared locale capability matches the fact.
    /// </summary>
    /// <param name="adapter">Channel adapter.</param>
    /// <param name="promptContext">Confirmation context built for the prompt.</param>
    private void LogContextDiagnostics(IChannelAdapter adapter, ConfirmationPromptContext promptContext)
    {
        if (promptContext is SuppressedConfirmationPromptContext suppressed)
        {
            _logger.LogDebug(
                "Confirmation prompt carries no initiator details. ChannelType: {ChannelType}, Reason: {Reason}",
                adapter.ChannelType,
                suppressed.Reason.Value);
        }

        // A channel that declares it supplies the recipient locale but ends up with an empty one is a
        // declaration diverging from the fact — the only place where both are known at once.
        if (adapter.Capabilities.ProvidesRecipientLocale && string.IsNullOrEmpty(promptContext.RecipientLocale))
        {
            _logger.LogWarning(
                "Channel declares it supplies the recipient locale, but the effective locale is empty. ChannelType: {ChannelType}",
                adapter.ChannelType);
        }
    }
}
