// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

namespace Veriqa.Core.AuthServer.UiConfig;

/// <summary>
/// Start-up validation of the ui_config catalog of a self-hosted deployment (SPEC-012 CFG-203,
/// SPEC-002 §4.6): a record that no request can ever select, or one the store will refuse to read,
/// stops the host.
/// </summary>
/// <remarks>
/// The catalog is the DEPLOYMENT'S OWN configuration, and both faults judged here are invisible
/// afterwards: a record under a blank code is unreachable, because a blank selector is rejected
/// before the catalog is even consulted, and a record whose schema version is stated below the first
/// one is silently degraded to the empty record at read time — the page renders with the global look
/// and the operator sees a styling that "did not apply" rather than a fault.
/// <para>
/// What a typo in the CODE ITSELF states cannot be judged here and is not attempted: a code is a
/// tenant artifact, valid exactly when a client entry lists it in <c>AllowedUiConfigs</c>, and a
/// catalog record for a code no client lists is a legitimate state — the code is simply not assigned
/// yet.
/// </para>
/// <para>
/// The run-time degradation stays where it is, and this pass does not make it unreachable: the
/// catalog is read through <c>IOptionsMonitor</c>, so an edit of the section after start reaches the
/// next read without passing through start-up validation again.
/// </para>
/// </remarks>
internal sealed class UiConfigurationsOptionsValidator : IValidateOptions<UiConfigurationsOptions>
{
    /// <summary>
    /// Address of the record catalog inside the host configuration.
    /// </summary>
    private static readonly string RecordsPath =
        UiConfigurationsOptions.SectionName + ":" + nameof(UiConfigurationsOptions.Records);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, UiConfigurationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        foreach (var (code, record) in options.Records)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                failures.Add(
                    $"{RecordsPath}: a record is stated under a blank code, and no request can select "
                    + "it — a blank ui_config selector is rejected before the catalog is read.");

                continue;
            }

            if (record is not null && !record.IsSchemaVersionValid())
            {
                failures.Add(
                    $"{RecordsPath}:{code}:{nameof(UiConfigRecord.SchemaVersion)}: "
                    + $"'{record.SchemaVersion}' is below the first schema version "
                    + $"({UiConfigRecord.SchemaVersionV1}), so the record is ignored at read time and "
                    + "the application keeps the global look.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
