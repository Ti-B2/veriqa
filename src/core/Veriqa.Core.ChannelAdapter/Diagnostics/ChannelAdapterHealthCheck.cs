// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Abstractions;

namespace Veriqa.Core.ChannelAdapter.Diagnostics;

/// <summary>
/// Health check reporting the state of every registered channel adapter. It asks each adapter the
/// question its own contract already answers — <see cref="IChannelAdapter.GetHealthAsync"/> — and
/// turns the answers into a single report that names every channel separately.
/// </summary>
/// <remarks>
/// <para>
/// The status this check produces never rises above <see cref="HealthStatus.Degraded"/>. An outage
/// on the channel platform's side says nothing about this replica, while the general health
/// endpoint aggregates every check without a tag filter and maps the failing status to HTTP 503 —
/// an unreachable Telegram would take a perfectly working instance out of rotation. Keeping the
/// ceiling inside the check makes that hold by construction, whichever endpoint the orchestrator
/// happens to probe.
/// </para>
/// <para>
/// The adapters are never probed from the request that reads the report. The same general health
/// endpoint is anonymous and exempt from rate limiting, so probing inline would tie two things to
/// the rate of incoming requests: the number of live calls made with the channel's production
/// credentials (an anonymous loop over the endpoint would walk a bot token into the platform's
/// 429 and take down the delivery of login codes), and the response time of the endpoint itself
/// (a channel that never answers would add its whole probe timeout to a probe an orchestrator
/// runs with a shorter deadline, and a healthy replica would be restarted). Instead the check
/// serves the last snapshot it took, and the read that finds that snapshot aged past
/// <see cref="SnapshotTtl"/> starts a new probe round in the background: the platform is called at
/// most once per TTL per replica, whoever asks and however often — and, past the round taken at
/// host start-up, not at all while nobody asks: a read is the only thing that moves the round.
/// </para>
/// <para>
/// Which channel is down is answered by the per-channel report in
/// <see cref="HealthCheckResult.Data"/>; why it is down — by a warning in the log. The reason an
/// adapter hands back is free text (an exception message among other things), and free text from
/// an HTTP client may carry a URL with a token, so it never reaches an endpoint an operator reads
/// (SPEC-003 §18.3, CA-121).
/// </para>
/// </remarks>
internal sealed class ChannelAdapterHealthCheck : IHealthCheck, IHostedService, IDisposable
{
    /// <summary>
    /// Report value of a channel that answered it is healthy.
    /// </summary>
    private const string HealthyValue = "healthy";

    /// <summary>
    /// Report value of a channel that answered it is not healthy, or failed to answer at all.
    /// </summary>
    private const string UnhealthyValue = "unhealthy";

    /// <summary>
    /// Report value of a channel that did not answer within <see cref="ProbeTimeout"/>.
    /// </summary>
    private const string TimeoutValue = "timeout";

    /// <summary>
    /// Suffix of the per-channel report key carrying the probe response time in milliseconds.
    /// </summary>
    private const string ResponseTimeKeySuffix = ".response_time_ms";

    /// <summary>
    /// Per-channel probe timeout: an unreachable channel must not hold the probe round itself.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a snapshot is served before reading the report starts a new probe round. It bounds
    /// the rate of live calls to the channel platforms from above — one round per interval per
    /// replica, however often the endpoint is hit — but it does not bound staleness on its own: past
    /// the round taken at host start-up only a read moves a round, there is no timer behind it. The
    /// effective refresh interval is therefore the longer of this interval and the interval the
    /// report is actually read at, and an outage is visible within two of those — two of this
    /// interval only while the report is read at least once per interval.
    /// </summary>
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Registered channel adapters; empty on a deployment with no channel enabled.
    /// </summary>
    private readonly IEnumerable<IChannelAdapter> _adapters;

    /// <summary>
    /// Clock of the channel contour, used to measure the probes this check times itself and to age
    /// the snapshot it serves.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Logger the reason of an unhealthy channel is written to.
    /// </summary>
    private readonly ILogger<ChannelAdapterHealthCheck> _logger;

    /// <summary>
    /// Cancelled when the host stops, so that a probe round in flight does not outlive it.
    /// </summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// Guards <see cref="_refresh"/>: only one probe round runs at a time, however many requests
    /// find the snapshot stale at once.
    /// </summary>
    private readonly Lock _refreshGate = new();

    /// <summary>
    /// The probe round in flight, if any.
    /// </summary>
    private Task? _refresh;

    /// <summary>
    /// The last completed probe round; null until the first one completes.
    /// </summary>
    private ChannelHealthSnapshot? _snapshot;

