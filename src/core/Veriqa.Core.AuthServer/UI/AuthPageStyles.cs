// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.UI;

namespace Veriqa.Core.AuthServer.UI;

/// <summary>
/// Access to the stylesheet of the sign-in window shipped as an embedded resource of this assembly.
/// The token layer it relies on is shared with the service pages and lives in
/// <see cref="CorePageStyles"/>; only the window's own rules are stored here.
/// <para>
/// Norms the window's rules implement. They are named here and not in the stylesheet, whose comments
/// are inlined into the page and reach the end user's browser (see <see cref="CorePageEmbeddedText"/>):
/// </para>
/// <list type="bullet">
/// <item><description>the channel brand colors are literals and are never repainted by the client's
/// branding — they are canonical constants of an external brand (SPEC-015 §3.8; docs/design-rules.md
/// items 1 and 5);</description></item>
/// <item><description>the Minimal preset shows the QR and the buttons only, with no decorative
/// elements (SPEC-012 §4.2);</description></item>
/// <item><description>a clipped third-party label gets the tooltip of the contour
/// (SPEC-015 §4.16);</description></item>
/// <item><description>the QR image keeps the 200px scanning minimum and the module density verified at
/// 220px; with the wide OS scrollbar setting both can be missed on part of the viewport range, which
/// is an accepted limitation (SPEC-015 §4.2);</description></item>
/// <item><description>the focus indicator is a <c>:focus-visible</c> outline (SPEC-015 §6.2); the
/// integrator's footer is painted with the muted text token because the soft one stays below 4.5:1 on
/// the light background (SPEC-015 §6.1), and not with the primary color, since the Branded preset
/// repaints only the listed elements (docs/design-rules.md item 3).</description></item>
/// </list>
/// </summary>
internal static class AuthPageStyles
{
    /// <summary>
    /// Resource name of the window rules (<c>UI/Styles/veriqa-auth-page.css</c>).
    /// </summary>
    private const string ResourceName = "Veriqa.Core.AuthServer.UI.Styles.veriqa-auth-page.css";

    /// <summary>
    /// Rules of the sign-in window (without the token layer).
    /// </summary>
    public static string Css => CorePageEmbeddedText.Load(typeof(AuthPageStyles).Assembly, ResourceName);
}
