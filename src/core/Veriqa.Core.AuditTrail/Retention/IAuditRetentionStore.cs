// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuditTrail.Retention;

/// <summary>
/// Deletion side of an audit store — the single channel through which a record ever leaves the
/// journal. Deliberately internal to this assembly: exposing it next to the write-only sink
/// contract would hand every consumer a way to erase audit records.
/// </summary>
internal interface IAuditRetentionStore
{
    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> records older than the cutoff.
    /// </summary>
    /// <param name="cutoff">Records with a timestamp strictly older than this are deleted.</param>
    /// <param name="batchSize">Upper bound on the number of records deleted in one call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of records actually deleted.</returns>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken);
}