    /// <summary>
    /// Creates the channel health check.
    /// </summary>
    /// <param name="adapters">Registered channel adapters.</param>
    /// <param name="timeProvider">Clock of the channel contour.</param>
    /// <param name="logger">Logger for the reason of an unhealthy channel.</param>
    public ChannelAdapterHealthCheck(
        IEnumerable<IChannelAdapter> adapters,
        TimeProvider timeProvider,
        ILogger<ChannelAdapterHealthCheck> logger)
    {
        _adapters = adapters;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Reports the channel states from the last snapshot, starting a new probe round in the
    /// background when that snapshot has aged out. Nothing here waits for a channel: the report is
    /// read by an anonymous endpoint whose latency and call rate must not follow the request rate.
    /// </summary>
    /// <param name="context">Health check context supplied by the health check service.</param>
    /// <param name="cancellationToken">
    /// Cancellation token of the probe. Unused: the report is built from memory and completes
    /// synchronously, so there is nothing to cancel.
    /// </param>
    /// <returns>Report of the channel states.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = Volatile.Read(ref _snapshot);

        if (snapshot is null || _timeProvider.GetElapsedTime(snapshot.TakenAt) >= SnapshotTtl)
        {
            StartRefresh();
        }

        // Until the first round completes, every channel counts as one that has not answered — the
        // very meaning of the unhealthy value here. Calling a channel healthy before anything asked
        // it is the one answer this check must never give; the window is the first probe round of
        // the process, and the round is already started at host start-up.
        return Task.FromResult(BuildReport(snapshot?.Probes ?? NotAnsweredYet()));
    }

    /// <summary>
    /// Takes the first snapshot at host start-up, so that the first reader of the report already
    /// finds channel states rather than the not-answered-yet placeholder.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token of the host start-up.</param>
    /// <returns>Completed task: the probe round runs in the background.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartRefresh();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the probe round in flight and waits for it, so that no channel call outlives the host —
    /// but no longer than the shutdown itself is allowed to take.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token of the host shutdown.</param>
    /// <returns>
    /// Task that completes once the round in flight has finished, or once the shutdown deadline has
    /// passed — whichever comes first.
    /// </returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();

        Task? refresh;

        lock (_refreshGate)
        {
            refresh = _refresh;
        }

        if (refresh is not null)
        {
            try
            {
                // The wait observes the shutdown deadline of the host: a third-party adapter that
                // ignores the probe token would otherwise hold the whole process well past its
                // ShutdownTimeout. The round swallows the cancellation the line above causes;
                // anything else it could surface here is a fault worth seeing in the shutdown log.
                await refresh.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown ran out of its own deadline first. The round is left behind rather than
                // waited out: all it can still touch is a snapshot nobody will read again.
                _logger.LogDebug("Channel health probe round outlived the shutdown deadline and was left behind");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
    }

    /// <summary>
    /// Turns a completed probe round into the health report.
    /// </summary>
    /// <param name="probes">Outcomes of the round, one per registered channel.</param>
    /// <returns>Report of the channel states.</returns>
    private static HealthCheckResult BuildReport(ChannelProbe[] probes)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        var healthy = 0;

        foreach (var probe in probes)
        {
            // The report value comes from the closed set of constants above and never from a string
            // the adapter handed back — that is what keeps a reason out of the response body.
            switch (probe.Outcome)
            {
                case ChannelProbeOutcome.Healthy:
                    data[probe.ChannelType] = HealthyValue;
                    healthy++;
                    break;

                case ChannelProbeOutcome.Timeout:
                    data[probe.ChannelType] = TimeoutValue;
                    break;

                default:
                    data[probe.ChannelType] = UnhealthyValue;
                    break;
            }

            data[probe.ChannelType + ResponseTimeKeySuffix] = probe.ResponseTimeMs;
        }

        var description = $"{healthy}/{probes.Length} channels healthy";

        // Every enabled channel healthy — Healthy; anything else, the case of no healthy channel at
        // all included, — Degraded. The two are told apart by the per-channel report rather than by
        // the status: the ceiling above forbids anything worse.
        // No channel enabled at all is a healthy "0/0": that deployment is already covered by the
        // start-up warning of the channel contour (CA-110) and needs no second signal.
        return healthy == probes.Length
            ? HealthCheckResult.Healthy(description, data)
            : HealthCheckResult.Degraded(description, data: data);
    }

    /// <summary>
    /// Placeholder round used before the first real one completes: every registered channel is
    /// reported as one that has not answered.
    /// </summary>
    /// <returns>Outcomes of the registered channels, none of them healthy.</returns>
    private ChannelProbe[] NotAnsweredYet() =>
        _adapters
            .Select(adapter => new ChannelProbe(adapter.ChannelType, ChannelProbeOutcome.Unhealthy, ResponseTimeMs: 0))
            .ToArray();

    /// <summary>
    /// Starts a probe round unless one is already running, the snapshot is fresh again or the host
    /// is stopping.
    /// </summary>
    private void StartRefresh()
    {
        lock (_refreshGate)
        {
            if (_stopping.IsCancellationRequested || _refresh is { IsCompleted: false })
            {
                return;
            }

            // The snapshot is read again under the gate: a round that finished between the caller's
            // own read of it and this line has already published a fresh one, and starting a second
            // round for the same staleness would break the one-round-per-TTL bound.
            var snapshot = Volatile.Read(ref _snapshot);

            if (snapshot is not null && _timeProvider.GetElapsedTime(snapshot.TakenAt) < SnapshotTtl)
            {
                return;
            }

            // Detached from whatever asked for it: the caller is a request that must not wait for a
            // channel, and its own cancellation token dies with the response.
            _refresh = Task.Run(RefreshAsync, CancellationToken.None);
        }
    }

