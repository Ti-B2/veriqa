// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Key of the inbound-verification axis — how the authenticity of an incoming event of a channel is
/// verified, as the integrator declares it (SPEC-003 §8, SPEC-012 CFG-247).
/// <para>
/// The home of this key is the mechanism assembly, and that is the same deliberate exception the
/// Logging axis already buys (<see cref="LoggingConfigKeys"/>) rather than a new norm: the two
/// consumers of the value live in contours that do not depend on one another — the channel contour,
/// which refuses to start a deployment that never declared the value, and the audit trail, which
/// writes it into a record — and this assembly is the only floor both already stand on. Declaring the
/// key inside either of them would make one a dependency of the other, and giving the journal a port
/// of its own would make the answer depend on the order in which two independent <c>Add*</c> calls of
/// a host ran.
/// </para>
/// <para>
/// Declaring a key and owning its home are different acts (CFG-203). This key enters the schema of a
/// deployment through the registrar of the catalogs, which the auth server registers; the channel
/// contour and the journal only READ it.
/// </para>
/// <para>
/// The core level is addressed BY CHANNEL, and the channel is the IDENTITY dimension of the key
/// (CFG-239): "how is an incoming event of channel X verified" loses its meaning without the channel,
/// so there is no step of the chain that addresses none — and the core has no closed list of channel
/// types, which is exactly why the channel is a dimension rather than a member per channel.
/// </para>
/// </summary>
public static class ChannelInboundVerificationConfigKeys
{
    /// <summary>
    /// Section of the application configuration the channel sections of the CORE level live under
    /// (self-hosted ≡ core, SPEC-012 §10.1). It is written here rather than taken from the section of
    /// some one channel: the address of this key names the channel by its DIMENSION, and no single
    /// channel's <c>SectionName</c> is the prefix of all of them.
    /// </summary>
    public const string ChannelsCoreSectionName = "Veriqa:Channels";

    /// <summary>
    /// Canonical name of the key.
    /// </summary>
    public const string InboundVerificationKeyName = "Channels.InboundVerification";

    /// <summary>
    /// Member of a channel section carrying the declared verification level.
    /// </summary>
    public const string InboundVerificationMember = "InboundVerification";

    /// <summary>
    /// Catalog of the declarations of this axis — what the one registrar of the deployment declares
    /// and binds. It is initialized before the declarations that fill it, which is the order the
    /// initializers of this class run in.
    /// </summary>
    private static readonly ConfigKeyCatalog Declared = new();

    /// <summary>
    /// Channels.InboundVerification (SPEC-012 CFG-247): the verification level a layer DECLARES for a
    /// channel. Owned by the core and by the tenant — in self-hosted the two coincide (CFG-202), and
    /// in Cloud the axis belongs to the tenant, who brings the bot and picks the transport. The
    /// application level is deliberately absent: an RP client makes no statement about how well the
    /// channel of the deployment is protected.
    /// <para>
    /// The value type is NULLABLE because the consumers of the resolved value have to tell "nobody
    /// declared one" from a member of the enum: a non-nullable enum would answer with its first member
    /// for a deployment that stated nothing, and the audit journal would record a check that never
    /// ran. The REFUSAL of such a deployment does not rest on it — the requirement is declared
    /// (<see cref="ConfigKeyBuilder{T}.Required"/>) and the mechanism answers it by the level that
    /// stated the value, not by the value itself.
    /// </para>
    /// <para>
    /// The value is written as a TOKEN of the dictionary the mechanism derives from the members of
    /// <see cref="ChannelInboundVerification"/> (<see cref="ConfigEnumTokens"/>): the tokens are
    /// snake_case, and the standard enum binder of the platform reads a member by its own name and by
    /// nothing else. The name of the member is admitted beside the token — an integrator writing
    /// <c>OutboundFetch</c> by the example of the neighbouring members of the same channel section
    /// names the same value as one writing <c>outbound_fetch</c> — while the canonical spelling that
    /// the documentation and the journal print stays the one token. A spelling that is neither leaves
    /// the level unset, and it is not passed off as a level that wrote nothing: the read says that the
    /// address holds a value it cannot take, and the refusal of the start names the whole dictionary.
    /// </para>
    /// </summary>
    public static ConfigKey<ChannelInboundVerification?> InboundVerification { get; } = Declared
        .Of<ChannelInboundVerification?>(InboundVerificationKeyName)
        .Identity(ConfigDimensionNames.Channel)
        .At(
            ConfigLevel.Core,
            ChannelsCoreSectionName + ":{" + ConfigDimensionNames.Channel + "}:" + InboundVerificationMember)

        // The tenant level is addressed by the identity of the key alone: the records of that level are
        // kept as rows "level × owner × key × dimensions" by the cloud contour, and there is no
        // document to walk into. A deployment that binds no such source simply leaves the level unset.
        .AtIdentity(ConfigLevel.Tenant)
        .EnumTokens<ChannelInboundVerification>()

        // How the authenticity of an incoming event is verified is a fact the integrator declares, and
        // the core neither derives it nor takes the adapter's word for it: a deployment that enabled a
        // channel and stated nothing here does not start. WHICH channels are owed the value is the
        // channel contour's question — the mechanism keeps no list of channel types.
        .Required()
        .Declare();

    /// <summary>
    /// Declarations of this axis — what the one registrar of the deployment declares and binds.
    /// </summary>
    public static ConfigKeyCatalog Catalog => Declared;
}
