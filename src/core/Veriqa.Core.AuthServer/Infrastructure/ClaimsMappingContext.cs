// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Context of the relying party the claims are being built for.
/// <para>
/// The type is deliberately narrow: it carries who is asking and in which tenant, and nothing else
/// of the current request. The OIDC context of the sign-in additionally holds the nonce, the browser
/// nonce and the PKCE code challenge — CSRF and PKCE secrets a substituted claims mapper has no
/// business receiving.
/// </para>
/// <para>
/// The attribution is what a substituted mapper needs to emit a pairwise subject identifier
/// (OIDC Core 1.0 section 8.1): the salt of a pairwise <c>sub</c> is per relying party. The built-in
/// mapper does not read it — its <c>sub</c> is global across relying parties.
/// </para>
/// </summary>
/// <param name="ClientId">OIDC client identifier of the requesting relying party; null when the
/// request carries none.</param>
/// <param name="TenantId">Tenant attribution resolved from the client identifier; null when the
/// tenant level is not stated.</param>
/// <param name="Scopes">Scopes requested by the relying party.</param>
/// <param name="TimeZone">Time zone stated for this sign-in (an IANA identifier), or null when none
/// was stated. It is presentation data of the request and no secret of it: the relying party either
/// sent it or the deployment's own default supplied it, and it is what the <c>zoneinfo</c> claim of
/// OIDC Core 1.0 section 5.1 is built from.</param>
public sealed record ClaimsMappingContext(
    string? ClientId,
    string? TenantId,
    IReadOnlySet<string> Scopes,
    string? TimeZone = null);
