// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Unified login confirmation mode (SPEC-012 §4.4, CFG-040…047).
/// A consolidation of the former two partially overlapping settings (the /start mode and the
/// completion mode, plus the Email pair) into one axis: "is human confirmation required and on
/// which surface". Resolved by the common resolver across ownership levels (SPEC-012 §10.6); the
/// core expands <see cref="ChannelDefault"/> and routes the requested surface using the channel's
/// single confirmation fact (§4.4.2). Placed in TransactionEngine (next to the former domain modes) to be
/// accessible to both the pipeline (ChannelAdapter) and the AuthServer without a reverse dependency.
/// </summary>
/// <remarks>
/// The value names are fixed by a human decision (config binding and serialization by name) —
/// must not be changed. The order and composition (5 members) are normalized by SPEC-012 §4.4.
/// </remarks>
public enum LoginConfirmationMode
{
    /// <summary>
    /// Sentinel: the surface follows from the channel's fact — it can confirm inside itself, or the
    /// core asks the question on its own surface (§4.4.1).
    /// The core default (CFG-041). Never reaches the confirmation logic — resolution always expands
    /// it into the channel's concrete surface (CFG-043).
    /// </summary>
    ChannelDefault,

    /// <summary>
    /// No confirmation required — silent auto-login, regardless of the channel (CFG-044).
    /// </summary>
    None,

    /// <summary>
    /// Confirmation in the bot/channel: inline "Confirm"/"Decline" buttons (the former
    /// <c>ConfirmationPrompt</c>, SPEC-003 §4.5). The transaction completes only after the callback from the channel (CFG-046).
    /// </summary>
    InChannel,

    /// <summary>
    /// Confirmation on the web page: the user answers "Yes" or "No" to the question (the former
    /// <c>WebConfirmation</c>). The button is protected by an AntiForgery token (SPEC-007 UI-038, CFG-045).
    /// </summary>
    OnWebPage,

    /// <summary>
    /// RESERVED for double confirmation (both in the channel and on the web).
    /// The value's contract is declared, but the double-confirmation logic is not implemented (CFG-047).
    /// Until then it behaves as a plain <see cref="InChannel"/> request and is routed accordingly
    /// with a WARNING (§4.4.2); it must not be selected in configuration as operational.
    /// </summary>
    InChannelAndOnWebPage
}
