// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Context suffix of a Natural Key — the industry disambiguation idiom (gettext <c>msgctxt</c>,
/// Qt <c>tr(text, context)</c>), spelled as "&lt;base text&gt; | &lt;context&gt;"
/// (frontend/localization.md §1a).
/// <para>
/// With Natural Keys the key IS the base-language text, so one phrase yields one key and one
/// translation. When the same English phrase must translate differently per context (per channel,
/// homonyms, a term vs an action), the key carries a semantic context label after the separator.
/// The label is metadata, never text for the user: whenever the key falls through to the base
/// language, only the part before the LAST separator is shown.
/// </para>
/// <para>
/// The rule has three implementations: this class, and — in the site contour, which lives in the
/// <c>veriqa/site</c> repository — <c>JsonStringLocalizer</c> (server) and <c>i18n.js</c> (browser).
/// That contour consumes the core as NuGet packages and its server localizer keeps its own copy of
/// the rule rather than calling this one. All three implement the same §1a; a change to the separator
/// or to the fallback rule has to land in all three, which now means in both repositories.
/// </para>
/// </summary>
public static class NaturalKeyContext
{
    /// <summary>
    /// Separator reserved for the context suffix (space, vertical bar, space). Reserved means it
    /// does not occur in ordinary UI text — otherwise the fallback would truncate a real sentence.
    /// </summary>
    public const string Separator = " | ";

    /// <summary>
    /// Returns the base-language text of a Natural Key — the part before the LAST
    /// <see cref="Separator"/>, or the key itself when it carries no context suffix.
    /// This is what a user sees when there is no translation, so the context label never leaks
    /// into a channel message or a served page.
    /// </summary>
    /// <param name="naturalKey">Natural Key, with or without a context suffix.</param>
    /// <returns>The key's base text.</returns>
    public static string BaseTextOf(string naturalKey)
    {
        // The last separator wins: a context label itself may not contain the separator, while the
        // base text of a legacy key might — truncating at the first one would cut real text
        var separatorIndex = naturalKey.LastIndexOf(Separator, StringComparison.Ordinal);

        return separatorIndex >= 0 ? naturalKey[..separatorIndex] : naturalKey;
    }
}
