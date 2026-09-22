// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Telegram.Bot;

using Veriqa.Core.ChannelAdapter.Telegram.Configuration;
using Veriqa.Core.ChannelAdapter.Telegram.Constants;
using Veriqa.Core.ChannelAdapter.Telegram.Enums;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;

namespace Veriqa.Core.ChannelAdapter.Telegram.Services;

/// <summary>
/// Telegram bot webhook initialization service (SPEC-012 §6.9).
/// On startup it registers the webhook in the Telegram Bot API; on shutdown it deletes it.
/// Active only when <see cref="TelegramUpdateMode.Webhook"/>.
/// </summary>
internal sealed class TelegramWebhookInitializerService : IHostedService
{
    /// <summary>
    /// Telegram Bot API client.
    /// </summary>
    private readonly IChannelClientFactory _clientFactory;

    /// <summary>
    /// Telegram adapter settings.
    /// </summary>
    private readonly TelegramOptions _options;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<TelegramWebhookInitializerService> _logger;

    /// <summary>
    /// Creates an instance of <see cref="TelegramWebhookInitializerService"/>.
    /// </summary>
    /// <param name="clientFactory">Per-tenant channel client factory.</param>
    /// <param name="options">Telegram adapter settings.</param>
    /// <param name="logger">Logger.</param>
    public TelegramWebhookInitializerService(
        IChannelClientFactory clientFactory,
        IOptions<TelegramOptions> options,
        ILogger<TelegramWebhookInitializerService> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers the webhook in the Telegram Bot API on startup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The webhook is registered only in the Webhook mode
        if (_options.UpdateMode is not TelegramUpdateMode.Webhook)
        {
            _logger.LogInformation(
                "Update mode is {UpdateMode}, webhook registration skipped",
                _options.UpdateMode);

            return;
        }

        // Build the full webhook URL
        var webhookUrl = _options.WebhookBaseUrl.TrimEnd('/')
            + TelegramAdapterConstants.WebhookPath;

        // In a valid configuration for the Webhook mode the secret token is required
        // (checked by TelegramOptionsValidator at startup)
        // Startup core-only: webhook is registered once at boot; no request tenant here, so this stays a direct core-level read (unlike the per-request adapters migrated to the seam in TASK-072/B4).
        var secretToken = _options.WebhookSecretToken;

        // Register the webhook via the Telegram Bot API
        await (await ResolveClientAsync(cancellationToken)).SetWebhook(
            url: webhookUrl,
            secretToken: secretToken,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Telegram webhook registered: {WebhookUrl}",
            webhookUrl);
    }

    /// <summary>
    /// Deletes the webhook from the Telegram Bot API on shutdown.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // The webhook is deleted only if it was registered
        if (_options.UpdateMode is not TelegramUpdateMode.Webhook)
        {
            return;
        }

        try
        {
            // Delete the webhook (CancellationToken.None — on host shutdown the token may already be cancelled)
            await (await ResolveClientAsync(CancellationToken.None)).DeleteWebhook(cancellationToken: CancellationToken.None);

            _logger.LogInformation("Telegram webhook deleted");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete the Telegram webhook on service shutdown");
        }
    }

    /// <summary>
    /// Resolves the default tenant's client on the ASYNCHRONOUS startup platform. The client is built
    /// from the tenant's credentials, and resolving credentials is asynchronous now — so it happens
    /// here, in <c>StartAsync</c>/<c>StopAsync</c>, rather than in a constructor a container calls
    /// synchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Client of the default tenant.</returns>
    private async Task<ITelegramBotClient> ResolveClientAsync(CancellationToken cancellationToken)
    {
        var result = await _clientFactory.GetOrCreateClientAsync<ITelegramBotClient>(
            ChannelCredentialContext.ForTenant(ChannelTypes.Telegram, tenantId: null),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to create the default tenant Telegram Bot client: {result.Error.Message}");
        }

        return result.Value;
    }
}
