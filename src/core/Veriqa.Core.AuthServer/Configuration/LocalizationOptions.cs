// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.UI;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Localization settings for the authentication page (SPEC-007 §12, UI-080).
/// Configuration section: Veriqa:Localization.
/// </summary>
public sealed class LocalizationOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Veriqa:Localization";

    /// <summary>
    /// Default language code (e.g. en, ru, zh) — used when the language could not be determined
    /// from the Accept-Language header. The value must be in the host's data-driven language
    /// registry (locale files shipped in the localization directory ∪ the base locale), otherwise
    /// the application fails to start (VeriqaOptionsValidator, SPEC-012 §8.1).
    /// Defaults to English — the base language (TASK-057), which needs no locale file and is
    /// therefore always in the registry, including for hosts that ship no locale files at all.
    /// </summary>
    public string DefaultLanguage { get; set; } = AuthPageStrings.LanguageEn;
}
