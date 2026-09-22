// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Veriqa.Core.ChannelAdapter.Constants;

namespace Veriqa.Core.AuthServer.UI;

/// <summary>
/// Inline SVG channel glyphs for the authentication page. The core contour carries no standalone
/// markup or image files of its own (SPEC-015 §1, §3.1), so each glyph must be embedded into the
/// C#-generated markup rather than referenced from an external file.
/// Path data is duplicated by the site's own icon sprite, which lives in the veriqa/site
/// repository — a change to a glyph here has to be repeated there.
/// telegram/whatsapp — official simple-icons glyphs (CC0); max — a neutral bold
/// "M" lettermark (not the official logo, to avoid confusion with WhatsApp); email — an envelope.
/// The Veriqa brand mark is not here: every page of the contour draws it, so it lives in the lower
/// assembly (<see cref="Veriqa.Core.ChannelAdapter.UI.CorePageBrandMark"/>).
/// </summary>
internal static partial class ChannelIconSvg
{
    /// <summary>
    /// Allowed shape of the glyph markup declared by a third-party adapter through
    /// <c>IChannelDisplayMetadata.IconSvgPath</c>: a sequence of plain SVG shape elements
    /// (<c>path</c>, <c>circle</c>, <c>ellipse</c>, <c>rect</c>, <c>line</c>, <c>polyline</c>,
    /// <c>polygon</c>, <c>g</c>) with double-quoted attributes only. Everything else is rejected:
    /// the value is embedded into the sign-in page as markup, and an adapter whose value comes from
    /// configuration could otherwise close the <c>&lt;svg&gt;</c> and inject arbitrary markup —
    /// with <c>style-src 'unsafe-inline'</c> that is enough for a full-page overlay on the IdP
    /// origin. Event-handler (<c>on*</c>) and <c>style</c> attributes are excluded for the same
    /// reason; namespaced attributes (<c>xlink:href</c>) do not match either.
    /// </summary>
    private const string CustomPathPattern =
        """\A(?:\s*<(?:path|circle|ellipse|rect|line|polyline|polygon|g)(?:\s+(?!on|style)[a-z][a-z0-9-]*\s*=\s*"[^"<>]*")*\s*/?>|\s*</g>)+\s*\z""";

    /// <summary>
    /// Path content of the Telegram glyph (viewBox 0 0 24 24).
    /// </summary>
    private const string TelegramPath =
        """<path d="M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z"/>""";

    /// <summary>
    /// Path content of the WhatsApp glyph (viewBox 0 0 24 24).
    /// </summary>
    private const string WhatsAppPath =
        """<path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413Z"/>""";

    /// <summary>
    /// Path content of the MAX glyph (viewBox 0 0 24 24). A neutral bold "M" lettermark
    /// (not the official logo; the chat bubble was removed to avoid confusion with WhatsApp).
    /// </summary>
    private const string MaxPath =
        """<path d="M2 21V3h5l5 8 5-8h5v18h-5V11l-5 8-5-8v10z"/>""";

    /// <summary>
    /// Path content of the Email glyph — an envelope (viewBox 0 0 24 24).
    /// </summary>
    private const string EmailPath =
        """<path d="M20 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 4-8 5-8-5V6l8 5 8-5v2z"/>""";

    /// <summary>
    /// Returns the path content of the channel glyph, or null for an unknown channel
    /// (the caller must work correctly without an icon).
    /// </summary>
    /// <param name="channelType">Channel type (<see cref="ChannelTypes"/>).</param>
    /// <returns>Glyph path markup, or null.</returns>
    public static string? GetPath(string channelType)
    {
        return channelType switch
        {
            ChannelTypes.Telegram => TelegramPath,
            ChannelTypes.WhatsApp => WhatsAppPath,
            ChannelTypes.Max => MaxPath,
            ChannelTypes.Email => EmailPath,
            _ => null
        };
    }

    /// <summary>
    /// Renders the inline SVG channel icon with the given CSS class. For an unknown
    /// channel returns an empty string (the page stays valid without an icon).
    /// </summary>
    /// <param name="channelType">Channel type (<see cref="ChannelTypes"/>).</param>
    /// <param name="cssClass">CSS class of the SVG element (e.g. veriqa-ch-icon).</param>
    /// <param name="customPath">
    /// Glyph markup declared by a third-party adapter through <c>IChannelDisplayMetadata</c>
    /// (public SPI) — used only for channels that have no bundled glyph. Null — no icon.
    /// </param>
    /// <returns>SVG markup, or an empty string.</returns>
    public static string Render(string channelType, string cssClass, string? customPath = null)
    {
        // A bundled glyph wins; a third-party one is embedded only if it passes the allowlist
        // (the check is repeated here so the sink itself can never emit unvalidated markup).
        var path = GetPath(channelType) ?? (IsValidCustomPath(customPath) ? customPath : null);
        return path is null
            ? string.Empty
            : $"""<svg class="{cssClass}" viewBox="0 0 24 24" aria-hidden="true" focusable="false">{path}</svg>""";
    }

    /// <summary>
    /// Checks the glyph markup declared by a third-party adapter against
    /// <see cref="CustomPathPattern"/>. Invalid markup is dropped by the caller (the channel is
    /// rendered without an icon — the page stays valid, per the SPI degradation contract).
    /// </summary>
    /// <param name="customPath">Glyph markup declared by the adapter, or null.</param>
    /// <returns><c>true</c> if the markup may be embedded into the page.</returns>
    public static bool IsValidCustomPath([NotNullWhen(true)] string? customPath)
    {
        return !string.IsNullOrWhiteSpace(customPath) && CustomPathRegex().IsMatch(customPath);
    }

    /// <summary>
    /// Compiled matcher of the third-party glyph markup contract.
    /// </summary>
    [GeneratedRegex(CustomPathPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CustomPathRegex();
}
