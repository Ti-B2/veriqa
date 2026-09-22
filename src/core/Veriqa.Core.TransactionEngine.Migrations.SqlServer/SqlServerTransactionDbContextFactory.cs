// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Veriqa.Core.TransactionEngine.Store.EfCore;

namespace Veriqa.Core.TransactionEngine.Migrations.SqlServer;

/// <summary>
/// DbContext factory for EF Core design-time tools (dotnet ef migrations) — SQL Server.
/// Reads the connection string from TRANSACTION_DB_CONNECTION_STRING;
/// if the variable is not set — uses a placeholder for local tooling
/// (no database connection is required to generate migrations).
/// </summary>
internal sealed class SqlServerTransactionDbContextFactory
    : IDesignTimeDbContextFactory<TransactionDbContext>
{
    /// <summary>
    /// Name of the environment variable holding the connection string.
    /// </summary>
    private const string ConnectionStringEnvVariable = "TRANSACTION_DB_CONNECTION_STRING";

    /// <summary>
    /// Default connection string for design-time tooling (placeholder without credentials).
    /// The real string is provided via TRANSACTION_DB_CONNECTION_STRING.
    /// </summary>
    private const string DefaultDesignTimeConnectionString =
        "Server=localhost;Database=veriqa_transactions;Integrated Security=true;TrustServerCertificate=true";

    /// <inheritdoc />
    public TransactionDbContext CreateDbContext(string[] args)
    {
        // Read the connection string from the environment variable or use the placeholder
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVariable)
            ?? DefaultDesignTimeConnectionString;

        var builder = new DbContextOptionsBuilder<TransactionDbContext>();

        // Migrations live in this assembly — specify it explicitly
        builder.UseSqlServer(
            connectionString,
            sqlServer => sqlServer.MigrationsAssembly(
                typeof(SqlServerTransactionDbContextFactory).Assembly.GetName().Name!));

        return new TransactionDbContext(builder.Options);
    }
}
