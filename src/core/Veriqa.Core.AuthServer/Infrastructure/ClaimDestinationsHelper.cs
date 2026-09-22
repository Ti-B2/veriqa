// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Security.Claims;
using OpenIddict.Abstractions;
using Veriqa.Core.Contracts;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Helper for computing claim destinations.
/// Determines which tokens (access_token / id_token) each claim ends up in
/// based on its type and the requested scopes.
/// </summary>
internal static class ClaimDestinationsHelper
{
    /// <summary>
    /// Profile claims available with the "profile" scope.
    /// </summary>
    private static readonly HashSet<string> ProfileClaims = new(VeriqaClaimTypes.NameComparer)
    {
        OpenIddictConstants.Claims.Name,
        OpenIddictConstants.Claims.GivenName,
        OpenIddictConstants.Claims.FamilyName,
        OpenIddictConstants.Claims.Nickname,
        OpenIddictConstants.Claims.PreferredUsername,
        OpenIddictConstants.Claims.Locale,
        OpenIddictConstants.Claims.Zoneinfo
    };

    /// <summary>
    /// Claims available with the "phone" scope.
    /// </summary>
    private static readonly HashSet<string> PhoneClaims = new(VeriqaClaimTypes.NameComparer)
    {
        OpenIddictConstants.Claims.PhoneNumber
    };

    /// <summary>
    /// Claims available with the "email" scope (OIDC Core §5.4).
    /// Email is PII: without the requested email scope these claims end up in no token
    /// (review finding — previously they went to the access_token unconditionally via the default branch).
    /// </summary>
    private static readonly HashSet<string> EmailClaims = new(VeriqaClaimTypes.NameComparer)
    {
        OpenIddictConstants.Claims.Email,
        OpenIddictConstants.Claims.EmailVerified
    };

    /// <summary>
    /// Claims available with the <see cref="VeriqaScopes.Channel"/> scope (SPEC-003 CA-024).
    /// Without the requested scope these claims end up in no token — and therefore not in the
    /// userinfo response either, which mirrors the access_token. Previously they reached every
    /// relying party unconditionally through the default branch below.
    /// </summary>
    private static readonly HashSet<string> ChannelClaims = new(VeriqaClaimTypes.NameComparer)
    {
        VeriqaClaimTypes.ChannelType,
        VeriqaClaimTypes.ChannelUserId
    };

    /// <summary>
    /// Determines the destinations for a claim — access_token and/or id_token,
    /// taking the requested scopes into account.
    /// </summary>
    /// <param name="claim">User claim.</param>
    /// <param name="requestedScopes">Set of scopes requested by the relying party.</param>
    /// <returns>List of destinations.</returns>
    public static IEnumerable<string> GetDestinations(Claim claim, IReadOnlySet<string> requestedScopes)
    {
        // The method determines where each claim ends up based on its type and the requested scopes

        // sub — always in both tokens
        if (string.Equals(claim.Type, OpenIddictConstants.Claims.Subject, VeriqaClaimTypes.NameComparison))
        {
            yield return OpenIddictConstants.Destinations.AccessToken;
            yield return OpenIddictConstants.Destinations.IdentityToken;
            yield break;
        }

        // auth_time — id_token only (SPEC-002 §5.3)
        if (string.Equals(claim.Type, OpenIddictConstants.Claims.AuthenticationTime, VeriqaClaimTypes.NameComparison))
        {
            yield return OpenIddictConstants.Destinations.IdentityToken;
            yield break;
        }

        // amr — both tokens. OIDC Core 1.0 section 2 defines it as an ID Token claim, so a relying
        // party reading the id_token by the standard has to find it there; the access_token copy is
        // kept because /connect/userinfo mirrors the access_token and the quickstarts read it from
        // there. Scopes do not gate it — like sub and auth_time, it is always issued.
        if (string.Equals(claim.Type, OpenIddictConstants.Claims.AuthenticationMethodReference, VeriqaClaimTypes.NameComparison))
        {
            yield return OpenIddictConstants.Destinations.AccessToken;
            yield return OpenIddictConstants.Destinations.IdentityToken;
            yield break;
        }

        // picture — the access token only, and only when the avatar scope is present (SPEC-002 §5.3).
        // It is not a profile claim here: its value may be an image of tens of kilobytes, which has no
        // place in the identity token, and the relying party reads it from userinfo, which mirrors the
        // access token. This branch must stay above the default one below, which would otherwise hand
        // it to every relying party.
        if (string.Equals(claim.Type, OpenIddictConstants.Claims.Picture, VeriqaClaimTypes.NameComparison))
        {
            if (requestedScopes.Contains(VeriqaScopes.Avatar))
            {
                yield return OpenIddictConstants.Destinations.AccessToken;
            }

            yield break;
        }

        // Profile claims — when the profile scope is present
        if (ProfileClaims.Contains(claim.Type))
        {
            if (requestedScopes.Contains(OpenIddictConstants.Scopes.Profile))
            {
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield return OpenIddictConstants.Destinations.IdentityToken;
            }

            yield break;
        }

        // Phone claims — when the phone scope is present
        if (PhoneClaims.Contains(claim.Type))
        {
            if (requestedScopes.Contains(OpenIddictConstants.Scopes.Phone))
            {
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield return OpenIddictConstants.Destinations.IdentityToken;
            }

            yield break;
        }

        // Email claims — when the email scope is present (OIDC Core §5.4, analogous to phone)
        if (EmailClaims.Contains(claim.Type))
        {
            if (requestedScopes.Contains(OpenIddictConstants.Scopes.Email))
            {
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield return OpenIddictConstants.Destinations.IdentityToken;
            }

            yield break;
        }

        // Channel claims — when the channel scope is present (SPEC-003 CA-024).
        // This branch must stay above the default one below: without it the channel claims fall
        // through to "all other custom claims" and reach every relying party with no scope at all.
        if (ChannelClaims.Contains(claim.Type))
        {
            if (requestedScopes.Contains(VeriqaScopes.Channel))
            {
                yield return OpenIddictConstants.Destinations.AccessToken;
                yield return OpenIddictConstants.Destinations.IdentityToken;
            }

            yield break;
        }

        // All other custom claims — access_token only
        yield return OpenIddictConstants.Destinations.AccessToken;
    }
}
