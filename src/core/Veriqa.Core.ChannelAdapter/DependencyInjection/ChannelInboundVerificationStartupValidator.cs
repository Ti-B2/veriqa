// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Start-up checks of the declared inbound-verification level of every channel enabled AT CORE LEVEL
/// (SPEC-003 CA-196/CA-197, SPEC-012 CFG-247):
/// <list type="number">
/// <item>an enabled channel whose level declared no value of the dictionary stops the host;</item>
/// <item>an enabled channel that FETCHES its updates itself and declares anything other than
/// <see cref="ChannelInboundVerification.OutboundFetch"/> is warned about, and the host starts.</item>
/// </list>
/// <para>
/// Only the second is this type's own. The first is the requirement DECLARED by the key
/// (<c>ConfigKeyBuilder.Required</c>), and the mechanism carries it out and words the refusal
/// (<see cref="RequiredConfigValues"/>): what stays here is the question the mechanism cannot ask —
/// WHICH channels are owed a value, which is the set of enabled core-level declarations and nothing
/// the mechanism keeps a list of.
/// </para>
/// <para>
/// Both walk the channels that declared themselves at core level
/// (<see cref="CoreChannelDeclaration"/>), and the pass keeps no list of channel types of its own —
/// there is not a single branch by channel type here, and there cannot be one.
/// </para>
/// <para>
/// The scope of the pass is therefore every registered <see cref="CoreChannelDeclaration"/> — the
/// very set the core-level <c>channels_enabled</c> is built from (<c>ChannelCoreConfigKeys</c>).
/// The pass covers exactly the channels this deployment can switch on at core level, and who ships
/// them makes no difference: the declaration type and its constructor are public, so a declaration
/// registered by a package outside this product is walked on the same terms as the shipped channels,
/// which write theirs through the internal <c>DeclareCoreChannel</c>. Outside the scope is a channel
/// that registered no declaration at all: <c>ChannelAdapterBuilder.AddChannel</c> registers the
/// adapter and its route, not a declaration, so a channel added by that call alone is in neither
/// <c>channels_enabled</c> nor this pass. The integrator owes the value for it just the same (the
/// key is read by the same resolver over the same <c>channel</c> dimension, and the audit record
/// carries it), but that duty is stated in the SPI documentation rather than enforced here.
/// </para>
/// <para>
/// It is a hosted LIFECYCLE service and not <c>ValidateOnStart</c> of an options class, for two
/// reasons of its own: the value is read by the resolver rather than bound into an options object,
/// and the check runs over the set of REGISTERED channels rather than over the contents of one
/// section. The precedent of the same shape is SPEC-012 CFG-119. <see cref="StartingAsync"/> also
/// fires before the web server binds and in a non-web host alike, so a polling-only deployment is
/// checked as well.
/// </para>
/// </summary>
internal sealed class ChannelInboundVerificationStartupValidator : IHostedLifecycleService
{
    /// <summary>
    /// Core-level facts declared by every registered channel.
    /// </summary>
    private readonly IEnumerable<CoreChannelDeclaration> _channels;

    /// <summary>
    /// Canonical resolver of the effective setting value.
    /// </summary>
    private readonly IConfigurationResolver _configurationResolver;

    /// <summary>
    /// The one refusal of a deployment that states no value for a setting declared obligatory.
    /// </summary>
    private readonly RequiredConfigValues _requiredConfigValues;

    /// <summary>
    /// Logger of the start-up warning.
    /// </summary>
    private readonly ILogger<ChannelInboundVerificationStartupValidator> _logger;

