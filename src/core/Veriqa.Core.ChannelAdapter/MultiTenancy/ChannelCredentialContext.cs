// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Resolution key of channel credentials and the channel client (SPEC-003 §17.4, CA-162/CA-166).
/// Carries the tenant identifier and the channel type. <see cref="TenantId"/> = null means
/// the default implicit tenant (self-hosted N=1, CFG-202): credentials are taken from the global
/// core-level <c>IOptions</c>. The type is a resolution layer, not adapter state: the adapter
/// remains a stateless singleton (CA-014), the tenant is fed in as a key.
/// <para>
/// The constructor is closed and the tenant is chosen by a factory, so the whole product has ONE
/// place that answers "credentials of which tenant": <see cref="ForCurrentTenant"/>, reading the
/// ambient tenant that whoever legitimately knows it has stated through the public
/// <see cref="ChannelTenantContext.BeginScope"/>. Naming a tenant next to a credential key without
/// going through a scope is <see cref="ForTenant"/> — internal, kept for the paths inside this tree
/// that hold the tenant of their own work and run outside an ambient scope; which paths those are is
/// not restated here (the examples live with <see cref="FromAmbient"/>, in one place, rather than as
/// copies). Its being internal buys no isolation: a scope plus <see cref="ForCurrentTenant"/>
/// reaches any tenant just as well, and an in-process adapter has the whole configuration within
/// reach anyway. It is protection from a mistake — reading someone else's credentials is not the
/// path of least resistance — while WHERE a tenant may legitimately be stated is settled by
/// <see cref="ChannelTenantContext.BeginScope"/>, not here.
/// </para>
/// </summary>
public sealed class ChannelCredentialContext
{
    /// <summary>
    /// Creates a channel credential resolution context.
    /// </summary>
    /// <param name="channelType">Channel type (telegram/whatsapp/max/email from <c>ChannelTypes</c>).</param>
    /// <param name="tenantId">Tenant identifier; null — the default implicit tenant (self-hosted).</param>
    /// <param name="fromAmbient">Whether the tenant was taken from the ambient flow.</param>
    private ChannelCredentialContext(string channelType, string? tenantId, bool fromAmbient)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelType);

        ChannelType = channelType;
        TenantId = tenantId;
        FromAmbient = fromAmbient;
    }

    /// <summary>
    /// Context for the tenant of the current processing flow — the only place the ambient tenant is
    /// turned into a credential resolution key.
    /// </summary>
    /// <param name="channelType">Channel type (from <c>ChannelTypes</c>).</param>
    /// <returns>Context keyed on the ambient tenant (null — the default implicit tenant).</returns>
    public static ChannelCredentialContext ForCurrentTenant(string channelType) =>
        new(channelType, ChannelTenantContext.CurrentTenantId, fromAmbient: true);

    /// <summary>
    /// Context for an explicitly named tenant — for the in-tree paths that hold the tenant of the
    /// work they are doing and run outside an ambient scope, instead of reading it from the flow.
    /// Telling such a path apart from one that merely forgot to open a scope is
    /// <see cref="FromAmbient"/>, which is also where those paths are named.
    /// </summary>
    /// <param name="channelType">Channel type (from <c>ChannelTypes</c>).</param>
    /// <param name="tenantId">Tenant identifier; null — the default implicit tenant (self-hosted).</param>
    /// <returns>Context keyed on the named tenant.</returns>
    internal static ChannelCredentialContext ForTenant(string channelType, string? tenantId) =>
        new(channelType, tenantId, fromAmbient: false);

    /// <summary>
    /// Tenant identifier. Null = the default implicit tenant (self-hosted/core).
    /// </summary>
    public string? TenantId { get; }

    /// <summary>
    /// Channel type for which credentials/the client are resolved.
    /// </summary>
    public string ChannelType { get; }

    /// <summary>
    /// Whether the tenant of this context was read from the ambient flow
    /// (<see cref="ForCurrentTenant"/>) rather than named explicitly (<see cref="ForTenant"/>).
    /// </summary>
    /// <remarks>
    /// Only an ambient-derived key can silently land on the default tenant because nobody opened a
    /// scope, so this is what tells a forgotten scope apart from a path that legitimately names its
    /// tenant itself: inside this tree those are the webhook initializers, naming the default tenant
    /// at startup, and the polling loops, naming the tenant of the current iteration. Resolution
    /// reports the former and stays silent about the latter — see <c>ChannelCredentialResolution</c>.
    /// An adapter's health probe is neither: it reads the ambient tenant, but under a scope it opened
    /// itself, so <see cref="ChannelTenantContext.IsEstablished"/> keeps it out of the report.
    /// </remarks>
    internal bool FromAmbient { get; }
}
