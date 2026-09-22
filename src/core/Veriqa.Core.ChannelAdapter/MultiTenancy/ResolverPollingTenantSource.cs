// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Default source of active polling tenants (SPEC-003 §17.4, CA-171/CA-172): degenerate N=1.
/// Returns exactly one implicit default tenant (<c>null</c>) if the channel is enabled for the
/// default tenant (the <c>channels_enabled</c> capability) and its <c>UpdateMode</c> = Polling;
/// otherwise — an empty set (equivalent to today's "polling not started"). This is the same code path
/// as multi-tenant: no <c>if cloud/onprem</c>. Whether a channel polls at all is the channel's own
/// declaration (<see cref="CoreChannelDeclaration.UsesPolling"/>), so a channel with no polling
/// transport — and a channel the product does not ship — needs no branch here. The Cloud profile
/// replaces the implementation opt-in behind the NuGet boundary; this class does not change in that
/// case (anti-fork).
/// </summary>
internal sealed class ResolverPollingTenantSource : IPollingTenantSource
{
    /// <summary>
    /// Default implicit tenant (N=1): a set of a single <c>null</c> element.
    /// </summary>
    private static readonly IReadOnlyCollection<string?> DefaultTenantSet = new string?[] { null };

    /// <summary>
    /// Empty set (polling is not active for the default tenant).
    /// </summary>
    private static readonly IReadOnlyCollection<string?> EmptyTenantSet = Array.Empty<string?>();

    /// <summary>
    /// Canonical layer resolver (source of the <c>channels_enabled</c> capability — the gate of
    /// channel availability to the tenant).
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Core-level facts declared by every registered channel (source of the default tenant's transport mode).
    /// </summary>
    private readonly IEnumerable<CoreChannelDeclaration> _channels;

    /// <summary>
    /// Creates the default polling tenant source on top of the canonical resolver and the channels'
    /// own declarations.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="channels">Core-level facts declared by every registered channel.</param>
    public ResolverPollingTenantSource(
        IConfigurationResolver resolver,
        IEnumerable<CoreChannelDeclaration> channels)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyCollection<string?>>> GetActivePollingTenantsAsync(
        string channelType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelType);

        // The channel itself says whether it pulls updates. A channel that never declared itself here
        // (an unregistered one, or one whose transport is webhook-only) does not poll.
        var isPollingActive = _channels.Any(channel =>
            string.Equals(channel.ChannelType, channelType, StringComparison.Ordinal)
            && channel.UsesPolling);

        // The default tenant is active for polling only if the channel is allowed for it (channels_enabled)
        // AND its UpdateMode = Polling. N=1: the single implicit tenant is null.
        var isEnabled = isPollingActive
            && await _resolver.IsChannelEnabledAsync(null, channelType, cancellationToken);

        return Result<IReadOnlyCollection<string?>>.Success(isEnabled ? DefaultTenantSet : EmptyTenantSet);
    }
}
