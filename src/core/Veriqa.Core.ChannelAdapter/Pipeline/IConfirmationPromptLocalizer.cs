// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Localizer of channel message Natural Keys into the recipient's language
/// (SPEC-017 §7.2, ICC-050; frontend/localization.md).
/// Resolution happens on the channel layer: the source of the recipient's locale is
/// the channel data (for example, the Telegram user's language_code in
/// ChannelIdentitySnapshot.Locale). When the locale or translation is unavailable,
/// the key's base-language text is used (<see cref="NaturalKeyContext.BaseTextOf"/>).
/// </summary>
public interface IConfirmationPromptLocalizer
{
    /// <summary>
    /// Returns the locale file's entry for a Natural Key in the recipient's locale, as it stands.
    /// Null — translation unavailable (no locale, no locale file, no key in it, or a blank entry);
    /// the caller uses the key's base-language text (<see cref="NaturalKeyContext.BaseTextOf"/>).
    /// An entry that merely repeats its key is returned as is — deciding that it is not a
    /// translation is <see cref="ResolveOrBaseText"/>'s job.
    /// </summary>
    /// <param name="naturalKey">Natural Key (base-language text, English — TASK-057).</param>
    /// <param name="recipientLocale">Recipient locale (IETF tag, e.g. "ru", "pt-BR"; null — base language).</param>
    /// <returns>Translated text or null.</returns>
    string? Resolve(string naturalKey, string? recipientLocale);

    /// <summary>
    /// Resolves a Natural Key into text that is always safe to show: the translation when there is
    /// one, otherwise the key's base-language text with the context suffix stripped
    /// (<see cref="NaturalKeyContext"/>, frontend/localization.md §1a).
    /// <para>
    /// This is the member consumers should call. Falling back to the raw key instead
    /// (<c>Resolve(...) ?? naturalKey</c>) is what puts a context label such as
    /// "Sign-in confirmed ✅ | telegram" in front of a user: a key absent from the locale file of the
    /// requested language — the base language included — resolves to null, and stripping the label is
    /// then the only thing standing between that label and the user.
    /// </para>
    /// <para>
    /// An entry ordinally equal to its key counts as a miss too, the same rule the message template
    /// resolution applies: a locale file carrying <c>"X | button": "X | button"</c> would otherwise put
    /// the label on screen. For a key without a context suffix the base text is the key itself, so
    /// such an entry shows its own text either way.
    /// </para>
    /// </summary>
    /// <param name="naturalKey">Natural Key, with or without a context suffix.</param>
    /// <param name="recipientLocale">Recipient locale (IETF tag; null — base language).</param>
    /// <returns>Text to show the user; never the raw context label.</returns>
    string ResolveOrBaseText(string naturalKey, string? recipientLocale)
    {
        var resolved = Resolve(naturalKey, recipientLocale);

        return resolved is null || string.Equals(resolved, naturalKey, StringComparison.Ordinal)
            ? NaturalKeyContext.BaseTextOf(naturalKey)
            : resolved;
    }

    /// <summary>
    /// Checks whether the key is registered in the locale files (ICC-050).
    /// Used to validate keys coming from external implementations
    /// (for example, AnomalyReasonKey from the anomaly detector) before insertion into a message.
    /// </summary>
    /// <param name="naturalKey">Natural Key to check.</param>
    /// <returns>true — the key is known (present in at least one locale file).</returns>
    bool IsKnownKey(string naturalKey);

    /// <summary>
    /// Normalized tags (trim, '_' → '-', lowercase) of the locales actually loaded from the
    /// locale files. Does NOT include the base locale — the consumer unions it in, because the
    /// base language needs no translation file (the key is the text).
    /// The default is an empty set: the language registry then degrades to { BaseLocale } — the
    /// same graceful degradation this contour already applies to a missing locale directory.
    /// <para>
    /// Extension rule for this interface: every new member is added WITH a default body, so
    /// third-party implementations stay source- and binary-compatible. A member that has no
    /// meaningful default is a signal that it belongs on a separate interface, not here.
    /// </para>
    /// </summary>
    IReadOnlySet<string> AvailableLocales => FrozenSet<string>.Empty;

    /// <summary>
    /// The regional culture this deployment states for a language stated WITHOUT a region — the tag
    /// whose conventions a message's dates and numbers are formatted with. Null (the default) — the
    /// language is formatted with the conventions the platform ships for it.
    /// </summary>
    /// <remarks>
    /// It is a locale question and therefore belongs to the port that owns locales here: a language
    /// without a region has no region conventions of its own, and which region a deployment's readers
    /// are in is something only that deployment knows. The base language of the product is the case
    /// that makes it worth stating — bare "en" orders a short date month-first, which a reader outside
    /// that region reads as another day. A language that already carries a region is never asked about.
    /// </remarks>
    /// <param name="language">Primary language subtag stated with no region (for example "en").</param>
    /// <returns>Culture tag to format with (for example "en-GB"), or null when none is stated.</returns>
    string? RegionalCultureFor(string language) => null;
}
