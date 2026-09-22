// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.RegularExpressions;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Syntactic check of a language tag (BCP 47 / RFC 5646) for the entries that accept one from a
/// caller. It answers about the SHAPE of the tag and about nothing else: a well-formed tag naming a
/// language the deployment does not carry is not an error here — it degrades through the ordinary
/// fallback chain at render time (SPEC-039 C14).
/// </summary>
/// <remarks>
/// The platform offers no such check. <c>CultureInfo</c> accepts nearly any string as a culture name
/// and therefore draws no line at all, and the language tag is used here as the key of a locale rather
/// than as a full BCP 47 tag — so the accepted shape is deliberately the common subset
/// <c>language[-script][-region][-variant…]</c>. Extension and private-use subtags (<c>-u-</c>,
/// <c>-x-</c>) and the grandfathered tags of the registry are refused: nothing in the product reads
/// them, and accepting a tag no consumer understands only moves the failure further away.
/// </remarks>
internal static partial class LanguageTagSyntax
{
    /// <summary>
    /// Longest tag the check accepts. A language tag of the accepted shape is far shorter; the bound
    /// keeps a caller from handing the matcher an arbitrarily long string.
    /// </summary>
    private const int MaxTagLength = 64;

    /// <summary>
    /// Reports whether the value is a well-formed language tag of the accepted shape.
    /// </summary>
    /// <param name="tag">Raw language tag.</param>
    /// <returns><see langword="true"/> when the tag is well-formed.</returns>
    public static bool IsWellFormed(string? tag) =>
        !string.IsNullOrEmpty(tag) && tag.Length <= MaxTagLength && Pattern().IsMatch(tag);

    /// <summary>
    /// <c>language[-script][-region][-variant…]</c>: a 2–8 letter primary subtag, an optional 4-letter
    /// script, an optional 2-letter or 3-digit region, and any number of variants (5–8 alphanumerics,
    /// or 4 starting with a digit) — RFC 5646 §2.1.
    /// </summary>
    /// <returns>The compiled matcher.</returns>
    [GeneratedRegex(
        "^[A-Za-z]{2,8}(-[A-Za-z]{4})?(-([A-Za-z]{2}|[0-9]{3}))?(-([A-Za-z0-9]{5,8}|[0-9][A-Za-z0-9]{3}))*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
