// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using Microsoft.Extensions.Options;
using Veriqa.Core.ChannelAdapter.Pipeline;

namespace Veriqa.Core.AuthServer.UI;

/// <summary>
/// Single source of the auth-window supported-language registry (SPEC-007 §12, UI-080).
/// The registry is data-driven: it is derived from the locale files actually loaded by
/// <see cref="IConfirmationPromptLocalizer"/>, unioned with the base locale (which needs no
/// translation file — the Natural Key is the text). Adding a locale file to the host's
/// localization directory adds the language; no code change is required.
/// <para>
/// Registered as a singleton: both inputs are process-constant, so each set is built lazily on
/// first access and reused for the lifetime of the process instead of being rebuilt per request.
/// A locale file added while the process runs is not picked up — the localizer loads its
/// dictionaries once per process too, so a restart is required (see wwwroot/locales/README.md).
/// All consumers (language detection, configuration validation) MUST take the registry from here —
/// a second copy of the union logic is how the registry starts to disagree with itself.
/// </para>
/// </summary>
internal sealed class AuthPageLanguageRegistry : IAuthPageLanguageRegistry
{
    /// <summary>
    /// Pseudo-locale tag (frontend/localization.md §6): a layout test locale, not a production
    /// language. It is detectable via an explicit Accept-Language header, but is rejected as
    /// Veriqa:Localization:DefaultLanguage — see <see cref="ConfigurableLanguages"/>.
    /// </summary>
    public const string PseudoLocale = "pseudo";

    /// <summary>
    /// Languages the auth window can render: loaded locales ∪ { base locale }.
    /// </summary>
    private readonly Lazy<IReadOnlySet<string>> _supportedLanguages;

    /// <summary>
    /// Languages accepted as Veriqa:Localization:DefaultLanguage: detection registry minus the
    /// pseudo-locale.
    /// </summary>
    private readonly Lazy<IReadOnlySet<string>> _configurableLanguages;

    /// <summary>
    /// Creates the language registry. Neither set is built here: each one is built on the first
    /// access to it and reused afterwards. In practice <see cref="ConfigurableLanguages"/> is built
    /// during startup validation (the validator reads it, and only it), while
    /// <see cref="SupportedLanguages"/> is built on the first /authorize request.
    /// </summary>
    /// <param name="localizer">Locale-file localizer (source of the loaded locales).</param>
    /// <param name="localizationOptions">Channel localization settings (source of the base locale).</param>
    public AuthPageLanguageRegistry(
        IConfirmationPromptLocalizer localizer,
        IOptions<ChannelLocalizationOptions> localizationOptions)
    {
        // ExecutionAndPublication: two concurrent /authorize requests must not build the set twice
        _supportedLanguages = new Lazy<IReadOnlySet<string>>(
            () => BuildSet(localizer, localizationOptions).ToFrozenSet(StringComparer.Ordinal),
            LazyThreadSafetyMode.ExecutionAndPublication);

        _configurableLanguages = new Lazy<IReadOnlySet<string>>(
            () =>
            {
                var languages = BuildSet(localizer, localizationOptions);
                languages.Remove(PseudoLocale);

                return languages.ToFrozenSet(StringComparer.Ordinal);
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public IReadOnlySet<string> SupportedLanguages => _supportedLanguages.Value;

    /// <inheritdoc />
    /// <remarks>
    /// The pseudo-locale is excluded because it is a layout test artifact: accepting it as the
    /// configured default (and advertising it to the administrator in the validation message as an
    /// allowed value) would present it as a production language.
    /// </remarks>
    public IReadOnlySet<string> ConfigurableLanguages => _configurableLanguages.Value;

    /// <summary>
    /// Builds the registry set (loaded locales ∪ { base locale }) shared by both sets. Returned
    /// mutable and unfrozen so each caller can refine it (dropping the pseudo-locale, for the
    /// configurable set) and freeze the final content once, instead of freezing an intermediate.
    /// </summary>
    /// <param name="localizer">Locale-file localizer (source of the loaded locales).</param>
    /// <param name="localizationOptions">Channel localization settings (source of the base locale).</param>
    /// <returns>Mutable set of normalized language codes.</returns>
    private static HashSet<string> BuildSet(
        IConfirmationPromptLocalizer localizer,
        IOptions<ChannelLocalizationOptions> localizationOptions)
    {
        var languages = new HashSet<string>(localizer.AvailableLocales, StringComparer.Ordinal);

        var baseLocale = NormalizeLocale(localizationOptions.Value.BaseLocale);
        if (baseLocale is not null)
        {
            languages.Add(baseLocale);
        }

        return languages;
    }

    /// <summary>
    /// Normalizes a locale tag the same way the locale-file localizer does (trim, '_' → '-',
    /// lowercase), so a configured base locale of "EN" unions into the registry as "en".
    /// </summary>
    /// <param name="locale">Raw locale tag.</param>
    /// <returns>Normalized tag or null when the value is empty.</returns>
    private static string? NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        return locale.Trim().Replace('_', '-').ToLowerInvariant();
    }
}
