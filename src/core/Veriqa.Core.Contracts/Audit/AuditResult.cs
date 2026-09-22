// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Contracts.Audit;

/// <summary>
/// Outcome of an audited event: the binary verdict plus, for a failure, the reason code.
/// </summary>
public sealed record AuditResult
{
    /// <summary>
    /// Binary outcome of the event.
    /// </summary>
    public required AuditOutcome Outcome { get; init; }

    /// <summary>
    /// Reason code of a failure. Set for <see cref="AuditOutcome.Failure"/>; null for a success.
    /// </summary>
    public string? ReasonCode { get; init; }
}
