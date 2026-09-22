// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.TransactionEngine.Configuration;

namespace Veriqa.Core.AuthServer.InitiatorContext;

/// <summary>
/// Startup diagnostics for the initiator context check configuration (SPEC-017):
/// warns when geolocation is enabled without an available provider (ICC-082)
/// and when the anomaly heuristic is enabled without a history source (ICC-072).
/// Does not block startup — graceful behavior.
/// </summary>
internal sealed class InitiatorContextStartupDiagnosticsService : IHostedService
{
    /// <summary>
    /// Initiator context check settings.
    /// </summary>
    private readonly IOptions<InitiatorContextOptions> _options;

    /// <summary>
    /// Offline GeoIP provider.
    /// </summary>
    private readonly IGeoIpProvider _geoIpProvider;

    /// <summary>
    /// Anomaly heuristic.
    /// </summary>
    private readonly IInitiatorAnomalyDetector _anomalyDetector;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<InitiatorContextStartupDiagnosticsService> _logger;

    /// <summary>
    /// Creates the diagnostics service.
    /// </summary>
    /// <param name="options">Initiator context check settings.</param>
    /// <param name="geoIpProvider">Offline GeoIP provider.</param>
    /// <param name="anomalyDetector">Anomaly heuristic.</param>
    /// <param name="logger">Logger.</param>
    public InitiatorContextStartupDiagnosticsService(
        IOptions<InitiatorContextOptions> options,
        IGeoIpProvider geoIpProvider,
        IInitiatorAnomalyDetector anomalyDetector,
        ILogger<InitiatorContextStartupDiagnosticsService> logger)
    {
        _options = options;
        _geoIpProvider = geoIpProvider;
        _anomalyDetector = anomalyDetector;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The method checks configuration consistency and writes warnings

        var options = _options.Value;

        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // Geolocation is enabled but the provider is unavailable — geo fields will remain null (ICC-082)
        if (options.CollectGeoLocation && !_geoIpProvider.IsAvailable)
        {
            _logger.LogWarning(
                "Initiator context geolocation is enabled but the GeoIP provider is unavailable — geo fields will remain null. Code: {Code}",
                InitiatorContextResultCodes.GeoIpUnavailable);
        }

        // The anomaly heuristic is enabled but there is no history to compare against — graceful no-op (ICC-072)
        if (options.AnomalyDetection.Enabled && !_anomalyDetector.HasHistorySource)
        {
            _logger.LogWarning(
                "Anomaly heuristic is enabled but the context history source is missing — the heuristic is inactive. Code: {Code}",
                InitiatorContextResultCodes.AnomalyNoHistory);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
