// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Reading of the <c>channels_enabled</c> capability (SPEC-003 §17.7, SPEC-012 §10.6) — the set of
/// channels available to a layer. An axis separate from credentials: a channel can be in the set
/// without credentials and vice versa.
/// <para>
/// A helper and deliberately not a seam: the narrowing this capability is about (CFG-211) is stated
/// by the KEY's own declaration — Set semantics, which levels participate, whether core is a ceiling
/// — and is applied by the canonical resolver. A substitutable interface here would add nothing to
/// that and would let an answer bypass it (CA-170, anti-fork).
/// </para>
/// </summary>
internal static class ChannelsEnabledResolution
{
    /// <summary>
    /// Checks whether the channel is available to the tenant (whether it is in <c>channels_enabled</c>).
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="tenantId">Tenant identifier; null — the default implicit tenant (self-hosted).</param>
    /// <param name="channelType">Channel type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the channel is available to this layer.</returns>
    internal static async ValueTask<bool> IsChannelEnabledAsync(
        this IConfigurationResolver resolver,
        string? tenantId,
        string channelType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentException.ThrowIfNullOrEmpty(channelType);

        var enabled = await resolver.ResolveAsync(
            ChannelConfigKeys.ChannelsEnabled,
            ResolutionContext.ForTenant(tenantId),
            ConfigDimensionValues.None,
            cancellationToken);

        // No level defined a set — nothing is available (the empty set, not "everything").
        return enabled.Value?.Contains(channelType, StringComparer.Ordinal) == true;
    }
}
