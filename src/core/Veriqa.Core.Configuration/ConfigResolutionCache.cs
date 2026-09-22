// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veriqa.Core.Configuration;

/// <summary>
/// Caching and degradation layer owned by <see cref="ConfigurationResolver"/> (SPEC-012 §10.6,
/// CFG-233). One component with two non-overlapping triggers, not two stores with policies of their
/// own: the last-good store below is used ONLY when a fresh read of a level failed, while freshness in
/// every other case is a matter of the source (a live source is not cached at all, an external one is
/// cached by TTL).
/// <para>
/// The last-good store is a plain dictionary rather than a cache primitive on purpose: it must NOT be
/// evictable under memory pressure, because that removes the record exactly when a source has broken
/// and the record is needed. Its growth is bounded explicitly instead
/// (<see cref="ConfigResolutionOptions.LastGoodCapacity"/>), and the entry key includes the tenant, so
/// the radius of a broken tenant stays inside that tenant.
/// </para>
/// </summary>
internal sealed class ConfigResolutionCache : IConfigResolutionCache
{
    /// <summary>
    /// A value read successfully at some level, kept for the case where a later read fails.
    /// The value is stored untyped — the store spans keys of every value type — and is cast back
    /// against the key's type parameter on retrieval; the store is internal to the mechanism, so no
    /// untyped member reaches a public contract.
    /// </summary>
    private sealed record LastGoodEntry(object? Value, bool DeclaresGate, long TimestampTicks, long Sequence);

    /// <summary>Size of one fresh entry: a single value, counted one apiece rather than measured.</summary>
    private const long FreshEntrySize = 1;

    /// <summary>
    /// Last successfully read values by composite entry key.
    /// </summary>
    private readonly ConcurrentDictionary<string, LastGoodEntry> _lastGood = new(StringComparer.Ordinal);

    /// <summary>
    /// Cache of FRESH values of cacheable levels. Unlike the last-good store below it is an ordinary
    /// cache primitive — it holds what a source would answer anyway, so losing an entry under memory
    /// pressure costs one extra read and nothing else. The primitive is <see cref="IMemoryCache"/> by
    /// the standing decision on the synchronous resolver paths; it is reached only through this class,
    /// which is what makes swapping it a matter of registration.
    /// </summary>
    private readonly IMemoryCache _fresh;

    /// <summary>
    /// Settings of the degradation layer (growth bound, maximum age of a secret's last-good value).
    /// </summary>
    private readonly ConfigResolutionOptions _options;

    /// <summary>
    /// Time source for the age of a last-good entry.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Logger of degradation facts.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Monotonic write counter: identifies the oldest entry when the growth bound is reached without
    /// relying on the clock, which may not move between two writes.
    /// </summary>
    private long _sequence;

    /// <summary>
    /// Approximate number of entries in <see cref="_lastGood"/>, maintained when a NEW entry key is
    /// inserted. It exists so that the growth bound can be tested without
    /// <see cref="ConcurrentDictionary{TKey,TValue}.Count"/>, which acquires every lock of the
    /// dictionary: the bound would otherwise be tested on each successful level read — up to six per
    /// Resolve, and Resolve runs per transaction and per request, which makes that a global
    /// serialization point on the hot path. Approximate is enough because it only decides WHEN
    /// eviction runs: the eviction loop itself reads the exact count and republishes it here, so a
    /// counter that drifted under a race is corrected at the next insertion of a NEW entry key that
    /// trips the bound. Refreshing an already remembered key never runs that check, so the bound is
    /// near-exact rather than exact: in a steady state of updates alone the store may stay above the
    /// bound by the few entries a race lost, until the next new key arrives.
    /// </summary>
    private int _approximateCount;

