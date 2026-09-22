// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;

using StackExchange.Redis;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Tells whether the <c>IConnectionMultiplexer</c> the channel stores will actually receive is the
/// one this package registered, or someone else's.
/// </summary>
/// <remarks>
/// <para>
/// The answer is computed from the registrations at the moment it is asked — at host start-up, when
/// every module has run — and not snapshotted when <c>UseRedisChannelStores</c> executed. The
/// Transaction Engine Redis satellite registers a non-keyed <c>IConnectionMultiplexer</c>
/// unconditionally and may do so <em>after</em> this package: its registration is then the last one
/// and wins the resolution, while a snapshot taken during registration would still claim this
/// package owns the connection.
/// </para>
/// <para>
/// The effective registration is the last non-keyed one, which is what a resolve of a single service
/// returns. Keyed registrations do not count: the host keeps its DataProtection multiplexer under a
/// key and hands the instance to <c>PersistKeysToStackExchangeRedis</c> directly, so it never takes
/// part in this resolution.
/// </para>
/// </remarks>
internal sealed class RedisChannelStoreConnectionSource
{
    /// <summary>
    /// Message of the "nothing to connect to" configuration error. It is raised from two places —
    /// the start-up validator and the connection factory itself — because the container may resolve
    /// the multiplexer while the hosted services are being constructed, which happens before the
    /// validator gets to speak. Without it that path fails with a bare "is empty (Parameter
    /// 'configuration')" from the Redis client.
    /// </summary>
    public const string MissingConnectionMessage =
        "Redis channel stores: RedisChannelStoreOptions.Configuration is empty and no other "
        + "IConnectionMultiplexer is registered in the container, so there is no Redis to connect to. "
        + "Set the connection string, or register the multiplexer in the container.";

    /// <summary>
    /// The service collection the host was configured with — the registrations are read from it at
    /// start-up rather than at registration time.
    /// </summary>
    private readonly IServiceCollection _services;

    /// <summary>
    /// The multiplexer registration this package added, or null when it added none because one was
    /// already there.
    /// </summary>
    private readonly ServiceDescriptor? _ownRegistration;

    /// <summary>
    /// Creates the connection source.
    /// </summary>
    /// <param name="services">Service collection of the host.</param>
    /// <param name="ownRegistration">Multiplexer registration added by this package, if any.</param>
    public RedisChannelStoreConnectionSource(
        IServiceCollection services,
        ServiceDescriptor? ownRegistration)
    {
        _services = services;
        _ownRegistration = ownRegistration;
    }

    /// <summary>
    /// Whether the multiplexer the stores will resolve comes from a registration other than this
    /// package's.
    /// </summary>
    /// <returns>true — someone else's connection wins.</returns>
    public bool ForeignConnectionWins()
    {
        return !ReferenceEquals(FindEffectiveRegistration(_services), _ownRegistration);
    }

    /// <summary>
    /// Finds the multiplexer registration that a resolve returns: the last non-keyed one.
    /// </summary>
    /// <param name="services">Service collection to inspect.</param>
    /// <returns>The effective registration, or null when nothing is registered.</returns>
    public static ServiceDescriptor? FindEffectiveRegistration(IServiceCollection services)
    {
        ServiceDescriptor? effective = null;

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IConnectionMultiplexer) && !descriptor.IsKeyedService)
            {
                effective = descriptor;
            }
        }

        return effective;
    }
}
