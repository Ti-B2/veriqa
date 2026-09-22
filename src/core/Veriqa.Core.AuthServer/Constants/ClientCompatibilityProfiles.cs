// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// Closed registry of client compatibility profiles — named sets of quirk keys from
/// <see cref="ClientCompatibilityQuirks"/>, addressed by the engine-and-plugin pair an integrator
/// actually runs. A profile is a convenience over the key set and not a mechanism of its own: it
/// expands into the very same keys, which then pass the very same validation, the same deployment
/// gate and the same per-client protective ceiling. Nothing downstream of the expansion can tell
/// whether a key was written out by hand or came from a profile.
/// <para>
/// Configuration selects a NAME from this set: neither a type nor an assembly name ever comes from
/// configuration and no reflection is performed on a configured string, so whoever edits the
/// configuration does not gain code execution inside the identity provider — the same property the
/// quirk registry holds.
/// </para>
/// <para>
/// <b>A profile may contain relaxations of the request form and nothing else.</b> Every key here
/// comes from <see cref="ClientCompatibilityQuirks"/>, whose whole registry is scoped to that; a key
/// that changed the claims Veriqa asserts about a user could never be enabled by a profile, because
/// enabling it must stay a deliberate act. The integrator switching a profile on is buying
/// compatibility with a broken client, not a different statement about who signed in.
/// </para>
/// </summary>
public static class ClientCompatibilityProfiles
{
    /// <summary>
    /// WordPress running the <c>openid-connect-generic</c> plugin: the plugin sends <c>scope</c> on the
    /// authorization-code token request, where RFC 6749 §4.1.3 does not define it.
    /// <para>
    /// Removal condition: the profile retires together with the last quirk it holds — see the removal
    /// condition of <see cref="ClientCompatibilityQuirks.DropScopeOnCodeExchange"/>.
    /// </para>
    /// </summary>
    public const string WordPressOpenIdConnectGeneric = "wordpress-openid-connect-generic";

    /// <summary>
    /// Moodle running the <c>auth_oidc</c> plugin in its generic (non-Entra) mode: the plugin keeps the
    /// Entra ID dialect it was written for and sends <c>resource</c> (RFC 8707) on every request, which
    /// this server, registering no resources, rejects as <c>invalid_target</c>.
    /// <para>
    /// Removal condition: the profile retires together with the last quirk it holds — see the removal
    /// condition of <see cref="ClientCompatibilityQuirks.DropResourceParameter"/>.
    /// </para>
    /// </summary>
    public const string MoodleAuthOidc = "moodle-auth-oidc";

    /// <summary>
    /// Profile name → the quirk keys it enables. Ordinal and case-sensitive, like the quirk registry:
    /// a case mistake is reported by the fail-fast together with the list of allowed names rather than
    /// being normalized away.
    /// </summary>
    private static readonly FrozenDictionary<string, IReadOnlySet<string>> Profiles =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [WordPressOpenIdConnectGeneric] = FrozenSet.ToFrozenSet(
                [ClientCompatibilityQuirks.DropScopeOnCodeExchange],
                StringComparer.Ordinal),
            [MoodleAuthOidc] = FrozenSet.ToFrozenSet(
                [ClientCompatibilityQuirks.DropResourceParameter],
                StringComparer.Ordinal)
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// All known profile names. Consumed by the client options validator (fail-fast on an unknown name).
    /// </summary>
    public static IReadOnlySet<string> KnownNames { get; } =
        FrozenSet.ToFrozenSet(Profiles.Keys, StringComparer.Ordinal);

    /// <summary>
    /// Expands a profile name into the quirk keys it enables.
    /// </summary>
    /// <param name="name">Profile name from this registry.</param>
    /// <returns>Quirk keys of the profile; an empty set for an unknown name.</returns>
    /// <remarks>
    /// An unknown name yields nothing rather than throwing: expansion runs in the post-configure step,
    /// which precedes validation, and it is the validator that reports the name and fails the start.
    /// Throwing here would replace that message with a less precise one.
    /// </remarks>
    public static IReadOnlySet<string> Expand(string name)
    {
        return Profiles.TryGetValue(name, out var quirks) ? quirks : FrozenSet<string>.Empty;
    }
}