    /// <summary>
    /// Probes every registered adapter in parallel and publishes the result as the new snapshot.
    /// </summary>
    /// <returns>Task that completes once the round has finished.</returns>
    private async Task RefreshAsync()
    {
        try
        {
            // The adapters are probed in parallel: a sequential walk would add up the timeouts of
            // every unreachable channel, and one round has to fit inside the snapshot interval.
            var probes = await Task.WhenAll(
                _adapters.Select(adapter => ProbeAsync(adapter, _stopping.Token)));

            Volatile.Write(ref _snapshot, new ChannelHealthSnapshot(probes, _timeProvider.GetTimestamp()));
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            // The host is stopping: the round is abandoned and the previous snapshot is left as it
            // is. A partial round is never published — a channel not asked is not a channel down.
            _logger.LogDebug("Channel health probe round abandoned: the host is stopping");
        }
    }

    /// <summary>
    /// Probes a single adapter under its own timeout and classifies the answer.
    /// Cancellation of the round itself is not classified — it is propagated, so a cancelled round
    /// publishes no partial snapshot.
    /// </summary>
    /// <param name="adapter">Adapter to probe.</param>
    /// <param name="cancellationToken">Cancellation token of the round.</param>
    /// <returns>Outcome of the single channel.</returns>
    private async Task<ChannelProbe> ProbeAsync(IChannelAdapter adapter, CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetTimestamp();

        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCancellation.CancelAfter(ProbeTimeout);

        try
        {
            var health = await adapter.GetHealthAsync(probeCancellation.Token);

            if (health.IsFailure)
            {
                _logger.LogWarning("Channel {ChannelType} health probe failed with error code {ErrorCode}", adapter.ChannelType, health.Error.Code);

                return new ChannelProbe(adapter.ChannelType, ChannelProbeOutcome.Unhealthy, Elapsed(startedAt));
            }

            var status = health.Value;
            var responseTimeMs = (long)status.ResponseTime.TotalMilliseconds;

            if (!status.IsHealthy)
            {
                _logger.LogWarning("Channel {ChannelType} reported itself unhealthy: {Reason}", adapter.ChannelType, status.Details);

                return new ChannelProbe(adapter.ChannelType, ChannelProbeOutcome.Unhealthy, responseTimeMs);
            }

            return new ChannelProbe(adapter.ChannelType, ChannelProbeOutcome.Healthy, responseTimeMs);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the linked source fired: the channel ran out of its own timeout while the round
            // is still alive, so the remaining channels are counted to the end.
            _logger.LogWarning(
                "Channel {ChannelType} health probe did not answer within {ProbeTimeoutSeconds} seconds",
                adapter.ChannelType,
                ProbeTimeout.TotalSeconds);

            return new ChannelProbe(adapter.ChannelType, ChannelProbeOutcome.Timeout, Elapsed(startedAt));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Safety net for a third-party adapter: the shipped ones catch their own exceptions and
            // answer with an unhealthy status instead. Only the exception type reaches the log.
            _logger.LogWarning(
                "Channel {ChannelType} health probe threw {ExceptionType}",
                adapter.ChannelType,
                exception.GetType().Name);

            return new ChannelProbe(adapter.ChannelType, ChannelProbeOutcome.Unhealthy, Elapsed(startedAt));
        }
    }

    /// <summary>
    /// Milliseconds elapsed since the timestamp, for the branches where no adapter-reported
    /// response time exists.
    /// </summary>
    /// <param name="startedAt">Timestamp taken before the probe.</param>
    /// <returns>Elapsed time in whole milliseconds.</returns>
    private long Elapsed(long startedAt) =>
        (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

    /// <summary>
    /// How a single channel answered its probe.
    /// </summary>
    private enum ChannelProbeOutcome
    {
        /// <summary>The channel answered it is healthy.</summary>
        Healthy,

        /// <summary>The channel answered it is not healthy, or failed to answer.</summary>
        Unhealthy,

        /// <summary>The channel did not answer within the probe timeout.</summary>
        Timeout
    }

    /// <summary>
    /// Outcome of a single channel probe.
    /// </summary>
    /// <param name="ChannelType">Channel type the outcome belongs to.</param>
    /// <param name="Outcome">How the channel answered.</param>
    /// <param name="ResponseTimeMs">Response time in whole milliseconds.</param>
    private readonly record struct ChannelProbe(
        string ChannelType,
        ChannelProbeOutcome Outcome,
        long ResponseTimeMs);

    /// <summary>
    /// A completed probe round together with the moment it finished.
    /// </summary>
    /// <param name="Probes">Outcomes of the round, one per registered channel.</param>
    /// <param name="TakenAt">Timestamp of the clock taken when the round finished.</param>
    private sealed record ChannelHealthSnapshot(ChannelProbe[] Probes, long TakenAt);
}
