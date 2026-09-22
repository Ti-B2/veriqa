// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Veriqa.Core.AuthServer.Data;

namespace Veriqa.Core.AuthServer.Migrations.SqlServer;

/// <summary>
/// DbContext factory for EF Core design-time tools (dotnet ef migrations) —
/// OpenIddict store, SQL Server.
/// Reads the connection string from OPENIDDICT_DB_CONNECTION_STRING;
/// if the variable is not set, uses a placeholder for local tooling
/// (generating migrations does not require a database connection).
/// </summary>
internal sealed class SqlServerOpenIddictDbContextFactory
    : IDesignTimeDbContextFactory<OpenIddictDbContext>
{
    /// <summary>
    /// Name of the environment variable holding the connection string.
    /// </summary>
    private const string ConnectionStringEnvVariable = "OPENIDDICT_DB_CONNECTION_STRING";

    /// <summary>
    /// Default connection string for design-time tooling (placeholder without credentials).
    /// The real string is provided via OPENIDDICT_DB_CONNECTION_STRING.
    /// </summary>
    private const string DefaultDesignTimeConnectionString =
        "Server=localhost;Database=veriqa_openiddict;Integrated Security=true;TrustServerCertificate=true";

    /// <inheritdoc />
    public OpenIddictDbContext CreateDbContext(string[] args)
    {
        // Read the connection string from the environment variable or use the placeholder
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVariable)
            ?? DefaultDesignTimeConnectionString;

        var builder = new DbContextOptionsBuilder<OpenIddictDbContext>();

        // Migrations live in this assembly — specify it explicitly
        builder.UseSqlServer(
            connectionString,
            sqlServer => sqlServer.MigrationsAssembly(
                typeof(SqlServerOpenIddictDbContextFactory).Assembly.GetName().Name!));

        return new OpenIddictDbContext(builder.Options);
    }
}
