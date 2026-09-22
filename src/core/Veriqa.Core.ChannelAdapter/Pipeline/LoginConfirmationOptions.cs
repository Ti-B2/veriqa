// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Login confirmation settings (SPEC-012 §4.4, CFG-040…047). A single axis that consolidated the previous
/// two partially overlapping settings: the /start mode and the completion mode. Configuration section:
/// <see cref="SectionName"/> (<c>Veriqa:LoginConfirmation</c>). Resolved by the shared resolver across
/// ownership levels (§10.6) — the core level is read from these global options. The ui_config record
/// does NOT carry this mode (security, CFG-049).
/// </summary>
public sealed class LoginConfirmationOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Veriqa:LoginConfirmation";

    /// <summary>
    /// Login confirmation mode (CFG-040). Default — <see cref="LoginConfirmationMode.ChannelDefault"/>
    /// (core default, CFG-041): the channel itself picks the concrete surface.
    /// </summary>
    public LoginConfirmationMode Mode { get; set; } = LoginConfirmationMode.ChannelDefault;
}
