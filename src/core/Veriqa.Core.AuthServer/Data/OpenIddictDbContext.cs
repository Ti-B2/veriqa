// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;

namespace Veriqa.Core.AuthServer.Data;

/// <summary>
/// Database context for storing OpenIddict entities
/// (applications, authorizations, tokens, scopes).
/// </summary>
public sealed class OpenIddictDbContext : DbContext
{
    /// <summary>
    /// Initializes the DB context with the specified options.
    /// </summary>
    /// <param name="options">Context options.</param>
    public OpenIddictDbContext(DbContextOptions<OpenIddictDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Configures the OpenIddict model.
    /// </summary>
    /// <param name="modelBuilder">Model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Register the OpenIddict entities in the EF Core model
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseOpenIddict();
    }
}
