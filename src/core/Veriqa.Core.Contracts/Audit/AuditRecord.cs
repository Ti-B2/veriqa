// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Contracts.Audit;

/// <summary>
/// A single audit-trail record. The schema is fixed: arbitrary or truncated shapes are forbidden,
/// so every producer writes the same fields and every reader can rely on them.
/// </summary>
/// <remarks>
/// The record is immutable by contract: the journal is append-only, and there is no member —
/// here or on <see cref="IAuditSink"/> — that changes a record once it has been written.
/// Personal data never reaches this model: user identifiers appear only masked, and snapshot
/// objects of the transaction are not carried, apart from the parameters of the confirmed
/// operation a deployment turned on explicitly (SPEC-011 C43, N39).
/// <para>
/// Two members — <see cref="Sequence"/> and <see cref="PreviousRecordHash"/> — are the shape of a
/// tamper-evident chain and are NOT filled in by anything today. They are reserved space for a
/// possible future implementation, not a feature in progress: no computation and no verification
/// of the chain is scheduled, and neither is promised by any release. They are here because the
/// model is a public contract: adding them later would be a major version and a schema migration
/// for every integrator, while reserving them now costs a null. Read them as "not implemented",
/// never as "the chain verified".
/// </para>
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>
    /// Moment the audited event occurred (UTC). It is the moment of the event itself, not the
    /// moment the record was written: with asynchronous delivery the two differ.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Who acted: a masked channel identity, the application's client_id, or
    /// <see cref="AuditActors.System"/> for lifecycle transitions nobody initiated directly.
    /// Non-empty.
    /// </summary>
    public required string Actor { get; init; }

    /// <summary>
    /// Stable code of the audited event (see <see cref="AuditActionCodes"/> for the lifecycle codes).
    /// Non-empty.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Identifier of the audited object; for transaction lifecycle events — the transaction
    /// identifier. Non-empty.
    /// </summary>
    public required string Target { get; init; }

    /// <summary>
    /// Outcome of the audited event and, for failures, the reason code.
    /// </summary>
    public required AuditResult Result { get; init; }

    /// <summary>
    /// Closed set of safe attributes of the event. An event carrying none of them yields an empty
    /// instance rather than null.
    /// </summary>
    public AuditMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Position of the record in the journal, counted from the first one. Reserved space for a
    /// possible future tamper-evident chain, and always <see langword="null"/>: nothing computes
    /// it, no release is committed to computing it, and a null therefore says "no chain", not
    /// "position unknown".
    /// </summary>
    public long? Sequence { get; init; }

    /// <summary>
    /// Hash of the preceding record — the link that would make a removal or a rewrite in the
    /// middle of the journal visible. Reserved together with <see cref="Sequence"/> as space for a
    /// possible future implementation, and always <see langword="null"/>: neither the computation
    /// nor the verification of the chain exists or is scheduled, so a non-null value here proves
    /// nothing on its own.
    /// </summary>
    public string? PreviousRecordHash { get; init; }
}
