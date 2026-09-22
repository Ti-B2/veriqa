// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Max.BotClient;
using Max.BotClient.Types;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Veriqa.Core.ChannelAdapter.Max.Configuration;
using Veriqa.Core.ChannelAdapter.Max.Constants;
using Veriqa.Core.ChannelAdapter.Max.Enums;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;

namespace Veriqa.Core.ChannelAdapter.Max.Services;

/// <summary>
/// MAX bot webhook initialization service (SPEC-003 §11, SPEC-012).
/// Registers the webhook in the MAX Bot API on startup and deletes it on shutdown.
/// Active only with <see cref="MaxUpdateMode.Webhook"/>.
/// </summary>
internal sealed class MaxWebhookInitializerService : IHostedService
{
    /// <summary>
    /// MAX Bot API client.
    /// </summary>
    private readonly IChannelClientFactory _clientFactory;

    /// <summary>
    /// MAX adapter settings.
    /// </summary>
    private readonly MaxOptions _options;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<MaxWebhookInitializerService> _logger;

    /// <summary>
    /// Full webhook URL registered at startup.
    /// Kept for deletion on shutdown.
    /// </summary>
    private string? _registeredWebhookUrl;

    /// <summary>
    /// Creates a <see cref="MaxWebhookInitializerService"/> instance.
    /// </summary>
    /// <param name="clientFactory">Per-tenant channel client factory.</param>
    /// <param name="options">MAX adapter settings.</param>
    /// <param name="logger">Logger.</param>
    public MaxWebhookInitializerService(
        IChannelClientFactory clientFactory,
        IOptions<MaxOptions> options,
        ILogger<MaxWebhookInitializerService> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers the webhook in the MAX Bot API at startup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The webhook is registered only in the Webhook mode
        if (_options.UpdateMode is not MaxUpdateMode.Webhook)
        {
            _logger.LogInformation(
                "MAX update mode is {UpdateMode}, webhook registration skipped",
                _options.UpdateMode);

            return;
        }

        // Build the full webhook URL
        _registeredWebhookUrl = _options.WebhookBaseUrl.TrimEnd('/')
            + MaxAdapterConstants.WebhookPath;

        // Update types required for the adapter to work
        var updateTypes = new[]
        {
            UpdateType.BotStarted,
            UpdateType.MessageCallback
        };

        // In a valid configuration the secret token is mandatory for the Webhook mode
        // (checked by MaxOptionsValidator at startup)
        // Startup core-only: webhook is registered once at boot; no request tenant here, so this stays a direct core-level read (unlike the per-request adapters migrated to the seam in TASK-072/B4).
        var secretToken = _options.WebhookSecretToken;

        // Register the webhook via the MAX Bot API
        await (await ResolveClientAsync(cancellationToken)).Subscribe(
            url: _registeredWebhookUrl,
            updateTypes: updateTypes,
            secret: secretToken,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "MAX webhook registered: {WebhookUrl}",
            _registeredWebhookUrl);
    }

    /// <summary>
    /// Deletes the webhook from the MAX Bot API on shutdown.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // The webhook is deleted only if it was registered
        if (_registeredWebhookUrl is null)
        {
            return;
        }

        try
        {
            // Delete the webhook (CancellationToken.None — the token may already be cancelled on host shutdown)
            await (await ResolveClientAsync(CancellationToken.None)).Unsubscribe(
                url: _registeredWebhookUrl,
                cancellationToken: CancellationToken.None);

            _logger.LogInformation("MAX webhook deleted: {WebhookUrl}", _registeredWebhookUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to delete the MAX webhook on service shutdown: {WebhookUrl}",
                _registeredWebhookUrl);
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
    private async Task<IBotClient> ResolveClientAsync(CancellationToken cancellationToken)
    {
        var result = await _clientFactory.GetOrCreateClientAsync<IBotClient>(
            ChannelCredentialContext.ForTenant(ChannelTypes.Max, tenantId: null),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to create the default tenant MAX Bot client: {result.Error.Message}");
        }

        return result.Value;
    }
}
