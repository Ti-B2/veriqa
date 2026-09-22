// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;

namespace Veriqa.Core.AuditTrail.Store.EfCore;

/// <summary>
/// DbContext of the audit journal.
/// Public as an anchor for migrations assemblies and design-time factories (<c>[DbContext]</c>,
/// <c>IDesignTimeDbContextFactory&lt;T&gt;</c>) of any relational provider — not as a data access API:
/// the record set stays internal, and the journal is written through <c>IAuditSink</c>.
/// Used at runtime only via IDbContextFactory, so the sink can stay a singleton.
/// The satellite is provider-agnostic: the concrete provider, the connection string and the
/// migrations assembly are supplied by the host when it selects the EF Core sink.
/// The schema is created by migrations only: creating it straight from the model would leave the
/// deployment without a migration history.
/// </summary>
public sealed class AuditDbContext : DbContext
{
    /// <summary>
    /// Audit records stored in the database.
    /// </summary>
    internal DbSet<AuditRecordEntity> AuditRecords { get; init; } = null!;

    /// <summary>
    /// Creates a context instance with the given options.
    /// </summary>
    /// <param name="options">DbContext options.</param>
    public AuditDbContext(DbContextOptions<AuditDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            // The retention sweep selects by age, and it is the only query the journal serves.
            entity.HasIndex(e => e.Timestamp);
        });
    }
}
