// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Channel client factory with a per-tenant cache (SPEC-003 §17.4, CA-166).
/// Takes the builder of the requested client type from <see cref="ChannelClientRegistry{TClient}"/>,
/// lets it build the client from the tenant's credentials and caches the client by the
/// <c>(tenant, ChannelType, client type)</c> key. The cache is thread-safe: concurrent requests for the
/// same key get a single instance without a race. For the default tenant (null) the client is built once
/// and lives for the process lifetime (N=1, behavior 1:1).
/// TASK-050 #9: a negative resolution outcome (no credentials / channel unavailable) is cached in a
/// short-TTL negative cache (<see cref="IMemoryCache"/>) — only the error code, not the message/payload
/// (core-rules §10) — so a known-to-fail resolve does not hit the DB layer on every call within the TTL.
/// </summary>
internal sealed class ChannelClientFactory : IChannelClientFactory
{
    /// <summary>
    /// Container the per-client-type registry is taken from. A non-generic factory cannot receive the
    /// builders of an arbitrary client type through its constructor, and the registry is a singleton —
    /// so this resolution is a dictionary lookup in the container, while the builder index and the
    /// client cache behind it are built once per client type.
    /// </summary>
    private readonly IServiceProvider _services;

    /// <summary>
    /// Negative cache of failed resolution outcomes (TASK-050 #9). The value is only the error code
    /// (<see cref="string"/>), not <c>Error.Message</c> and not credential data (core-rules §10).
    /// One instance shared by all client types — the key carries the client type as its type parameter
    /// (<see cref="ChannelClientCacheKey{TClient}"/>), so the entries stay partitioned.
    /// </summary>
    private readonly IMemoryCache _negativeCache;

    /// <summary>
    /// Creates the channel client factory.
    /// </summary>
    /// <param name="services">Container the per-client-type registry is taken from.</param>
    /// <param name="negativeCache">Cache of failed resolution outcomes (TASK-050 #9).</param>
    public ChannelClientFactory(
        IServiceProvider services,
        IMemoryCache negativeCache)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _negativeCache = negativeCache ?? throw new ArgumentNullException(nameof(negativeCache));
    }

    /// <inheritdoc />
    public async ValueTask<Result<TClient>> GetOrCreateClientAsync<TClient>(
        ChannelCredentialContext context,
        CancellationToken cancellationToken = default)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(context);

        var registry = _services.GetRequiredService<ChannelClientRegistry<TClient>>();

        // No builder for (channel type, client type) — the channel does not resolve as a client channel.
        var builder = registry.FindBuilder(context.ChannelType);
        if (builder is null)
        {
            return Result<TClient>.Failure(
                ChannelCredentialErrorCodes.ChannelNotAvailable,
                $"No client factory {registry.ClientTypeName} for channel '{context.ChannelType}'.");
        }

        var cacheKey = new ChannelClientCacheKey<TClient>(context.TenantId, context.ChannelType);

        // Fast path: the client is already cached.
        if (registry.Clients.TryGetValue(cacheKey, out var cached))
        {
            return Result<TClient>.Success(cached);
        }

        // Negative cache (TASK-050 #9): a known-to-fail resolve does not hit the DB layer within the TTL.
        // Only the error code is stored — Result.Failure is reconstructed from the code (no credential data).
        if (_negativeCache.TryGetValue(cacheKey, out var cachedErrorCode) && cachedErrorCode is string errorCode)
        {
            return Result<TClient>.Failure(errorCode, DescribeNegativeResult(errorCode, context.ChannelType));
        }

        // Build the client from the tenant's credentials; if credentials are missing — an error, no exception.
        var buildResult = await builder.BuildAsync(context, cancellationToken);
        if (buildResult.IsFailure)
        {
            // Cache ONLY the error code (not Message/payload, core-rules §10) with a short TTL.
            // Known limitation: ANY failure code is cached without filtering
            // by "expected" ones. If the lower layer degraded a transient DB failure to ChannelCredentialsMissing,
            // "no credentials" settles in the cache for the whole TTL, including after the DB recovers. This is
            // a property of the existing degradation design (an infra failure is indistinguishable from
            // genuine-missing at this layer), and TTL-based invalidation is a deliberate simplification
            // (TASK-050 §2 item 4): the negative cache is not event-invalidated, so transient codes are not
            // filtered before being written.
            _negativeCache.Set(
                cacheKey,
                buildResult.Error.Code,
                TimeSpan.FromSeconds(ChannelClientFactoryCacheConstants.NegativeCacheTtlSeconds));

            return Result<TClient>.Failure(buildResult.Error);
        }

        // A success carrying no client violates the builder contract. Treat it as a missing-credentials
        // failure instead of caching null and handing it to the caller. It is NOT written to the negative
        // cache: this is a defect of the builder, not a resolution outcome, and hiding it for the whole TTL
        // would only delay its diagnosis.
        var built = buildResult.Value;
        if (built is null)
        {
            return Result<TClient>.Failure(
                ChannelCredentialErrorCodes.ChannelCredentialsMissing,
                $"Builder of channel '{context.ChannelType}' returned no client.");
        }

        // Success takes priority over the negative: remove the negative entry of the same key (success/failure race).
        _negativeCache.Remove(cacheKey);

        // GetOrAdd guarantees a single instance per key under a race. If a concurrent thread already
        // put a client, the one we just built is redundant (orphan); dispose it if it is
        // disposable (current bot clients hold no resources, but this is a safeguard for future clients).
        var client = registry.Clients.GetOrAdd(cacheKey, built);
        if (!ReferenceEquals(client, built) && built is IDisposable disposableOrphan)
        {
            disposableOrphan.Dispose();
        }

        return Result<TClient>.Success(client);
    }

    /// <summary>
    /// Reconstructs the description of a negative outcome from the cached error code (TASK-050 #9).
    /// The negative cache stores only the code — the message is constructed here from the code + channel
    /// type, so as not to cache sensitive data of the original error (core-rules §10).
    /// </summary>
    /// <param name="errorCode">Cached error code.</param>
    /// <param name="channelType">Channel type (for a readable message).</param>
    /// <returns>Failure description without sensitive data.</returns>
    // Known limitation: on a cache hit the original error text from the builder
    // is lost — the message is reconstructed from the code only (there are exactly two codes in
    // ChannelCredentialErrorCodes, the mapping is correct). Functionally safe: downstream branches on
    // Error.Code (preserved exactly), while Message is diagnostics only; the original text is deliberately
    // not cached (core-rules §10). A discrepancy is only possible between the cached/uncached text of the
    // same code.
    private static string DescribeNegativeResult(string errorCode, string channelType)
        => errorCode == ChannelCredentialErrorCodes.ChannelNotAvailable
            ? $"Channel '{channelType}' is unavailable."
            : $"Credentials of channel '{channelType}' not found.";
}
