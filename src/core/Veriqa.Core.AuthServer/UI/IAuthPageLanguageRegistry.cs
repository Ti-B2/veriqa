// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.UI;

/// <summary>
/// Process-wide registry of the languages the auth window can render (SPEC-007 §12, UI-080).
/// Built once per process: both inputs — the locale files loaded by the localizer and the
/// configured base locale — are process-constant.
/// </summary>
internal interface IAuthPageLanguageRegistry
{
    /// <summary>
    /// Loaded locales ∪ { base locale }. Used by language detection.
    /// </summary>
    IReadOnlySet<string> SupportedLanguages { get; }

    /// <summary>
    /// Detection registry minus the pseudo-locale. Used by DefaultLanguage validation.
    /// </summary>
    IReadOnlySet<string> ConfigurableLanguages { get; }
}
