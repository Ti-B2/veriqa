// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Security.Claims;

using Microsoft.IdentityModel.Tokens;

using OpenIddict.Abstractions;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// The single home of what a token issued by the Client Credentials Grant looks like and of how a
/// protected route recognizes one (SPEC-039 R2, E23).
/// <para>
/// A token issued by that grant represents the CLIENT, while a token issued by the authorization code
/// grant represents a signed-in user. The server-to-server entry must accept only the first kind:
/// accepting the second would let any user who passed the sign-in act on behalf of the application.
/// Issuance and recognition live together here precisely so the two cannot drift — a convention split
/// across an issuing endpoint and a consuming one is a convention that eventually stops holding.
/// </para>
/// <para>
/// The mark is not an invention of ours: the token states its own <c>client_id</c> (RFC 9068 §2.2) and
/// its subject IS that client identifier, which is what the grant means. A user token fails both
/// halves — nothing on the sign-in path states the <c>client_id</c> claim, and its subject is the
/// channel identity of a person.
/// </para>
/// </summary>
internal static class ClientCredentialsToken
{
    /// <summary>
    /// Builds the principal of a token representing the calling client itself.
    /// </summary>
    /// <param name="clientId">Client identifier of the authenticated client.</param>
    /// <param name="scopes">Scopes the request asked for; the server checks them against the client's
    /// permissions on its own.</param>
    /// <returns>Principal to sign in with.</returns>
    public static ClaimsPrincipal Create(string clientId, IReadOnlyCollection<string> scopes)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentNullException.ThrowIfNull(scopes);

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, clientId);
        identity.SetClaim(OpenIddictConstants.Claims.ClientId, clientId);

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);

        // Both claims are what the entry reads off the token, so both have to reach the access token;
        // neither belongs in an id_token, which this grant does not produce anyway.
        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
        }

        return principal;
    }

    /// <summary>
    /// Reads the client identifier off a token that represents a client, and reports whether the token
    /// is one at all.
    /// </summary>
    /// <param name="principal">Principal of the validated access token; null — no token.</param>
    /// <param name="clientId">Client identifier the token was issued to.</param>
    /// <returns><see langword="true"/> when the token represents the client itself.</returns>
    public static bool TryGetClientId(ClaimsPrincipal? principal, out string clientId)
    {
        clientId = string.Empty;

        if (principal is null)
        {
            return false;
        }

        var subject = principal.FindFirstValue(OpenIddictConstants.Claims.Subject);
        var stated = principal.FindFirstValue(OpenIddictConstants.Claims.ClientId);

        // Both halves are required, and they have to agree: a user token states no client identifier,
        // and one that somehow carried it would still name a subject that is not the client.
        if (string.IsNullOrEmpty(subject)
            || string.IsNullOrEmpty(stated)
            || !string.Equals(subject, stated, StringComparison.Ordinal))
        {
            return false;
        }

        clientId = stated;
        return true;
    }
}
