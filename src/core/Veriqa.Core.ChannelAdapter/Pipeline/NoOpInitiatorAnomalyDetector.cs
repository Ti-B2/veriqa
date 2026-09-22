// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// No-op implementation of the anomaly heuristic (SPEC-017 §9, ICC-072):
/// there is no "typical context" history store by default, so the
/// heuristic is inactive and never flags an anomaly — the flow does not break.
/// The history store is a separate project decision (ICC-063): once it exists,
/// the host registers its own IInitiatorAnomalyDetector implementation.
/// </summary>
internal sealed class NoOpInitiatorAnomalyDetector : IInitiatorAnomalyDetector
{
    /// <inheritdoc />
    public bool HasHistorySource => false;

    /// <inheritdoc />
    public Task<InitiatorAnomalyResult> EvaluateAsync(
        string channelType,
        string channelUserId,
        InitiatorContextSnapshot initiatorContext,
        CancellationToken cancellationToken = default)
    {
        // No history source — no inference (ICC-071, ICC-072)
        return Task.FromResult(InitiatorAnomalyResult.None);
    }
}
