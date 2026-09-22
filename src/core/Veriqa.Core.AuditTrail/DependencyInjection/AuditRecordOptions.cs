// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuditTrail.DependencyInjection;

/// <summary>
/// What a record carries beyond the safe attributes every record carries. Host-owned and supplied
/// through the registration delegate, for the same reason as
/// <see cref="AuditRetentionOptions"/>: the audit axis declares no configuration keys of its own
/// beyond the ones SPEC-012 already has.
/// </summary>
public sealed class AuditRecordOptions
{
    /// <summary>
    /// Whether the records of a confirmation transaction carry the parameters of the confirmed
    /// operation (the declared action kind and its slot values).
    /// </summary>
    /// <remarks>
    /// Every audited event of such a transaction carries them, not the confirmation alone: the
    /// question "which operation was this" is asked of a failure, an expiry and a completion just
    /// as often, and none of them can be answered from the transaction any more.
    /// <para>
    /// Off by default, and deliberately so: those parameters are subject data of the relying party
    /// and may contain personal data, so writing them into a journal kept for days or months is the
    /// integrator's decision to make. Turned on, the journal answers "which operation, with which
    /// parameters" on its own — the transaction it points at is swept minutes after it completes,
    /// long before the record expires (SPEC-011 C43, N39).
    /// </para>
    /// </remarks>
    public bool IncludeConfirmationParameters { get; set; }
}
