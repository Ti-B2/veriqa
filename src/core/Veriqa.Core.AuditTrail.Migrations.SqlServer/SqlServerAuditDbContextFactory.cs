// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Veriqa.Core.AuditTrail.Store.EfCore;

namespace Veriqa.Core.AuditTrail.Migrations.SqlServer;

/// <summary>
/// DbContext factory for EF Core design-time tools (dotnet ef migrations) — SQL Server.
/// Reads the connection string from AUDIT_DB_CONNECTION_STRING;
/// if the variable is not set — uses a placeholder for local tooling
/// (no database connection is required to generate migrations).
/// </summary>
internal sealed class SqlServerAuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    /// <summary>
    /// Name of the environment variable holding the connection string.
    /// </summary>
    private const string ConnectionStringEnvVariable = "AUDIT_DB_CONNECTION_STRING";

    /// <summary>
    /// Default connection string for design-time tooling (placeholder without credentials).
    /// The real string is provided via AUDIT_DB_CONNECTION_STRING.
    /// </summary>
    private const string DefaultDesignTimeConnectionString =
        "Server=localhost;Database=veriqa_transactions;Integrated Security=true;TrustServerCertificate=true";

    /// <inheritdoc />
    public AuditDbContext CreateDbContext(string[] args)
    {
        // Read the connection string from the environment variable or use the placeholder
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVariable)
            ?? DefaultDesignTimeConnectionString;

        var builder = new DbContextOptionsBuilder<AuditDbContext>();

        // Migrations live in this assembly — specify it explicitly. The history table is the one
        // the runtime uses (the journal may share its database with the transaction store): named
        // differently, the tooling and the application would each keep their own history and apply
        // the same migration twice.
        builder.UseSqlServer(
            connectionString,
            sqlServer =>
            {
                sqlServer.MigrationsAssembly(typeof(SqlServerAuditDbContextFactory).Assembly.GetName().Name!);
                sqlServer.MigrationsHistoryTable(AuditMigrationDefaults.MigrationsHistoryTable);
            });

        return new AuditDbContext(builder.Options);
    }
}
