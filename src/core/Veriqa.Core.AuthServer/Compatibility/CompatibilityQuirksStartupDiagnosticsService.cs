// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Compatibility;

/// <summary>
/// Startup report on the enabled client compatibility quirks. Each quirk is a relaxation of the request
/// form, i.e. a deployment anomaly that the operator must be able to see in the log — hence the Warning
/// level and a single record per start.
/// <para>
/// The report is built from the EFFECTIVE value returned by the resolver, not from the raw configuration:
/// enabling is two-step, and a quirk that is configured but not in effect would otherwise leave the
/// integrator with a silent "switched it on, nothing happened". That case gets its own record, which reads
/// the deployment gate and names either the closed gate or — with the gate open — the configuration itself
/// as the reason, because the two call for different operator actions. A deployment without configured
/// quirks stays quiet.
/// </para>
/// </summary>
internal sealed class CompatibilityQuirksStartupDiagnosticsService : IHostedService
{
    /// <summary>
    /// Name of the deployment gate as it is spelled in configuration (named in the log together with its
    /// state whenever a configured relaxation does not apply).
    /// </summary>
    private const string GateConfigurationKey =
        OidcClientsOptions.SectionName + ":" + nameof(OidcClientsOptions.CompatibilityQuirksAllowApplicationOverride);

    /// <summary>
    /// OIDC client settings.
    /// </summary>
    private readonly OidcClientsOptions _clientsOptions;

    /// <summary>
    /// Canonical configuration resolver (the effective quirk set per client).
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<CompatibilityQuirksStartupDiagnosticsService> _logger;

    /// <summary>
    /// Creates the startup report service.
    /// </summary>
    /// <param name="clientsOptions">OIDC client settings.</param>
    /// <param name="resolver">Canonical configuration resolver.</param>
    /// <param name="logger">Logger.</param>
    public CompatibilityQuirksStartupDiagnosticsService(
        IOptions<OidcClientsOptions> clientsOptions,
        IConfigurationResolver resolver,
        ILogger<CompatibilityQuirksStartupDiagnosticsService> logger)
    {
        _clientsOptions = clientsOptions.Value;
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The method splits the configured clients into those whose relaxations are in effect and those
        // whose relaxations are configured but suppressed by the closed gate, and reports each group once.

        var effective = new List<string>();
        var suppressed = new List<string>();

        foreach (var client in _clientsOptions.Clients)
        {
            if (string.IsNullOrEmpty(client.ClientId))
            {
                continue;
            }

            var configured = client.CompatibilityQuirks is { Count: > 0 };

            var resolved = (await _resolver.ResolveAsync(
                    AuthServerConfigKeys.ClientCompatibilityQuirks,
                    ResolutionContext.Of(tenantId: null, client.ClientId, uiConfigSelector: null),
                    ConfigDimensionValues.None,
                    cancellationToken)).Value
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (resolved.Count > 0)
            {
                effective.Add($"{client.ClientId} -> {string.Join(", ", resolved)}");
            }
            else if (configured)
            {
                suppressed.Add(client.ClientId);
            }
        }

        if (effective.Count > 0)
        {
            _logger.LogWarning(
                "Client compatibility quirks are in effect: {Quirks}. Each quirk relaxes the request form for one client and is expected to be retired once its removal condition is met",
                string.Join("; ", effective));
        }

        if (suppressed.Count > 0)
        {
            // Two different operator actions hide behind the same "configured but not in effect": with a
            // closed gate the fix is to open it, while an empty effective set under an OPEN gate means the
            // client entry never reached the resolver at all (a second entry with the same ClientId shadows
            // it, or the value was lost elsewhere in the configuration) and opening anything would not help.
            // Hence the gate is read here instead of being assumed to be the cause.
            if (_clientsOptions.CompatibilityQuirksAllowApplicationOverride)
            {
                _logger.LogWarning(
                    "Client compatibility quirks are configured but NOT in effect for {Clients}: the deployment gate {Gate} is open, so the effective set is empty for another reason; check the configuration for a second client entry with the same identifier shadowing this one",
                    string.Join(", ", suppressed),
                    GateConfigurationKey);
            }
            else
            {
                _logger.LogWarning(
                    "Client compatibility quirks are configured but NOT in effect for {Clients}: the deployment gate {Gate} is closed",
                    string.Join(", ", suppressed),
                    GateConfigurationKey);
            }
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
