// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Builder for configuring channel adapters.
/// Allows enabling the desired channels via a fluent API.
/// </summary>
public sealed class ChannelAdapterBuilder
{
    /// <summary>
    /// Service collection the builder registers into. Exposed for ecosystem packages that add their
    /// own registrations through an extension method on this builder; not part of the everyday
    /// configuration surface.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection Services { get; }

    /// <summary>
    /// Application configuration.
    /// </summary>
    internal IConfiguration Configuration { get; }

    /// <summary>
    /// Whether at least one adapter has been registered.
    /// </summary>
    internal bool HasAdapters { get; private set; }

    /// <summary>
    /// Registry of the registered channels (public SPI); created on the first
    /// <c>AddChannel</c> call and registered in DI once.
    /// </summary>
    private CustomChannelRegistry? _channels;

    /// <summary>
    /// Creates the channel adapter builder.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    internal ChannelAdapterBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
    }

    /// <summary>
    /// Shared channel registration skeleton (CA-083): configuration section binding,
    /// validator, and ValidateOnStart. Deduplicates the copy-pasted
    /// <c>Configure + IValidateOptions + AddOptionsWithValidateOnStart</c> from the channel methods.
    /// Channel-specific registrations (client/provider/adapter/hosted) stay in the channel method.
    /// </summary>
    /// <typeparam name="TOptions">Channel Options type.</typeparam>
    /// <typeparam name="TValidator">Channel Options validator type.</typeparam>
    /// <param name="sectionName">Channel configuration section name.</param>
    internal void ConfigureChannelOptions<TOptions, TValidator>(string sectionName)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        // Binding of the Veriqa:Channels:{Type}:* section — flat (invariant-1).
        Services.Configure<TOptions>(Configuration.GetSection(sectionName));

        // Validator + ValidateOnStart — a single skeleton for all channels.
        Services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
        Services.AddOptionsWithValidateOnStart<TOptions>();
    }

    /// <summary>
    /// Shared channel-enabled check: reads <c>Enabled</c> from the section.
    /// A single helper instead of duplicated private <c>IsXxxEnabled()</c> per channel.
    /// </summary>
    /// <typeparam name="TOptions">Channel Options type.</typeparam>
    /// <param name="sectionName">Channel configuration section name.</param>
    /// <param name="isEnabled">Enabled-flag selector (the channel's semantics are preserved).</param>
    /// <returns><c>true</c> if the channel is enabled in the configuration.</returns>
    internal bool IsChannelEnabled<TOptions>(string sectionName, Func<TOptions, bool> isEnabled)
        where TOptions : class
    {
        var options = Configuration.GetSection(sectionName).Get<TOptions>();
        return options is not null && isEnabled(options);
    }

    /// <summary>
    /// Declares the core-level facts of a channel (<see cref="CoreChannelDeclaration"/>) off its own
    /// options: the channel contour reads them from this declaration rather than from each channel's
    /// named option type.
    /// Declared before the enabled check on purpose — a disabled channel still belongs to the schema
    /// of the deployment, it is simply not in the available set.
    /// </summary>
    /// <typeparam name="TOptions">Channel Options type.</typeparam>
    /// <param name="channelType">Channel type the declaration speaks for.</param>
    /// <param name="isEnabled">Reads the channel's <c>Enabled</c> flag off its options.</param>
    /// <param name="usesPolling">
    /// Reads whether the channel pulls updates itself off its options; <c>null</c> — the channel has
    /// no polling transport at all.
    /// </param>
    internal void DeclareCoreChannel<TOptions>(
        string channelType,
        Func<TOptions, bool> isEnabled,
        Func<TOptions, bool>? usesPolling = null)
        where TOptions : class
    {
        // The flags are read through two different accessors on purpose:
        // IOptionsMonitor for the availability set, which follows a live reload of the section, and
        // IOptions for the transport mode, which is a start-up decision.
        Services.AddSingleton(provider => new CoreChannelDeclaration(
            channelType,
            () => isEnabled(provider.GetRequiredService<IOptionsMonitor<TOptions>>().CurrentValue),
            usesPolling is null
                ? null
                : () => usesPolling(provider.GetRequiredService<IOptions<TOptions>>().Value)));
    }

    /// <summary>
    /// Registers a channel adapter (public SPI) — the single registration path, taken by the channels
    /// shipped with the product and by a third-party one alike.
    /// The adapter is registered as a singleton <see cref="IChannelAdapter"/> implementation, so it
    /// shows up in every core consumer of <c>IEnumerable&lt;IChannelAdapter&gt;</c> (sign-in window,
    /// webhook pipeline).
    /// </summary>
    /// <typeparam name="TAdapter">Adapter type implementing <see cref="IChannelAdapter"/>.</typeparam>
    /// <param name="channelType">
    /// Channel type of the adapter. Must match <see cref="CustomChannelConstants.ChannelTypePattern"/>
    /// (<c>\A[a-z][a-z0-9-]{0,63}\z</c>) and equal the value returned by the adapter's
    /// <see cref="IChannelAdapter.ChannelType"/>. A violation or a duplicate registration fails fast
    /// at startup.
    /// </param>
    /// <param name="mapWebhook">
    /// <c>true</c> (default) — map the generic webhook endpoint
    /// <c>POST /api/channels/{channelType}/webhook</c> (plus the optional tenant-segment variant)
    /// for this channel; <c>false</c> — register an outbound/polling-only channel without a webhook.
    /// </param>
    /// <returns>Builder for call chaining.</returns>
    public ChannelAdapterBuilder AddChannel<TAdapter>(string channelType, bool mapWebhook = true)
        where TAdapter : class, IChannelAdapter
        => AddChannel<TAdapter>(channelType, new ChannelRegistrationOptions { MapWebhook = mapWebhook });

    /// <summary>
    /// Registers a channel adapter with the full set of registration settings (public SPI) — status
    /// texts of its own and the platform's verification handshake.
    /// </summary>
    /// <typeparam name="TAdapter">Adapter type implementing <see cref="IChannelAdapter"/>.</typeparam>
    /// <param name="channelType">Channel type of the adapter (see the overload above).</param>
    /// <param name="options">Registration settings of the channel.</param>
    /// <returns>Builder for call chaining.</returns>
    public ChannelAdapterBuilder AddChannel<TAdapter>(string channelType, ChannelRegistrationOptions options)
        where TAdapter : class, IChannelAdapter
    {
        RegisterChannel(channelType, typeof(TAdapter), options);
        Services.AddSingleton<IChannelAdapter, TAdapter>();

        HasAdapters = true;
        return this;
    }

    /// <summary>
    /// Registers a channel adapter created by a factory (public SPI) — for adapters that
    /// need explicit construction (own options, own clients). Semantics are identical to
    /// <see cref="AddChannel{TAdapter}(string, bool)"/>.
    /// </summary>
    /// <typeparam name="TAdapter">Adapter type implementing <see cref="IChannelAdapter"/>.</typeparam>
    /// <param name="channelType">Channel type of the adapter (see the overload above).</param>
    /// <param name="adapterFactory">Adapter factory resolved from the service provider.</param>
    /// <param name="mapWebhook">Whether to map the generic webhook endpoint (see the overload above).</param>
    /// <returns>Builder for call chaining.</returns>
    public ChannelAdapterBuilder AddChannel<TAdapter>(
        string channelType,
        Func<IServiceProvider, TAdapter> adapterFactory,
        bool mapWebhook = true)
        where TAdapter : class, IChannelAdapter
        => AddChannel(channelType, adapterFactory, new ChannelRegistrationOptions { MapWebhook = mapWebhook });

    /// <summary>
    /// Registers a channel adapter created by a factory, with the full set of registration settings
    /// (public SPI).
    /// </summary>
    /// <typeparam name="TAdapter">Adapter type implementing <see cref="IChannelAdapter"/>.</typeparam>
    /// <param name="channelType">Channel type of the adapter (see the overload above).</param>
    /// <param name="adapterFactory">Adapter factory resolved from the service provider.</param>
    /// <param name="options">Registration settings of the channel.</param>
    /// <returns>Builder for call chaining.</returns>
    public ChannelAdapterBuilder AddChannel<TAdapter>(
        string channelType,
        Func<IServiceProvider, TAdapter> adapterFactory,
        ChannelRegistrationOptions options)
        where TAdapter : class, IChannelAdapter
    {
        ArgumentNullException.ThrowIfNull(adapterFactory);

        RegisterChannel(channelType, typeof(TAdapter), options);
        Services.AddSingleton<IChannelAdapter>(adapterFactory);

        HasAdapters = true;
        return this;
    }

    /// <summary>
    /// Validates the channel type and records it in the registry shared with the webhook
    /// pipeline. The registry is registered in DI once, on the first registered channel.
    /// </summary>
    /// <param name="channelType">Channel type of the adapter.</param>
    /// <param name="adapterType">Adapter type being registered for this channel type.</param>
    /// <param name="options">Registration settings of the channel.</param>
    private void RegisterChannel(string channelType, Type adapterType, ChannelRegistrationOptions options)
    {
        if (_channels is null)
        {
            // The registry is one per IServiceCollection, not one per builder: AddChannelAdapters /
            // AddVeriqaChannelAdapters may be called more than once (host + extension library), and a
            // second registry would shadow the first in DI — the routes of the first builder would
            // silently never be mapped, and its channel types would escape the collision checks.
            // So an already registered instance is reused.
            _channels = FindRegisteredRegistry();

            if (_channels is null)
            {
                _channels = new CustomChannelRegistry();
                Services.AddSingleton(_channels);

                // The registration ↔ adapter cross-check is bound to the host start, not to the
                // webhook mapping: an outbound/polling-only channel never maps a webhook and would
                // otherwise never be checked. A hosted lifecycle service (not an IStartupFilter) so it
                // also runs in a non-web host, still failing fast before the first request.
                Services.AddHostedService<CustomChannelStartupValidator>();
            }
        }

        // Validation and collision detection live in the registry — it fails fast at startup.
        _channels.Register(channelType, adapterType, options);
    }

    /// <summary>
    /// Returns the channel registry already registered in the service collection
    /// by an earlier <c>AddChannel</c> call (possibly from another builder), or null.
    /// </summary>
    /// <returns>The registry instance registered in DI, or null.</returns>
    private CustomChannelRegistry? FindRegisteredRegistry()
    {
        foreach (var descriptor in Services)
        {
            if (descriptor.ServiceType == typeof(CustomChannelRegistry)
                && descriptor.ImplementationInstance is CustomChannelRegistry registry)
            {
                return registry;
            }
        }

        return null;
    }
}
