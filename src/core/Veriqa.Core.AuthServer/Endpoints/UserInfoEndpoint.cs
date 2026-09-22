// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.Contracts;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// OIDC user info endpoint (GET /connect/userinfo).
/// Returns the user's claims from the access token.
/// Protected by the OpenIddict Validation scheme (Bearer token).
/// </summary>
public static class UserInfoEndpoint
{
    /// <summary>
    /// Registers the userinfo endpoint.
    /// </summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <returns>The route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapUserInfoEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(OidcEndpoints.UserInfo, HandleUserInfoAsync)
            .RequireAuthorization(policy =>
                policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                      .RequireAuthenticatedUser());

        return endpoints;
    }

    /// <summary>
    /// Returns user information in the OpenID Connect format.
    /// </summary>
    private static Task<IResult> HandleUserInfoAsync(ClaimsPrincipal user)
    {
        // The method extracts claims from the access token and builds the userinfo response

        var sub = user.FindFirstValue(OpenIddictConstants.Claims.Subject);
        if (string.IsNullOrEmpty(sub))
        {
            return Task.FromResult(Results.Unauthorized());
        }

        // Build the claims dictionary (excluding duplicate / system claims)
        var response = new Dictionary<string, object>
        {
            [OpenIddictConstants.Claims.Subject] = sub
        };

        // Add all remaining claims from the token. The response mirrors the presented access token:
        // the scope gate ran earlier, when the token was issued (destinations, SPEC-002 section 5.3
        // and the allowlist rules of section 6.2), so no second filter is applied here. That carries
        // the token's own service claims — exp and iat — into the response, and no scope gated those.
        // Keeping them is a decision, not an oversight (SPEC-002 section 4.5): OIDC Core 1.0 section
        // 5.3.2 does not close the set of members a response may state, a consumer ignores the members
        // it does not know, and no relying party has been harmed by them. They are narrowed when one
        // actually is — or when this response becomes a signed JWT, where exp and iat would belong to
        // the response itself.
        foreach (var claim in user.Claims)
        {
            if (string.Equals(claim.Type, OpenIddictConstants.Claims.Subject, VeriqaClaimTypes.NameComparison))
            {
                continue;
            }

            // Skip OpenIddict-specific claims
            if (claim.Type.StartsWith(OidcConstants.OpenIddictInternalClaimPrefix, VeriqaClaimTypes.NameComparison))
            {
                continue;
            }

            // If the claim already exists — skip it (the first value takes precedence)
            response.TryAdd(claim.Type, ToResponseValue(claim));
        }

        return Task.FromResult(Results.Ok(response));
    }

    /// <summary>
    /// Converts a claim into the value the userinfo response states for it. A <see cref="Claim"/>
    /// always holds a string, so writing it straight into the response turned every non-textual claim
    /// — <c>exp</c>, <c>iat</c>, <c>auth_time</c>, <c>email_verified</c> — into a JSON string, while
    /// the very same claim reaches the relying party as a number or a boolean inside the token. The
    /// declared <see cref="Claim.ValueType"/> is what the token writer reads, so the response follows
    /// it too and the two views of one claim agree (OIDC Core 1.0 section 5.3.2).
    /// </summary>
    /// <remarks>
    /// A value that does not parse under its declared type is written as the string it is: a
    /// malformed claim degrades the shape of one field and never fails the whole response.
    /// </remarks>
    /// <param name="claim">Claim of the access token.</param>
    /// <returns>Boolean, number or string, following the declared value type.</returns>
    private static object ToResponseValue(Claim claim)
    {
        return claim.ValueType switch
        {
            ClaimValueTypes.Boolean when bool.TryParse(claim.Value, out var flag) => flag,

            ClaimValueTypes.UInteger32 or ClaimValueTypes.UInteger64
                when ulong.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned)
                => unsigned,

            ClaimValueTypes.Integer or ClaimValueTypes.Integer32 or ClaimValueTypes.Integer64
                when long.TryParse(claim.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                => number,

            _ => claim.Value
        };
    }
}
