// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Telegram.Bot;

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.ChannelAdapter.Telegram.Configuration;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.Telegram.MultiTenancy;

/// <summary>
/// Builder of a Telegram Bot client from the tenant's effective token (SPEC-003 §17.4, CA-166).
/// Channel-specific layer on top of the shared <see cref="BotTokenClientBuilder{TClient,TCredentials}"/> skeleton:
/// channel/client type, credential group type, and construction of a <see cref="TelegramBotClient"/> from the token.
/// </summary>
internal sealed class TelegramClientBuilder : BotTokenClientBuilder<ITelegramBotClient, TelegramTenantCredentials>
{
    /// <summary>
    /// Creates the Telegram client builder over the canonical resolver.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="logger">Logger of the builder.</param>
    public TelegramClientBuilder(IConfigurationResolver resolver, ILogger<TelegramClientBuilder> logger)
        : base(resolver, logger)
    {
    }

    /// <inheritdoc />
    public override string ChannelType => ChannelTypes.Telegram;

    /// <inheritdoc />
    protected override ConfigKey<TelegramTenantCredentials> CredentialsKey => TelegramConfigKeys.Credentials;

    /// <inheritdoc />
    protected override string ChannelDisplayName => "Telegram";

    /// <inheritdoc />
    protected override string? GetToken(TelegramTenantCredentials credentials) => credentials.BotToken;

    /// <inheritdoc />
    protected override ITelegramBotClient CreateClient(string token) => new TelegramBotClient(token);
}