    /// <summary>
    /// Creates the start-up validator.
    /// </summary>
    /// <param name="channels">Core-level facts declared by every registered channel.</param>
    /// <param name="configurationResolver">Configuration resolver.</param>
    /// <param name="requiredConfigValues">Refusal of a deployment stating no obligatory value.</param>
    /// <param name="logger">Logger.</param>
    public ChannelInboundVerificationStartupValidator(
        IEnumerable<CoreChannelDeclaration> channels,
        IConfigurationResolver configurationResolver,
        RequiredConfigValues requiredConfigValues,
        ILogger<ChannelInboundVerificationStartupValidator> logger)
    {
        _channels = channels;
        _configurationResolver = configurationResolver;
        _requiredConfigValues = requiredConfigValues;
        _logger = logger;
    }

    /// <summary>
    /// Checks every core-level channel declaration before the host starts. A channel enabled without
    /// a declared value stops the host ahead of the first request; a polling channel declaring
    /// anything other than an outbound fetch is warned about and the start continues.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    /// <exception cref="InvalidOperationException">
    /// An enabled channel has no declared inbound-verification level — raised by the mechanism, which
    /// words the refusal out of the declaration of the key.
    /// </exception>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // A disabled channel raises no endpoint and is owed nothing: the shipped configurations carry
        // every channel switched off, and a refusal over their sections would stop them all. WHICH
        // channels are asked about is the one half of the question the mechanism cannot answer, and it
        // is answered here.
        var enabled = _channels.Where(channel => channel.IsEnabled).ToList();

        // Self-hosted (N=1): the core level is the one populated here. The tenant level of a cloud
        // deployment is checked by the onboarding of the tenant, not by the start of a host.
        await _requiredConfigValues.EnsureStatedAsync(
            ChannelInboundVerificationConfigKeys.InboundVerification,
            ResolutionContext.Core,
            enabled.Select(channel =>
                ConfigDimensionValues.Of((ConfigDimensionNames.Channel, channel.ChannelType))),
            cancellationToken);

        var mismatched = new List<(string ChannelType, ChannelInboundVerification Declared)>();

        foreach (var channel in enabled)
        {
            var declared = await _configurationResolver.ResolveAsync(
                ChannelInboundVerificationConfigKeys.InboundVerification,
                ResolutionContext.Core,
                ConfigDimensionValues.Of((ConfigDimensionNames.Channel, channel.ChannelType)),
                cancellationToken);

            if (declared.Value is { } verification
                && channel.UsesPolling
                && verification is not ChannelInboundVerification.OutboundFetch)
            {
                mismatched.Add((channel.ChannelType, verification));
            }
        }

        foreach (var (channelType, verification) in mismatched)
        {
            _logger.LogWarning(
                "Channel {ChannelType} fetches its updates itself, and no incoming request of the platform "
                + "reaches it, yet the deployment declares {DeclaredVerification} as the verification of its "
                + "inbound events at '{ConfigPath}'. Expected '{ExpectedVerification}'.",
                channelType,
                verification.ToToken(),
                AddressOf(channelType),
                ChannelInboundVerification.OutboundFetch.ToToken());
        }
    }

    /// <summary>
    /// No-op: the checks run in <see cref="StartingAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the checks run in <see cref="StartingAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the validator holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the validator holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the validator holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;


    /// <summary>
    /// Address a channel's value is declared at, spelled as an operator writes it in the application
    /// configuration of the host. The channel segment is the channel type as the channel declared it
    /// (lowercase), while the shipped sections are spelled in PascalCase: the two are the same address,
    /// because configuration section names are matched case-insensitively, and the message that prints
    /// this address says so. The core has no map of channel type to section name to spell it otherwise,
    /// and cannot have one — that map would be a branch by channel type.
    /// </summary>
    /// <param name="channelType">Channel type.</param>
    /// <returns>Configuration key of the value.</returns>
    private static string AddressOf(string channelType) =>
        $"{ChannelInboundVerificationConfigKeys.ChannelsCoreSectionName}"
        + $"{ConfigNode.PathSeparator}{channelType}"
        + $"{ConfigNode.PathSeparator}{ChannelInboundVerificationConfigKeys.InboundVerificationMember}";
}
