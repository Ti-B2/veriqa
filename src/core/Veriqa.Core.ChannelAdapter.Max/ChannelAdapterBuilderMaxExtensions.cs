// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Max.BotClient;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.Max;
using Veriqa.Core.ChannelAdapter.Max.Configuration;
using Veriqa.Core.ChannelAdapter.Max.Constants;
using Veriqa.Core.ChannelAdapter.Max.Enums;
using Veriqa.Core.ChannelAdapter.Max.MultiTenancy;
using Veriqa.Core.ChannelAdapter.Max.Services;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.MessageTemplates;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Registration of the MAX channel over <see cref="ChannelAdapterBuilder"/>.
/// The namespace is the builder's own, so <c>adapters.AddMax()</c> stays the same line of code it
/// was while the channel lived in the base assembly — only the package it arrives from changed.
/// </summary>
public static class ChannelAdapterBuilderMaxExtensions
{
    /// <summary>
    /// Registers the built-in MAX channel adapter.
    /// </summary>
    /// <param name="adapters">Channel adapter builder.</param>
    /// <returns>Builder for call chaining.</returns>
    public static ChannelAdapterBuilder AddMax(this ChannelAdapterBuilder adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        // Registration skeleton (shared helper)
        adapters.ConfigureChannelOptions<MaxOptions, MaxOptionsValidator>(MaxOptions.SectionName);

        // What the channel contour has to know about MAX, said by MAX itself: its keys and the
        // core-level facts read off its options. Both are declared before the enabled check: a
        // disabled channel still belongs to the schema of the deployment.
        adapters.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRegisterConfigKeys, MaxConfigKeyRegistrar>());

        // The catalog of this channel — the declaration half of the same pair. It is registered HERE,
        // at the composition of the channel, so that the key reaches the schema of a deployment that
        // added this channel and no other: the call guards the catalog on its own, so adding the
        // channel twice still leaves one registration.
        adapters.Services.AddVeriqaConfigKeyCatalog(adapters.Configuration, MaxConfigKeys.Catalog);
        adapters.DeclareCoreChannel<MaxOptions>(
            ChannelTypes.Max,
            static options => options.Enabled,
            static options => options.UpdateMode is MaxUpdateMode.Polling);

        // If MAX is disabled, only register the options and validator
        if (!adapters.IsChannelEnabled<MaxOptions>(MaxOptions.SectionName, static o => o.Enabled))
        {
            return adapters;
        }

        // Multi-tenancy seam: per-tenant client factory + default credential provider
        adapters.Services.AddChannelMultiTenancy();

        // Named IHttpClientFactory client for the direct keyboard-clearing edit (attachments: []) that the
        // SDK cannot express. Explicit timeout instead of the default 100s, so a stuck edit on the
        // confirm/decline/expiry path fails fast. Idempotent — safe for a MAX-only setup too.
        adapters.Services.AddHttpClient(
            MaxAdapterConstants.HttpClientName,
            static client => client.Timeout = TimeSpan.FromSeconds(MaxAdapterConstants.EditMessageTimeoutSeconds));

        // Builder of the MAX Bot client from the tenant's token (replaces the single-token singleton).
        // The client type is the type parameter of the service — the factory key comes from the
        // registration, not from a property an implementation could declare wrongly.
        adapters.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IChannelClientBuilder<IBotClient>, MaxClientBuilder>());

        // Register the bot information provider. TryAddSingleton, like every shipped default of a
        // one-implementation port here: a host that registered its own provider beforehand keeps it.
        adapters.Services.TryAddSingleton<IMaxBotInfoProvider, MaxBotInfoProvider>();

        // Register the hosted services
        adapters.Services.AddHostedService<MaxWebhookInitializerService>();
        adapters.Services.AddHostedService<MaxPollingService>();

        // Register the MAX adapter through the one registration path every channel takes (singleton
        // per channel type, CA-014): the webhook route and the error status text come from the declaration.
        return adapters.AddChannel<MaxChannelAdapter>(
            ChannelTypes.Max,
            new ChannelRegistrationOptions
            {
                ErrorMessageText = MessageTemplateNaturalKeys.OutcomeErrorInChannel,

                // A sign-in link that cannot be served any more IS answered here: a message to a user
                // who already opened this chat costs the installation nothing on this platform, and
                // the alternative is the silence the user reads as a broken bot.
                StaleLinkReplyText = MessageTemplateNaturalKeys.StaleLinkReply
            });
    }
}
