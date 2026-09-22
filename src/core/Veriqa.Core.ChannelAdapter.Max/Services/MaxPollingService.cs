// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Max.BotClient;
using Max.BotClient.Types;

using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.DependencyInjection;
using Veriqa.Core.ChannelAdapter.Max.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.TransactionEngine.Events;
using Veriqa.Core.TransactionEngine.Identity;
using Veriqa.Core.TransactionEngine.Services;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.ChannelAdapter.Max.Services;

/// <summary>
/// Per-tenant supervisor of MAX update long polling (SPEC-003 §11, TASK-051).
/// The supervisor takes the set of active polling tenants from <see cref="IPollingTenantSource"/> and
/// starts an independent long-poll loop for each one with its own bot client (from
/// <see cref="IChannelClientFactory"/> by <c>(Max, tenantId)</c>) and its own <c>marker</c>.
/// N=1 (self-hosted) is the degenerate case: the set <c>[null]</c> ⇒ one loop, the default client.
/// Reaction to set changes without a restart — in the base class.
/// A simplified implementation for local development; the Webhook mode is recommended in production.
/// </summary>
internal sealed class MaxPollingService : PerTenantPollingSupervisor
{
    /// <summary>
    /// Channel client factory (per-tenant bot client by <c>(ChannelType, tenantId)</c>).
    /// </summary>
    private readonly IChannelClientFactory _clientFactory;

    /// <summary>
    /// MAX channel adapter.
    /// </summary>
    private readonly MaxChannelAdapter _channelAdapter;

    /// <summary>
    /// Transaction service for pipeline processing.
    /// </summary>
    private readonly ITransactionService _transactionService;

    /// <summary>
    /// Transaction store: the pipeline reads a reported decision through it, past the TTL gate of the
    /// service, so a press that arrives after the deadline is answered by what happened to the
    /// transaction (SPEC-003 CA-192).
    /// </summary>
    private readonly ITransactionStore _transactionStore;

    /// <summary>
    /// Clock the pipeline reads the deadline of a transaction against. There is no HttpContext on a
    /// polling loop, so it comes from the container and travels onwards as a parameter.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Channel identity resolution service (TASK-002).
    /// </summary>
    private readonly IIdentityResolutionService _identityResolutionService;

    /// <summary>
    /// Per-user rate limiter (optional — may be absent in dev/test).
    /// </summary>
    private readonly IUserAuthRateLimiter? _userAuthRateLimiter;

    /// <summary>
    /// Component metrics: this loop drives the same pipeline as the webhook route, and a failed user
    /// notification on it is counted into the same instrument. There is no HttpContext on a polling
    /// loop, so the instrument comes from the container and travels as a parameter.
    /// </summary>
    private readonly ChannelAdapterMetrics _metrics;

    /// <summary>
    /// Confirmation prompt orchestrator. Registered by the same call that registers this hosted
    /// service, hence mandatory: a loop running without it would answer every confirmation
    /// transaction by dropping the decision, and it would do so silently (SPEC-039 E41).
    /// </summary>
    private readonly IConfirmationPromptOrchestrator _confirmationPromptOrchestrator;

    /// <summary>
    /// Channel message localizer. Registered by the same call as this hosted service, hence mandatory:
    /// absent, it would leave every status text of this loop in the base language unannounced.
    /// </summary>
    private readonly IConfirmationPromptLocalizer _promptLocalizer;

    /// <summary>
    /// Terminal text of the transaction: this loop states the address of the receipt and the core
    /// answers with the wording, exactly as the webhook route does (SPEC-036 TPL-123).
    /// </summary>
    private readonly IOutcomeReceiptText _receiptText;

    /// <summary>
    /// Store of sent-prompt coordinates: the outcome of a press whose event carried no message id is
    /// shown by editing the stored prompt, exactly as on the webhook route.
    /// Registered by the same call that registers this hosted service, hence not optional.
    /// </summary>
    private readonly IChannelPromptMessageStore _promptMessageStore;

    /// <summary>
    /// Texts this channel declared at its registration. The loop drives the very same pipeline as the
    /// webhook route, so it states the very same texts — read off the registration rather than named
    /// here, or an installation that switched a reply off would still be answered by this loop.
    /// </summary>
    private readonly ChannelReplyTexts _replyTexts;

    /// <summary>
    /// Bus the refusals of the interaction surface are written to (SPEC-039 E48). There is no
    /// HttpContext on a polling loop, so it comes from the container and travels as a parameter.
    /// </summary>
    private readonly ITransactionEventPublisher _eventPublisher;

