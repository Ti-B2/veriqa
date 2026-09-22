// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Constants of the per-tenant polling supervisor (TASK-051 anti-magic core-rules §6).
/// Shared by Telegram and MAX (the supervisor mechanics are the same; loop bodies differ).
/// </summary>
internal static class PollingSupervisorConstants
{
    /// <summary>
    /// Interval for re-querying <see cref="IPollingTenantSource"/> to react to tenant set changes
    /// without a restart (seconds). Order of magnitude — tens of seconds: tenant set dynamics are
    /// picked up no later than the interval elapses (a deliberate latency-vs-source-load trade-off).
    /// In self-hosted (stable <c>[null]</c>) rescanning yields an empty delta and produces no work.
    /// </summary>
    public const int RescanIntervalSeconds = 30;
}
