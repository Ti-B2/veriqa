// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Veriqa.Core.AuthServer.InitiatorContext;

namespace Veriqa.Core.AuthServer.DependencyInjection;

/// <summary>Offline MaxMind GeoIP registration extensions for the Veriqa auth server.</summary>
/// <remarks>
/// The namespace is the one <c>AddVeriqaAuthServer</c> itself lives in, so taking the GeoIP provider
/// out of the base package costs the caller a package reference and a single line of wiring.
/// </remarks>
public static class MaxMindGeoIpServiceCollectionExtensions
{
    /// <summary>
    /// Uses the offline MaxMind GeoIP provider for the initiator context check (SPEC-017, ICC-013).
    /// The local database is host-owned: point
    /// <c>Veriqa:InitiatorContext:GeoIp:DatabasePath</c> at a GeoLite2/GeoIP2 City or Country
    /// <c>.mmdb</c> file — the product ships none, because that data carries MaxMind's own EULA.
    /// With no path configured the provider stays unavailable and the geo fields remain null
    /// (graceful, ICC-082) exactly as they do without this package.
    /// <code>
    /// builder.Services.AddMaxMindGeoIp();
    /// </code>
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMaxMindGeoIp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // A plain Add, while the shipped null object in the base package is registered with TryAdd:
        // that pair makes this call win in both reachable orders — declared before AddVeriqa* the
        // TryAdd leaves it alone, declared after it is simply the later registration of a single
        // service.
        services.AddSingleton<IGeoIpProvider, MaxMindGeoIpProvider>();

        return services;
    }
}
