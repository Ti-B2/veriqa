// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Channel client cache key <c>(tenant, ChannelType, client type)</c>: the client type is the type
/// parameter of the key itself, so two keys of different client types never compare equal even with
/// the same tenant and channel. That keeps the shared negative cache (<c>IMemoryCache</c>, one instance
/// for all client types) partitioned by client type without storing a <see cref="Type"/> in the key.
/// </summary>
/// <typeparam name="TClient">Channel client type the key belongs to.</typeparam>
/// <param name="TenantId">Tenant identifier (null — the default implicit tenant).</param>
/// <param name="ChannelType">Channel type.</param>
internal readonly record struct ChannelClientCacheKey<TClient>(string? TenantId, string ChannelType)
    where TClient : class;
