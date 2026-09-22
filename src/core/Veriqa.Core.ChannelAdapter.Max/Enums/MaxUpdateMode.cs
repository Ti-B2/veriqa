// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Max.Enums;

/// <summary>
/// Mode of receiving updates from the MAX Bot API.
/// </summary>
public enum MaxUpdateMode
{
    /// <summary>
    /// Webhook mode: MAX sends updates to the specified URL.
    /// Recommended for production.
    /// </summary>
    Webhook,

    /// <summary>
    /// Long-polling mode: the adapter periodically polls the MAX Bot API.
    /// Recommended for local development.
    /// </summary>
    Polling
}
