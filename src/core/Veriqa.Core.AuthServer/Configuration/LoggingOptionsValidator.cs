// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Validator of the logging options. Retention is a critical setting, and it fails in both
/// directions: a non-positive value would make the retention process delete everything it sweeps,
/// while an implausibly large one (a date typed in place of a number of days) puts the cutoff out of
/// the calendar range and silently disables the sweep forever. The application refuses to start on
/// either instead of destroying the journal or never trimming it.
/// </summary>
public sealed class LoggingOptionsValidator : IValidateOptions<LoggingOptions>
{
    /// <summary>
    /// Configuration key of the retention setting, as it is written in configuration —
    /// the failure message must name the key the operator has to fix.
    /// </summary>
    private const string RetentionDaysConfigurationKey = $"{LoggingOptions.SectionName}:RetentionDays";

    /// <summary>
    /// Upper bound of the retention, in days (a hundred years). It is deliberately far above any
    /// retention an operator may legitimately require, and far below the values a mistyped date
    /// produces.
    /// </summary>
    private const int MaxRetentionDays = 36_500;

    /// <summary>
    /// Validates the logging options.
    /// </summary>
    /// <param name="name">Name of the options instance.</param>
    /// <param name="options">Options instance to validate.</param>
    /// <returns>Validation result.</returns>
    public ValidateOptionsResult Validate(string? name, LoggingOptions options)
    {
        if (options.RetentionDays <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"'{RetentionDaysConfigurationKey}' must be greater than 0, but is {options.RetentionDays}.");
        }

        if (options.RetentionDays > MaxRetentionDays)
        {
            return ValidateOptionsResult.Fail(
                $"'{RetentionDaysConfigurationKey}' must not exceed {MaxRetentionDays} days, "
                + $"but is {options.RetentionDays}. The value is a number of days, not a date.");
        }

        return ValidateOptionsResult.Success;
    }
}
