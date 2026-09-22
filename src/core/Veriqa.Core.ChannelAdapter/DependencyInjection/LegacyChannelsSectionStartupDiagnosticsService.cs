// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Startup report on a channel configuration left at the top level of the host configuration.
/// <para>
/// Veriqa is an embedded library, and its channel sections used to occupy the integrator's top-level
/// <c>Channels</c> key. They now live under the single <c>Veriqa</c> root, and the old path is not read
/// at all — a deployment that keeps it gets no channels registered, no routes mapped and, without this
/// report, no log record either. Hence the report: the failure it describes is otherwise silent.
/// </para>
/// <para>
/// The level is Warning rather than a startup failure: a top-level <c>Channels</c> key may belong to the
/// integrator's own application and have nothing to do with Veriqa, and a library is not entitled to
/// refuse to start over somebody else's configuration. A false warning is the cheaper of the two errors.
/// </para>
/// <para>
/// Scope: only the configuration of the process that called <c>AddVeriqaChannelAdapters</c> is examined.
/// Settings held by a separate orchestration process that hands the host just an explicitly listed set of
/// keys (an Aspire AppHost forwarding them as environment variables) never reach this configuration, so a
/// value left at the old path there stays unreported and has to be migrated by hand.
/// </para>
/// </summary>
internal sealed class LegacyChannelsSectionStartupDiagnosticsService : IHostedService
{
    /// <summary>
    /// Top-level configuration path the channel sections occupied before the move.
    /// </summary>
    private const string LegacySectionName = "Channels";

    /// <summary>
    /// Path the channel sections live under now — the parent of every channel's own
    /// <c>SectionName</c> (for example <c>Veriqa:Channels:Telegram</c>).
    /// </summary>
    private const string ExpectedSectionName = "Veriqa:Channels";

    /// <summary>
    /// Host configuration.
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<LegacyChannelsSectionStartupDiagnosticsService> _logger;

    /// <summary>
    /// Creates the startup report service.
    /// </summary>
    /// <param name="configuration">Host configuration.</param>
    /// <param name="logger">Logger.</param>
    public LegacyChannelsSectionStartupDiagnosticsService(
        IConfiguration configuration,
        ILogger<LegacyChannelsSectionStartupDiagnosticsService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // An absent section is the normal state after the move, and a declared-but-empty one
        // ("Channels": {}) states nothing at all — neither is worth a record.
        var legacySection = _configuration.GetSection(LegacySectionName);
        var isEmpty = string.IsNullOrEmpty(legacySection.Value) && !legacySection.GetChildren().Any();

        if (isEmpty)
        {
            return Task.CompletedTask;
        }

        // Only the two path names reach the log — never the values under them (tokens, addresses).
        _logger.LogWarning(
            "Configuration section {LegacySection} is present at the top level, but Veriqa reads its "
            + "channel settings from {ExpectedSection} only: settings left at the old path are ignored, "
            + "and no channel will be registered from them. Move the section under {ExpectedSection}. "
            + "If the top-level section belongs to your own application rather than to Veriqa, this "
            + "warning can be ignored.",
            LegacySectionName,
            ExpectedSectionName,
            ExpectedSectionName);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
