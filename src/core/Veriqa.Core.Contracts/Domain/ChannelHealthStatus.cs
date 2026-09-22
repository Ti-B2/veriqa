// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// Channel health status (SPEC-003 §6.4).
/// </summary>
/// <param name="IsHealthy">Whether the channel is available.</param>
/// <param name="ChannelType">Channel type.</param>
/// <param name="ResponseTime">Channel response time.</param>
/// <param name="Details">Additional state details.</param>
/// <param name="CheckedAt">Check time (UTC).</param>
public sealed record ChannelHealthStatus(
    bool IsHealthy,
    string ChannelType,
    TimeSpan ResponseTime,
    string? Details,
    DateTimeOffset CheckedAt);
