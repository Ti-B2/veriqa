// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Max.BotClient;

using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.Max.Configuration;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.Max.MultiTenancy;

/// <summary>
/// Builder of the MAX Bot client from the tenant's effective token (SPEC-003 §17.4, CA-166).
/// Channel specifics on top of the shared <see cref="BotTokenClientBuilder{TClient,TCredentials}"/> skeleton:
/// channel/client type, credential group type and construction of <see cref="BotClient"/> from the token.
/// </summary>
internal sealed class MaxClientBuilder : BotTokenClientBuilder<IBotClient, MaxTenantCredentials>
{
    /// <summary>
    /// Creates the MAX client builder over the canonical resolver.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="logger">Logger of the builder.</param>
    public MaxClientBuilder(IConfigurationResolver resolver, ILogger<MaxClientBuilder> logger)
        : base(resolver, logger)
    {
    }

    /// <inheritdoc />
    public override string ChannelType => ChannelTypes.Max;

    /// <inheritdoc />
    protected override ConfigKey<MaxTenantCredentials> CredentialsKey => MaxConfigKeys.Credentials;

    /// <inheritdoc />
    protected override string ChannelDisplayName => "MAX";

    /// <inheritdoc />
    protected override string? GetToken(MaxTenantCredentials credentials) => credentials.BotToken;

    /// <inheritdoc />
    protected override IBotClient CreateClient(string token) => new BotClient(token);
}
