// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.MultiTenancy;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Start-up report on the two halves of a channel disagreeing (SPEC-003 CA-198/CA-199): the
/// ADAPTER, which serves the channel, and the core-level DECLARATION (<see cref="CoreChannelDeclaration"/>), which
/// is the only thing the availability set <c>channels_enabled</c> is built from. A channel is
/// normally both; either half alone works in silence, and this pass is what breaks that silence.
/// <list type="number">
/// <item>an adapter registered without a declaration serves its route and answers the platform, yet
/// no reader of the availability set — the sign-in page, the polling source, the inbound
/// verification pass — ever sees the channel;</item>
/// <item>a declaration that reports the channel ENABLED while no adapter serves it leaves the
/// deployment showing a channel that has neither a route nor a client behind it.</item>
/// </list>
/// <para>
/// Both are reported at Warning and neither stops the host (CA-199), which is the decision this
/// pass carries out. Registering a channel outside the core availability axis is legitimate — an
/// adapter driven wholly by the integrator's own composition is entitled to stay out of it — so a
/// refusal would forbid a supported arrangement over a suspicion; a warning states the fact and
/// lets the level of logging silence it where the arrangement is deliberate.
/// </para>
/// <para>
/// The second case is reached two ways, and the message names both, because the cure differs. Either
/// the declaration simply has no adapter beside it — the two halves are separate registration steps,
/// and a channel from outside the product, which declares itself on the same public terms as a
/// shipped one, is one missed step away from this. Or the channel gates its own infrastructure on a
/// configuration flag, the way the channels shipped with the product do: that gate reads
/// <c>Enabled</c> off the channel's configuration section while the registration call runs, whereas
/// the declaration reads the same flag off the channel's bound options afterwards. The two disagree
/// whenever the value is not in the section at that moment — supplied by a <c>Configure</c> delegate
/// written in code, or by a configuration source added after the registration call. The section is
/// therefore a cure only where a channel binds itself to one: a channel from outside the product
/// owns its configuration, which the core neither knows nor binds.
/// </para>
/// <para>
/// It runs in <see cref="StartingAsync"/> — before any hosted service starts and before the web
/// server binds — and is registered ahead of the inbound-verification pass on purpose: that pass
/// REFUSES the start of a deployment that enabled a channel without declaring the verification of
/// its inbound events, and a channel enabled with no adapter behind it hits exactly that refusal.
/// The report has to reach the log before the refusal, or the operator is left with a message about
/// a missing setting for a channel their host never registered.
/// </para>
/// </summary>
internal sealed class ChannelDeclarationStartupDiagnosticsService : IHostedLifecycleService
{
    /// <summary>
    /// All channel adapters registered in the container (built-in and third-party).
    /// </summary>
    private readonly IEnumerable<IChannelAdapter> _adapters;

    /// <summary>
    /// Core-level facts declared by every registered channel.
    /// </summary>
    private readonly IEnumerable<CoreChannelDeclaration> _declarations;

    /// <summary>
    /// Logger of the start-up report.
    /// </summary>
    private readonly ILogger<ChannelDeclarationStartupDiagnosticsService> _logger;

    /// <summary>
    /// Creates the start-up report service.
    /// </summary>
    /// <param name="adapters">All channel adapters registered in the container.</param>
    /// <param name="declarations">Core-level facts declared by every registered channel.</param>
    /// <param name="logger">Logger.</param>
    public ChannelDeclarationStartupDiagnosticsService(
        IEnumerable<IChannelAdapter> adapters,
        IEnumerable<CoreChannelDeclaration> declarations,
        ILogger<ChannelDeclarationStartupDiagnosticsService> logger)
    {
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Compares the registered adapters against the registered declarations and reports every channel
    /// that has only one of the two. Channel types are matched the way the rest of the contour matches
    /// them — as the adapter and the declaration spell them, which the registration pattern of a
    /// channel type already keeps lowercase.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        var declarations = _declarations.ToList();
        var declaredTypes = declarations
            .Select(declaration => declaration.ChannelType)
            .ToHashSet(StringComparer.Ordinal);
        var servedTypes = _adapters
            .Select(adapter => adapter.ChannelType)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var channelType in servedTypes
            .Where(type => !declaredTypes.Contains(type))
            .Order(StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "Channel {ChannelType} has an adapter registered but no core-level declaration, so it "
                + "stays outside the availability set the core builds: the sign-in page does not offer "
                + "it, the polling source does not drive it and the inbound-verification pass does not "
                + "check it. Register a {DeclarationType} for it next to AddChannel. If the channel is "
                + "deliberately driven outside the core, this warning can be ignored.",
                channelType,
                nameof(CoreChannelDeclaration));
        }

        foreach (var declaration in declarations
            .Where(declaration => declaration.IsEnabled && !servedTypes.Contains(declaration.ChannelType))
            .OrderBy(declaration => declaration.ChannelType, StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "Channel {ChannelType} is declared ENABLED at core level, but no adapter serves it: no "
                + "route is mapped and no client is registered for the channel, while readers of the "
                + "availability set see it as available. Either no adapter is registered for the "
                + "channel at all, and the fix is to register one next to the declaration; or the "
                + "channel gates its infrastructure on the Enabled flag of a configuration section, "
                + "as the channels shipped with the product do, and that section did not carry the "
                + "flag while the channel was being registered: a value that reaches only the bound "
                + "options, from a Configure delegate in code or from a configuration source added "
                + "after the registration call, is what this declaration reads, and it registers "
                + "nothing.",
                declaration.ChannelType);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op: the report runs in <see cref="StartingAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the report runs in <see cref="StartingAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the report holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the report holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the report holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
