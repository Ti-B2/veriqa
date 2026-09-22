// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.UI;

/// <summary>
/// The Veriqa brand mark of the "Core" UI contour: the brand red, the wordmark and the red diamond.
/// <para>
/// One definition for every page family: the sign-in window draws the diamond in its brand row, and
/// every page of the contour draws it in the attribution line (<see cref="CorePageAttribution"/>). The
/// mark lives in this assembly because it is the lower of the two that render pages — the pages of the
/// channel satellites cannot see Veriqa.Core.AuthServer, and a second copy of the diamond there would
/// be a second image of one mark (SPEC-015 §3.1).
/// </para>
/// </summary>
internal static class CorePageBrandMark
{
    /// <summary>
    /// Veriqa brand red (brand constant, not themed).
    /// </summary>
    internal const string VeriqaBrandRed = "#e53837";

    /// <summary>
    /// Brand name for the default logo block.
    /// </summary>
    internal const string VeriqaBrandName = "Veriqa";

    /// <summary>
    /// Inline SVG of the Veriqa brand mark — a red diamond (landing page canon: a square with a small
    /// corner radius, rotated 45°). Decorative everywhere it is drawn: text naming the brand always
    /// stands next to it.
    /// </summary>
    internal const string VeriqaDiamondSvg =
        $"""<svg class="veriqa-brand-mark" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><rect x="5" y="5" width="14" height="14" rx="2" transform="rotate(45 12 12)" fill="{VeriqaBrandRed}"/></svg>""";
}
