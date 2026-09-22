// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Caching policy of a setting value (SPEC-012 §10.6). The decision "is this level cached at all"
/// belongs to whoever HOLDS the record — the reader of the level, and through it the store behind it
/// (<see cref="IConfigRecordReader.IsLive"/>); a live one is never cached, because its freshness is
/// the configuration provider's job. This record only carries the parameters of the cache ENTRY, for
/// the deployments where the level turns out not to be live.
/// <para>
/// <b>A declared lifetime comes with a named trade-off.</b> The resolver performs no invalidation:
/// <see cref="IConfigResolutionCache.ForgetAsync"/> exists and is called from nowhere, because there
/// is nowhere to call it from — no code path writes a record of an external store today. A record
/// changed outside the store therefore reaches a page no sooner than the lifetime expires. The debt
/// is repaid by the work that introduces such a write (an editor of the records), which is also where
/// the invalidation call belongs.
/// </para>
/// </summary>
public sealed record ConfigCachePolicy
{
    /// <summary>
    /// No caching (the default of every key that did not ask for it).
    /// </summary>
    public static ConfigCachePolicy None { get; } = new();

    /// <summary>
    /// Lifetime a key served by an EXTERNAL record store is cached for by default. It is short on
    /// purpose: there is no invalidation, so this number is also the longest a change made outside the
    /// store stays invisible — and it is the whole window within which repeated assemblies of a page
    /// stop going to the store.
    /// </summary>
    public static TimeSpan DefaultExternalStoreTtl { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Policy of a key whose levels may be served by an external record store: cached for
    /// <see cref="DefaultExternalStoreTtl"/> where the store is not live, and not cached at all where
    /// it is — the store's answer decides, and the key's policy does not overrule it.
    /// </summary>
    public static ConfigCachePolicy ExternalStore { get; } = new() { Ttl = DefaultExternalStoreTtl };

    /// <summary>
    /// Lifetime of a cache entry; <c>null</c> — do not cache.
    /// </summary>
    public TimeSpan? Ttl { get; init; }

    /// <summary>
    /// <c>true</c> — the value must not leave the memory of the process (secrets: decrypted tenant
    /// credentials). It governs the ROUTE of a cache write and nothing else; the nature of the value
    /// is declared by <see cref="ConfigKey{T}.IsSecret"/> instead.
    /// </summary>
    public bool LocalOnly { get; init; }
}
