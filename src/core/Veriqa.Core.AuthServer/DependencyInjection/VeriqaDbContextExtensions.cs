// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenIddict.EntityFrameworkCore;

using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.Data;

namespace Veriqa.Core.AuthServer.DependencyInjection;

/// <summary>
/// Extensions for registering the EF Core DbContext for OpenIddict.
/// The core is provider-agnostic: the relational provider, its connection string and the
/// migrations assembly belong to the host (host-owned, SPEC-001 §10.2). Without a delegate the
/// store falls back to the volatile InMemory provider — a choice the caller has to speak
/// (<c>UseInMemoryOpenIddictStore</c>), otherwise the host refuses to start outside Development
/// (SPEC-012 CFG-118, CFG-119).
/// </summary>
internal static class VeriqaDbContextExtensions
{
    /// <summary>
    /// Registers the EF Core DbContext for OpenIddict.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configureDatabase">
    /// Action configuring the EF Core provider (for example UseNpgsql) together with its
    /// migrations assembly. It runs after OpenIddict is attached to the options, so a model
    /// customizer it installs (<c>ReplaceService&lt;IModelCustomizer, …&gt;</c>) stays in effect.
    /// When <see langword="null"/> the InMemory provider is used and the startup check of the store
    /// choice is registered alongside it.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddVeriqaDbContext(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configureDatabase)
    {
        // The provider is either supplied by the host through the delegate or falls back to InMemory
        services.AddDbContext<OpenIddictDbContext>(options =>
        {
            // OpenIddict first: it replaces IModelCustomizer itself, and the last replacement wins —
            // attached after the host's delegate it would silently discard the host's customizer.
            options.UseOpenIddict();

            if (configureDatabase is null)
            {
                // InMemory: storage for development and testing
                options.UseInMemoryDatabase(OidcConstants.InMemoryDatabaseName);
            }
            else
            {
                configureDatabase(options);
            }
        });

        // The OpenIddict store options follow the provider the context actually uses, whoever chose it:
        // the fallback above or the host's delegate. TryAddEnumerable keeps a second call to this
        // method from registering the configurator twice.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IConfigureOptions<OpenIddictEntityFrameworkCoreOptions>,
            OpenIddictInMemoryStoreOptionsConfigurator>());

        if (configureDatabase is null)
        {
            // No provider was supplied — the store is volatile, and whether that is acceptable is
            // decided at startup, when the environment is known (SPEC-012 CFG-119). A configured
            // provider needs no check at all, which is why the registration is conditional.
            // TryAddEnumerable keeps a second call to this method from producing a second check.
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OpenIddictStoreStartupCheckService>(
                serviceProvider => new OpenIddictStoreStartupCheckService(
                    // Both dependencies are optional by design: the environment may be absent outside a
                    // generic host, and the marker is absent exactly when the choice was not spoken.
                    serviceProvider.GetService<IHostEnvironment>(),
                    serviceProvider.GetService<VolatileOpenIddictStoreOptIn>(),
                    serviceProvider.GetRequiredService<ILogger<OpenIddictStoreStartupCheckService>>())));
        }

        return services;
    }
}
