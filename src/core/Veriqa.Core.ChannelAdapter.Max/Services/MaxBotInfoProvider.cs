// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Max.BotClient;

using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.Max.Configuration;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.Max.Services;

/// <summary>
/// MAX bot info provider (per-tenant).
/// If <see cref="MaxTenantCredentials.BotPublicName"/> is set in the tenant's tenant-credential —
/// uses it directly (without an API call). Otherwise resolves the tenant's client via the factory and
/// caches the username per tenant. The shared skeleton (cache/fast path/ambient tenant) lives in the
/// base <see cref="BotUsernameProvider{TClient,TCredentials}"/>; only channel specifics are here.
/// </summary>
internal sealed class MaxBotInfoProvider
    : BotUsernameProvider<IBotClient, MaxTenantCredentials>, IMaxBotInfoProvider
{
    /// <summary>
    /// Creates a <see cref="MaxBotInfoProvider"/> instance.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="clientFactory">Per-tenant channel client factory.</param>
    /// <param name="logger">Logger.</param>
    public MaxBotInfoProvider(
        IConfigurationResolver resolver,
        IChannelClientFactory clientFactory,
        ILogger<MaxBotInfoProvider> logger)
        : base(resolver, clientFactory, logger)
    {
    }

    /// <inheritdoc />
    protected override string ChannelType => ChannelTypes.Max;

    /// <inheritdoc />
    protected override ConfigKey<MaxTenantCredentials> CredentialsKey => MaxConfigKeys.Credentials;

    /// <inheritdoc />
    protected override string ChannelDisplayName => "MAX";

    /// <inheritdoc />
    protected override string? GetConfiguredUsername(MaxTenantCredentials credentials)
        => credentials.BotPublicName;

    /// <inheritdoc />
    protected override async Task<string?> FetchUsernameAsync(IBotClient client, CancellationToken cancellationToken)
    {
        var botInfo = await client.GetMe(cancellationToken);
        return botInfo.Username;
    }
}
