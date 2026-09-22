// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

using Microsoft.Extensions.Options;

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.Configuration;

/// <summary>
/// Start-up validation of the initiator context section (SPEC-017 §10): a field name or an anomaly
/// signal the product does not know stops the host.
/// </summary>
/// <remarks>
/// Both settings are CLOSED sets of names the product ships, and a name outside them is
/// not a narrower configuration — it is a value nothing will ever match. The reading of a misspelt
/// entry is the opposite of what happens: the operator believes a field is shown in the confirmation
/// and the deployment shows it nowhere, because the readers ask the set for a name they spell
/// themselves. The comparison is case-insensitive, exactly as the reader of the set compares
/// (<c>StringComparer.OrdinalIgnoreCase</c>), so a differently-cased spelling is accepted here for
/// the same reason it works there.
/// <para>
/// The GeoIP provider name of the same section is deliberately not judged here: a provider the
/// product does not recognize leaves the geolocation unavailable, and the deployment already learns
/// that from the start-up diagnostics of the initiator context, which reports the unavailable
/// provider by name. A refusal on top of an existing report would be a second answer to one question.
/// </para>
/// </remarks>
internal sealed class InitiatorContextOptionsValidator : IValidateOptions<InitiatorContextOptions>
{
    /// <summary>
    /// Field names the confirmation is able to show (SPEC-017 §10, ICC-015): the application name plus
    /// the fields of <see cref="InitiatorContextFieldTable"/>.
    /// </summary>
    private static readonly FrozenSet<string> KnownDisplayFields = InitiatorContextFieldTable.Rows
        .Select(row => row.Name)
        .Append(InitiatorContextFields.DisplayFields.Application)
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Signals the anomaly heuristic is able to compare (SPEC-017 §9).
    /// </summary>
    private static readonly FrozenSet<string> KnownAnomalySignals = new[]
    {
        InitiatorContextFields.AnomalySignals.Country,
        InitiatorContextFields.AnomalySignals.DeviceType
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Address of the displayed field set inside the host configuration.
    /// </summary>
    private static readonly string DisplayFieldsPath =
        InitiatorContextOptions.SectionName + ":" + nameof(InitiatorContextOptions.DisplayFields);

    /// <summary>
    /// Address of the anomaly signal set inside the host configuration.
    /// </summary>
    private static readonly string SignalsPath =
        InitiatorContextOptions.SectionName
        + ":" + nameof(InitiatorContextOptions.AnomalyDetection)
        + ":" + nameof(InitiatorAnomalyDetectionOptions.Signals);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, InitiatorContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        failures.AddRange(UnknownNames(
            options.DisplayFields, KnownDisplayFields, DisplayFieldsPath, "initiator context display field"));

        failures.AddRange(UnknownNames(
            options.AnomalyDetection.Signals, KnownAnomalySignals, SignalsPath, "initiator context anomaly signal"));

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Names of a stated set that the closed set of the product does not carry.
    /// </summary>
    /// <param name="stated">Names the deployment stated.</param>
    /// <param name="known">Names the product knows.</param>
    /// <param name="path">Address of the set inside the host configuration.</param>
    /// <param name="subject">What a name of this set denotes, for the message.</param>
    /// <returns>One message per unknown name, naming the value, its address and the known names.</returns>
    private static IEnumerable<string> UnknownNames(
        IReadOnlyList<string> stated,
        FrozenSet<string> known,
        string path,
        string subject)
    {
        for (var index = 0; index < stated.Count; index++)
        {
            var value = stated[index];

            // A blank entry is judged with the rest rather than skipped: it names nothing, so the
            // reader matches it against nothing, and it is as much a mistake as a misspelt name.
            if (string.IsNullOrWhiteSpace(value) || !known.Contains(value))
            {
                yield return
                    $"{path}[{index}]: '{value}' is not a known {subject}. "
                    + $"Known values: {string.Join(", ", known.Order(StringComparer.Ordinal))}.";
            }
        }
    }
}
