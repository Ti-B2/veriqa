// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Contracts.Audit;

/// <summary>
/// Binary outcome of an audited event.
/// </summary>
public enum AuditOutcome
{
    /// <summary>
    /// The audited event succeeded.
    /// </summary>
    Success,

    /// <summary>
    /// The audited event failed; the reason lives in <see cref="AuditResult.ReasonCode"/>.
    /// </summary>
    Failure
}
