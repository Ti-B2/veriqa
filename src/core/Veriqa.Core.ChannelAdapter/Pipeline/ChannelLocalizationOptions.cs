// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Channel message localization settings (SPEC-017 §7.2, ICC-050).
/// Configuration section: Veriqa:Channels:Localization.
/// Locale files are JSON with a Natural Key → translation dictionary (frontend/localization.md);
/// a single source of translations for the UI and channel messages.
/// </summary>
public sealed class ChannelLocalizationOptions
{
    /// <summary>
    /// Configuration section name (CFG-113).
    /// </summary>
    public const string SectionName = "Veriqa:Channels:Localization";

    /// <summary>
    /// Default path to the locale files directory (relative to the host content root).
    /// </summary>
    public const string DefaultLocalesPath = "wwwroot/locales";

    /// <summary>
    /// Default base locale (Natural Keys — English text, TASK-057).
    /// </summary>
    public const string DefaultBaseLocale = "en";

    /// <summary>
    /// Path to the locale files directory (*.json). A relative path is resolved
    /// from the host content root. When the directory is missing, localization degrades
    /// to the base language (graceful, a warning is logged).
    /// </summary>
    public string LocalesPath { get; set; } = DefaultLocalesPath;

    /// <summary>
    /// Base locale (the language of Natural Keys). Its locale file is optional — a key with no
    /// entry falls back to the key's own text — but an entry that IS there is used like any other
    /// translation, so the base-language wording is editable as data.
    /// </summary>
    public string BaseLocale { get; set; } = DefaultBaseLocale;

    /// <summary>
    /// Regional culture per language stated WITHOUT a region: language subtag → culture tag, for
    /// example <c>{ "en": "en-GB" }</c>. It decides the CONVENTIONS a message's dates and numbers are
    /// formatted with, and nothing about which translation is used — a language mapped here still
    /// resolves its texts through its own locale file.
    /// <para>
    /// Empty (the default) — every language is formatted with the conventions the platform ships for
    /// it. The entry worth stating is the base language: bare <c>en</c> orders a short date
    /// month-first, which a reader outside that region reads as another day.
    /// </para>
    /// </summary>
    public Dictionary<string, string> NeutralLocaleCultures { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
