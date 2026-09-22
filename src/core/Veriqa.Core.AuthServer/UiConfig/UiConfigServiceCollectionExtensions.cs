// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Veriqa.Core.AuthServer.UiConfig;

/// <summary>
/// Registration of the ui_config entry and the self-hosted store (SPEC-012 CFG-203, SPEC-002 §4.6).
/// The self-hosted store is registered by default; Cloud/Demo stores are added opt-in on top
/// (TryAdd does not overwrite an explicit consumer registration — by contract the first one in DI wins,
/// while here the base store is substituted only if nothing has been registered — R2).
/// </summary>
public static class UiConfigServiceCollectionExtensions
{
    /// <summary>
    /// Registers the catalog of ui_config entries from configuration and the default self-hosted store.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddVeriqaUiConfig(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Catalog of ui_config entries (optional section; empty when absent — 1:1 behavior)
        services.Configure<UiConfigurationsOptions>(
            configuration.GetSection(UiConfigurationsOptions.SectionName));

        // A record no request can select, and one the store degrades to the empty record, both leave
        // the deployment with the global look and no fault to see. Judged at start instead.
        services.AddSingleton<IValidateOptions<UiConfigurationsOptions>, UiConfigurationsOptionsValidator>();
        services.AddOptionsWithValidateOnStart<UiConfigurationsOptions>();

        // Default self-hosted store. TryAdd: if the cloud/demo circuit has already registered
        // its own IUiConfigStore implementation — it stays (opt-in on top), otherwise the base one is set.
        services.TryAddSingleton<IUiConfigStore, ConfigurationUiConfigStore>();

        return services;
    }
}
