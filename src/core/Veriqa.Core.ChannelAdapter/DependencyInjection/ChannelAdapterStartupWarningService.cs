// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Hosted service that logs a warning that no channel adapter is enabled.
/// Used instead of a startup exception so that profiles without channel configuration
/// (e.g. demo-core) start correctly.
/// </summary>
internal sealed class ChannelAdapterStartupWarningService : IHostedService
{
    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<ChannelAdapterStartupWarningService> _logger;

    /// <summary>
    /// Creates the warning service instance.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public ChannelAdapterStartupWarningService(ILogger<ChannelAdapterStartupWarningService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // CA-110: warn that no channel adapter is active
        _logger.LogWarning(
            "No channel adapter is enabled. "
            + "For production use enable at least one channel "
            + "(for example, Veriqa:Channels:Telegram:Enabled = true).");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
