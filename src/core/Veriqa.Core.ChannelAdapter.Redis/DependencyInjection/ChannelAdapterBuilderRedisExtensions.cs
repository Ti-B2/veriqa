// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using StackExchange.Redis;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Email.Services;
using Veriqa.Core.ChannelAdapter.Redis;
using Veriqa.Core.ChannelAdapter.Redis.Stores;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>Redis channel store registration extensions for <see cref="ChannelAdapterBuilder"/>.</summary>
public static class ChannelAdapterBuilderRedisExtensions
{
    /// <summary>
    /// Uses Redis-backed channel stores (prompt coordinates, Email action tokens and Email push
    /// correlations) instead of the in-process defaults, so a magic link opened on another replica
    /// still resolves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supported extension point: the implementation is complete, but no host in this repository
    /// wires it (hosts keep the in-process defaults). Its degradation is therefore not tracked by CI
    /// — validate it in the consuming host when you enable it.
    /// </para>
    /// <para>
    /// The three ports are registered unconditionally, so the result does not depend on whether this
    /// call comes before or after <c>AddEmail()</c>: the shipped in-process defaults are registered
    /// with <c>TryAddSingleton</c>, and a <c>TryAdd</c> here would lose to them and silently leave
    /// the deployment on in-process stores. The multiplexer, by contrast, is registered with
    /// <c>TryAddSingleton</c> — the Transaction Engine Redis satellite registers one too, and a
    /// second unconditional registration would shadow it. Which of the two multiplexer registrations
    /// ends up winning is judged when the host starts, so a conflicting connection string is caught
    /// whichever of the two calls came first.
    /// </para>
    /// </remarks>
    /// <param name="adapters">Channel adapter builder.</param>
    /// <param name="configure">Action configuring the store parameters.</param>
    /// <returns>Builder for chaining.</returns>
    public static ChannelAdapterBuilder UseRedisChannelStores(
        this ChannelAdapterBuilder adapters,
        Action<RedisChannelStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(configure);

        adapters.Services.Configure(configure);

        var multiplexerAlreadyRegistered =
            RedisChannelStoreConnectionSource.FindEffectiveRegistration(adapters.Services) is not null;

        // TryAdd, not Add: the Transaction Engine Redis satellite registers the same non-keyed
        // singleton, and taking it over would repoint the engine's own store at another connection.
        adapters.Services.TryAddSingleton<IConnectionMultiplexer>(serviceProvider =>
        {
            var storeOptions = serviceProvider
                .GetRequiredService<IOptions<RedisChannelStoreOptions>>()
                .Value;

            // This factory only runs when no other registration won, so an empty connection string
            // here means there is nothing to connect to. The check lives here as well as in the
            // start-up validator because the container resolves the multiplexer while the hosted
            // services are being constructed — before the validator runs — and the Redis client
            // would otherwise report a bare "is empty" argument error.
            if (string.IsNullOrWhiteSpace(storeOptions.Configuration))
            {
                throw new InvalidOperationException(
                    RedisChannelStoreConnectionSource.MissingConnectionMessage);
            }

            return ConnectionMultiplexer.Connect(storeOptions.Configuration);
        });

        // The registration just appended is this package's own — unless one was already there, in
        // which case TryAdd added nothing. Which registration ultimately wins is decided at start-up,
        // by the validator: the engine satellite may still register its own multiplexer after this
        // call and take the resolution over.
        var ownRegistration = multiplexerAlreadyRegistered
            ? null
            : RedisChannelStoreConnectionSource.FindEffectiveRegistration(adapters.Services);

        adapters.Services.TryAddSingleton(
            new RedisChannelStoreConnectionSource(adapters.Services, ownRegistration));

        // Unconditional: the in-process defaults are TryAdd-registered, so this call must win
        // regardless of the order it appears in relative to AddEmail().
        adapters.Services.AddSingleton<IChannelPromptMessageStore, RedisChannelPromptMessageStore>();
        adapters.Services.AddSingleton<IEmailActionTokenStore, RedisEmailActionTokenStore>();
        adapters.Services.AddSingleton<IEmailPushCorrelationStore, RedisEmailPushCorrelationStore>();

        adapters.Services.AddHostedService<RedisChannelStoreStartupValidator>();

        return adapters;
    }
}
