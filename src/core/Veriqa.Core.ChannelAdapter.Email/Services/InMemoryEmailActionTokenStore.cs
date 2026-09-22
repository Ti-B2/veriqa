// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

namespace Veriqa.Core.ChannelAdapter.Email.Services;

/// <summary>
/// In-memory implementation of the Email Pull-mode action token store.
/// Thread-safe implementation via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Suitable for single-instance and dev environments. For multi-instance — use Redis.
/// Tokens are removed immediately after use; expired ones are periodically cleaned up.
/// </summary>
internal sealed class InMemoryEmailActionTokenStore : IEmailActionTokenStore
{
    /// <summary>
    /// Dictionary: token → token data.
    /// </summary>
    private readonly ConcurrentDictionary<string, EmailActionToken> _tokens =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Counter of StoreTokenAsync operations for periodic cleanup of expired records.
    /// </summary>
    private int _storeCallCount;

    /// <summary>
    /// Run cleanup of expired tokens every N StoreTokenAsync calls.
    /// </summary>
    private const int CleanupEveryNCalls = 50;

    /// <summary>
    /// Clock the token expiration is judged against.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates an in-memory action token store.
    /// </summary>
    /// <param name="timeProvider">Time provider.</param>
    public InMemoryEmailActionTokenStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task StoreTokenAsync(string token, EmailActionToken data, CancellationToken cancellationToken = default)
    {
        // The method stores the token in the dictionary (a random 256-bit token — collisions are excluded)
        _tokens[token] = data;

        // Periodically clean up expired tokens to prevent unbounded memory growth
        var count = System.Threading.Interlocked.Increment(ref _storeCallCount);
        if (count % CleanupEveryNCalls is 0)
        {
            RemoveExpiredTokens();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<EmailActionToken?> GetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        // The method looks up the token in the dictionary and returns its data, including expired ones
        // (the IsExpired flag is handled on the caller's side)
        if (!_tokens.TryGetValue(token, out var data))
        {
            return Task.FromResult<EmailActionToken?>(null);
        }

        return Task.FromResult<EmailActionToken?>(data);
    }

    /// <inheritdoc />
    public Task<bool> TryMarkOpenedAsync(string token, CancellationToken cancellationToken = default)
    {
        // The method atomically records the first open of the confirm page via the shared CAS helper:
        // exactly one concurrent call gets true; repeat views, an expired
        // or used token — false (the "opened" status is not duplicated)
        return ConcurrentCasHelper.TryUpdateWithRetryAsync(
            _tokens,
            token,
            canUpdate: current => current.OpenedAt is null && !current.IsExpired(_timeProvider.GetUtcNow()) && !current.IsUsed,
            update: current => current with { OpenedAt = _timeProvider.GetUtcNow() },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> MarkUsedAsync(string token, CancellationToken cancellationToken = default)
    {
        // The method atomically consumes the token via a single TryRemove.
        // ConcurrentDictionary.TryRemove is thread-safe: exactly one concurrent call
        // gets true, all the rest — false. The previous two-step TryUpdate+TryRemove
        // introduced a race: thread B could read UsedAt≠null and return true before
        // thread A finished TryRemove — both treated the token as successfully consumed.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tokens.TryRemove(token, out _));
    }

    /// <summary>
    /// Removes all expired tokens from the dictionary.
    /// </summary>
    private void RemoveExpiredTokens()
    {
        // The method iterates over all records and removes the expired ones
        var now = _timeProvider.GetUtcNow();
        foreach (var (key, value) in _tokens)
        {
            if (value.IsExpired(now))
            {
                _tokens.TryRemove(key, out _);
            }
        }
    }
}