    /// <summary>
    /// Creates the caching and degradation layer.
    /// </summary>
    /// <param name="options">Settings of the layer.</param>
    /// <param name="fresh">Cache primitive of fresh values.</param>
    /// <param name="timeProvider">Time source.</param>
    /// <param name="logger">Logger of degradation facts.</param>
    public ConfigResolutionCache(
        IOptions<ConfigResolutionOptions> options,
        IMemoryCache fresh,
        TimeProvider timeProvider,
        ILogger<ConfigResolutionCache> logger)
    {
        _options = options.Value;
        _fresh = fresh;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<LayerValue<T>> GetOrCreateAsync<T>(
        ConfigCacheKey key,
        ConfigCachePolicy policy,
        Func<CancellationToken, ValueTask<LayerValue<T>>> read,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(read);

        if (policy.Ttl is not { } ttl)
        {
            // The key declared no lifetime — nothing to cache; the level is read every time.
            return await read(cancellationToken);
        }

        var entryKey = key.ToString();

        if (_fresh.TryGetValue(entryKey, out var cached) && cached is LayerValue<T> hit)
        {
            return hit;
        }

        var layer = await read(cancellationToken);

        // A level that declares nothing is not remembered: an absent value is cheap to re-read and
        // caching it would make a newly declared value wait out the lifetime for no reason.
        if (!layer.HasValue)
        {
            return layer;
        }

        // The size is set unconditionally: a host that configured a SizeLimit rejects a sizeless entry on
        // commit (the Dispose below), and here that would read as a failed level read and silently take
        // the cached level out of service. A host without a SizeLimit ignores the value.
        using var entry = _fresh.CreateEntry(entryKey);
        entry.AbsoluteExpirationRelativeToNow = ttl;
        entry.Size = FreshEntrySize;
        entry.Value = layer;

        return layer;
    }

    /// <inheritdoc />
    public ValueTask<bool> ForgetAsync(ConfigCacheKey key, CancellationToken cancellationToken = default)
    {
        var entryKey = key.ToString();

        if (!_fresh.TryGetValue(entryKey, out _))
        {
            return ValueTask.FromResult(false);
        }

        _fresh.Remove(entryKey);

        return ValueTask.FromResult(true);
    }

    /// <summary>
    /// Remembers a value successfully read at a level, so a later failed read of the same level can
    /// still answer.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Composite key of the entry.</param>
    /// <param name="isSecret">The value of the setting is a secret.</param>
    /// <param name="layer">Value read at the level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value that was remembered.</returns>
    public ValueTask<LayerValue<T>> StoreLastGoodAsync<T>(
        ConfigCacheKey key,
        bool isSecret,
        LayerValue<T> layer,
        CancellationToken cancellationToken = default)
    {
        var entryKey = key.ToString();
        var entry = new LastGoodEntry(
            layer.Value,
            layer.DeclaresGate,
            _timeProvider.GetUtcNow().UtcTicks,
            Interlocked.Increment(ref _sequence));

        if (!_lastGood.TryAdd(entryKey, entry))
        {
            // Refreshing a level that is already remembered: the store cannot grow, so the bound
            // cannot be crossed here and there is nothing to test. This is the steady state — the set
            // of entry keys is bounded by (setting × level × tenant × scope) and stops growing once
            // the deployment has been exercised, so it is also the case that carries the hot path.
            _lastGood[entryKey] = entry;
            return ValueTask.FromResult(layer);
        }

        var capacity = _options.LastGoodCapacity;
        if (capacity > 0 && Interlocked.Increment(ref _approximateCount) > capacity)
        {
            EnforceCapacity(capacity);
        }

        return ValueTask.FromResult(layer);
    }

    /// <summary>
    /// Returns the last value successfully read at the level, or <see cref="LayerValue{T}.None"/> when
    /// there is none (or when a secret's last-good value has outlived its maximum age).
    /// <para>
    /// A degraded level never reports an OPEN gate: when the failing level used to state a gate of its
    /// own, the gate comes back CLOSED (CFG-212 — degradation must not weaken protection); when it
    /// stated none, it keeps having no opinion, so a gate opened by a level below it is not revoked.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Composite key of the entry.</param>
    /// <param name="isSecret">The value of the setting is a secret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Degraded level value, or None.</returns>
    public ValueTask<LayerValue<T>> GetLastGoodOrNoneAsync<T>(
        ConfigCacheKey key,
        bool isSecret,
        CancellationToken cancellationToken = default)
    {
        var entryKey = key.ToString();

        if (!_lastGood.TryGetValue(entryKey, out var entry))
        {
            return ValueTask.FromResult(LayerValue<T>.None);
        }

        if (isSecret && IsOlderThanSecretMaxAge(entry))
        {
            // A secret may have been revoked while the source was unreachable: refusing is safer than
            // working on it indefinitely. The consumer observes the level as unset, which is its
            // existing "source unavailable" path.
            _logger.LogWarning(
                "Configuration entry {ConfigEntry}: the last valid value is older than the maximum age of a secret and is not served.",
                entryKey);

            return ValueTask.FromResult(LayerValue<T>.None);
        }

        if (entry.Value is not T typed)
        {
            // A null value is legitimate for reference and nullable types; anything else means the
            // entry does not belong to this key's type and is not usable.
            return ValueTask.FromResult(entry.Value is null
                ? Project<T>(default!, entry.DeclaresGate)
                : LayerValue<T>.None);
        }

        return ValueTask.FromResult(Project(typed, entry.DeclaresGate));
    }

    /// <summary>
    /// Projects a stored value back into a level value with a gate that is never open.
    /// </summary>
    private static LayerValue<T> Project<T>(T value, bool declaresGate) =>
        declaresGate ? LayerValue<T>.Set(value, allowLowerOverride: false) : LayerValue<T>.Set(value);

    /// <summary>
    /// Checks whether a last-good entry has outlived the maximum age allowed for a secret.
    /// </summary>
    private bool IsOlderThanSecretMaxAge(LastGoodEntry entry) =>
        _timeProvider.GetUtcNow().UtcTicks - entry.TimestampTicks > _options.SecretLastGoodMaxAge.Ticks;

    /// <summary>
    /// Keeps the store within its declared growth bound by evicting the oldest entries. Eviction here
    /// is a bounded-growth measure, not a freshness one, so every removal is reported.
    /// <para>
    /// Called only from the insertion of a NEW entry key, and only when the approximate counter says
    /// the bound may have been crossed, which is why it may read the exact
    /// <see cref="ConcurrentDictionary{TKey,TValue}.Count"/>: the cost is paid on the rare eviction
    /// path instead of on every level read. Before returning it republishes the exact size, so a
    /// counter that drifted (an update racing with an eviction of the same entry) is brought back to
    /// reality — on this path only, which is what makes the bound near-exact rather than exact.
    /// </para>
    /// </summary>
    /// <param name="capacity">Declared growth bound; always greater than zero at this point.</param>
    private void EnforceCapacity(int capacity)
    {
        while (_lastGood.Count > capacity)
        {
            var oldest = default(KeyValuePair<string, LastGoodEntry>);
            var found = false;

            foreach (var candidate in _lastGood)
            {
                if (!found || candidate.Value.Sequence < oldest.Value.Sequence)
                {
                    oldest = candidate;
                    found = true;
                }
            }

            if (!found || !_lastGood.TryRemove(oldest.Key, out _))
            {
                // Another thread removed the same entry — the bound is being enforced anyway.
                break;
            }

            _logger.LogWarning(
                "Last valid configuration values reached the declared bound of {Capacity} entries; the oldest entry {EntryKey} was evicted.",
                capacity,
                oldest.Key);
        }

        Volatile.Write(ref _approximateCount, _lastGood.Count);
    }
}
