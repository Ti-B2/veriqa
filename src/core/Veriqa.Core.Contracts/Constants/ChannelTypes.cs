// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Channel types supported by the Veriqa system (SPEC-003 §21.1).
/// </summary>
public static class ChannelTypes
{
    /// <summary>
    /// Telegram channel.
    /// </summary>
    public const string Telegram = "telegram";

    /// <summary>
    /// WhatsApp channel.
    /// </summary>
    public const string WhatsApp = "whatsapp";

    /// <summary>
    /// Max channel.
    /// </summary>
    public const string Max = "max";

    /// <summary>
    /// Email channel.
    /// </summary>
    public const string Email = "email";
}
