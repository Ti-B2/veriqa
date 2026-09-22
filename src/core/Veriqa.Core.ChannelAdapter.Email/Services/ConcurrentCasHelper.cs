// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

namespace Veriqa.Core.ChannelAdapter.Email.Services;

/// <summary>
/// Shared CAS helper (compare-and-swap with a bounded number of attempts) for atomic
/// updates of records in the Email channel's in-memory stores. A single point for the
/// contract "CAS attempts exhausted = the operation is treated as already performed (false)" —
/// previously the loop was copied across three methods and could diverge when edited (review feedback).
/// </summary>
internal static class ConcurrentCasHelper
{
    /// <summary>
    /// Maximum number of CAS attempts. Guards against an infinite loop under high contention.
    /// </summary>
    private const int MaxRetryAttempts = 10;

    /// <summary>
    /// Atomically updates a dictionary record via the retry loop TryGetValue → guard → TryUpdate.
    /// Exactly one concurrent caller gets true; retries/race losers get false.
    /// </summary>
    /// <typeparam name="TValue">The dictionary record type (immutable record).</typeparam>
    /// <param name="dictionary">The thread-safe dictionary.</param>
    /// <param name="key">The record key.</param>
    /// <param name="canUpdate">Guard: true — the record is in a state that allows updating.</param>
    /// <param name="update">Function building the updated record (with-copy).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// true — the update was performed by the current call;
    /// false — the record was not found, the guard rejected the state, or CAS attempts were exhausted
    /// (the operation is treated as already performed by another thread).
    /// </returns>
    public static async Task<bool> TryUpdateWithRetryAsync<TValue>(
        ConcurrentDictionary<string, TValue> dictionary,
        string key,
        Func<TValue, bool> canUpdate,
        Func<TValue, TValue> update,
        CancellationToken cancellationToken)
        where TValue : class
    {
        // The method performs a lock-free CAS: TryUpdate replaces the value only if it
        // was not changed by another thread; on a race loss — yield the CPU and retry
        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!dictionary.TryGetValue(key, out var current))
            {
                return false;
            }

            if (!canUpdate(current))
            {
                return false;
            }

            var updated = update(current);

            if (dictionary.TryUpdate(key, updated, current))
            {
                return true;
            }

            await Task.Yield();
        }

        // CAS attempts exhausted — the operation is treated as already performed by another thread
        return false;
    }
}
