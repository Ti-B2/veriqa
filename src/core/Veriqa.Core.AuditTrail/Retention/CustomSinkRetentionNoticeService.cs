// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Veriqa.Core.Contracts.Audit;

namespace Veriqa.Core.AuditTrail.Retention;

/// <summary>
/// Hosted service stating at startup that the built-in retention sweep is not running and why.
/// A sink outside the shipped ones stores records where its implementation decides, and the
/// deletion side of that storage is not something the satellite can reach, so the lifetime of the
/// records belongs to the sink. Saying so once at startup is what keeps that ownership observable
/// instead of leaving an operator to wonder why nothing is ever swept. The claim is re-checked
/// against the sink the container actually resolved before it is made — see
/// <see cref="StartAsync"/>.
/// </summary>
internal sealed class CustomSinkRetentionNoticeService : IHostedService
{
    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<CustomSinkRetentionNoticeService> _logger;

    /// <summary>
    /// The sink the container actually resolves — the subject of the notice, taken at startup
    /// rather than assumed from the registration that asked for the notice.
    /// </summary>
    private readonly IAuditSink _auditSink;

    /// <summary>
    /// Whether the registration also configured the sweep parameters — parameters that now have no
    /// sweep to configure.
    /// </summary>
    private readonly bool _retentionConfigured;

    /// <summary>
    /// Creates the notice service.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="auditSink">Sink the container resolves.</param>
    /// <param name="retentionConfigured">Whether the sweep parameters were configured.</param>
    public CustomSinkRetentionNoticeService(
        ILogger<CustomSinkRetentionNoticeService> logger,
        IAuditSink auditSink,
        bool retentionConfigured)
    {
        _logger = logger;
        _auditSink = auditSink;
        _retentionConfigured = retentionConfigured;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The notice is registered by a registration that saw no built-in sink; a later
        // AddVeriqaAuditTrail call on the same collection can still choose one and take over both
        // the records and their deletion. The claim below is therefore checked against the sink the
        // container ended up with: only a shipped sink carries the deletion side of its store, so
        // implementing the (assembly-internal) IAuditRetentionStore is what makes a sink built-in.
        // Saying nothing here is the whole point — the built-in sweep is registered and speaks for
        // itself.
        if (_auditSink is IAuditRetentionStore)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "Audit retention belongs to the audit sink in use: it is not one of the built-in sinks, "
            + "so the built-in retention sweep is not registered and no record is ever deleted by Veriqa.");

        if (_retentionConfigured)
        {
            _logger.LogInformation(
                "Audit retention parameters supplied through ConfigureRetention(...) are ignored: "
                + "they configure the built-in sweep, which is not registered for this sink.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
