// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Base builder of a bot client from a tenant token (CA-166): deduplicates the shared
/// skeleton of the Telegram/MAX builders (obtain the tenant's tenant-credential → check the token →
/// construct the client). Channel specifics live in the derived classes: channel/client type,
/// credential group type, token extraction from credentials and the client factory from the token.
/// </summary>
/// <typeparam name="TClient">Channel client type (e.g. <c>ITelegramBotClient</c>).</typeparam>
/// <typeparam name="TCredentials">Channel tenant-credential group type (041.1).</typeparam>
internal abstract class BotTokenClientBuilder<TClient, TCredentials> : IChannelClientBuilder<TClient>
    where TClient : class
    where TCredentials : class
{
    /// <summary>
    /// Canonical layer resolver — the standard mechanism by which an in-process adapter obtains its
    /// own configuration.
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Logger of the builder — the place the credential read below is made from.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Creates the base builder over the canonical resolver.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="logger">Logger of the builder.</param>
    protected BotTokenClientBuilder(IConfigurationResolver resolver, ILogger logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public abstract string ChannelType { get; }

    /// <summary>
    /// Credential key of the channel — what the builder resolves for the tenant of the context.
    /// </summary>
    protected abstract ConfigKey<TCredentials> CredentialsKey { get; }

    /// <summary>
    /// Extracts the bot token from the channel's tenant-credential group.
    /// </summary>
    /// <param name="credentials">Tenant's tenant-credential group.</param>
    /// <returns>Bot token (may be empty if credentials are not set).</returns>
    protected abstract string? GetToken(TCredentials credentials);

    /// <summary>
    /// Creates a channel client from the tenant's effective token.
    /// </summary>
    /// <param name="token">Tenant's effective bot token.</param>
    /// <returns>Channel client.</returns>
    protected abstract TClient CreateClient(string token);

    /// <summary>
    /// Human-readable channel name for the missing-token diagnostic message.
    /// </summary>
    protected abstract string ChannelDisplayName { get; }

    /// <inheritdoc />
    public async ValueTask<Result<TClient>> BuildAsync(
        ChannelCredentialContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Obtain the tenant's tenant-credential through the canonical resolver.
        var credentials = await _resolver.ResolveCredentialsAsync(CredentialsKey, context, _logger, cancellationToken);
        if (credentials.IsFailure)
        {
            return Result<TClient>.Failure(credentials.Error);
        }

        // Without a token the client cannot be built — credentials are missing for the tenant.
        var token = GetToken(credentials.Value);
        if (string.IsNullOrEmpty(token))
        {
            return Result<TClient>.Failure(
                ChannelCredentialErrorCodes.ChannelCredentialsMissing,
                $"BotToken {ChannelDisplayName} is not set for tenant "
                + $"'{context.TenantId ?? ChannelTenantContext.DefaultTenantDisplayName}'.");
        }

        return Result<TClient>.Success(CreateClient(token));
    }
}
