// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Contracts.Audit;

/// <summary>
/// Write-only sink of the audit journal. Append is the only operation the contract offers:
/// the journal is append-only, so a member that reads, changes or drops a record would break
/// that guarantee structurally rather than by convention (SPEC-011 R33).
/// Retention is enforced by the journal implementation itself, not through this contract.
/// </summary>
public interface IAuditSink
{
    /// <summary>
    /// Appends a record to the journal.
    /// </summary>
    /// <param name="record">Record to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completing when the record has been appended.</returns>
    Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default);
}
