// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

namespace Veriqa.Core.TransactionEngine.Common;

/// <summary>
/// Shared runner for applying EF Core migrations at startup with retry and exponential backoff.
/// Used by the schema initialization hosted services (OpenIddict database, transaction store),
/// so the retry policy lives in one place instead of being duplicated in each service.
/// Transient connection errors are detected via <see cref="DbTransientErrorDetector"/>.
/// </summary>
public static class MigrationRetryRunner
{
    /// <summary>
    /// Maximum number of migration application attempts.
    /// </summary>
    public const int MaxRetries = 5;

    /// <summary>
    /// Initial delay before a retry (seconds). Grows exponentially: 2, 4, 8, 16.
    /// </summary>
    public const double InitialRetryDelaySeconds = 2.0;

    /// <summary>
    /// Exponential backoff multiplier between attempts.
    /// </summary>
    public const double RetryBackoffMultiplier = 2.0;

    /// <summary>
    /// Executes <paramref name="migrateAsync"/> with retry: on a transient connection error
    /// (the database is not yet ready when the container starts) retries with exponential delay;
    /// rethrows a non-transient error (access rights, invalid schema) immediately without retries;
    /// when attempts are exhausted, throws InvalidOperationException, stopping the host.
    /// </summary>
    /// <param name="migrateAsync">Migration delegate (creates a context and calls MigrateAsync).</param>
    /// <param name="logger">Logger of the calling service.</param>
    /// <param name="operationName">Operation name for logs, phrased as a possessive (e.g. "the transaction store").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task RunWithRetryAsync(
        Func<CancellationToken, Task> migrateAsync,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migrateAsync);
        ArgumentNullException.ThrowIfNull(logger);

        var delaySeconds = InitialRetryDelaySeconds;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await migrateAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal host shutdown — not an initialization error, do not retry
                throw;
            }
            catch (Exception ex) when (!DbTransientErrorDetector.IsTransient(ex))
            {
                // Non-transient error (access rights, invalid schema) — rethrow immediately without retry
                logger.LogError(
                    ex,
                    "Initialization of {Operation} failed with a non-transient error (attempt {Attempt}/{MaxRetries}). No retry",
                    operationName,
                    attempt,
                    MaxRetries);
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;

                if (attempt < MaxRetries)
                {
                    logger.LogWarning(
                        ex,
                        "Error initializing {Operation} (attempt {Attempt}/{MaxRetries}). Retrying in {Delay} s.",
                        operationName,
                        attempt,
                        MaxRetries,
                        delaySeconds);

                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    delaySeconds *= RetryBackoffMultiplier;
                }
            }
        }

        // All transient attempts exhausted — stop the application
        logger.LogCritical(
            lastException,
            "Initialization of {Operation} not completed after {MaxRetries} attempts",
            operationName,
            MaxRetries);

        throw new InvalidOperationException(
            $"Failed to initialize {operationName} after {MaxRetries} attempt(s)",
            lastException);
    }
}
