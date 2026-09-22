// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;

using Veriqa.Core.AuthServer.Compatibility;
using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.Data;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.Contracts;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.DependencyInjection;

/// <summary>
/// Extensions for registering OpenIddict Core, Server, and Validation.
/// </summary>
internal static class VeriqaOpenIddictServerExtensions
{
    /// <summary>
    /// Registers OpenIddict with Authorization Code Flow, Refresh Token Flow, PKCE,
    /// scopes, token endpoints, and certificate settings.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment (used to select certificates).</param>
    /// <returns>Service collection for chaining.</returns>
    internal static IServiceCollection AddVeriqaOpenIddictServer(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // The method registers the full OpenIddict stack

        // Read the server settings
        var serverOptions = configuration
            .GetSection(OidcServerOptions.SectionName)
            .Get<OidcServerOptions>() ?? new OidcServerOptions();

        // Re-leveling of token settings (CFG-220/224): the value comes from the declared key, not from
        // a direct serverOptions.X read scattered over the callback.
        // LIMITATION: the OpenIddict server is configured ONCE, by a SYNCHRONOUS callback that runs
        // before the DI container exists, so only the Core level is available here — and it is pure
        // memory, which is why it can be read without awaiting anything (see ConfigCoreValues).
        // A per-tenant/per-app override of these global settings at startup is unattainable (global
        // server configuration) — it requires multi-tenant OpenIddict, which is not supported.
        // For self-hosted (N=1) this is the core value 1:1.
        var startupCore = ConfigCoreValues.Declare(core =>
        {
            core.RegisterCore(AuthServerConfigKeys.AccessTokenLifetimeSeconds, () => serverOptions.AccessTokenLifetimeSeconds);
            core.RegisterCore(AuthServerConfigKeys.RefreshTokenLifetimeSeconds, () => serverOptions.RefreshTokenLifetimeSeconds);
            core.RegisterCore(AuthServerConfigKeys.AuthorizationCodeLifetimeSeconds, () => serverOptions.AuthorizationCodeLifetimeSeconds);
            core.RegisterCore(AuthServerConfigKeys.EnableRefreshTokenRotation, () => serverOptions.EnableRefreshTokenRotation);
            core.RegisterCore(AuthServerConfigKeys.EnableRevocation, () => serverOptions.EnableRevocation);
            core.RegisterCore(AuthServerConfigKeys.Issuer, () => serverOptions.Issuer);
        });

        var accessTokenLifetimeSeconds = startupCore.Read(AuthServerConfigKeys.AccessTokenLifetimeSeconds);
        var refreshTokenLifetimeSeconds = startupCore.Read(AuthServerConfigKeys.RefreshTokenLifetimeSeconds);
        var authorizationCodeLifetimeSeconds = startupCore.Read(AuthServerConfigKeys.AuthorizationCodeLifetimeSeconds);
        var enableRefreshTokenRotation = startupCore.Read(AuthServerConfigKeys.EnableRefreshTokenRotation);
        var enableRevocation = startupCore.Read(AuthServerConfigKeys.EnableRevocation);
        var issuer = startupCore.Read(AuthServerConfigKeys.Issuer);

        services.AddOpenIddict()
            .AddCore(options =>
            {
                // EF Core store for OpenIddict
                options.UseEntityFrameworkCore()
                       .UseDbContext<OpenIddictDbContext>();
            })
            .AddServer(options =>
            {
                // Authorization Code Flow + Refresh Token Flow
                options.AllowAuthorizationCodeFlow();
                options.AllowRefreshTokenFlow();

                // Client Credentials Grant (RFC 6749 §4.4) — the authentication of the
                // server-to-server confirmation entry (SPEC-039 R2). Enabling it server-wide opens
                // nothing on its own: a token is issued only to a confidential client whose own entry
                // was granted the matching permission (ClientSeeder.BuildDescriptor), and the entry
                // ships disabled.
                options.AllowClientCredentialsFlow();

                // Confirmation token grant (SPEC-039 C51) — an extension grant (RFC 6749 §4.5)
                // exchanging a completed confirmation transaction for the identity token of the
                // confirming party. Enabled server-wide for the same reason as the grant above: only a
                // client whose entry states AllowConfirmationTokenGrant holds the permission
                // (ClientSeeder.BuildDescriptor), and the entry ships disabled.
                options.AllowCustomFlow(ConfirmationTokenGrant.GrantType);

                // PKCE is mandatory for public clients (SPEC-002 §10.2). The requirement lives in the
                // server pipeline and reads the CLIENT TYPE of the application record, so it holds for
                // every public client, including one an integrator registers through
                // IOpenIddictApplicationManager without any per-record requirement. The server-wide
                // RequireProofKeyForCodeExchange() is not used: it would demand PKCE from confidential
                // clients too, which the spec leaves optional. For them PKCE is still recommended — a
                // client secret does not protect against a substituted authorization code — and an
                // integrator can demand it per record with Requirements.Features.ProofKeyForCodeExchange,
                // which OpenIddict's own handler keeps enforcing.
                // The order places the rule right after that built-in handler, once the client identifier,
                // redirect URI and permissions have been validated.
                options.AddEventHandler<OpenIddictServerEvents.ValidateAuthorizationRequestContext>(builder =>
                    builder.AddFilter<OpenIddictServerHandlerFilters.RequireDegradedModeDisabled>()
                           .UseScopedHandler<PublicClientProofKeyRequirementHandler>()
                           .SetOrder(OpenIddictServerHandlers.Authentication
                               .ValidateProofKeyForCodeExchangeRequirement.Descriptor.Order + 1));

                // Only S256 is accepted: plain offers no protection, because a code challenge equal
                // to its verifier is disclosed by the authorization request itself (SPEC-002 §10.2).
                options.Configure(openIddictServerOptions =>
                    openIddictServerOptions.CodeChallengeMethods.Remove(OpenIddictConstants.CodeChallengeMethods.Plain));

                // Register the supported scopes.
                // email — gates the email/email_verified claims (OIDC Core §5.4, review feedback)
                // channel — gates the channel_type/channel_user_id claims (SPEC-003 CA-024);
                //           without the registration a client requesting it gets invalid_scope
                // avatar  — gates the picture claim (SPEC-002 §5.3); advertised in scopes_supported so a
                //           relying party can discover that the avatar is requested by scope
                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Phone,
                    OpenIddictConstants.Scopes.OfflineAccess,
                    VeriqaScopes.Channel,
                    VeriqaScopes.Avatar);

                // picture in claims_supported: together with the avatar scope it is the signal to the
                // relying party that the avatar exists and is read from userinfo.
                options.RegisterClaims(OpenIddictConstants.Claims.Picture);

                // Endpoint configuration. The introspection endpoint (RFC 7662) is answered by OpenIddict
                // itself and only to a client holding the introspection permission (ClientSeeder).
                options.SetAuthorizationEndpointUris(OidcEndpoints.Authorize)
                       .SetTokenEndpointUris(OidcEndpoints.Token)
                       .SetUserInfoEndpointUris(OidcEndpoints.UserInfo)
                       .SetIntrospectionEndpointUris(OidcEndpoints.Introspect);

                // Reference tokens, unconditionally: the access and refresh tokens a relying party
                // receives are opaque identifiers, and their payload stays in the OpenIddict token store.
                // The access token may carry an image (the picture claim), which must not end up in a
                // client-side store such as an authentication cookie. The local validation below enables
                // the token entry validation by itself; the identity token is not affected.
                options.UseReferenceAccessTokens()
                       .UseReferenceRefreshTokens();

                // Revocation Endpoint (RFC 7009). The value is read via the resolver.
                if (enableRevocation)
                {
                    options.SetRevocationEndpointUris(OidcEndpoints.Revoke);
                }

                // Token lifetimes — via the resolver (CFG-220, tenant ceiling → application stricter)
                options.SetAccessTokenLifetime(
                    TimeSpan.FromSeconds(accessTokenLifetimeSeconds));
                options.SetRefreshTokenLifetime(
                    TimeSpan.FromSeconds(refreshTokenLifetimeSeconds));
                options.SetAuthorizationCodeLifetime(
                    TimeSpan.FromSeconds(authorizationCodeLifetimeSeconds));

                // Refresh token rotation — enabled by default in OpenIddict.
                // If the configuration explicitly disables it — turn it off (value via the resolver).
                if (!enableRefreshTokenRotation)
                {
                    options.DisableRollingRefreshTokens();
                }

                // Explicit Issuer via the resolver (CFG-224, tenant level)
                if (!string.IsNullOrEmpty(issuer))
                {
                    options.SetIssuer(new Uri(issuer));
                }

                // Signing and encryption certificates
                if (environment.IsDevelopment())
                {
                    // Dev certificates are only acceptable in Development
                    options.AddDevelopmentEncryptionCertificate()
                           .AddDevelopmentSigningCertificate();
                }
                else
                {
                    // Production: load X.509 certificates from PFX files
                    options.AddSigningCertificate(serverOptions.LoadSigningCertificate());
                    options.AddEncryptionCertificate(serverOptions.LoadEncryptionCertificate());
                }

                // Client compatibility quirks: the extraction-stage handler is registered
                // UNCONDITIONALLY — whether a relaxation applies is decided at runtime from the effective
                // quirk set of the calling client, and a conditional registration would move that decision
                // to startup where the client is not yet known. The order puts the handler after the
                // ASP.NET Core extraction handlers (Basic credentials parsing included), so ClientId is
                // already available.
                options.AddEventHandler<OpenIddictServerEvents.ExtractTokenRequestContext>(builder =>
                    builder.UseSingletonHandler<DropScopeOnCodeExchangeHandler>()
                           .SetOrder(int.MaxValue - 100_000));

                // The resource quirk covers BOTH extraction events: RFC 8707 §2 defines the parameter
                // for the authorization request and the token request alike, and a client carrying a
                // foreign provider's dialect sends it wherever that provider took it. Registered
                // unconditionally and ordered exactly like the quirk above, for the same reasons.
                options.AddEventHandler<OpenIddictServerEvents.ExtractAuthorizationRequestContext>(builder =>
                    builder.UseSingletonHandler<DropResourceParameterHandler>()
                           .SetOrder(int.MaxValue - 100_000));

                options.AddEventHandler<OpenIddictServerEvents.ExtractTokenRequestContext>(builder =>
                    builder.UseSingletonHandler<DropResourceParameterHandler>()
                           .SetOrder(int.MaxValue - 100_000));

                // form_post response mode: the page OpenIddict ships holds an inline script without a
                // nonce, which our content security policy refuses — and <noscript> does not cover for
                // it, because that fallback shows only when scripts are DISABLED, not when one is
                // blocked by policy. The shipped handler is removed and ours takes its place at the
                // very same order, so query and fragment keep being answered by OpenIddict untouched.
                options.RemoveEventHandler(
                    OpenIddictServerAspNetCoreHandlers.Authentication.ProcessFormPostResponse.Descriptor);

                options.AddEventHandler<OpenIddictServerEvents.ApplyAuthorizationResponseContext>(builder =>
                    builder.UseSingletonHandler<FormPostResponseHandler>()
                           .SetOrder(OpenIddictServerAspNetCoreHandlers.Authentication
                               .ProcessFormPostResponse.Descriptor.Order));

                // ASP.NET Core integration with passthrough for manual handling
                options.UseAspNetCore()
                       .EnableAuthorizationEndpointPassthrough()
                       .EnableTokenEndpointPassthrough()
                       .EnableUserInfoEndpointPassthrough();
                // The revocation and introspection endpoints are handled by OpenIddict automatically (no passthrough)
            })
            .AddValidation(options =>
            {
                // Local token validation
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        return services;
    }
}
