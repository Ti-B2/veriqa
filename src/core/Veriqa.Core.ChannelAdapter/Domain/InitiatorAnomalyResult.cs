// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// Verdict of the basic initiator context anomaly heuristic (SPEC-017 §9).
/// </summary>
/// <param name="IsAnomalous">Whether the context diverges from the one typical for the user.</param>
/// <param name="ReasonKey">Natural Key of the localizable anomaly reason (null — no anomaly or a generic marker).</param>
public sealed record InitiatorAnomalyResult(bool IsAnomalous, string? ReasonKey)
{
    /// <summary>
    /// The "no anomaly" verdict (no data — no verdict, ICC-071).
    /// </summary>
    public static readonly InitiatorAnomalyResult None = new(false, null);
}
