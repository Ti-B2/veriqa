// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Port of the caching and degradation layer owned by the resolver (SPEC-012 §10.6, CFG-233). The
/// resolver depends on this contract and not on a cache primitive: which primitive is behind it is a
/// matter of DI registration, so moving to a distributed one costs a registration rather than an edit
/// of the resolver.
/// <para>
/// The port is deliberately minimal — get-or-create, remember the last valid value, serve it, forget
/// an entry — and it carries the degradation branch explicitly, which no ready-made cache contract
/// does. Every member is asynchronous: which primitive stands behind the port is a registration, and
/// a distributed one is async-only.
/// </para>
/// <para>
/// The two triggers do not overlap: the cache answers a FRESH read of a cacheable level, the
/// last-good store answers only a read that FAILED.
/// </para>
/// <para>
/// The port is PUBLIC because it is declared replaceable: a port that cannot be implemented outside
/// the assembly that declares it is replaceable only on paper. The primitive behind it is chosen at
/// the single point of registration — the builder of
/// <c>AddVeriqaConfigurationResolver</c> — and nowhere else (SPEC-012 §10.6, CFG-235).
/// </para>
/// </summary>
public interface IConfigResolutionCache
{
    /// <summary>
    /// Returns the cached value of a level, reading it through <paramref name="read"/> on a miss and
    /// remembering the result for the lifetime declared by the policy.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Composite key of the cache entry.</param>
    /// <param name="policy">Caching policy declared by the setting key.</param>
    /// <param name="read">Fresh read of the level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Level value.</returns>
    ValueTask<LayerValue<T>> GetOrCreateAsync<T>(
        ConfigCacheKey key,
        ConfigCachePolicy policy,
        Func<CancellationToken, ValueTask<LayerValue<T>>> read,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remembers a value successfully read at a level, so a later failed read of the same level can
    /// still answer.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Composite key of the entry.</param>
    /// <param name="isSecret">The value of the setting is a secret.</param>
    /// <param name="layer">Value read at the level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value that was remembered.</returns>
    ValueTask<LayerValue<T>> StoreLastGoodAsync<T>(
        ConfigCacheKey key,
        bool isSecret,
        LayerValue<T> layer,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the last value successfully read at the level, or <see cref="LayerValue{T}.None"/> when
    /// there is none — or when a secret's last-good value has outlived its maximum age.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Composite key of the entry.</param>
    /// <param name="isSecret">The value of the setting is a secret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Degraded level value, or None.</returns>
    ValueTask<LayerValue<T>> GetLastGoodOrNoneAsync<T>(
        ConfigCacheKey key,
        bool isSecret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the cached value of a level. The last valid value is NOT forgotten: it exists for the
    /// case where the source has broken, which is exactly when dropping it would hurt.
    /// </summary>
    /// <param name="key">Composite key of the entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when an entry was actually forgotten.</returns>
    ValueTask<bool> ForgetAsync(ConfigCacheKey key, CancellationToken cancellationToken = default);
}
