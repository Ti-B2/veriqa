// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Threading.RateLimiting;

using Microsoft.Extensions.Options;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// In-memory implementation of a per-user authentication rate limiter.
/// Uses <see cref="PartitionedRateLimiter{T}"/> with the key {channel_type}:{channel_user_id}.
/// Limits the number of unique partitions via <see cref="RateLimitOptions.UserAuthMaxPartitions"/>:
/// new users beyond the limit are routed to a shared overflow partition.
/// Single-instance only: counters are in-process and are not shared across nodes.
/// </summary>
internal sealed class UserAuthRateLimiter : IUserAuthRateLimiter, IDisposable
{
    /// <summary>
    /// Overflow partition key used for users beyond the limit.
    /// The NUL character guarantees no collisions with real keys.
    /// </summary>
    private const string OverflowPartitionKey = "\0overflow";

    /// <summary>
    /// Partitioned fixed-window request limiter, built on the FIRST acquisition — that is, on the
    /// asynchronous path. Its permit limit and window are levelled settings, and resolving a levelled
    /// setting is asynchronous; a constructor a container calls synchronously is therefore the wrong
    /// place for it, and the class no longer resolves anything there.
    /// </summary>
    private PartitionedRateLimiter<string>? _limiter;

    /// <summary>
    /// Guard of the one-time construction of the limiter: several requests may reach the first
    /// acquisition at once, and exactly one of them must build it.
    /// </summary>
    private readonly SemaphoreSlim _limiterGate = new(1, 1);

    /// <summary>
    /// Builds the limiter from the effective permit limit and window.
    /// </summary>
    private readonly Func<CancellationToken, Task<PartitionedRateLimiter<string>>> _buildLimiter;

    /// <summary>
    /// Registry of registered partitions.
    /// Used to control the number of unique keys and prevent unbounded memory growth.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _partitionKeys =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Atomic counter of registered partitions.
    /// Duplicates _partitionKeys.Count, but Count on a ConcurrentDictionary is O(n).
    /// </summary>
    private int _partitionCount;

    /// <summary>
    /// Upper bound on the number of unique partitions.
    /// </summary>
    private readonly int _maxPartitions;

    /// <summary>
    /// Creates an instance of <see cref="UserAuthRateLimiter"/> with parameters from configuration.
    /// </summary>
    /// <param name="options">Rate limiting settings.</param>
    /// <param name="resolver">Canonical multi-level configuration resolver.</param>
    public UserAuthRateLimiter(IOptions<RateLimitOptions> options, IConfigurationResolver resolver)
    {
        // The machinery (partitions/queue) stays in the core and is NOT split across levels (CFG-230):
        // QueueLimit/MaxPartitions are read directly from config.
        var config = options.Value;
        var queueLimit = config.UserAuthQueueLimit;
        _maxPartitions = config.UserAuthMaxPartitions;

        // Re-leveling of the limit VALUE and WINDOW through the single asynchronous resolver (CFG-223,
        // CFG-235). The limiter is built once, on the first acquisition: that is an asynchronous path,
        // so the resolution has a seam to happen on and needs neither a blocking wait nor a second
        // entry of resolution. Runtime configuration changes are not applied afterwards (acceptable:
        // a rate limit is not a hot-path configuration).
        _buildLimiter = async cancellationToken =>
        {
            var permitLimit = (await resolver.ResolveAsync(
                RateLimitConfigKeys.UserAuthPermitLimit,
                ResolutionContext.Core,
                ConfigDimensionValues.None,
                cancellationToken)).Value;

            var windowSeconds = (await resolver.ResolveAsync(
                RateLimitConfigKeys.UserAuthWindowSeconds,
                ResolutionContext.Core,
                ConfigDimensionValues.None,
                cancellationToken)).Value;

            return PartitionedRateLimiter.Create<string, string>(
                resourceId => RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(resourceId),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueLimit = queueLimit
                    }));
        };
    }

    /// <summary>
    /// Returns the limiter, building it on the first acquisition. This is the asynchronous startup
    /// platform of this class: the permit limit and the window are levelled settings, and resolving
    /// them belongs on an awaitable path rather than in a constructor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The partitioned limiter.</returns>
    private async ValueTask<PartitionedRateLimiter<string>> GetLimiterAsync(CancellationToken cancellationToken)
    {
        var built = Volatile.Read(ref _limiter);
        if (built is not null)
        {
            return built;
        }

        await _limiterGate.WaitAsync(cancellationToken);
        try
        {
            built = Volatile.Read(ref _limiter);
            if (built is null)
            {
                built = await _buildLimiter(cancellationToken);
                Volatile.Write(ref _limiter, built);
            }

            return built;
        }
        finally
        {
            _limiterGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<UserAuthRateLimitResult> TryAcquireAsync(
        string userKey,
        CancellationToken cancellationToken = default)
    {
        // Try to acquire 1 permit from the user's partition.
        // The lease is released immediately (allow/deny); window state is managed by the limiter.
        var limiter = await GetLimiterAsync(cancellationToken);
        using var lease = await limiter.AcquireAsync(userKey, 1, cancellationToken);

        if (lease.IsAcquired)
        {
            return UserAuthRateLimitResult.Allowed;
        }

        // Get the actual wait time from the limiter metadata
        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter);

        return UserAuthRateLimitResult.Rejected(retryAfter);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to dispose until the limiter has actually been built: a host that never took a
        // permit never created one.
        Volatile.Read(ref _limiter)?.Dispose();
        _limiterGate.Dispose();
    }

    /// <summary>
    /// Returns the partition key for the given userKey.
    /// If the number of registered partitions has reached <see cref="_maxPartitions"/>,
    /// returns <see cref="OverflowPartitionKey"/> — all "over-limit" users
    /// fall into a single shared partition, limiting memory growth.
    /// </summary>
    /// <param name="userKey">Original user key.</param>
    /// <returns>Partition key for the limiter.</returns>
    private string ResolvePartitionKey(string userKey)
    {
        // If the key is already known — return it directly
        if (_partitionKeys.ContainsKey(userKey))
        {
            return userKey;
        }

        // Partition limit reached: route the new user to overflow
        if (_partitionCount >= _maxPartitions)
        {
            return OverflowPartitionKey;
        }

        // Atomically add the new key and increment the counter.
        // A small overshoot of the limit during concurrent addition is acceptable.
        if (_partitionKeys.TryAdd(userKey, 0))
        {
            Interlocked.Increment(ref _partitionCount);
        }

        return _partitionKeys.ContainsKey(userKey) ? userKey : OverflowPartitionKey;
    }
}
