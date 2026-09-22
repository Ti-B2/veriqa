// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Veriqa.Core.AuditTrail.Retention;
using Veriqa.Core.AuditTrail.Store.EfCore;
using Veriqa.Core.Contracts.Audit;

namespace Veriqa.Core.AuditTrail.DependencyInjection;

/// <summary>EF Core sink registration extensions for <see cref="AuditTrailBuilder"/>.</summary>
/// <remarks>
/// The namespace is the one the builder itself lives in, so a sink in a package of its own costs the
/// caller a package reference and no additional <c>using</c>: <c>audit.UseEfCoreSink(...)</c> resolves
/// wherever the builder's namespace is already imported.
/// </remarks>
public static class AuditTrailBuilderEntityFrameworkCoreExtensions
{
    /// <summary>
    /// Uses the EF Core sink. The provider, the connection string and the migrations assembly
    /// belong to the caller (host-owned), the satellite stays provider-agnostic:
    /// <code>
    /// audit.UseEfCoreSink(ef => ef.UseNpgsql(connectionString,
    ///     npgsql => npgsql.MigrationsAssembly("Veriqa.Core.AuditTrail.Migrations.PostgreSql")));
    /// </code>
    /// Registers the DbContext factory, the sink and the migration service (MigrateAsync).
    /// </summary>
    /// <param name="audit">Audit trail builder.</param>
    /// <param name="configure">Action configuring EF Core (UseNpgsql etc.).</param>
    /// <returns>Builder for chaining.</returns>
    public static AuditTrailBuilder UseEfCoreSink(
        this AuditTrailBuilder audit,
        Action<DbContextOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(configure);

        audit.Services.AddDbContextFactory<AuditDbContext>(configure);

        // Single registration of the concrete type; the abstractions are factories over it
        // (see AuditTrailBuilder.UseInMemorySink for why).
        audit.Services.AddSingleton<EfCoreAuditSink>();
        audit.Services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<EfCoreAuditSink>());
        audit.Services.AddSingleton<IAuditRetentionStore>(sp => sp.GetRequiredService<EfCoreAuditSink>());

        audit.Services.AddHostedService<AuditMigrationService>();

        audit.BuiltInSinkRegistered = true;

        return audit;
    }
}
