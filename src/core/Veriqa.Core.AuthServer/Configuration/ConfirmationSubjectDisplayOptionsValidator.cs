// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Configuration.Enums;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Startup validation of the subject-display surfaces (SPEC-012 CFG-108): a surface name the product
/// does not know stops the host.
/// </summary>
/// <remarks>
/// It is fatal rather than ignorable because the two readings of a misspelt value are opposite: the
/// operator believes the subject is being shown somewhere, and the deployment shows it nowhere. The
/// question is asked of the configuration TEXT and not only of the bound object, for the same reason
/// the comparable identity types are (CFG-160): a value the binder cannot convert leaves the bound
/// list looking like a deliberate empty set, which is exactly the silence this refuses.
/// </remarks>
internal sealed class ConfirmationSubjectDisplayOptionsValidator
    : IValidateOptions<ConfirmationSubjectDisplayOptions>
{
    /// <summary>
    /// Address of the stated set inside the host configuration.
    /// </summary>
    private static readonly string SurfacesPath =
        ConfirmationSubjectDisplayOptions.SectionName
        + ":" + nameof(ConfirmationSubjectDisplayOptions.Surfaces);

    /// <summary>
    /// Host configuration — read for the one question the bound object can no longer answer: what the
    /// operator actually wrote.
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Creates the validator over the configuration the options are bound from.
    /// </summary>
    /// <param name="configuration">Host configuration.</param>
    public ConfirmationSubjectDisplayOptionsValidator(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ConfirmationSubjectDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        // A value outside the enumeration reaches a bound object only as a raw number; the named
        // spellings are judged below, on the text the deployment wrote.
        for (var index = 0; index < options.Surfaces.Count; index++)
        {
            if (!Enum.IsDefined(options.Surfaces[index]))
            {
                failures.Add(
                    $"{SurfacesPath}[{index}]: '{options.Surfaces[index]}' is not a known confirmation "
                    + "subject display surface.");
            }
        }

        failures.AddRange(UnknownStatedSurfaces());

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Surfaces the deployment STATED that the enumeration does not carry.
    /// </summary>
    /// <returns>One message per stated value outside the closed set, naming the value and the setting.</returns>
    private IEnumerable<string> UnknownStatedSurfaces()
    {
        foreach (var entry in _configuration.GetSection(SurfacesPath).GetChildren())
        {
            var stated = entry.Value;

            if (string.IsNullOrWhiteSpace(stated))
            {
                continue;
            }

            // TryParse accepts a raw number as well, so what it produced still has to BE a member:
            // "Surfaces: [ 7 ]" is as unknown as "Surfaces: [ Whatever ]".
            if (!Enum.TryParse<ConfirmationSubjectSurface>(stated, ignoreCase: true, out var surface)
                || !Enum.IsDefined(surface))
            {
                yield return
                    $"{SurfacesPath}[{entry.Key}]: '{stated}' is not a known confirmation subject "
                    + "display surface.";
            }
        }
    }
}
