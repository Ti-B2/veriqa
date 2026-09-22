// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Configuration.Enums;

/// <summary>
/// Display mode of the authentication channels on the login page (SPEC-012 §4.1).
/// </summary>
public enum ChannelDisplayMode
{
    /// <summary>
    /// All available channels are displayed simultaneously.
    /// </summary>
    Multichannel,

    /// <summary>
    /// Only the selected channels are displayed. Multiple channels can be specified. For example: Telegram and MAX.
    /// </summary>
    Selective
}
