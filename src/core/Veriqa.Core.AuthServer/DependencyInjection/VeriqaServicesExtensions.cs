// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Veriqa.Core.AuthServer.Compatibility;
using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.InitiatorContext;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.AuthServer.UI;
using Veriqa.Core.AuthServer.UI.Services;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.TransactionEngine.Events;
using Veriqa.Core.TransactionEngine.Identity;

namespace Veriqa.Core.AuthServer.DependencyInjection;

/// <summary>
/// Extensions for registering Veriqa authentication, infrastructure, and UI services.
/// </summary>
internal static class VeriqaServicesExtensions
{
    /// <summary>
    /// Registers Cookie authentication for the intermediate callback flow.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddVeriqaAuthentication(this IServiceCollection services)
    {
        // The method registers the Cookie scheme for one-time authentication between callback and authorize
        services.AddAuthentication()
            .AddCookie(OidcConstants.CookieAuthScheme, options =>
            {
                options.LoginPath = OidcEndpoints.Authorize;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(OidcConstants.AuthCookieExpireMinutes);
                // The cookie is one-time: an authorize request that keeps it (another client's request)
                // must not re-issue it with a fresh expiration past the original window.
                options.SlidingExpiration = false;
                options.Cookie.Name = OidcConstants.AuthCookieName;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.HttpOnly = true;
                // SPEC-002: unconditional Secure to protect authentication cookies
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Registers infrastructure services: the claims mapper, database migrations, and ClientSeeder.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddVeriqaInfrastructure(this IServiceCollection services)
    {
        // The method registers IClaimsMapper (stateless, singleton),
        // DatabaseMigrationService (EF Core migrations for relational providers: PostgreSQL/MySQL)
        // and the ClientSeeder background service.
        // DatabaseMigrationService is registered BEFORE ClientSeeder
        // so that migrations are applied before clients are created.
        //
        // TryAddSingleton, not AddSingleton: this is the shipped default of a replaceable extension
        // point. AddVeriqaAuthServer invokes the host's configure delegate BEFORE this line, so a
        // plain AddSingleton here would silently overwrite UseClaimsMapper<T>() and any direct
        // registration the host made before AddVeriqa*. A substitution made afterwards still wins —
        // it is simply the later registration of a single service.
        services.TryAddSingleton<IClaimsMapper, ClaimsMapper>();

        // Reversible protection of the relying party's expectations (SPEC-039 C23). The engine states
        // the port and compares the values; the means comes from here, because the key ring belongs to
        // the host. TryAddSingleton for the same reason as IClaimsMapper above: an integrator that
        // registered its own protector before AddVeriqa* keeps it.
        services.TryAddSingleton<IIdentityValueProtector, DataProtectionIdentityValueProtector>();
        services.AddHostedService<DatabaseMigrationService>();
        services.AddHostedService<ClientSeeder>();

        // Startup report on the effective client compatibility quirks (a relaxation in effect is a
        // deployment anomaly the operator must see). Registered after ClientSeeder so the report follows
        // the client registration lines in the log.
        services.AddHostedService<CompatibilityQuirksStartupDiagnosticsService>();

        // Startup report on the client entries whose custom sign-in page script the closed deployment
        // gate refuses (SPEC-012 §4.3): the two-step enabling would otherwise fail silently. Next to
        // the quirks report because both read the same client entries.
        services.AddHostedService<AuthPageCustomJsStartupDiagnosticsService>();

        // Startup report on a subject-display surface the axis knows and no display stands behind
        // (SPEC-012 §4.11): the value is accepted, so silence would leave the operator believing the
        // display is configured.
        services.AddHostedService<ConfirmationSubjectDisplayStartupDiagnosticsService>();

        // The QR pixel scales outside the range their setting admits are reported by the common
        // snapshot report of the configuration mechanism, over the catalogs registered with it
        // (SPEC-012 §8.2, CFG-152/CFG-154) — no report of its own is registered here.

        // Initiator context check (SPEC-017): context collector,
        // UA normalizer, offline GeoIP, and configuration diagnostics at startup
        services.AddSingleton<IUserAgentNormalizer, UaParserUserAgentNormalizer>();

        // The GeoIP default is a null object: this package ships no GeoIP library, because the
        // .mmdb database a lookup needs is not shipped either. A real provider arrives with the
        // Veriqa.Core.AuthServer.MaxMind satellite through AddMaxMindGeoIp().
        // TryAddSingleton, not AddSingleton, for the same reason as IClaimsMapper above — and here
        // it also makes the satellite win in both reachable orders: registered before this line it
        // is left alone, registered after it is the later registration of a single service.
        services.TryAddSingleton<IGeoIpProvider, UnavailableGeoIpProvider>();
        services.AddSingleton<IInitiatorContextCollector, InitiatorContextCollector>();
        services.AddHostedService<InitiatorContextStartupDiagnosticsService>();

        return services;
    }

    /// <summary>
    /// Registers UI services: QR generation, channel data, the page renderer, and the SignalR event handler (SPEC-007).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddVeriqaUiServices(this IServiceCollection services)
    {
        // The method registers all UI services for the authentication page

        // QR code generation (SPEC-007 §3.1, UI-013). One instance behind both faces: the public PNG
        // service and the internal module-matrix seam the confirmation answer draws its image from, so
        // the two cannot resolve the scale or pick the error correction level differently.
        services.AddSingleton<QrCodeService>();
        services.AddSingleton<IQrCodeService>(serviceProvider => serviceProvider.GetRequiredService<QrCodeService>());
        services.AddSingleton<IQrModuleMatrixSource>(serviceProvider => serviceProvider.GetRequiredService<QrCodeService>());

        // Channel data preparation (deep link + QR) (SPEC-007 §3, §4)
        services.AddScoped<IChannelDisplayService, ChannelDisplayService>();

        // Data-driven registry of the languages the auth window can render (SPEC-007 §12, UI-080).
        // Singleton: both inputs are process-constant, the sets are built once per process.
        services.TryAddSingleton<IAuthPageLanguageRegistry, AuthPageLanguageRegistry>();

        // Authentication page renderer (SPEC-007 §7, UI-050).
        // TryAddSingleton for the same reason as IClaimsMapper above: the shipped default must not
        // overwrite UseAuthPageRenderer<T>() called from the configure delegate.
        services.TryAddSingleton<IAuthPageRenderer, DefaultAuthPageRenderer>();

        // Branding of the generated core pages and the resource scope of a request. Both live in the
        // channel contour, which owns the render points, and are registered here as well because a
        // host may run the auth server without a single channel adapter — one shared method, so the
        // two roots cannot drift apart.
        services.AddCorePageUiServices();

        // SignalR transaction event handler (SPEC-007 §5)
        services.AddSingleton<ITransactionEventHandler, SignalRTransactionEventHandler>();

        return services;
    }
}
