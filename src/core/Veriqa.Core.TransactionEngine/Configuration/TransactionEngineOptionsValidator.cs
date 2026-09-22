// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

namespace Veriqa.Core.TransactionEngine.Configuration;

/// <summary>
/// Transaction Engine configuration validator.
/// Performs critical validation at startup via ValidateOnStart.
/// </summary>
internal sealed class TransactionEngineOptionsValidator : IValidateOptions<TransactionEngineOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TransactionEngineOptions options)
    {
        // Validate all parameters against their allowed ranges
        var failures = new List<string>();

        if (options.TransactionTtlSeconds is < TransactionEngineOptions.MinTransactionTtlSeconds
            or > TransactionEngineOptions.MaxTransactionTtlSeconds)
        {
            failures.Add(
                $"TransactionTtlSeconds must be in the range {TransactionEngineOptions.MinTransactionTtlSeconds}–{TransactionEngineOptions.MaxTransactionTtlSeconds}, current value: {options.TransactionTtlSeconds}");
        }

        if (options.ConfirmationFinalizationTimeoutSeconds is < 10 or > 120)
        {
            failures.Add($"ConfirmationFinalizationTimeoutSeconds must be in the range 10–120, current value: {options.ConfirmationFinalizationTimeoutSeconds}");
        }

        if (options.CompletedRetentionSeconds is < 60 or > 3600)
        {
            failures.Add($"CompletedRetentionSeconds must be in the range 60–3600, current value: {options.CompletedRetentionSeconds}");
        }

        if (options.CleanupIntervalSeconds is < 10 or > 300)
        {
            failures.Add($"CleanupIntervalSeconds must be in the range 10–300, current value: {options.CleanupIntervalSeconds}");
        }

        if (options.CleanupBatchSize is < 1 or > 1000)
        {
            failures.Add($"CleanupBatchSize must be in the range 1–1000, current value: {options.CleanupBatchSize}");
        }

        if (options.CleanupErrorRetryDelaySeconds is < 1 or > 300)
        {
            failures.Add($"CleanupErrorRetryDelaySeconds must be in the range 1–300, current value: {options.CleanupErrorRetryDelaySeconds}");
        }

        if (options.MaxInMemoryEntries is < 100 or > 100_000)
        {
            failures.Add($"MaxInMemoryEntries must be in the range 100–100000, current value: {options.MaxInMemoryEntries}");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
