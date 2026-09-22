// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;
using Veriqa.Core.AuthServer.Data;
using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Hosted service that automatically applies EF Core migrations at startup.
/// All relational providers (PostgreSQL, MySQL) share a single MigrateAsync path with migration history;
/// EnsureCreatedAsync is not used (upgrade-safety). InMemory: does nothing.
/// </summary>
internal sealed class DatabaseMigrationService : IHostedService
{
    /// <summary>
    /// Service provider for creating a scope.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<DatabaseMigrationService> _logger;

    /// <summary>
    /// Creates an instance of the migration service.
    /// </summary>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="logger">Logger.</param>
    public DatabaseMigrationService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseMigrationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Operation name for the migration runner logs.
    /// </summary>
    private const string OperationName = "the OpenIddict database";

    /// <summary>
    /// Applies migrations at startup for relational providers (PostgreSQL, MySQL).
    /// Retries with exponential backoff on transient connection errors (typical when the
    /// database container starts up in Aspire) — the shared retry policy lives in MigrationRetryRunner.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The context is built once for the whole startup. The provider is owned by the host
        // (host-owned, SPEC-001 §10.2), so the decision to migrate is taken from the context itself:
        // only a relational provider has a migration history. IsRelational() reads provider metadata —
        // no database connection is opened by this check — and the same context is reused for the run.
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenIddictDbContext>();

        // A non-relational provider (InMemory) creates the schema automatically — nothing to do here,
        // and no database connection is opened.
        if (!dbContext.Database.IsRelational())
        {
            return;
        }

        _logger.LogInformation(
            "Initializing the OpenIddict database (applying migrations, provider {Provider})...",
            dbContext.Database.ProviderName);

        // A single path for all relational providers: migrations from the per-provider assembly.
        // Transient "database not ready yet" errors are retried on the same context (the connection
        // is reopened on each attempt); the shared retry policy lives in MigrationRetryRunner.
        await MigrationRetryRunner.RunWithRetryAsync(
            ct => dbContext.Database.MigrateAsync(ct),
            _logger,
            OperationName,
            cancellationToken);

        _logger.LogInformation("OpenIddict database initialization completed successfully");
    }

    /// <summary>
    /// Stopping requires no action.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
