// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Contracts.Audit;

/// <summary>
/// Parameters of the confirmed operation, as the relying party declared them: which action kind was
/// confirmed and with which slot values.
/// </summary>
/// <remarks>
/// A copy of the pair the transaction already carries (<c>ConfirmationSnapshot</c>: the allowlisted
/// action type and the caller-supplied slot values, SPEC-039 C14/C16), not a second shape of the
/// same data — the journal must answer "what was confirmed" on its own, long after the transaction
/// itself is gone. The record outlives it by orders of magnitude: a completed transaction is swept
/// within minutes, while the journal keeps records for days or months, and
/// <see cref="AuditRecord.Target"/> then points at an object nobody can read any more.
/// <para>
/// These values are subject data of the relying party and may carry personal data, so they are
/// written only when the deployment turned them on explicitly (SPEC-011 C43, N39) — off by default,
/// because deciding otherwise would be deciding for the integrator.
/// </para>
/// </remarks>
public sealed record AuditConfirmationParameters
{
    /// <summary>
    /// Identifier of the declared confirmation message kind that was confirmed.
    /// </summary>
    public string? ActionType { get; init; }

    /// <summary>
    /// Slot values of that action kind: slot name → value in the canonical textual form of its
    /// declared type. Null or empty both mean "no values".
    /// </summary>
    public IReadOnlyDictionary<string, string>? SlotValues { get; init; }
}
