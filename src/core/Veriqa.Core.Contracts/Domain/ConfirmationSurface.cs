// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Resolved confirmation surface: what the user actually gets (SPEC-012 §4.4.2, CFG-049b).
/// Carries no sentinel by construction — the channel default is already substituted and the value
/// is already clamped to the channel's ceiling, so a consumer cannot receive "let the channel
/// decide" and has no expansion rule left to remember.
/// </summary>
/// <remarks>
/// The mirror of the requested axis <see cref="LoginConfirmationMode"/>: that axis keeps the
/// sentinel and the reserved double-confirmation value (config binding goes by those names), while
/// this one holds only the surfaces that reach the confirmation logic.
/// </remarks>
public enum ConfirmationSurface
{
    /// <summary>
    /// No confirmation required — silent auto-login (CFG-044).
    /// </summary>
    None,

    /// <summary>
    /// Confirmation in the bot/channel: inline "Confirm"/"Decline" buttons (SPEC-003 §4.5).
    /// The transaction completes only after the callback from the channel (CFG-046).
    /// </summary>
    InChannel,

    /// <summary>
    /// Confirmation on the web page: the user answers "Yes" or "No" to the question.
    /// The button is protected by an AntiForgery token (SPEC-007 UI-038, CFG-045).
    /// </summary>
    OnWebPage
}
