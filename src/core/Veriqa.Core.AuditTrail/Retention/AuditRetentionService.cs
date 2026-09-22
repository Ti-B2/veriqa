// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Veriqa.Core.AuditTrail.DependencyInjection;
using Veriqa.Core.Configuration;
using Veriqa.Core.Contracts.Audit;

namespace Veriqa.Core.AuditTrail.Retention;

/// <summary>
/// Background process enforcing the retention of the audit journal: records older than the
/// effective retention are deleted in batches. It is the only channel that removes a record.
/// </summary>
/// <remarks>
/// Retention bounds the lifetime of records that already exist, so this process runs regardless of
/// the current logging mode — switching the mode off stops new records from appearing, it does not
/// freeze the ones already written. A failed sweep is logged and retried on the next tick; records
/// younger than the cutoff are never touched.
/// </remarks>
internal sealed class AuditRetentionService : BackgroundService
{
    /// <summary>
    /// Canonical resolver of the effective setting value.
    /// </summary>
    private readonly IConfigurationResolver _configurationResolver;

    /// <summary>
    /// Store the sweep deletes from — the same instance the receiver appends to.
    /// </summary>
    private readonly IAuditRetentionStore _retentionStore;

    /// <summary>
    /// The sink the container actually resolves — the one the records go to. Held only to check at
    /// startup that it is still the store this sweep deletes from; the sweep never writes.
    /// </summary>
    private readonly IAuditSink _auditSink;

    /// <summary>
    /// Sweep parameters (host-owned, validated at registration).
    /// </summary>
    private readonly AuditRetentionOptions _options;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<AuditRetentionService> _logger;

