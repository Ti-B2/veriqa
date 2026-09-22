// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Catalog of the canonical resolver's keys (TASK-040, SPEC-012 §10.6) for the channel track.
/// Credentials are resolved as simple values (CFG-210, the first defined layer wins),
/// while the <c>channels_enabled</c> capability — as a set with narrowing (CFG-211); which levels
/// take part in that narrowing is stated by the key's own declaration, see its summary below. Both
/// keys declare the core and the tenant levels — the ones populated today. Each channel declares its
/// own credential key (<c>Channels.{Type}.Credentials</c>) typed by that channel's credential group
/// through <see cref="CredentialKey{TCredentials}"/>, so that a single resolver serves all channels
/// without an untyped value on the way and without this registry naming a single channel.
/// There is no second precedence mechanism (anti-fork CFG-235).
/// </summary>
public static class ChannelConfigKeys
{
    /// <summary>
    /// Canonical name of the "channels available to the layer" capability key.
    /// </summary>
    public const string ChannelsEnabledKeyName = "Channels.Enabled";

    /// <summary>
    /// Catalog of the declarations of the CONTOUR — what its composition registers, and what the
    /// schema of a deployment is assembled from. It is initialized before the declarations that fill
    /// it, which is the order the initializers of this class run in.
    /// </summary>
    private static readonly ConfigKeyCatalog Declared = new();

    /// <summary>
    /// The <c>channels_enabled</c> capability (SPEC-003 §17.7, SPEC-012 §10.3 CFG-210/211):
    /// the set of channels available to a layer. Semantics — a set with narrowing (CFG-211), and what
    /// that means HERE follows from this declaration: the key declares the core and the tenant levels
    /// and opts core OUT of the intersection (<c>coreParticipatesInSetIntersection: false</c>, see
    /// <see cref="ConfigKey{T}.CoreParticipatesInSetIntersection"/>). Hence the effective set today IS
    /// the tenant set: core stands under it as the degenerate N=1 default (self-hosted ≡ core,
    /// SPEC-003 CA-170), read only when no owning level above has defined a set — it is NOT a ceiling
    /// over the tenant. Narrowing could come only from a level BELOW the tenant by ownership
    /// (application, user); the key declares neither, so the intersection has a single participant and
    /// <see cref="IntersectChannelSets"/> combines nothing today. Reaching such a level is a widening
    /// of the key's boundaries of its own (the level in <c>Levels</c> plus a binding at that level),
    /// not something this key already does. An axis separate from credentials.
    /// <para>
    /// Why core is opted out: as a ceiling it would break the capability — an empty global core set
    /// (a normal multi-tenant case: channels are enabled per tenant, not globally) would zero out the
    /// tenant set through the intersection, the CFG-211 defect. The flag is therefore load-bearing:
    /// clearing it, or populating the global core set to "make it work", brings that defect back.
    /// </para>
    /// <para>
    /// The key is DECLARED here, by catalog, and its core level is BOUND elsewhere, by a getter over
    /// the registered channels (<see cref="ChannelCoreConfigKeys"/>): the two are different acts
    /// (CFG-203), and this level has no address for the declaration to carry — the core set is not
    /// written anywhere, it is composed of what the registered channels declare about themselves.
    /// </para>
    /// </summary>
    public static ConfigKey<IReadOnlyCollection<string>> ChannelsEnabled { get; } = Declared
        .Of<IReadOnlyCollection<string>>(ChannelsEnabledKeyName)

        // Both levels are declared WITHOUT a path, and the registrar of the catalogs therefore binds
        // neither: the tenant level is read by no node reader of this deployment, and the core level
        // is not written at an address at all — its set is composed of what the registered channels
        // declare about themselves, which is why the owner binds it with a getter of its own
        // (ChannelCoreConfigKeys).
        .AtIdentity(ConfigLevel.Core)
        .AtIdentity(ConfigLevel.Tenant)
        .Set(IntersectChannelSets, coreParticipatesInSetIntersection: false)
        .Declare();

    /// <summary>
    /// Declares the credential key of one channel: the name is <c>Channels.{Type}.Credentials</c>,
    /// and the value type is the channel's own credential group. One key per channel rather than one
    /// parameterized key of an erased type: the channel and its credential group are known statically
    /// at every point that declares or consumes the key, so nothing has to cast back on the way out.
    /// Public because the channel that owns the credentials declares its own key: the shape of the
    /// key — its canonical name and its level model — stays here, in one place, while the channel
    /// supplies the two things only it knows, its type and its credential group.
    /// <para>
    /// The catalog is a PARAMETER because the key is produced per channel and has no static home of
    /// its own: the channel that owns the credentials owns their declaration too, and hands in the
    /// catalog its own composition registers. The core level is bound by that channel with a getter
    /// over its <c>IOptions</c>, so the declaration carries no address for it.
    /// </para>
    /// </summary>
    /// <typeparam name="TCredentials">Channel tenant-credential group type (041.1).</typeparam>
    /// <param name="catalog">Catalog of the OWNING channel — the declaration is written into it.</param>
    /// <param name="channelType">Channel type of the owning channel.</param>
    /// <returns>Credential key of the channel (Value semantics: the first defined layer).</returns>
    public static ConfigKey<TCredentials> CredentialKey<TCredentials>(
        ConfigKeyCatalog catalog,
        string channelType)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog
            .Of<TCredentials>($"Channels.{channelType}.Credentials")

            // Neither level carries a path, so the registrar of the catalogs binds neither: the tenant
            // level has no node reader in this deployment, and the core level is read off the channel's
            // own IOptions by a getter the channel registers itself.
            .AtIdentity(ConfigLevel.Core)
            .AtIdentity(ConfigLevel.Tenant)

            // Channel credentials are the secret keys of the product: the resolver keeps their values
            // out of diagnostics, and a last valid value is served for degradation only while it is
            // young enough for a revocation not to have been missed.
            .Secret()
            .Declare();
    }

    /// <summary>
    /// Intersection of channel sets (CFG-211): the result is what both the upper and the lower
    /// layer allow. Narrowing, not expanding (the lower layer cannot add a channel beyond the upper one).
    /// </summary>
    /// <param name="upper">Set of the level higher in ownership (tenant).</param>
    /// <param name="lower">Set of the level lower in ownership.</param>
    /// <returns>Intersection of the sets.</returns>
    private static IReadOnlyCollection<string> IntersectChannelSets(
        IReadOnlyCollection<string> upper,
        IReadOnlyCollection<string> lower)
    {
        // Narrowing: keep only the channels present on both layers.
        var upperSet = new HashSet<string>(upper, StringComparer.Ordinal);
        return lower.Where(upperSet.Contains).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Declarations of the contour — what its composition registers
    /// (<c>AddVeriqaChannelAdapters</c>), and what the schema of a deployment carries because of it.
    /// </summary>
    public static ConfigKeyCatalog Catalog => Declared;
}
