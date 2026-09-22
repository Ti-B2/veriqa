// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Configuration.Enums;

/// <summary>
/// Visibility of the QR code on the sign-in page (SPEC-012 §9, presentational group SPEC-003 §17.3).
/// The value is a request to the page, not a server-side branch: the markup is the same for every
/// value and the page itself decides what to show (SPEC-007 UI-020).
/// </summary>
public enum QrCodeVisibility
{
    /// <summary>
    /// The QR code is shown on every device. The default — the behavior of a deployment that
    /// configures nothing.
    /// </summary>
    Always,

    /// <summary>
    /// The QR code is shown everywhere except a phone, where scanning one's own screen is
    /// impossible and the deep-link button is the way in. A tablet counts as a desktop here: the
    /// QR is scanned by ANOTHER device's camera, so a doubtful device keeps the QR rather than
    /// losing it.
    /// </summary>
    DesktopOnly,

    /// <summary>
    /// The QR code is never shown — only the deep-link buttons. Applies unconditionally, before any
    /// device check.
    /// </summary>
    Never
}
