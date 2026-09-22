// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Telegram.Enums;

/// <summary>
/// Update delivery mode for the Telegram bot (SPEC-012 §6.9).
/// </summary>
public enum TelegramUpdateMode
{
    /// <summary>
    /// Telegram delivers updates via webhook.
    /// </summary>
    Webhook,

    /// <summary>
    /// The bot fetches updates via long polling.
    /// </summary>
    Polling
}
