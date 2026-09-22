// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Telegram.Bot;

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.ChannelAdapter.Telegram.Configuration;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.Telegram.Services;

/// <summary>
/// Telegram bot information provider (per-tenant).
/// If the tenant's tenant-credential defines <see cref="TelegramTenantCredentials.BotUsername"/> —
/// it is used directly (without an API call). Otherwise it resolves the tenant client through the factory and
/// caches the username per tenant. The shared skeleton (cache/fast-path/ambient tenant) lives in the base
/// <see cref="BotUsernameProvider{TClient,TCredentials}"/>; only the channel specifics are here.
/// </summary>
internal sealed class BotInfoProvider
    : BotUsernameProvider<ITelegramBotClient, TelegramTenantCredentials>, IBotInfoProvider
{
    /// <summary>
    /// Creates an instance of <see cref="BotInfoProvider"/>.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="clientFactory">Per-tenant channel client factory.</param>
    /// <param name="logger">Logger.</param>
    public BotInfoProvider(
        IConfigurationResolver resolver,
        IChannelClientFactory clientFactory,
        ILogger<BotInfoProvider> logger)
        : base(resolver, clientFactory, logger)
    {
    }

    /// <inheritdoc />
    protected override string ChannelType => ChannelTypes.Telegram;

    /// <inheritdoc />
    protected override ConfigKey<TelegramTenantCredentials> CredentialsKey => TelegramConfigKeys.Credentials;

    /// <inheritdoc />
    protected override string ChannelDisplayName => "Telegram";

    /// <inheritdoc />
    protected override string? GetConfiguredUsername(TelegramTenantCredentials credentials)
        => credentials.BotUsername;

    /// <inheritdoc />
    protected override async Task<string?> FetchUsernameAsync(ITelegramBotClient client, CancellationToken cancellationToken)
    {
        var botUser = await client.GetMe(cancellationToken);
        return botUser.Username;
    }
}
