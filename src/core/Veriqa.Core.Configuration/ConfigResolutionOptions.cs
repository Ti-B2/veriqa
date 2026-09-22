// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Settings of the resolver's caching and degradation layer (<see cref="ConfigResolutionCache"/>).
/// </summary>
public sealed class ConfigResolutionOptions
{
    /// <summary>
    /// Declared growth bound of the last-good store, in entries. The store is deliberately not
    /// subject to memory-pressure eviction — a record that disappears exactly when a source breaks is
    /// worse than useless — so its size is bounded explicitly instead. One entry per
    /// (key, level, tenant, level scope); on overflow the oldest entry is evicted and the fact is
    /// logged at Warning.
    /// </summary>
    public int LastGoodCapacity { get; set; } = 1024;

    /// <summary>
    /// Maximum age of a last-good value of a key declared secret (<see cref="ConfigKey{T}.IsSecret"/>).
    /// Past that age a failed read yields "source unavailable" instead of the previous value: working
    /// indefinitely on a possibly revoked secret is more dangerous than refusing. The default is of
    /// the same order as the credentials cache TTL — minutes, not hours: beyond that order a
    /// revocation stops reaching a running process at all.
    /// </summary>
    public TimeSpan SecretLastGoodMaxAge { get; set; } = TimeSpan.FromMinutes(5);
}
