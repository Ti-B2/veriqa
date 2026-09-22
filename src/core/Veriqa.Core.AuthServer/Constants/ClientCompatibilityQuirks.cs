// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// Closed registry of client compatibility quirk keys — named relaxations of the request form that are
/// enabled per client_id. Configuration selects a KEY from this set: neither a type nor an assembly name
/// ever comes from configuration and no reflection is performed on a configured string, so whoever edits
/// the configuration does not gain code execution inside the identity provider.
/// <para>
/// A quirk is a behavior branch, therefore every key documents its REMOVAL CONDITION: once the upstream
/// defect that forced the relaxation is fixed, the key is retired together with its handler.
/// </para>
/// </summary>
public static class ClientCompatibilityQuirks
{
    /// <summary>
    /// Drops the <c>scope</c> parameter from a token request made with
    /// <c>grant_type=authorization_code</c>, where RFC 6749 §4.1.3 does not define it and the built-in
    /// OpenIddict validation rejects the request as <c>invalid_request</c>. For the code grant the
    /// submitted scope is not used at all — the granted set comes from the authorization code — so
    /// dropping the parameter is equivalent to the client never having sent it. The <c>refresh_token</c>
    /// path is untouched: there <c>scope</c> is allowed by RFC 6749 §6 and narrows the granted rights.
    /// <para>
    /// Removal condition: the upstream fix oidc-wp/openid-connect-generic#497 is released and rolled out
    /// by the integrators running that plugin.
    /// </para>
    /// </summary>
    public const string DropScopeOnCodeExchange = "drop-scope-on-code-exchange";

    /// <summary>
    /// Drops the <c>resource</c> parameter (RFC 8707) from an authorization request and from a token
    /// request. Veriqa registers no resources, so the built-in OpenIddict validation rejects any value
    /// of the parameter as <c>invalid_target</c> and the whole sign-in fails before it starts. A client
    /// written against a provider that does accept the parameter — Moodle's <c>auth_oidc</c> carries the
    /// Entra ID dialect into its generic mode — sends it on every request and can not be told not to.
    /// <para>
    /// Dropping it is safe precisely because nothing here consumes it: with no registered resources the
    /// granted rights come from the scopes alone, so the issued token is identical to the one a client
    /// that never sent the parameter would receive. The relaxation grants no audience and narrows none.
    /// </para>
    /// <para>
    /// Removal condition: Moodle's <c>auth_oidc</c> stops sending <c>resource</c> in its generic
    /// (non-Entra) mode. No upstream ticket for that is known as of 2026-09-08 — one is not stated here
    /// rather than invented; whoever finds it records the number in this paragraph.
    /// </para>
    /// </summary>
    public const string DropResourceParameter = "drop-resource-parameter";

    /// <summary>
    /// All known quirk keys. Consumed by the client options validator (fail-fast on an unknown key).
    /// Comparison is Ordinal and case-sensitive without normalization: a case mistake
    /// is caught by the fail-fast together with the list of allowed keys, which is more precise than
    /// silently accepting a differently-cased spelling.
    /// </summary>
    public static IReadOnlySet<string> KnownKeys { get; } =
        FrozenSet.ToFrozenSet([DropScopeOnCodeExchange, DropResourceParameter], StringComparer.Ordinal);
}
