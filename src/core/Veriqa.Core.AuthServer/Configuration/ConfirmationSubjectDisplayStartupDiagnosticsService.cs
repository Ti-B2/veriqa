// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Configuration.Enums;
using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Startup report on a subject-display surface the configuration accepts but no display stands
/// behind (SPEC-012 §4.11).
/// <para>
/// The value is not refused: the closed set of the axis is the specification's, and rejecting a
/// member of it would be a new rule of configuration rather than an implementation of the one that
/// exists (CFG-108 makes only an UNKNOWN value fatal). Staying silent is the other thing it must not
/// do — the operator wrote a surface down and would take the display for configured. Hence one
/// Warning, at the start, for the level this host owns.
/// </para>
/// <para>
/// The set is read through the canonical resolver, at the CORE level, and not off the bound options:
/// the axis has one reader, and a report judging by a second one could say something the resolution
/// does not (SPEC-012 CFG-235). A level above the core is not asked about here — this is a report of
/// the start, and no tenant is owning anything at that moment.
/// </para>
/// </summary>
internal sealed class ConfirmationSubjectDisplayStartupDiagnosticsService : IHostedService
{
    /// <summary>
    /// Name of the setting as it is spelled in configuration (named in the log so the operator sees
    /// where the value is written).
    /// </summary>
    private const string SurfacesConfigurationKey =
        ConfirmationSubjectDisplayOptions.SectionName
        + ":" + nameof(ConfirmationSubjectDisplayOptions.Surfaces);

    /// <summary>
    /// Canonical layer resolver — the one reader of the axis.
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<ConfirmationSubjectDisplayStartupDiagnosticsService> _logger;

    /// <summary>
    /// Creates the startup report service.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="logger">Logger.</param>
    public ConfirmationSubjectDisplayStartupDiagnosticsService(
        IConfigurationResolver resolver,
        ILogger<ConfirmationSubjectDisplayStartupDiagnosticsService> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var showsOnHop = await _resolver.ShowsSubjectOnAsync(
            ResolutionContext.Core, ConfirmationSubjectSurface.HopInterstitial, cancellationToken);

        if (!showsOnHop)
        {
            return;
        }

        _logger.LogWarning(
            "The subject display surface {Surface} is reserved and no display is wired behind it: it is "
            + "accepted by {Setting}, and the subject is shown on the other surfaces of the set, but not "
            + "on this one",
            ConfirmationSubjectSurface.HopInterstitial,
            SurfacesConfigurationKey);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
