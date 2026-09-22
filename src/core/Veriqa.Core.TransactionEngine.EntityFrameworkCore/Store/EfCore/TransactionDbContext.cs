// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;

namespace Veriqa.Core.TransactionEngine.Store.EfCore;

/// <summary>
/// Transaction store DbContext.
/// Public as an anchor for migrations assemblies and design-time factories (<c>[DbContext]</c>,
/// <c>IDesignTimeDbContextFactory&lt;T&gt;</c>) of any relational provider — not as a data access API:
/// the entity set stays internal, and the store is reached through <c>ITransactionStore</c>.
/// Used at runtime only via IDbContextFactory for safe use in singleton services.
/// The model is provider-agnostic: the concrete provider (for example UseNpgsql) and the migrations
/// assembly are set by the host when calling UseEfCoreStore (SPEC-001 §10.2).
/// </summary>
public sealed class TransactionDbContext : DbContext
{
    /// <summary>
    /// Maximum length of the idempotency key columns: a bounded length keeps the columns indexable
    /// on every provider (an unbounded string maps to a type some providers refuse in an index key).
    /// </summary>
    private const int IdempotencyColumnMaxLength = 256;

    /// <summary>
    /// Set of transactions in the database.
    /// </summary>
    internal DbSet<TransactionEntity> Transactions { get; init; } = null!;

    /// <summary>
    /// Creates a context instance with the given options.
    /// </summary>
    /// <param name="options">DbContext options.</param>
    public TransactionDbContext(DbContextOptions<TransactionDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Base model configuration
        base.OnModelCreating(modelBuilder);

        // Transaction entity configuration
        modelBuilder.Entity<TransactionEntity>(entity =>
        {
            // Primary key
            entity.HasKey(e => e.Id);

            // Optimistic concurrency token — included in the WHERE clause on UPDATE
            entity.Property(e => e.ConcurrencyToken).IsConcurrencyToken();

            // Explicit length of the idempotency index columns
            entity.Property(e => e.IdempotencyKey).HasMaxLength(IdempotencyColumnMaxLength);
            entity.Property(e => e.IdempotencyScope).HasMaxLength(IdempotencyColumnMaxLength);

            // Unique composite index on (IdempotencyScope, IdempotencyKey). The model carries no
            // provider branch, and rows without a key never conflict:
            // PostgreSQL treats NULLs in a unique index as distinct (NULLS DISTINCT by default);
            // the SQL Server provider adds an IS NOT NULL filter to a unique index over nullable
            // columns by itself.
            entity.HasIndex(e => new { e.IdempotencyScope, e.IdempotencyKey })
                .IsUnique();
        });
    }
}
