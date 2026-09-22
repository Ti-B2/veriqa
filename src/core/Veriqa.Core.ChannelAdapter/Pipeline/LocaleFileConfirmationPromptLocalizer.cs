// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Channel message localizer based on JSON locale files
/// (SPEC-017 §7.2, ICC-050; frontend/localization.md §2).
/// Loads the Natural Key → translation dictionaries from the host's localization directory
/// (wwwroot/locales by default) lazily and once; the file name without extension is
/// the locale tag (ru.json, en.json, zh.json, pseudo.json).
/// The recipient's locale comes from the external channel and is NOT used to construct
/// the file path — only a dictionary lookup over the already loaded locales is performed
/// (protection against path traversal, core-rules §10).
/// A missing directory/file/key, or a blank entry — graceful degradation to the base language.
/// </summary>
internal sealed class LocaleFileConfirmationPromptLocalizer : IConfirmationPromptLocalizer
{
    /// <summary>
    /// Search pattern for locale files in the localization directory.
    /// </summary>
    private const string LocaleFileSearchPattern = "*.json";

    /// <summary>
    /// Channel message localization settings.
    /// </summary>
    private readonly IOptions<ChannelLocalizationOptions> _options;

    /// <summary>
    /// Host environment (content root for resolving the relative path).
    /// </summary>
    private readonly IHostEnvironment _environment;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<LocaleFileConfirmationPromptLocalizer> _logger;

    /// <summary>
    /// Lazily loaded translation dictionaries: locale tag (lowercase) → (Natural Key → translation).
    /// </summary>
    private readonly Lazy<FrozenDictionary<string, FrozenDictionary<string, string>>> _translations;

    /// <summary>
    /// Lazily computed registry of known keys — the union of keys from all locale files (ICC-050).
    /// </summary>
    private readonly Lazy<FrozenSet<string>> _knownKeys;

    /// <summary>
    /// Lazily computed registry of loaded locale tags — the keys of the translation dictionaries.
    /// </summary>
    private readonly Lazy<FrozenSet<string>> _availableLocales;

