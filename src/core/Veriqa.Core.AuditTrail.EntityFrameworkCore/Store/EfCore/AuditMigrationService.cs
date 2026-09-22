// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.AuditTrail.Store.EfCore;

/// <summary>
/// Hosted service applying the audit journal's EF Core migrations at application startup.
/// Registered only when the host selects the EF Core sink. The retry policy is the shared one, so
/// a database that is not ready yet behaves exactly as it does for the other stores.
/// </summary>
/// <remarks>
/// Migrations live in a separate per-provider assembly
/// (Veriqa.Core.AuditTrail.Migrations.PostgreSql), named by the host when it configures the
/// provider. A new migration is generated into that assembly's directory:
/// <code>
/// dotnet ef migrations add &lt;MigrationName&gt;
///   --project src/core/Veriqa.Core.AuditTrail.Migrations.PostgreSql
/// </code>
/// </remarks>
internal sealed class AuditMigrationService : IHostedService
{
    /// <summary>
    /// Operation name for the migration runner logs (used as a possessive phrase).
    /// </summary>
    private const string OperationName = "the audit journal";

    /// <summary>
    /// DbContext factory for creating the context at startup.
    /// </summary>
    private readonly IDbContextFactory<AuditDbContext> _contextFactory;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<AuditMigrationService> _logger;

    /// <summary>
    /// Creates the migration service.
    /// </summary>
    /// <param name="contextFactory">DbContext factory.</param>
    /// <param name="logger">Logger.</param>
    public AuditMigrationService(
        IDbContextFactory<AuditDbContext> contextFactory,
        ILogger<AuditMigrationService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing the audit journal (applying EF Core migrations)...");

        await MigrationRetryRunner.RunWithRetryAsync(
            ApplyMigrationsAsync,
            _logger,
            OperationName,
            cancellationToken);

        _logger.LogInformation("Audit journal initialization completed successfully");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Creates a DbContext and applies migrations from the host-configured migrations assembly.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
    }
}
