// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;

using OpenIddict.EntityFrameworkCore;

using Veriqa.Core.AuthServer.Data;

namespace Veriqa.Core.AuthServer.DependencyInjection;

/// <summary>
/// Configures the OpenIddict EF Core store options by the provider the OpenIddict database context
/// actually uses. On the EF Core InMemory provider the bulk operations are turned off: that provider
/// does not execute them, so pruning would fail and remove nothing.
/// </summary>
/// <remarks>
/// The single source of that decision, registered by the owner of the provider choice. The provider
/// is read from the context itself, so it is recognized both when the core falls back to InMemory and
/// when the host selected InMemory in its own database delegate.
/// </remarks>
internal sealed class OpenIddictInMemoryStoreOptionsConfigurator : IConfigureOptions<OpenIddictEntityFrameworkCoreOptions>
{
    /// <summary>
    /// Scope factory: the context options are scoped, so the context is built in a scope of its own.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Creates an instance of the configurator.
    /// </summary>
    /// <param name="scopeFactory">Scope factory.</param>
    public OpenIddictInMemoryStoreOptionsConfigurator(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public void Configure(OpenIddictEntityFrameworkCoreOptions options)
    {
        // Runs once, on the first read of the options. IsInMemory() reads the provider the context was
        // configured with through the regular registration: the host's delegate is not invoked by this
        // class, and no database connection is opened.
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OpenIddictDbContext>();

        if (dbContext.Database.IsInMemory())
        {
            options.DisableBulkOperations = true;
        }
    }
}
