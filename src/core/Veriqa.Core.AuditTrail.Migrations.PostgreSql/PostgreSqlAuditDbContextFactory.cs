// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Veriqa.Core.AuditTrail.Store.EfCore;

namespace Veriqa.Core.AuditTrail.Migrations.PostgreSql;

/// <summary>
/// DbContext factory for EF Core design-time tools (dotnet ef migrations) — PostgreSQL.
/// Reads the connection string from AUDIT_DB_CONNECTION_STRING;
/// if the variable is not set — uses a placeholder for local tooling
/// (no database connection is required to generate migrations).
/// </summary>
internal sealed class PostgreSqlAuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    /// <summary>
    /// Name of the environment variable holding the connection string.
    /// </summary>
    private const string ConnectionStringEnvVariable = "AUDIT_DB_CONNECTION_STRING";

    /// <summary>
    /// Default connection string for design-time tooling (placeholder, not a secret).
    /// The real string is provided via AUDIT_DB_CONNECTION_STRING.
    /// </summary>
    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Database=veriqa_transactions;Username=postgres;Password=dev-local";

    /// <inheritdoc />
    public AuditDbContext CreateDbContext(string[] args)
    {
        // Read the connection string from the environment variable or use the placeholder
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVariable)
            ?? DefaultDesignTimeConnectionString;

        var builder = new DbContextOptionsBuilder<AuditDbContext>();

        // Migrations live in this assembly — specify it explicitly. The history table is the one
        // the runtime uses (the journal shares its database with the transaction store): named
        // differently, the tooling and the application would each keep their own history and apply
        // the same migration twice.
        builder.UseNpgsql(
            connectionString,
            npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(PostgreSqlAuditDbContextFactory).Assembly.GetName().Name!);
                npgsql.MigrationsHistoryTable(AuditMigrationDefaults.MigrationsHistoryTable);
            });

        return new AuditDbContext(builder.Options);
    }
}
