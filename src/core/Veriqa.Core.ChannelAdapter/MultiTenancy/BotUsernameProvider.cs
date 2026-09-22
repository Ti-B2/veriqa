// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Base bot username provider (per-tenant, CA-166): deduplicates the shared
/// skeleton of the Telegram/MAX bot info providers (per-tenant cache, fast path, ambient tenant,
/// "name from credentials first → otherwise resolve via the tenant's Bot API client"). Channel
/// specifics live in the derived classes: channel type, credential group type, extraction of the
/// configured name from credentials, and obtaining the username from the API client.
/// </summary>
/// <typeparam name="TClient">Channel client type (e.g. <c>ITelegramBotClient</c>).</typeparam>
/// <typeparam name="TCredentials">Channel tenant-credential group type (041.1).</typeparam>
internal abstract class BotUsernameProvider<TClient, TCredentials>
    where TClient : class
    where TCredentials : class
{
    /// <summary>
    /// Canonical layer resolver (for reading the configured bot identity name from the credentials).
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Per-tenant channel client factory (for resolving the username via the Bot API when absent in credentials).
    /// </summary>
    private readonly IChannelClientFactory _clientFactory;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Bot username cache per tenant (null key = default implicit tenant). Per-tenant so that
    /// in multi-tenant mode one bot's username is not served to other tenants.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _usernameByTenant = new(StringComparer.Ordinal);

    /// <summary>
    /// Cache key for the default (null) tenant.
    /// </summary>
    private const string DefaultTenantKey = "\0default";

    /// <summary>
    /// Creates the base bot username provider.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="clientFactory">Per-tenant channel client factory.</param>
    /// <param name="logger">Logger.</param>
    protected BotUsernameProvider(
        IConfigurationResolver resolver,
        IChannelClientFactory clientFactory,
        ILogger logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Channel type (for the credential/client resolution context).
    /// </summary>
    protected abstract string ChannelType { get; }

    /// <summary>
    /// Credential key of the channel — what the configured bot identity name is read from.
    /// </summary>
    protected abstract ConfigKey<TCredentials> CredentialsKey { get; }

    /// <summary>
    /// Human-readable channel name for diagnostic messages.
    /// </summary>
    protected abstract string ChannelDisplayName { get; }

    /// <summary>
    /// Extracts the configured bot username from the tenant-credential group (identity, §17.3).
    /// </summary>
    /// <param name="credentials">Tenant's tenant-credential group.</param>
    /// <returns>Username from credentials, or <c>null</c>/empty if not set.</returns>
    protected abstract string? GetConfiguredUsername(TCredentials credentials);

    /// <summary>
    /// Obtains the bot username via the Bot API of the tenant's channel client.
    /// </summary>
    /// <param name="client">Tenant's channel client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bot username, or <c>null</c> if the API returned no username.</returns>
    protected abstract Task<string?> FetchUsernameAsync(TClient client, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the current tenant's bot username: first from credentials (if set),
    /// otherwise from the Bot API of the tenant's client; the result is cached per-tenant.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bot username without the @ symbol.</returns>
    public async Task<string> GetBotUsernameAsync(CancellationToken cancellationToken = default)
    {
        var context = ChannelCredentialContext.ForCurrentTenant(ChannelType);
        var cacheKey = context.TenantId ?? DefaultTenantKey;

        // Fast path: the tenant's username is already cached.
        if (_usernameByTenant.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // If the username is set in the tenant's credentials (identity, §17.3) — use it without an API call.
        var credentials = await _resolver.ResolveCredentialsAsync(CredentialsKey, context, _logger, cancellationToken);
        if (credentials.IsSuccess)
        {
            var configured = GetConfiguredUsername(credentials.Value);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return _usernameByTenant.GetOrAdd(cacheKey, configured);
            }
        }

        // Otherwise resolve the username via the Bot API of the tenant's client.
        var clientResult = await _clientFactory.GetOrCreateClientAsync<TClient>(context, cancellationToken);
        if (clientResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to resolve the {ChannelDisplayName} client of tenant '{context.TenantId ?? ChannelTenantContext.DefaultTenantDisplayName}' to obtain username: {clientResult.Error.Message}");
        }

        var username = await FetchUsernameAsync(clientResult.Value, cancellationToken)
            ?? throw new InvalidOperationException($"{ChannelDisplayName} Bot API returned bot info without a username.");

        _logger.LogInformation(
            "Received {ChannelDisplayName} bot info: username = {BotUsername}",
            ChannelDisplayName,
            username);

        return _usernameByTenant.GetOrAdd(cacheKey, username);
    }
}
