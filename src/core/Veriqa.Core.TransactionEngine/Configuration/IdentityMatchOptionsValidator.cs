// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Veriqa.Core.TransactionEngine.Configuration;

/// <summary>
/// Startup validation of the comparable identity types (SPEC-012 CFG-160). A declaration that is
/// silently ignored would leave the operator believing a comparison is in effect that is not, so the
/// host refuses to start instead.
/// </summary>
internal sealed class IdentityMatchOptionsValidator : IValidateOptions<IdentityMatchOptions>
{
    /// <summary>
    /// Address of the declared set inside the host configuration.
    /// </summary>
    private static readonly string ComparableTypesPath =
        IdentityMatchOptions.SectionName + ":" + nameof(IdentityMatchOptions.ComparableTypes);

    /// <summary>
    /// Host configuration — read for the ONE question the bound object can no longer answer: what the
    /// operator actually wrote for the normalization rule.
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Creates the validator over the configuration the options are bound from.
    /// </summary>
    /// <param name="configuration">Host configuration.</param>
    public IdentityMatchOptionsValidator(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IdentityMatchOptions options)
    {
        // The three things CFG-160 makes fatal: a repeated type name, an empty name or claim name,
        // and a normalization rule outside the closed set the core ships.
        var failures = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < options.ComparableTypes.Count; index++)
        {
            var declared = options.ComparableTypes[index];
            var position = $"{ComparableTypesPath}[{index}]";

            if (string.IsNullOrWhiteSpace(declared.Name))
            {
                failures.Add($"{position}: Name must not be empty.");
            }
            else if (!seen.Add(declared.Name))
            {
                failures.Add(
                    $"{position}: comparable identity type '{declared.Name}' is declared more than once.");
            }

            if (string.IsNullOrWhiteSpace(declared.ClaimName))
            {
                failures.Add(
                    $"{position}: ClaimName must not be empty for comparable identity type '{declared.Name}'.");
            }

            // A rule outside the enumeration reaches a bound object only as a raw numeric value; the
            // named spellings are judged below, on the text the deployment wrote.
            if (!Enum.IsDefined(declared.Normalization))
            {
                failures.Add(
                    $"{position}: unknown Normalization rule for comparable identity type '{declared.Name}'.");
            }
        }

        failures.AddRange(UnknownStatedRules());

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Normalization rules the deployment STATED and the enumeration does not carry.
    /// </summary>
    /// <remarks>
    /// The question is asked of the configuration text and not of the bound object on purpose: the
    /// binder drops a value it cannot convert, so an entry saying <c>Normalization: Loose</c> arrives
    /// here already reading as the default <c>Exact</c> — the strictest rule, and therefore a silent
    /// change of meaning nobody would notice. CFG-160 requires the opposite: the start stops. A
    /// deployment that configures the options in code states no text here and is judged by the check
    /// on the bound value above.
    /// </remarks>
    /// <returns>One message per stated rule that is not a member of the closed set.</returns>
    private IEnumerable<string> UnknownStatedRules()
    {
        foreach (var entry in _configuration.GetSection(ComparableTypesPath).GetChildren())
        {
            var stated = entry[nameof(ComparableIdentityType.Normalization)];

            if (string.IsNullOrWhiteSpace(stated))
            {
                continue;
            }

            // TryParse accepts a raw number as well, so what it produced still has to BE a member —
            // "Normalization: 7" is as unknown as "Normalization: Loose".
            if (!Enum.TryParse<IdentityNormalizationRule>(stated, ignoreCase: true, out var rule)
                || !Enum.IsDefined(rule))
            {
                yield return
                    $"{ComparableTypesPath}[{entry.Key}]: '{stated}' is not a known Normalization rule.";
            }
        }
    }
}
