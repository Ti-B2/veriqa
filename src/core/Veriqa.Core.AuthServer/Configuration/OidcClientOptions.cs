// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Settings for a single OIDC client.
/// </summary>
public sealed class OidcClientOptions
{
    /// <summary>
    /// Client identifier (client_id).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret (client_secret). Null for public clients.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Client display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Allowed redirect URIs.
    /// </summary>
    public IReadOnlyList<string> AllowedRedirectUris { get; set; } = [];

    /// <summary>
    /// Allowed scopes.
    /// </summary>
    public IReadOnlyList<string> AllowedScopes { get; set; } = [];

    /// <summary>
    /// Allow issuing refresh tokens.
    /// </summary>
    public bool AllowRefreshTokens { get; set; }

    /// <summary>
    /// Allow the OAuth 2.0 Client Credentials Grant (RFC 6749 §4.4) for this client — the
    /// authentication of the server-to-server confirmation entry (SPEC-039 R2). Default: disabled, so
    /// a deployment opens the entry by a deliberate act rather than by upgrading.
    /// <para>
    /// A client stating it must be confidential: the grant proves the client by its secret alone, so a
    /// public client would let anyone holding the client identifier act as the application. The
    /// requirement is a startup validation rule, not a silent downgrade.
    /// </para>
    /// <para>
    /// Such a client needs neither a redirect URI nor an OIDC scope — it never drives a browser — so
    /// the two rules demanding them are narrowed for it. Everything else about the entry stays as it
    /// was for every other client.
    /// </para>
    /// </summary>
    public bool AllowClientCredentials { get; set; }

    /// <summary>
    /// Allow the confirmation token grant for this client (SPEC-039 R50, C51): once a confirmation
    /// transaction this client created has ended as confirmed, the client exchanges its identifier a
    /// single time for the identity token of the confirming party. Default: disabled.
    /// <para>
    /// This is the integrator's permission, not the relying party's: nothing in a request widens what
    /// is disclosed. The claims are bounded by <see cref="AllowedScopes"/> of this same entry, and the
    /// subject is the channel identity of the person who confirmed (SPEC-039 N54).
    /// </para>
    /// <para>
    /// The flag requires <see cref="AllowClientCredentials"/> — only a client credentials client
    /// creates the transaction the grant redeems — and <c>openid</c> in <see cref="AllowedScopes"/>,
    /// without which no identity token can be issued. Either gap fails the start.
    /// </para>
    /// </summary>
    public bool AllowConfirmationTokenGrant { get; set; }

    /// <summary>
    /// Allow this client to call the token introspection endpoint (RFC 7662). Default: disabled.
    /// <para>
    /// The introspection response names the subject and the client of ANY token the caller presents,
    /// not only of tokens issued to that caller: the issued tokens carry no audience, so OpenIddict has
    /// no authorized party to restrict the answer to. The permission is therefore meant only for the
    /// integrator's own confidential clients. A client stating it must have a secret — a public client
    /// cannot authenticate to the endpoint — and a public entry stating it fails the start.
    /// </para>
    /// </summary>
    public bool AllowIntrospection { get; set; }

    /// <summary>
    /// Tenant this application belongs to. Null or blank — the entry states no tenant, and the
    /// transaction is created without one, which is what a self-hosted installation with a single
    /// tenant means. The shipped <see cref="Veriqa.Core.TransactionEngine.Services.IClientTenantResolver"/>
    /// reads it once per request on three HTTP paths: the authorization endpoint stores it on the
    /// transaction's OIDC context, the confirmation transaction endpoint on the transaction's request
    /// context, and the form-post response resolves it again for branding, the transaction being gone
    /// by then. Readers of a transaction take it from the transaction, not from configuration.
    /// <para>
    /// The field states a fact about a client and nothing more: it is not a tenant catalog and
    /// carries no isolation. Isolation is provided by <c>client_id</c>, which sits above the tenant,
    /// so an installation cannot reach another tenant through it by construction. What the tenant
    /// buys is the level at which tenant-scoped configuration keys resolve.
    /// </para>
    /// <para>
    /// It stays whatever a later tenant model does. An installation that comes to derive the tenant
    /// some other way keeps this entry as the compatibility answer, and a deployment that never
    /// states it is unaffected either way.
    /// </para>
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Default UI configuration.
    /// </summary>
    public string? DefaultUiConfig { get; set; }