    /// <summary>
    /// How the deployment wants the receipt of a terminal outcome to appear in the conversation. This
    /// loop drives the very same pipeline as the webhook route, so it states the very same intent;
    /// there is no HttpContext here, so it comes from the container and travels as a parameter.
    /// </summary>
    private readonly IOutcomeNoticeDisplayIntentSource _displayIntentSource;

    /// <summary>
    /// Creates a <see cref="MaxPollingService"/> instance.
    /// </summary>
    /// <param name="clientFactory">Channel client factory (per-tenant bot client).</param>
    /// <param name="tenantSource">Source of active polling tenants.</param>
    /// <param name="channelAdapters">Registered channel adapters.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="transactionStore">Transaction store a reported decision is read through.</param>
    /// <param name="timeProvider">Clock the deadline of a transaction is read against.</param>
    /// <param name="identityResolutionService">Channel identity resolution service.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="promptMessageStore">Store of sent-prompt coordinates.</param>
    /// <param name="metrics">Component metrics of the channel contour.</param>
    /// <param name="receiptText">Terminal text of the transaction.</param>
    /// <param name="confirmationPromptOrchestrator">Confirmation prompt orchestrator.</param>
    /// <param name="promptLocalizer">Channel message localizer.</param>
    /// <param name="channels">Registry the channel's own declared reply texts are read from.</param>
    /// <param name="eventPublisher">Bus the refusals of the interaction surface are written to.</param>
    /// <param name="displayIntentSource">Port the desired display intent of the receipt comes from.</param>
    /// <param name="userAuthRateLimiter">Per-user rate limiter (null — no limiting).</param>
    public MaxPollingService(
        IChannelClientFactory clientFactory,
        IPollingTenantSource tenantSource,
        IEnumerable<IChannelAdapter> channelAdapters,
        ITransactionService transactionService,
        ITransactionStore transactionStore,
        TimeProvider timeProvider,
        IIdentityResolutionService identityResolutionService,
        ILogger<MaxPollingService> logger,
        IChannelPromptMessageStore promptMessageStore,
        ChannelAdapterMetrics metrics,
        IOutcomeReceiptText receiptText,
        IConfirmationPromptOrchestrator confirmationPromptOrchestrator,
        IConfirmationPromptLocalizer promptLocalizer,
        CustomChannelRegistry channels,
        ITransactionEventPublisher eventPublisher,
        IOutcomeNoticeDisplayIntentSource displayIntentSource,
        IUserAuthRateLimiter? userAuthRateLimiter = null)
        : base(tenantSource, logger)
    {
        // Initialize the dependencies and explicitly pick the MAX adapter
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _channelAdapter = ResolveMaxAdapter(channelAdapters);
        _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
        _transactionStore = transactionStore ?? throw new ArgumentNullException(nameof(transactionStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _identityResolutionService = identityResolutionService ?? throw new ArgumentNullException(nameof(identityResolutionService));
        _promptMessageStore = promptMessageStore ?? throw new ArgumentNullException(nameof(promptMessageStore));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _receiptText = receiptText ?? throw new ArgumentNullException(nameof(receiptText));
        ArgumentNullException.ThrowIfNull(confirmationPromptOrchestrator);
        ArgumentNullException.ThrowIfNull(promptLocalizer);
        _confirmationPromptOrchestrator = confirmationPromptOrchestrator;
        _promptLocalizer = promptLocalizer;
        ArgumentNullException.ThrowIfNull(channels);
        _replyTexts = channels.ReplyTextsOf(ChannelTypes.Max);
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _displayIntentSource = displayIntentSource ?? throw new ArgumentNullException(nameof(displayIntentSource));
        _userAuthRateLimiter = userAuthRateLimiter;
    }

    /// <inheritdoc />
    protected override string ChannelType => ChannelTypes.Max;

    /// <inheritdoc />
    protected override int RetryBaseDelayMilliseconds => MaxAdapterConstants.RetryBaseDelayMs;

    /// <summary>
    /// Picks the registered adapter for the MAX channel.
    /// </summary>
    /// <param name="channelAdapters">Collection of registered channel adapters.</param>
    /// <returns>MAX channel adapter.</returns>
    private static MaxChannelAdapter ResolveMaxAdapter(IEnumerable<IChannelAdapter> channelAdapters)
    {
        // Validate the input and look for the adapter with the MAX channel type
        ArgumentNullException.ThrowIfNull(channelAdapters);

        foreach (var adapter in channelAdapters)
        {
            if (adapter is MaxChannelAdapter maxAdapter)
            {
                return maxAdapter;
            }
        }

        throw new InvalidOperationException("The MAX channel adapter is not registered.");
    }

    /// <summary>
    /// Per-tenant MAX long-poll loop: its own bot client, its own <c>marker</c>, update processing
    /// in the tenant scope. Local retry on failure — does not bring down other tenants' loops.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (<c>null</c> — the default implicit tenant).</param>
    /// <param name="cancellationToken">Cancellation token of the tenant's loop.</param>
    protected override async Task RunTenantPollLoopAsync(string? tenantId, CancellationToken cancellationToken)
    {
        // The tenant's bot client from the per-tenant factory by (Max, tenantId).
        var clientResult = await _clientFactory.GetOrCreateClientAsync<IBotClient>(
            ChannelCredentialContext.ForTenant(ChannelTypes.Max, tenantId),
            cancellationToken);

        if (clientResult.IsFailure)
        {
            // No token in the logs (core-rules §10): only the resolution error code/message.
            Logger.LogWarning(
                "Failed to create the tenant {TenantId} MAX client: {ErrorCode} — {ErrorMessage}. Loop not started",
                tenantId ?? "<default>",
                clientResult.Error.Code,
                clientResult.Error.Message);
            return;
        }

        var botClient = clientResult.Value;

        // marker is a local variable of THIS loop (per-tenant position isolation, requirement 2).
        long? marker = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Request updates via MAX Bot API long polling
                var (updates, nextMarker) = await botClient.GetUpdates(
                    limit: MaxAdapterConstants.LongPollUpdatesLimit,
                    timeout: MaxAdapterConstants.LongPollTimeoutSeconds,
                    marker: marker,
                    types: null,
                    cancellationToken: cancellationToken);

                // Process every update in the tenant scope (requirement 3).
                foreach (var update in updates)
                {
                    await ProcessUpdateSafelyAsync(update, tenantId, cancellationToken);
                }

                // Update the marker for the next request
                marker = nextMarker;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal completion when the tenant loop/host is stopped
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Error in the MAX polling loop of tenant {TenantId}. Retrying in {DelayMs} ms",
                    tenantId ?? "<default>",
                    MaxAdapterConstants.RetryBaseDelayMs);

                // Local delay before retrying (one tenant's failure does not bring down the others)
                await Task.Delay(MaxAdapterConstants.RetryBaseDelayMs, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Safely processes one update via the pipeline (similar to webhook processing),
    /// setting the tenant scope around the processing (<see cref="ChannelTenantContext.BeginScope"/>).
    /// </summary>
    /// <param name="update">MAX update.</param>
    /// <param name="tenantId">Tenant identifier (<c>null</c> — default).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ProcessUpdateSafelyAsync(Update update, string? tenantId, CancellationToken cancellationToken)
    {
        try
        {
            // Tenant context around the processing: the adapter/factory/pipeline take this tenant's credentials.
            using var tenantScope = ChannelTenantContext.BeginScope(tenantId);

            // Pass the update to the channel adapter for processing
            // Polling receives an already deserialized update from the platform library; serializing
            // it back just to pass through the neutral envelope would be wasteful and fragile, so the
            // loop enters the adapter's internal reception point. Both paths meet inside the adapter.
            var result = await _channelAdapter.ProcessUpdateAsync(update, cancellationToken);

            if (result.IsFailure)
            {
                Logger.LogWarning(
                    "Error processing a MAX update: {ErrorCode} — {ErrorMessage}",
                    result.Error.Code,
                    result.Error.Message);
                return;
            }

            // Routing via the pipeline. The texts the channel states for itself come from its
            // registration, the same source the webhook route reads them from.
            await ChannelWebhookPipeline.ProcessInboundResultAsync(
                result.Value,
                _channelAdapter,
                _transactionService,
                _transactionStore,
                _timeProvider,
                _identityResolutionService,
                Logger,
                _metrics,
                _userAuthRateLimiter,
                _confirmationPromptOrchestrator,
                _promptLocalizer,
                _promptMessageStore,
                _receiptText,
                _displayIntentSource,
                _eventPublisher,
                _replyTexts,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Unhandled exception while processing a MAX update");
        }
    }
}
