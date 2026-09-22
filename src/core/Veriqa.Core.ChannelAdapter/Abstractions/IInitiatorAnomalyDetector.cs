// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Abstractions;

/// <summary>
/// Basic initiator context anomaly heuristic (SPEC-017 §9).
/// A no-op implementation is registered by default (no history source — no verdict, ICC-072);
/// the host may replace it with its own implementation backed by a historical "typical context" store.
/// </summary>
public interface IInitiatorAnomalyDetector
{
    /// <summary>
    /// Indicates whether a history source is available for comparison.
    /// false — the heuristic is inactive even when the configuration flag is enabled (graceful no-op).
    /// </summary>
    bool HasHistorySource { get; }

    /// <summary>
    /// Compares the current initiator context with the one typical for the channel user.
    /// An anomaly is an informative marker (ICC-073) and does not affect the transaction flow.
    /// </summary>
    /// <param name="channelType">Confirmation channel type.</param>
    /// <param name="channelUserId">User identifier in the channel.</param>
    /// <param name="initiatorContext">Current initiator context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Anomaly heuristic verdict.</returns>
    Task<InitiatorAnomalyResult> EvaluateAsync(
        string channelType,
        string channelUserId,
        InitiatorContextSnapshot initiatorContext,
        CancellationToken cancellationToken = default);
}