    /// <summary>
    /// Allowed UI configurations.
    /// </summary>
    public IReadOnlyList<string>? AllowedUiConfigs { get; set; }

    /// <summary>
    /// Per-application set of displayed initiator context fields (SPEC-017 ICC-081).
    /// Null — tenant/core default (global InitiatorContextOptions.DisplayFields). Resolved
    /// through the shared resolver (narrowing: an application may only narrow the top-level set).
    /// </summary>
    public IReadOnlyList<string>? InitiatorContextDisplayFields { get; set; }

    /// <summary>
    /// Per-application flag for displaying the initiator context in the confirmation.
    /// Null — tenant/core default. Controls specifically the DISPLAY of the context in the confirmation,
    /// not snapshot collection (collection is governed globally, decision No. 8).
    /// </summary>
    public bool? InitiatorContextEnabled { get; set; }

    /// <summary>
    /// Per-application value of the bot-rejection policy (CFG-222, TransactionEngine.RejectBots).
    /// Null — the level is not set (tenant/core default applies). A relaxation (false) takes effect only
    /// when the tenant/core gate <c>TransactionEngineOptions.RejectBotsAllowApplicationOverride</c> is open;
    /// otherwise the resolver keeps the stricter upper-level value (protective ceiling, CFG-212).
    /// </summary>
    public bool? RejectBots { get; set; }

    /// <summary>
    /// Per-application login confirmation surface (CFG-042: the key is declared at the core, tenant and
    /// application levels, with no user step and no gate). Null — the level is not set, the tenant or
    /// core value applies. The application
    /// overrides freely: unlike <see cref="RejectBots"/> this key has no protective ceiling — the
    /// surface is the deploying party's policy, and the clamp that can still change it is the channel
    /// routing of CFG-049a, not a level above.
    /// <para>
    /// This is NOT reachable through <c>ui_config</c>: the client-selectable surface must not carry
    /// the confirmation policy (CFG-049), so the value comes from the client's own configuration entry.
    /// </para>
    /// </summary>
    public LoginConfirmationMode? LoginConfirmationMode { get; set; }

    /// <summary>
    /// Per-application set of enabled compatibility quirk keys — named relaxations of the request form
    /// (the closed registry is <c>ClientCompatibilityQuirks</c>). Null — the level is not set, so the
    /// effective set is the empty core-level one. Enabling is two-step: a quirk listed here takes effect
    /// only when the deployment gate
    /// <see cref="OidcClientsOptions.CompatibilityQuirksAllowApplicationOverride"/> is open — otherwise
    /// the resolver intersects this set with the empty core set and the relaxation does not apply
    /// (protective ceiling). The flag lives in the client configuration rather than in the OpenIddict
    /// entity Properties: clients are seeded from configuration on every start, so Properties would be
    /// overwritten each time.
    /// </summary>
    public IReadOnlyList<string>? CompatibilityQuirks { get; set; }

    /// <summary>
    /// Compatibility profile of this application — a name from the closed registry
    /// <c>ClientCompatibilityProfiles</c>, addressed by the engine-and-plugin pair the integrator runs
    /// (for example <c>wordpress-openid-connect-generic</c>). The post-configure step expands it into
    /// the quirk keys it holds and unions them with <see cref="CompatibilityQuirks"/>, so a profile is
    /// a shorthand and not a second mechanism: the resulting keys pass the same validation, the same
    /// deployment gate and the same protective ceiling as keys written out by hand. Null or blank —
    /// no profile; an unknown name fails the start with the list of allowed names, exactly as an
    /// unknown quirk key does.
    /// </summary>
    public string? CompatibilityProfile { get; set; }

    // The sign-in page settings an entry may state — the logo, the primary color and their themed
    // maps, the custom stylesheet and script with their SRI hashes, the QR section — carry NO property
    // here (SPEC-012 §10.6). Their keys state the address of this level themselves and are read inside
    // the entry by path, so the shape of the entry does not grow a member per setting and the names an
    // already deployed installation writes stay exactly what they were.
}
