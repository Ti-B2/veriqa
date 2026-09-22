// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Veriqa.Core.AuthServer.Configuration.Enums;
using Veriqa.Core.AuthServer.UI;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Validator of the root Veriqa configuration (SPEC-012 §8.1).
/// Performs critical checks at application startup via ValidateOnStart.
/// <para>
/// The type is internal: it is consumed only through the public IValidateOptions&lt;VeriqaOptions&gt;
/// contract it implements, and it is registered by AddVeriqaConfiguration, which is internal to this
/// assembly. That registration constructs the type itself rather than letting the container activate
/// it: the configuration instance the validator judges is the one the integrator handed us, not
/// whatever the container resolves for IConfiguration.
/// </para>
/// </summary>
internal sealed class VeriqaOptionsValidator : IValidateOptions<VeriqaOptions>
{
    /// <summary>
    /// Address of the declared scopes inside the host configuration.
    /// </summary>
    private static readonly string ScopesPath =
        ScopesClaimsOptions.SectionName + ":" + nameof(ScopesClaimsOptions.Scopes);

    /// <summary>
    /// Process-wide registry of the auth-window languages.
    /// </summary>
    private readonly IAuthPageLanguageRegistry _languageRegistry;

    /// <summary>
    /// Host configuration — read for the ONE question the bound object can no longer answer: what the
    /// operator actually wrote for the source of a claim.
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Creates the root configuration validator.
    /// </summary>
    /// <param name="languageRegistry">Registry of the auth-window languages.</param>
    /// <param name="configuration">Host configuration the options are bound from.</param>
    public VeriqaOptionsValidator(IAuthPageLanguageRegistry languageRegistry, IConfiguration configuration)
    {
        _languageRegistry = languageRegistry;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Validates the root Veriqa configuration.
    /// </summary>
    /// <param name="name">Options instance name.</param>
    /// <param name="options">Options instance to validate.</param>
    /// <returns>Validation result.</returns>
    public ValidateOptionsResult Validate(string? name, VeriqaOptions options)
    {
        // The method performs critical configuration checks

        var failures = new List<string>();

        // ScopesClaimsOptions validation: no duplicate scope names
        ValidateScopesClaims(options.ScopesClaims, failures);

        // Reserved (experimental) ScopesClaims flags must not be silently dead (EM-171 precedent)
        ValidateReservedScopesClaims(options.ScopesClaims, failures);

        // The source of a claim is judged on the TEXT the deployment wrote (SPEC-012 §8.2)
        failures.AddRange(UnknownStatedClaimSources());

        // Default language validation: must be in the supported languages registry
        ValidateLocalization(options.Localization, failures);

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Checks the uniqueness of scope names.
    /// </summary>
    /// <param name="scopesClaims">Scopes and claims settings.</param>
    /// <param name="failures">Failure list to append to.</param>
    private static void ValidateScopesClaims(ScopesClaimsOptions scopesClaims, List<string> failures)
    {
        // The method checks for duplicate scope names
        // Normalization (Scopes ??= []) is done in VeriqaOptionsPostConfigure

        if (scopesClaims.Scopes.Count is 0)
        {
            return;
        }

        var scopeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in scopesClaims.Scopes)
        {
            if (string.IsNullOrWhiteSpace(scope.Name))
            {
                failures.Add("Veriqa:ScopesClaims:Scopes contains a scope with an empty name.");
                continue;
            }

            if (!scopeNames.Add(scope.Name))
            {
                failures.Add($"Veriqa:ScopesClaims:Scopes contains a duplicate scope name '{scope.Name}'.");
            }
        }
    }

    /// <summary>
    /// Claim sources the deployment STATED that the enumeration does not carry.
    /// </summary>
    /// <remarks>
    /// The question is asked of the configuration TEXT and not of the bound object on purpose: a claim
    /// is an element of a collection, and the binder drops an element it cannot convert — an entry
    /// saying <c>Source: Bogus</c> takes the whole claim out of the scope before anything of ours sees
    /// it, so the deployment silently loses a claim it declared. A raw NUMBER is the other half of the
    /// same mistake: it converts into a member that does not exist, which is why what the parse
    /// produced still has to BE a member. A deployment that configures the options in code states no
    /// text here and nothing is judged.
    /// </remarks>
    /// <returns>One message per stated source that is not a member of the closed set.</returns>
    private IEnumerable<string> UnknownStatedClaimSources()
    {
        foreach (var scope in _configuration.GetSection(ScopesPath).GetChildren())
        {
            foreach (var claim in scope.GetSection(nameof(ScopeDefinition.Claims)).GetChildren())
            {
                var stated = claim[nameof(ClaimDefinition.Source)];

                if (string.IsNullOrWhiteSpace(stated))
                {
                    continue;
                }

                if (!Enum.TryParse<ClaimSource>(stated, ignoreCase: true, out var source)
                    || !Enum.IsDefined(source))
                {
                    yield return
                        $"{ScopesPath}[{scope.Key}]:{nameof(ScopeDefinition.Claims)}[{claim.Key}]:"
                        + $"{nameof(ClaimDefinition.Source)} is '{stated}', which is not a known claim "
                        + $"source. Allowed values: {string.Join(", ", Enum.GetNames<ClaimSource>())}.";
                }
            }
        }
    }

    /// <summary>
    /// Fails the start when a reserved (experimental) ScopesClaims flag is enabled (SPEC-016 EM-171
    /// precedent: a reserved contract whose logic is not implemented must not be silently dead).
    /// <c>RequirePhone</c>/<c>RequireEmail</c> are declared in the config model but not yet consumed by
    /// the core (not yet activated); enabling one now would silently do nothing. A hard error is
    /// chosen over a warning so the misconfiguration is not missed by operators who do not read logs.
    /// </summary>
    /// <param name="scopesClaims">Scopes and claims settings.</param>
    /// <param name="failures">Failure list to append to.</param>
    private static void ValidateReservedScopesClaims(ScopesClaimsOptions scopesClaims, List<string> failures)
    {
        if (scopesClaims.RequirePhone)
        {
            failures.Add(
                "Veriqa:ScopesClaims:RequirePhone is reserved (contract declared, not implemented yet) — "
                + "remove it from configuration until it is activated.");
        }

        if (scopesClaims.RequireEmail)
        {
            failures.Add(
                "Veriqa:ScopesClaims:RequireEmail is reserved (contract declared, not implemented yet) — "
                + "remove it from configuration until it is activated.");
        }
    }

    /// <summary>
    /// Checks that the default language is in the data-driven supported languages registry
    /// (SPEC-007 UI-080). The registry is derived from the locale files actually shipped by the
    /// host, so a language claimed in the configuration but not backed by data fails the start
    /// (fail-fast, SPEC-012 §8.1) instead of silently rendering an English page.
    /// The pseudo-locale is excluded: it is a layout test locale, not a configurable default.
    /// </summary>
    /// <param name="localization">Localization settings.</param>
    /// <param name="failures">Failure list to append to.</param>
    private void ValidateLocalization(LocalizationOptions localization, List<string> failures)
    {
        // The method checks that the default language code is valid
        // Normalization (Localization ??= new) is done in VeriqaOptionsPostConfigure

        var configurableLanguages = _languageRegistry.ConfigurableLanguages;

        // Ordinal membership, the configured value is not normalized — an unexpected casing ("RU")
        // is a configuration defect and is reported as such, not silently accepted.
        if (localization.DefaultLanguage is null
            || !configurableLanguages.Contains(localization.DefaultLanguage))
        {
            var supported = string.Join(", ", configurableLanguages.Order(StringComparer.Ordinal));
            failures.Add(
                $"Veriqa:Localization:DefaultLanguage contains an unsupported language " +
                $"'{localization.DefaultLanguage}'. Allowed values: {supported}.");
        }
    }
}
