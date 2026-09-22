// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Configuration;

/// <summary>
/// Resolution context of the effective setting value (Parameter Object, SPEC-012 §10.3).
/// Carries ownership level identifiers. All fields are nullable: a null level = "not set",
/// it is skipped in the precedence (§10.3 of the same spec). Self-hosted (N=1): all fields null ⇒
/// the resolver reads only the core level (behavior 1:1 with direct IOptions access).
/// </summary>
/// <remarks>
/// The three ownership levels are <c>required</c>: nullable stays, the default does not. An absent
/// level is a legitimate state of the domain, but it is stated by hand — an initializer that stays
/// silent about one of them does not compile, so a forgotten level can no longer resolve the setting
/// of somebody else's level unnoticed. The normalization "blank value = level not stated" and the
/// collapse into <see cref="Core"/> live in <see cref="Of"/> alone.
/// </remarks>
public sealed class ResolutionContext
{
    /// <summary>
    /// Core/self-hosted context without the tenant/application/ui_config/per-request/user levels.
    /// The resolver will return the core-level value (SPEC-012 §10.3, self-hosted invariant).
    /// </summary>
    public static ResolutionContext Core { get; } = new()
    {
        TenantId = null,
        ApplicationId = null,
        UiConfigSelector = null
    };

    /// <summary>
    /// Tenant identifier. Null = self-hosted/core (the tenant level is empty).
    /// </summary>
    public required string? TenantId { get; init; }

    /// <summary>
    /// Application identifier (OIDC client_id). Null = the application level is empty.
    /// </summary>
    public required string? ApplicationId { get; init; }

    /// <summary>
    /// ui_config record selector (Code/configId). Null = the ui_config level is empty.
    /// </summary>
    public required string? UiConfigSelector { get; init; }

    /// <summary>
    /// Request parameters (per-request). Null = the per-request level is empty.
    /// </summary>
    public PerRequestContext? PerRequest { get; init; }

    /// <summary>
    /// User/bot overrides. Null = the user-override level is empty.
    /// </summary>
    public UserOverrideContext? UserOverride { get; init; }

    /// <summary>
    /// Builds the context out of the three ownership levels — the single place where a level is
    /// normalized and where the degenerate case collapses.
    /// </summary>
    /// <remarks>
    /// An empty or whitespace value states no level: a level stated as whitespace is a level nobody
    /// owns, and a blank <c>client_id</c> must never create a phantom application level. When none of
    /// the three is stated, the shared <see cref="Core"/> instance is returned, which keeps the
    /// self-hosted invariant "the resolver reads the core level alone" (SPEC-012 CFG-202).
    /// </remarks>
    /// <param name="tenantId">Tenant identifier; null or blank — the tenant level is not stated.</param>
    /// <param name="applicationId">Application identifier (OIDC <c>client_id</c>); null or blank — not stated.</param>
    /// <param name="uiConfigSelector"><c>ui_config</c> record selector; null or blank — not stated.</param>
    /// <returns>Context carrying the stated levels, or <see cref="Core"/> when none is stated.</returns>
    public static ResolutionContext Of(string? tenantId, string? applicationId, string? uiConfigSelector)
    {
        var tenant = Stated(tenantId);
        var application = Stated(applicationId);
        var uiConfig = Stated(uiConfigSelector);

        if (tenant is null && application is null && uiConfig is null)
        {
            return Core;
        }

        return new ResolutionContext
        {
            TenantId = tenant,
            ApplicationId = application,
            UiConfigSelector = uiConfig
        };
    }

    /// <summary>
    /// Builds the context of a resolution whose only populated level is the tenant. A null tenant
    /// yields <see cref="Core"/> (self-hosted N=1), so a caller holding an optional tenant does not
    /// spell out the degenerate case itself.
    /// </summary>
    /// <param name="tenantId">Tenant identifier; null — the default implicit tenant (self-hosted).</param>
    /// <returns>Context carrying the tenant level only.</returns>
    public static ResolutionContext ForTenant(string? tenantId) =>
        Of(tenantId, applicationId: null, uiConfigSelector: null);

    /// <summary>
    /// Normalizes a raw ownership level value: an empty or whitespace value states no level.
    /// </summary>
    /// <param name="value">Raw level value.</param>
    /// <returns>The value, or null when it is null, empty or whitespace.</returns>
    private static string? Stated(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// Parameters of the specific request for resolution (per-request level, SPEC-012 §10.3).
/// </summary>
public sealed class PerRequestContext
{
    /// <summary>
    /// Requested scopes (OIDC scope), if applicable.
    /// </summary>
    public IReadOnlyList<string>? RequestedScopes { get; init; }

    /// <summary>
    /// acr_values values, if applicable.
    /// </summary>
    public IReadOnlyList<string>? AcrValues { get; init; }
}

/// <summary>
/// User/bot level overrides (highest priority in the order of SPEC-012 §10.3).
/// Applied only when an upper-level gate exists for protective settings.
/// </summary>
public sealed class UserOverrideContext
{
    /// <summary>
    /// User/bot identifier (for diagnostics and override lookup).
    /// </summary>
    public string? UserId { get; init; }
}
