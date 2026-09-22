// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// In-memory implementation of <see cref="IChannelPromptMessageStore"/> (SPEC-003 §4.5).
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>. Suitable for single-instance,
/// self-hosted and demo scenarios (mirrors the engine's default in-memory transaction store). For a
/// multi-instance deployment a distributed implementation (e.g. Redis) can be layered on later.
///
/// Entries are removed on read (<see cref="TakeAsync"/>). Entries of transactions that never emit a
/// TTL-expiry event (confirmed/declined) are reclaimed by an opportunistic retention sweep so the
/// dictionary cannot grow unbounded.
/// </summary>
internal sealed class InMemoryChannelPromptMessageStore : IChannelPromptMessageStore
{
    /// <summary>
    /// Dictionary: transaction id (string form) → stored prompt entry.
    /// </summary>
    private readonly ConcurrentDictionary<string, StoredEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Clock the retention window is measured against.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates an in-memory prompt message store.
    /// </summary>
    /// <param name="timeProvider">Time provider.</param>
    public InMemoryChannelPromptMessageStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Counter of <see cref="SaveAsync"/> calls that triggers a periodic retention sweep.
    /// </summary>
    private int _saveCallCount;

    /// <summary>
    /// Guard flag (0/1) ensuring at most one retention sweep runs at a time.
    /// </summary>
    private int _cleanupInProgress;

    /// <summary>
    /// Run the retention sweep every N <see cref="SaveAsync"/> calls.
    /// </summary>
    private const int CleanupEveryNCalls = 50;

    /// <summary>
    /// How long a stored entry is kept before the sweep treats it as stale. Set well above any
    /// realistic transaction TTL so a genuine expiry event always finds its entry.
    /// </summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public Task SaveAsync(TransactionId transactionId, ChannelPromptMessageRef reference, CancellationToken cancellationToken = default)
    {
        // Honor cancellation up front: don't leave an entry living until the retention sweep.
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        // A resent prompt supersedes the previous message for the same transaction.
        _entries[transactionId.ToString()] = new StoredEntry(reference, _timeProvider.GetUtcNow());

        // Periodically reclaim entries of transactions that never expired (confirmed/declined).
        var count = Interlocked.Increment(ref _saveCallCount);
        if (count % CleanupEveryNCalls is 0)
        {
            TryRemoveStaleEntries();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ChannelPromptMessageRef?> TakeAsync(TransactionId transactionId, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ChannelPromptMessageRef?>(cancellationToken);
        }

        // Remove-on-read guarantees a single edit of the message; a stale (over-retention) entry is
        // treated as absent.
        if (_entries.TryRemove(transactionId.ToString(), out var entry) && !IsStale(entry))
        {
            return Task.FromResult<ChannelPromptMessageRef?>(entry.Reference);
        }

        return Task.FromResult<ChannelPromptMessageRef?>(null);
    }

    /// <summary>
    /// Returns true when the entry is older than <see cref="Retention"/>.
    /// </summary>
    private bool IsStale(StoredEntry entry) => _timeProvider.GetUtcNow() - entry.StoredAt > Retention;

    /// <summary>
    /// Runs the retention sweep, but no more than one at a time (concurrent calls are discarded).
    /// </summary>
    private void TryRemoveStaleEntries()
    {
        if (Interlocked.CompareExchange(ref _cleanupInProgress, 1, 0) is not 0)
        {
            return;
        }

        try
        {
            foreach (var (key, entry) in _entries)
            {
                if (IsStale(entry))
                {
                    _entries.TryRemove(key, out _);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _cleanupInProgress, 0);
        }
    }

    /// <summary>
    /// Stored prompt entry: coordinates plus the moment they were recorded (for retention).
    /// </summary>
    private readonly record struct StoredEntry(ChannelPromptMessageRef Reference, DateTimeOffset StoredAt);
}
