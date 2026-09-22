// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using Veriqa.Core.AuditTrail.Retention;
using Veriqa.Core.Contracts.Audit;

namespace Veriqa.Core.AuditTrail.Store;

/// <summary>
/// In-process audit sink for development and for running without a database.
/// </summary>
/// <remarks>
/// A development implementation with no durability guarantees whatsoever: the journal lives in the
/// process, is lost on restart and is bounded by <see cref="MaxRecords"/> — once the bound is
/// reached, the oldest record is dropped to make room for the new one. It therefore satisfies none
/// of the audit trail's retention or integrity promises, and the bound is the honest alternative to
/// a development process growing until it runs out of memory. Production deployments choose the
/// EF Core sink.
/// </remarks>
internal sealed class InMemoryAuditSink : IAuditSink, IAuditRetentionStore
{
    /// <summary>
    /// Upper bound on the number of records kept in the process.
    /// </summary>
    private const int MaxRecords = 10_000;

    /// <summary>
    /// Stored records keyed by the ordinal of the append. A dictionary rather than a list: the
    /// retention sweep removes entries concurrently with appends; an ordinal rather than a
    /// surrogate identifier: it puts the records in append order, which is what makes dropping the
    /// oldest one a matter of arithmetic instead of a scan.
    /// </summary>
    private readonly ConcurrentDictionary<long, AuditRecord> _records = new();

    /// <summary>
    /// Ordinal of the last append.
    /// </summary>
    private long _appendSequence;

    /// <summary>
    /// Ordinal up to which the records have already been dropped by the bound.
    /// </summary>
    private long _evictionCursor;

    /// <inheritdoc />
    public Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        var appended = Interlocked.Increment(ref _appendSequence);
        _records[appended] = record;

        // The ordinal is claimed before the record is stored, so an append stalled in between can
        // land after the eviction cursor has already passed its ordinal. Re-reading the cursor after
        // the store (ordered by the barrier) makes one of the two sides see the other.
        Interlocked.MemoryBarrier();

        if (Volatile.Read(ref _evictionCursor) >= appended)
        {
            _records.TryRemove(appended, out _);
        }

        // Everything older than the window of the last MaxRecords appends is dropped. The ordinal is
        // claimed before the removal, so concurrent appends never skip one and never race over the
        // same one; ordinals the retention sweep has already removed are simply not there.
        var oldestKept = appended - MaxRecords;

        while (true)
        {
            var cursor = Volatile.Read(ref _evictionCursor);
            if (cursor >= oldestKept)
            {
                break;
            }

            if (Interlocked.CompareExchange(ref _evictionCursor, cursor + 1, cursor) != cursor)
            {
                continue;
            }

            _records.TryRemove(cursor + 1, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> DeleteOlderThanAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // The keys are collected first: enumerating a concurrent dictionary while removing from it
        // is safe, but materializing the batch keeps the bound exact.
        var expiredKeys = _records
            .Where(entry => entry.Value.Timestamp < cutoff)
            .Take(batchSize)
            .Select(entry => entry.Key)
            .ToList();

        var deleted = 0;

        foreach (var key in expiredKeys)
        {
            if (_records.TryRemove(key, out _))
            {
                deleted++;
            }
        }

        return Task.FromResult(deleted);
    }
}
