// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// Error codes for the channel display subsystem (SPEC-007, §3–§4).
/// All codes use lower_snake_case for consistency with the other ErrorCodes classes.
/// </summary>
public static class ChannelDisplayErrorCodes
{
    /// <summary>
    /// No available authentication channel was found
    /// (adapters disabled, filtered out, or all deep link requests failed).
    /// </summary>
    public const string NoChannelsAvailable = "no_channels_available";
}
