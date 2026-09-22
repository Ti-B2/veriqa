// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Configuration.Enums;

/// <summary>
/// Claim source for scope configuration (SPEC-012 §6.6).
/// </summary>
public enum ClaimSource
{
    /// <summary>
    /// The claim is provided by a channel adapter (Telegram, WhatsApp, MAX).
    /// </summary>
    Channel,

    /// <summary>
    /// The claim is provided by the client's external system (Identity Resolution).
    /// </summary>
    External
}
