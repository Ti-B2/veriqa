// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Everything the client factory keeps per one client type (SPEC-003 §17.4, CA-166): the index of the
/// builders registered for <typeparamref name="TClient"/> and the per-tenant cache of the clients built
/// from them. Registered as an open generic singleton, so the index and the cache of a given client type
/// are built once at its first use and live for the process lifetime.
/// </summary>
/// <remarks>
/// A non-generic factory cannot take the builders of an arbitrary client type through its constructor,
/// so this type is the place where they arrive by plain constructor injection: the container resolves
/// the closed generic <c>IChannelClientBuilder&lt;TClient&gt;</c> — that is, the container itself is the
/// index by client type, and no builder declares that type on its own.
/// </remarks>
/// <typeparam name="TClient">Channel client type (e.g. <c>ITelegramBotClient</c>).</typeparam>
internal sealed class ChannelClientRegistry<TClient>
    where TClient : class
{
    /// <summary>
    /// Builders of <typeparamref name="TClient"/> keyed by channel type.
    /// </summary>
    private readonly IReadOnlyDictionary<string, IChannelClientBuilder<TClient>> _builders;

    /// <summary>
    /// Creates the registry over the builders registered for this client type.
    /// </summary>
    /// <param name="builders">Registered builders of <typeparamref name="TClient"/>.</param>
    public ChannelClientRegistry(IEnumerable<IChannelClientBuilder<TClient>> builders)
    {
        ArgumentNullException.ThrowIfNull(builders);

        // Index the builders by channel type; on collision the last one wins.
        var map = new Dictionary<string, IChannelClientBuilder<TClient>>(StringComparer.Ordinal);
        foreach (var builder in builders)
        {
            map[builder.ChannelType] = builder;
        }

        _builders = map;
    }

    /// <summary>
    /// Cache of the built clients keyed by <c>(tenant, ChannelType)</c> within this client type.
    /// Thread-safe: concurrent requests for the same key get a single instance without a race.
    /// </summary>
    public ConcurrentDictionary<ChannelClientCacheKey<TClient>, TClient> Clients { get; } = new();

    /// <summary>
    /// Client type name for the "no builder" diagnostic message.
    /// </summary>
    public string ClientTypeName { get; } = typeof(TClient).Name;

    /// <summary>
    /// Returns the builder registered for the channel, or <c>null</c> when the channel does not resolve
    /// to a client of this type.
    /// </summary>
    /// <param name="channelType">Channel type.</param>
    /// <returns>The builder, or <c>null</c>.</returns>
    public IChannelClientBuilder<TClient>? FindBuilder(string channelType)
        => _builders.GetValueOrDefault(channelType);
}
