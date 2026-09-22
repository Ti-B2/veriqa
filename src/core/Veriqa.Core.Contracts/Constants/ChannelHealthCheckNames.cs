// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Names under which the channel contour registers its health check.
/// They live in the contracts package for the same reason the telemetry names do
/// (<see cref="ChannelTelemetry"/>): they are part of the product's observable contract. The
/// component owns the check, while the endpoint that publishes it belongs to the host — and the
/// host can only select the check by the very tag the registration used.
/// </summary>
public static class ChannelHealthCheckNames
{
    /// <summary>
    /// Name of the registered check; it is what the report identifies the entry by.
    /// </summary>
    public const string Name = "channels";

    /// <summary>
    /// Tag of the registered check. Deliberately a tag of its own rather than the readiness tag:
    /// an outage on a channel platform is not a reason to take a working replica out of rotation,
    /// so the channels stay out of the readiness probe.
    /// </summary>
    public const string Tag = "channels";
}
