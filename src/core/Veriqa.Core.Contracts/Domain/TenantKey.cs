// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// How a tenant is spelled inside a composite key — the rate-limiter partition, the channel-identity
/// record. One place, because the two mechanisms must agree: a counter and an identity record of the
/// same user have to belong to the same tenant, and two spellings of the default tenant would split
/// them apart without any failure to observe.
/// </summary>
public static class TenantKey
{
    /// <summary>
    /// The single spelling of the tenant that was never stated — the default implicit tenant of a
    /// self-hosted installation. The angle brackets keep it out of the space of real identifiers,
    /// which come from route segments and configuration keys.
    /// </summary>
    public const string Default = "<default>";

    /// <summary>
    /// Turns a stated-or-not tenant into its key segment.
    /// </summary>
    /// <param name="tenantId">Tenant identifier; null, empty or whitespace — none was stated.</param>
    /// <returns><paramref name="tenantId"/>, or <see cref="Default"/> when none was stated.</returns>
    public static string Segment(string? tenantId)
        => string.IsNullOrWhiteSpace(tenantId) ? Default : tenantId;
}