    /// <summary>
    /// Creates the locale-file based localizer.
    /// </summary>
    /// <param name="options">Channel message localization settings.</param>
    /// <param name="environment">Host environment.</param>
    /// <param name="logger">Logger.</param>
    public LocaleFileConfirmationPromptLocalizer(
        IOptions<ChannelLocalizationOptions> options,
        IHostEnvironment environment,
        ILogger<LocaleFileConfirmationPromptLocalizer> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
        _translations = new Lazy<FrozenDictionary<string, FrozenDictionary<string, string>>>(
            LoadTranslations,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _knownKeys = new Lazy<FrozenSet<string>>(
            BuildKnownKeys,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _availableLocales = new Lazy<FrozenSet<string>>(
            BuildAvailableLocales,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public IReadOnlySet<string> AvailableLocales => _availableLocales.Value;

    /// <inheritdoc />
    public string? RegionalCultureFor(string language)
    {
        // The stated map is read with the same tag normalization as a translation lookup, so a
        // deployment writing "EN" or "pt_BR" addresses the same language either way.
        var normalized = ChannelLocaleTag.Normalize(language);
        if (normalized is null)
        {
            return null;
        }

        return _options.Value.NeutralLocaleCultures.TryGetValue(normalized, out var culture)
            && !string.IsNullOrWhiteSpace(culture)
                ? culture
                : null;
    }

    /// <inheritdoc />
    public string? Resolve(string naturalKey, string? recipientLocale)
    {
        // The method resolves a Natural Key into the recipient's language: normalizes the locale tag,
        // looks up the dictionary by the full tag, then by the primary subtag ("pt-br" → "pt");
        // any miss — null (the caller takes the base text); a blank entry counts as a miss.
        // The base locale is looked up like any other: English-first means "no entry for the English
        // key → the key itself", not "the base locale never consults a file", so an administrator can
        // reword the base-language text in its locale file instead of editing constants

        if (string.IsNullOrWhiteSpace(naturalKey))
        {
            return null;
        }

        var normalizedLocale = ChannelLocaleTag.Normalize(recipientLocale);
        if (normalizedLocale is null)
        {
            return null;
        }

        var translations = _translations.Value;

        var fullTagValue = LookUpTranslation(translations, normalizedLocale, naturalKey);
        if (fullTagValue is not null)
        {
            return fullTagValue;
        }

        // Fallback to the primary subtag: "pt-br" → "pt", "zh-hans" → "zh"
        var separatorIndex = normalizedLocale.IndexOf('-');
        if (separatorIndex > 0)
        {
            return LookUpTranslation(translations, normalizedLocale[..separatorIndex], naturalKey);
        }

        return null;
    }

    /// <inheritdoc />
    public bool IsKnownKey(string naturalKey)
    {
        if (string.IsNullOrWhiteSpace(naturalKey))
        {
            return false;
        }

        return _knownKeys.Value.Contains(naturalKey);
    }

    /// <summary>
    /// Looks a Natural Key up in the dictionary of a single locale.
    /// An entry that is empty or whitespace counts as a miss, not as a translation: blanking a
    /// value in a locale file must degrade to the key's base text, otherwise an empty caption
    /// reaches the user (a channel button with no label is rejected by the messenger API, and a
    /// page control renders unlabelled). The key-parity gate compares key sets, not values,
    /// so nothing else catches a blank entry.
    /// </summary>
    /// <param name="translations">Loaded translation dictionaries by locale tag.</param>
    /// <param name="localeTag">Normalized locale tag to look the key up in.</param>
    /// <param name="naturalKey">Natural Key.</param>
    /// <returns>The translation, or null when the locale, the key or a usable value is absent.</returns>
    private static string? LookUpTranslation(
        FrozenDictionary<string, FrozenDictionary<string, string>> translations,
        string localeTag,
        string naturalKey)
    {
        if (!translations.TryGetValue(localeTag, out var entries)
            || !entries.TryGetValue(naturalKey, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// Loads all locale files from the localization directory.
    /// Read errors for an individual file or a missing directory are not fatal —
    /// a warning is logged, localization degrades to the base language.
    /// </summary>
    /// <returns>Translation dictionaries by locale tag.</returns>
    private FrozenDictionary<string, FrozenDictionary<string, string>> LoadTranslations()
    {
        var localesPath = _options.Value.LocalesPath;
        var resolvedPath = Path.IsPathRooted(localesPath)
            ? localesPath
            : Path.Combine(_environment.ContentRootPath, localesPath);

        if (!Directory.Exists(resolvedPath))
        {
            _logger.LogWarning(
                "Locale files directory not found — channel messages are sent in the base language. LocalesPath: {LocalesPath}",
                resolvedPath);

            return FrozenDictionary<string, FrozenDictionary<string, string>>.Empty;
        }

        var result = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal);

        // Directory enumeration is also protected: Lazy in ExecutionAndPublication mode
        // caches a factory exception forever — a single transient IO failure (TOCTOU between
        // Directory.Exists and the enumeration) must not permanently break the localizer
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(resolvedPath, LocaleFileSearchPattern))
            {
                // The tag from the file name is normalized by the same function as the incoming locale
                // (trim, "_" → "-", lowercase) — otherwise pt_BR.json would never match "pt-br"
                var localeTag = ChannelLocaleTag.Normalize(Path.GetFileNameWithoutExtension(filePath));
                if (localeTag is null)
                {
                    continue;
                }

                try
                {
                    using var stream = File.OpenRead(filePath);
                    var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);

                    if (entries is null)
                    {
                        continue;
                    }

                    result[localeTag] = entries.ToFrozenDictionary(StringComparer.Ordinal);
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to load a locale file — locale skipped. LocaleFile: {LocaleFile}",
                        filePath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Failed to enumerate locale files — some locales loaded, remaining messages use the base language. LocalesPath: {LocalesPath}",
                resolvedPath);
        }

        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds the registry of known keys — the union of keys from all loaded locale files.
    /// </summary>
    /// <returns>Set of known Natural Keys.</returns>
    private FrozenSet<string> BuildKnownKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var localeEntries in _translations.Value.Values)
        {
            keys.UnionWith(localeEntries.Keys);
        }

        return keys.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds the registry of loaded locale tags. The tags are already normalized by
    /// <see cref="ChannelLocaleTag.Normalize"/> when the files are loaded, so the dictionary keys are taken as is.
    /// </summary>
    /// <returns>Set of normalized locale tags actually loaded from files.</returns>
    private FrozenSet<string> BuildAvailableLocales()
    {
        return _translations.Value.Keys.ToFrozenSet(StringComparer.Ordinal);
    }
}
