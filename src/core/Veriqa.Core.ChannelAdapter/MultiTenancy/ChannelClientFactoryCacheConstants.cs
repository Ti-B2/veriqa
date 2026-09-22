// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Constants of the channel client factory negative cache (TASK-050 #9, anti-magic core-rules §6).
/// Only the code of a failed client/credential resolution outcome is cached, keyed by
/// <c>(TenantId, ChannelType, ClientType)</c> — not the message and not the payload (core-rules §10).
/// Invalidation — by TTL (TASK-050 §2 item 4) and on a successful build of the same key.
/// </summary>
internal static class ChannelClientFactoryCacheConstants
{
    /// <summary>
    /// TTL of a negative cache entry (seconds). Short: a tenant's newly appeared credentials are
    /// picked up no later than the TTL elapses (a deliberate latency-vs-freshness trade-off, TASK-050 §2 item 4).
    /// </summary>
    public const int NegativeCacheTtlSeconds = 45;
}