    /// <summary>
    /// Clock the retention cutoff is computed from.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates the retention service.
    /// </summary>
    /// <param name="configurationResolver">Configuration resolver.</param>
    /// <param name="retentionStore">Audit store to sweep.</param>
    /// <param name="auditSink">Sink the container resolves — checked against the store at startup.</param>
    /// <param name="options">Sweep parameters.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time provider.</param>
    public AuditRetentionService(
        IConfigurationResolver configurationResolver,
        IAuditRetentionStore retentionStore,
        IAuditSink auditSink,
        IOptions<AuditRetentionOptions> options,
        ILogger<AuditRetentionService> logger,
        TimeProvider timeProvider)
    {
        _configurationResolver = configurationResolver;
        _retentionStore = retentionStore;
        _auditSink = auditSink;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WarnIfRecordsGoElsewhere();

        _logger.LogInformation(
            "Audit retention sweep started; interval: {SweepInterval}, batch: {BatchSize}",
            _options.SweepInterval,
            _options.BatchSize);

        using var timer = new PeriodicTimer(_options.SweepInterval);

        try
        {
            // The first sweep runs at startup, before the timer: PeriodicTimer only ticks after a
            // full interval, so a host whose uptime is shorter than that interval (frequent
            // deployments, scale-to-zero, a crash loop) would never enforce the retention at all —
            // and this is the only channel that deletes a record.
            await SweepAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        _logger.LogInformation("Audit retention sweep stopped");
    }

    /// <summary>
    /// Says out loud at startup that the records and their deletion have come apart: the sink the
    /// container resolved is not the store this sweep deletes from.
    /// </summary>
    /// <remarks>
    /// The registration refuses a sink that shadows the built-in one, but it can only see the
    /// collection as it stands when it returns — an <see cref="IAuditSink"/> registered into the
    /// host collection on the next line is invisible to it, and the divergence it exists to prevent
    /// then happens without a word: the records go to the newcomer while this sweep keeps clearing
    /// a built-in store nobody writes to any more. By the time the sweep starts both sides are
    /// resolved, so the pair is compared here — the last point at which the mismatch is still
    /// observable at all.
    /// <para>
    /// A notice, not a refusal: a sink that decorates the built-in one is a different instance yet
    /// writes through to the very store swept here, and stopping the host over a legitimate setup
    /// would cost more than a notice an operator can dismiss. The sweep itself runs on either
    /// reading — it clears the store it was given, which a decorated built-in sink still fills, and
    /// which a genuine replacement leaves empty.
    /// </para>
    /// </remarks>
    private void WarnIfRecordsGoElsewhere()
    {
        if (ReferenceEquals(_auditSink, _retentionStore))
        {
            return;
        }

        _logger.LogWarning(
            "Audit records go to {SinkType} while the retention sweep deletes from {StoreType}: an "
            + "IAuditSink registered after AddVeriqaAuditTrail(...) returned has taken the records "
            + "over, and nothing ever deletes them. Keep exactly one sink, or choose it inside "
            + "AddVeriqaAuditTrail so that its retention is stated. A sink that wraps the built-in "
            + "one writes through to the swept store and this notice does not apply to it.",
            _auditSink.GetType(),
            _retentionStore.GetType());
    }

    /// <summary>
    /// Runs one sweep: resolves the effective retention, computes the cutoff and deletes in batches
    /// until a batch comes back short — that is how an accumulated backlog is cleared in one pass.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token of the host.</param>
    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The effective value comes from the canonical resolver, never from the options class
            // directly: the retention is a levelled setting, and a second reader would be a second
            // precedence.
            var retentionDays = (await _configurationResolver.ResolveAsync(
                LoggingConfigKeys.RetentionDays,
                ResolutionContext.Core,
                ConfigDimensionValues.None,
                stoppingToken)).Value;

            // A non-positive retention is never an instruction to wipe the journal: sweeping on it
            // would put the cutoff at "now" and delete every record ever written, through the one
            // channel allowed to delete at all. The pass is skipped instead, loudly.
            //
            // TWO different deployments arrive here, and the resolution cannot tell them apart — it
            // reports "no level defined the setting" for both, and the value is the default of the
            // type — so the warning names the operator's action for each:
            //   * the key is not DECLARED in this host. The catalog of the Logging axis travels with
            //     the configuration composition of the auth server (and with that of a channel
            //     satellite); the audit trail never registers it, so a host wiring the journal onto
            //     the transaction engine alone has these keys outside its schema. The strict domain
            //     of the key does not hold such a host at the start either: the start refuses a value
            //     the snapshot report COVERS, and an undeclared key it does not cover at all;
            //   * the key is declared, and a configuration RELOAD brought an inadmissible retention
            //     where a readable one used to stand, with no earlier value to answer with. A host
            //     that states such a retention at the START does not start at all — the setting
            //     refuses to be served another value in place of one it does not admit.
            if (retentionDays <= 0)
            {
                _logger.LogWarning(
                    "Audit retention resolved to {RetentionDays} day(s); the sweep is skipped and no record "
                    + "is deleted. Either no level defines the retention in this host — register the config "
                    + "key catalogs of the logging axis, which the configuration of the auth server brings "
                    + "and the audit trail does not — or the configuration now states a retention outside "
                    + "what the setting admits, which it refuses to have replaced by another value: state a "
                    + "positive number of days in the logging section. The next tick sweeps",
                    retentionDays);

                return;
            }

            var cutoff = _timeProvider.GetUtcNow().AddDays(-retentionDays);

            var totalDeleted = 0;
            int deleted;

            do
            {
                deleted = await _retentionStore.DeleteOlderThanAsync(cutoff, _options.BatchSize, stoppingToken);
                totalDeleted += deleted;
            }
            while (deleted == _options.BatchSize);

            if (totalDeleted > 0)
            {
                _logger.LogInformation(
                    "Audit retention sweep removed {DeletedCount} record(s) older than {Cutoff}",
                    totalDeleted,
                    cutoff);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown in the middle of a sweep — the remaining records wait for the next run.
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Audit retention sweep failed; it will be retried on the next tick");
        }
    }
}
