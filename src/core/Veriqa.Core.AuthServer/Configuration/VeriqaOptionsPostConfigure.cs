// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;
using Veriqa.Core.AuthServer.UI;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// PostConfigure that normalizes the root Veriqa options.
/// Fills null collections with empty lists before validation runs.
/// Extracted from VeriqaOptionsValidator: normalization is the job of IPostConfigureOptions,
/// not IValidateOptions.
/// </summary>
public sealed class VeriqaOptionsPostConfigure : IPostConfigureOptions<VeriqaOptions>
{
    /// <summary>
    /// Normalizes the options: replaces null collections with empty lists.
    /// </summary>
    /// <param name="name">Named options instance name.</param>
    /// <param name="options">Options instance to normalize.</param>
    public void PostConfigure(string? name, VeriqaOptions options)
    {
        // Normalize nested sections — the configuration binder may return null
        // both for an explicit "SectionName": null and for a completely missing section
        // (when the parent object was overridden by the binder).

        options.ScopesClaims ??= new ScopesClaimsOptions();
        options.ScopesClaims.Scopes ??= [];

        options.ChannelDisplay ??= new ChannelDisplayOptions();
        options.ChannelDisplay.ChannelOrder ??= [];
        options.ChannelDisplay.Hints ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        options.Localization ??= new LocalizationOptions();

        // Normalize an empty/unset default language to English (the base language, TASK-057),
        // so an explicit null/"" in the configuration does not fail validation at startup.
        // The base language is always in the data-driven registry — it needs no locale file.
        if (string.IsNullOrWhiteSpace(options.Localization.DefaultLanguage))
        {
            options.Localization.DefaultLanguage = AuthPageStrings.LanguageEn;
        }
    }
}
