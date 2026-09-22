// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using OpenIddict.Abstractions;
using Veriqa.Core.AuthServer.Constants;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Validation rules of a SINGLE OIDC client entry — the unit of validation for
/// <see cref="OidcClientsOptions.Clients"/> (SPEC-012 CFG-210).
/// <para>
/// The rules live apart from <see cref="OidcClientsOptionsValidator"/> because two places apply them
/// to the same entry with different consequences: at startup a broken entry stops the process (a
/// deployment error), while on a configuration reload it is dropped on its own so that the remaining
/// clients keep working. Both call the same rules, so the two modes cannot drift apart.
/// </para>
/// </summary>
internal static class OidcClientValidationRules
{
    /// <summary>
    /// Collects the rule violations of a single client entry.
    /// </summary>
    /// <param name="client">Client entry (already normalized by <see cref="OidcClientsOptionsPostConfigure"/>).</param>
    /// <param name="index">Position of the entry in the Clients array (for the failure text).</param>
    /// <returns>Failure descriptions; empty when the entry is valid.</returns>
    public static IReadOnlyList<string> Collect(OidcClientOptions client, int index)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(client.ClientId))
        {
            failures.Add($"Clients[{index}].ClientId cannot be empty.");
        }

        // A client credentials client proves itself by its secret and by nothing else, so a public
        // entry stating the grant would hand the application to whoever knows its identifier
        // (SPEC-039 R2). The entry is refused at startup instead of being quietly downgraded.
        if (client is { AllowClientCredentials: true, ClientSecret: null or "" })
        {
            failures.Add(
                $"Clients[{index}] ({client.ClientId}): ClientSecret is required when " +
                "AllowClientCredentials is enabled — a public client cannot use the Client Credentials Grant.");
        }

        // Introspection answers with the subject and the client of any presented token, so only a client
        // that proves itself by a secret may hold it; a public entry stating the flag is refused at
        // startup rather than seeded with a permission it could not use safely.
        if (client is { AllowIntrospection: true, ClientSecret: null or "" })
        {
            failures.Add(
                $"Clients[{index}] ({client.ClientId}): ClientSecret is required when " +
                "AllowIntrospection is enabled — a public client cannot use the introspection endpoint.");
        }

        // The two rules below are NARROWED, not lifted: a client credentials client never drives a
        // browser, so it has no redirect URI to state and no OIDC scope to be granted. Every other
        // client keeps both requirements and their wording.
        if (!client.AllowClientCredentials && client.AllowedRedirectUris.Count is 0)
        {
            failures.Add($"Clients[{index}] ({client.ClientId}): AllowedRedirectUris cannot be empty.");
        }

        foreach (var uri in client.AllowedRedirectUris)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
            {
                failures.Add($"Clients[{index}] ({client.ClientId}): invalid redirect URI '{uri}'.");
            }
        }

        if (!client.AllowClientCredentials && client.AllowedScopes.Count is 0)
        {
            failures.Add($"Clients[{index}] ({client.ClientId}): AllowedScopes cannot be empty.");
        }

        // The confirmation token grant redeems a transaction the same client created, and only a
        // client credentials client creates one; what it issues is an identity token, which needs
        // openid (SPEC-039 C51). Either gap leaves the flag dead, so the start fails instead.
        if (client.AllowConfirmationTokenGrant)
        {
            if (!client.AllowClientCredentials)
            {
                failures.Add(
                    $"Clients[{index}] ({client.ClientId}): AllowConfirmationTokenGrant requires " +
                    "AllowClientCredentials — only a client credentials client creates the confirmation " +
                    "transaction the grant redeems.");
            }

            if (!client.AllowedScopes.Contains(OpenIddictConstants.Scopes.OpenId, StringComparer.Ordinal))
            {
                failures.Add(
                    $"Clients[{index}] ({client.ClientId}): AllowConfirmationTokenGrant requires " +
                    $"'{OpenIddictConstants.Scopes.OpenId}' in AllowedScopes — the grant issues an identity token.");
            }
        }

        // Compatibility quirks: the configuration selects keys from a closed registry, so an unknown
        // key is a deployment error and must stop the start rather than be skipped silently. The
        // comparison is Ordinal and case-sensitive — a differently-cased spelling is rejected here
        // together with the list of allowed keys instead of being normalized away. Null is valid
        // (the level is simply not set); when the entry names a known profile, the post-configure step,
        // which runs before this rule, has already replaced it with the profile's keys.
        if (client.CompatibilityQuirks is { } quirks)
        {
            foreach (var quirk in quirks)
            {
                if (!ClientCompatibilityQuirks.KnownKeys.Contains(quirk))
                {
                    failures.Add(
                        $"Clients[{index}] ({client.ClientId}): unknown compatibility quirk '{quirk}'. " +
                        $"Allowed keys: {string.Join(", ", ClientCompatibilityQuirks.KnownKeys)}.");
                }
            }
        }

        // The profile name is checked separately from the keys it expands into: expansion happens in
        // the post-configure step, and an unknown name expands to nothing, so without this rule a typo
        // would silently disable the relaxation instead of failing the start.
        if (!string.IsNullOrWhiteSpace(client.CompatibilityProfile)
            && !ClientCompatibilityProfiles.KnownNames.Contains(client.CompatibilityProfile))
        {
            failures.Add(
                $"Clients[{index}] ({client.ClientId}): unknown compatibility profile " +
                $"'{client.CompatibilityProfile}'. " +
                $"Allowed profiles: {string.Join(", ", ClientCompatibilityProfiles.KnownNames)}.");
        }

        return failures;
    }
}
