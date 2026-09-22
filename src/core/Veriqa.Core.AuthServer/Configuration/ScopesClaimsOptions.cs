// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Configuration.Enums;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Definition of a single claim within a scope (SPEC-012 §6.6).
/// </summary>
public sealed class ClaimDefinition
{
    /// <summary>
    /// Claim name (e.g. phone_number).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the claim is required.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Claim source: Channel (from the channel) or External (from an external system).
    /// </summary>
    public ClaimSource Source { get; set; } = ClaimSource.Channel;
}

/// <summary>
/// Definition of a single scope (SPEC-012 §6.6).
/// </summary>
public sealed class ScopeDefinition
{
    /// <summary>
    /// Scope name (e.g. profile).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of claims included in the scope.
    /// </summary>
    public IReadOnlyList<ClaimDefinition> Claims { get; set; } = [];

    /// <summary>
    /// Whether the scope is required.
    /// </summary>
    public bool IsRequired { get; set; }
}

/// <summary>
/// Scopes and claims settings (SPEC-012 §6.6).
/// Configuration section: Veriqa:ScopesClaims.
/// </summary>
public sealed class ScopesClaimsOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Veriqa:ScopesClaims";

    /// <summary>
    /// List of scope definitions. Default: the standard set of OIDC scopes.
    /// </summary>
    public IReadOnlyList<ScopeDefinition> Scopes { get; set; } = [];

    /// <summary>
    /// Require the phone_number claim.
    /// Default: false.
    /// </summary>
    public bool RequirePhone { get; set; }

    /// <summary>
    /// Require the email claim.
    /// Default: false.
    /// </summary>
    public bool RequireEmail { get; set; }
}
