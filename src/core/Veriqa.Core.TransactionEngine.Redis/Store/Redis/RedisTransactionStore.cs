// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Veriqa.Core.TransactionEngine.Configuration;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.TransactionEngine.Store.Redis;

/// <summary>
/// Redis implementation of the transaction store.
/// Key removal is native (Redis TTL), so TransactionCleanupService skips its own deletion pass —
/// that is what ISupportsNativeExpiry signals. The expiry of a transaction is not native: the key of
/// a non-terminal transaction outlives <c>ExpiresAt</c> by <c>CompletedRetentionSeconds</c>, which is the
/// window the cleanup pass has to read it back, move it to Expired and publish TransactionExpiredEvent.
/// A key deleted exactly at <c>ExpiresAt</c> would make that expiry pass silently.
/// </summary>
internal sealed class RedisTransactionStore : ITransactionStore, ISupportsNativeExpiry
{
    /// <summary>
    /// Key segment for the idempotency index.
    /// </summary>
    private const string IdempotencyIndexSegment = "idx:idmp:";

    /// <summary>
    /// Key segment for the expiry sorted set index (to support GetExpiredAsync).
    /// </summary>
    private const string ExpiryIndexSegment = "expiry:index";

    /// <summary>
    /// Segment separator in the idempotency key.
    /// </summary>
    private const string KeySegmentSeparator = ":";

    /// <summary>
    /// Lua script for atomically saving a transaction along with the idempotency index.
    /// Uses SET NX for both keys, rolls back the main key on an idmpKey conflict.
    /// The two keys get their own TTLs: the main key outlives <c>ExpiresAt</c> by the retention window
    /// (see <see cref="ComputeEffectiveExpiryTimestamp"/>), while the idempotency key ends exactly at
    /// <c>ExpiresAt</c> — a repeat of the same idempotency key after the transaction expired must start a
    /// new transaction, not hit a conflict on a dead one.
    /// KEYS[1] — main transaction key.
    /// KEYS[2] — idempotency index key (passed only when an idempotency scope/key is present).
    /// ARGV[1] — transaction JSON.
    /// ARGV[2] — TTL of the main key in seconds (always >0, validated in C# before the call).
    /// ARGV[3] — TTL of the idempotency key in seconds (always >0, validated in C# before the call).
    /// ARGV[4] — string transaction ID (value for the idempotency key).
    /// Returns: 1 = success, 0 = duplicate ID, -1 = idempotency key conflict.
    /// </summary>
    private const string AddTransactionLuaScript = @"
local mainExpiry = tonumber(ARGV[2])
local ok = redis.call('SET', KEYS[1], ARGV[1], 'NX', 'EX', mainExpiry)
if not ok then
    return 0
end
if #KEYS >= 2 then
    local idmpExpiry = tonumber(ARGV[3])
    local idmpOk = redis.call('SET', KEYS[2], ARGV[4], 'NX', 'EX', idmpExpiry)
    if not idmpOk then
        redis.call('DEL', KEYS[1])
        return -1
    end
end
return 1";

    /// <summary>
    /// Lua script for an atomic compare-and-swap on ConcurrencyToken.
    /// KEYS[1] — transaction key.
    /// ARGV[1] — expected ConcurrencyToken.
    /// ARGV[2] — new transaction JSON.
    /// ARGV[3] — expiry Unix timestamp (seconds, >0). Must always be >0 for live transactions.
    /// Returns: 1 — success, 0 — conflict or not found.
    /// </summary>
    private const string UpdateTransactionLuaScript = @"
local data = redis.call('GET', KEYS[1])
if data == false then
    return 0
end
local ok, obj = pcall(cjson.decode, data)
if not ok then
    return 0
end
if obj['ConcurrencyToken'] ~= ARGV[1] then
    return 0
end
redis.call('SET', KEYS[1], ARGV[2])
redis.call('EXPIREAT', KEYS[1], tonumber(ARGV[3]))
return 1";

    /// <summary>
    /// Store options.
    /// </summary>
    private readonly RedisTransactionStoreOptions _options;

    /// <summary>
    /// Transaction Engine configuration (for CompletedRetentionSeconds — the retention window of a
    /// dead transaction, expired or terminal).
    /// </summary>
    private readonly IOptionsMonitor<TransactionEngineOptions> _engineOptions;

    /// <summary>
    /// Logical Redis database handle.
    /// </summary>
    private readonly IDatabase _db;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<RedisTransactionStore> _logger;

    /// <summary>
    /// Clock the relative Redis TTLs are computed from.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// One-time warning flag for the GetStalledConfirmedAsync limitation.
    /// 0 = not logged yet, 1 = already logged.
    /// </summary>
    private int _stalledConfirmedWarnedOnce;

    /// <summary>
    /// Creates an instance of the Redis transaction store.
    /// </summary>
    /// <param name="connectionMultiplexer">Redis connection multiplexer.</param>
    /// <param name="options">Store options.</param>
    /// <param name="engineOptions">Transaction Engine configuration.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="timeProvider">Time provider.</param>
    public RedisTransactionStore(
        IConnectionMultiplexer connectionMultiplexer,
        IOptions<RedisTransactionStoreOptions> options,
        IOptionsMonitor<TransactionEngineOptions> engineOptions,
        ILogger<RedisTransactionStore> logger,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _engineOptions = engineOptions;
        _db = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken = default)
    {
        // Atomically save the transaction and the idempotency index via a single Lua script.
        // Guarantees that under a data race either both keys are written or neither is.
        ArgumentNullException.ThrowIfNull(transaction);

        var entity = TransactionEntityMapper.ToEntity(transaction);
        var json = SerializeEntity(entity);
        var mainKey = GetTransactionKey(transaction.Id.ToString());
        var expirySecs = GetExpirySecs(entity.ExpiresAt);

        // A transaction whose TTL has already expired is not written to the store.
        // The guard is on ExpiresAt itself, not on the retained key: the retention below keeps a dead
        // transaction readable, it does not make an already-dead one worth creating.
        if (expirySecs <= 0)
        {
            throw new InvalidOperationException(
                $"Transaction with ID '{transaction.Id}' has already expired (ExpiresAt={entity.ExpiresAt:O}) and cannot be saved");
        }

        // The main key outlives ExpiresAt by the retention window (same rule as ComputeEffectiveExpiryTimestamp),
        // the idempotency key does not — see the script's summary for why they differ.
        var mainKeyTtlSecs = expirySecs + GetRetentionSeconds();

        var hasIdmp = entity.IdempotencyScope is not null && entity.IdempotencyKey is not null;

        // Pass only the keys in use: without idempotency — only mainKey.
        // This prevents CROSSSLOT errors in Redis Cluster (all KEYS must be in the same slot).
        var keys = hasIdmp
            ? new RedisKey[] { mainKey, GetIdempotencyKey(entity.IdempotencyScope!, entity.IdempotencyKey!) }
            : new RedisKey[] { mainKey };

        var result = (long)await _db.ScriptEvaluateAsync(
            AddTransactionLuaScript,
            keys,
            new RedisValue[] { json, mainKeyTtlSecs, expirySecs, transaction.Id.ToString() });

        switch (result)
        {
            case 0:
                _logger.LogWarning(
                    "Duplicate TransactionId in the Redis Lua script (ADD). TransactionId: {TransactionId}, Scope: {Scope}, KeyFingerprint: {KeyFingerprint}",
                    transaction.Id,
                    entity.IdempotencyScope,
                    IdempotencyKeyFingerprint.Compute(entity.IdempotencyScope, entity.IdempotencyKey));
                throw new InvalidOperationException(
                    $"Transaction with ID '{transaction.Id}' already exists in the store");
            case -1:
                _logger.LogWarning(
                    "Idempotency key conflict in the Redis Lua script. TransactionId: {TransactionId}, Scope: {Scope}, KeyFingerprint: {KeyFingerprint}",
                    transaction.Id,
                    entity.IdempotencyScope,
                    IdempotencyKeyFingerprint.Compute(entity.IdempotencyScope, entity.IdempotencyKey));
                throw new DuplicateIdempotencyKeyException(
                    entity.IdempotencyScope!,
                    entity.IdempotencyKey!);
        }

        _logger.LogDebug(
            "Transaction saved in Redis (Lua ADD). TransactionId: {TransactionId}, TTL: {TtlSeconds}s, key TTL: {KeyTtlSeconds}s",
            transaction.Id, expirySecs, mainKeyTtlSecs);

        // Register the transaction in the expiry sorted set index.
        // Used by GetExpiredAsync to publish TransactionExpiredEvent.
        // Doing this outside the Lua script is acceptable: on a partial failure Redis TTL will still delete the key.
        await _db.SortedSetAddAsync(
            GetExpiryIndexKey(),
            transaction.Id.ToString(),
            entity.ExpiresAt.ToUnixTimeSeconds());
    }

    /// <inheritdoc />
    public async Task<Transaction?> GetByIdAsync(TransactionId id, CancellationToken cancellationToken = default)
    {
        // Get the transaction JSON by key and deserialize it
        var mainKey = GetTransactionKey(id.ToString());
        var entity = await GetEntityAsync(mainKey);

        return entity is not null
            ? TransactionEntityMapper.ToDomain(entity)
            : null;
    }

    /// <inheritdoc />
    public async Task<Transaction?> GetByIdempotencyKeyAsync(
        string scope,
        string key,
        CancellationToken cancellationToken = default)
    {
        // Look up the idempotency index key, then the main transaction key
        var idmpKey = GetIdempotencyKey(scope, key);
        var txIdValue = await _db.StringGetAsync(idmpKey);

        if (txIdValue.IsNullOrEmpty)
        {
            return null;
        }

        var txId = txIdValue.ToString();
        var mainKey = GetTransactionKey(txId);
        var entity = await GetEntityAsync(mainKey);

        if (entity is null)
        {
            // The index is stale (the transaction's TTL expired) — clean it up
            await _db.KeyDeleteAsync(idmpKey);
            return null;
        }

        return TransactionEntityMapper.ToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(
        Transaction transaction,
        string expectedConcurrencyToken,
        CancellationToken cancellationToken = default)
    {
        // Atomic CAS via Lua: check the token and update the JSON, re-stating the key deadline.
        // ExpiresAt is immutable after creation; the deadline of the key is derived from it and from
        // the state being written (ComputeEffectiveExpiryTimestamp), so every write passes it as EXPIREAT.
        var entity = TransactionEntityMapper.ToEntity(transaction);
        var newJson = SerializeEntity(entity);
        var mainKey = GetTransactionKey(transaction.Id.ToString());

        var expiryTimestamp = ComputeEffectiveExpiryTimestamp(entity);

        var result = await _db.ScriptEvaluateAsync(
            UpdateTransactionLuaScript,
            new RedisKey[] { mainKey },
            new RedisValue[] { expectedConcurrencyToken, newJson, expiryTimestamp });

        var updated = (long)result == 1L;

        // On a successful transition to a terminal state, remove from the expiry index:
        // TransactionExpiredEvent is no longer needed, and TTL will delete the key by the new deadline.
        if (updated && IsTerminalState(entity.State))
        {
            await _db.SortedSetRemoveAsync(GetExpiryIndexKey(), transaction.Id.ToString());
        }

        return updated;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(TransactionId id, CancellationToken cancellationToken = default)
    {
        // Delete the main key and the associated idempotency index
        var mainKey = GetTransactionKey(id.ToString());
        var entity = await GetEntityAsync(mainKey);

        var deleted = await _db.KeyDeleteAsync(mainKey);

        // Delete the idempotency index key (if known)
        if (entity is not null
            && entity.IdempotencyScope is not null
            && entity.IdempotencyKey is not null)
        {
            var idmpKey = GetIdempotencyKey(entity.IdempotencyScope, entity.IdempotencyKey);
            await _db.KeyDeleteAsync(idmpKey);
        }

        // Remove from the expiry sorted set index
        await _db.SortedSetRemoveAsync(GetExpiryIndexKey(), id.ToString());

        _logger.LogDebug(
            "Transaction deleted from Redis. TransactionId: {TransactionId}, KeyExisted: {KeyExisted}",
            id, deleted);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses the expiry sorted set index to efficiently find expired transactions.
    /// Loads entities via MGET (batch), then decides on every record on its own — the three outcomes
    /// are <see cref="ExpirySweepOutcome"/>. Removing an id from the index is the right of exactly two
    /// of them: the record is gone, or the record is understood and the pass can do nothing with it.
    /// "This build could not read the record" is not one of them: that is a property of the build, not
    /// of the record, and the removal is irreversible (only AddAsync ever puts an id back), so the id
    /// stays in the shared index for a build that reads it. The ids to remove leave in a single ZREM.
    /// </remarks>
    public async Task<IReadOnlyList<Transaction>> GetExpiredAsync(
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // ZRANGEBYSCORE: all elements with score (ExpiresAt) <= now
        var expiryIndexKey = GetExpiryIndexKey();
        var expiredIds = await _db.SortedSetRangeByScoreAsync(
            expiryIndexKey,
            start: 0,
            stop: now.ToUnixTimeSeconds(),
            take: batchSize);

        if (expiredIds.Length == 0)
        {
            return Array.Empty<Transaction>();
        }

        // MGET: load all entities with a single batch request
        var mainKeys = expiredIds
            .Select(id => (RedisKey)GetTransactionKey(id.ToString()))
            .ToArray();
        var rawValues = await _db.StringGetAsync(mainKeys);

        var result = new List<Transaction>(expiredIds.Length);
        var toRemoveFromIndex = new List<RedisValue>();

        for (var i = 0; i < expiredIds.Length; i++)
        {
            switch (DecideOnIndexedRecord(expiredIds[i], rawValues[i]))
            {
                case (ExpirySweepOutcome.Expire, { } transaction):
                    result.Add(transaction);
                    break;

                case (ExpirySweepOutcome.DropFromIndex, _):
                    toRemoveFromIndex.Add(expiredIds[i]);
                    break;

                    // LeaveForAnotherBuild: the id stays in the index, already reported by the decision.
            }
        }

        // Batch-remove stale records from the index with a single ZREM call
        if (toRemoveFromIndex.Count > 0)
        {
            await _db.SortedSetRemoveAsync(expiryIndexKey, toRemoveFromIndex.ToArray());
        }

        return result.AsReadOnly();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Redis manages the key lifecycle via TTL.
    /// This method always returns an empty list.
    /// </remarks>
    public Task<IReadOnlyList<Transaction>> GetStaleTerminalAsync(
        DateTimeOffset olderThan,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // Redis TTL deletes stale keys natively — no explicit cleanup is needed
        return Task.FromResult<IReadOnlyList<Transaction>>(Array.Empty<Transaction>());
    }

    /// <inheritdoc />
    /// <remarks>
    /// Redis does not support an efficient query by state without additional indexes.
    /// This method always returns an empty list — a limitation of the Redis store.
    /// </remarks>
    public Task<IReadOnlyList<Transaction>> GetStalledConfirmedAsync(
        DateTimeOffset confirmedBefore,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // Log the warning once: TransactionCleanupService calls this method
        // every cycle (once per minute by default). Subsequent calls — at Debug level.
        if (Interlocked.CompareExchange(ref _stalledConfirmedWarnedOnce, 1, 0) == 0)
        {
            _logger.LogWarning(
                "The Redis store does not support GetStalledConfirmedAsync. " +
                "Stalled Confirmed transactions will not be moved to Failed. " +
                "If this feature is required, use the EfCore store.");
        }
        else
        {
            _logger.LogDebug("GetStalledConfirmedAsync: Redis store, returning an empty list");
        }

        return Task.FromResult<IReadOnlyList<Transaction>>(Array.Empty<Transaction>());
    }

    /// <summary>
    /// Decides what the expiry pass may do with one record of the expiry index, and rehydrates the
    /// transaction when the outcome is to expire it. Every way this build can fail to read a stored
    /// record — an empty value, unreadable JSON, the JSON literal null, a state name it does not know,
    /// a field its mapper rejects — ends in the same outcome: reported and left where it is. Only the
    /// absence of the key itself says the record is gone. Nothing here throws:
    /// a throw on one unreadable record would take down the whole pass, and with it every transaction
    /// of the batch, on every interval until the key dies by TTL.
    /// </summary>
    /// <param name="transactionId">Identifier as it stands in the index.</param>
    /// <param name="rawValue">Raw value of the transaction key from the batch read.</param>
    /// <returns>The outcome, with the rehydrated transaction when the outcome is Expire.</returns>
    private (ExpirySweepOutcome Outcome, Transaction? Transaction) DecideOnIndexedRecord(
        RedisValue transactionId,
        RedisValue rawValue)
    {
        // The key died by TTL before the pass got to it: there is no record left to expire, and the
        // index entry is all that remains of it. Only a missing key means that — a live key holding
        // an empty value is a record this build cannot read, not a record that is gone.
        if (rawValue.IsNull)
        {
            return (ExpirySweepOutcome.DropFromIndex, null);
        }

        if (rawValue.Length() == 0)
        {
            return LeaveForABuildThatReadsIt(transactionId, "the stored value is empty", state: null);
        }

        TransactionEntity? entity;

        try
        {
            entity = JsonSerializer.Deserialize<TransactionEntity>(
                rawValue.ToString(),
                TransactionEntityMapper.SerializerOptions);
        }
        catch (JsonException failure)
        {
            return LeaveForABuildThatReadsIt(transactionId, "the stored value is not readable JSON", state: null, failure);
        }

        if (entity is null)
        {
            // The key is alive and holds the JSON literal null: the record is there, this build just
            // cannot make a transaction of it.
            return LeaveForABuildThatReadsIt(transactionId, "the stored value is the JSON literal null", state: null);
        }

        // The state is read off the stored record before any rehydration, and it splits three ways.
        // Not a name of the enum, or a value the enum does not define (a bare number in the record
        // parses into an undefined member): this build does not know the state, and the record is not
        // its to drop.
        if (!Enum.TryParse<TransactionState>(entity.State, out var state) || !Enum.IsDefined(state))
        {
            return LeaveForABuildThatReadsIt(transactionId, "the stored state is not a state this build knows", entity.State);
        }

        // Known, and past the point of expiry in the lifecycle — terminal, or confirmed. Which states
        // a deadline still ends is the transition table's to say, and every store asks it the same
        // way. Its key now outlives ExpiresAt by the retention window, and left in the index it
        // would come back to every pass of that window, first in score order, taking the batch slots
        // of the transactions the pass has to expire.
        if (!TransactionStateMachine.ExpirableStates.Contains(state))
        {
            return (ExpirySweepOutcome.DropFromIndex, null);
        }

        try
        {
            return (ExpirySweepOutcome.Expire, TransactionEntityMapper.ToDomain(entity));
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The state was known, some other field of the record is not: same class, same outcome.
            return LeaveForABuildThatReadsIt(transactionId, "the stored record does not rehydrate on this build", entity.State, failure);
        }
    }

    /// <summary>
    /// Reports a record this build could not read and leaves it in the shared index.
    /// The report is a warning on every pass: the record is a genuine anomaly, and the number of passes
    /// it can be seen by is bounded by the TTL of its key. The raw value is never logged — a stored
    /// record holds identity snapshots and client context.
    /// </summary>
    /// <param name="transactionId">Identifier as it stands in the index.</param>
    /// <param name="reason">What about the record this build could not read.</param>
    /// <param name="state">Stored state string, when the record got far enough to have one.</param>
    /// <param name="failure">Failure that ended the reading, when there was one.</param>
    /// <returns>The LeaveForAnotherBuild outcome, without a transaction.</returns>
    private (ExpirySweepOutcome Outcome, Transaction? Transaction) LeaveForABuildThatReadsIt(
        RedisValue transactionId,
        string reason,
        string? state,
        Exception? failure = null)
    {
        _logger.LogWarning(
            failure,
            "The expiry pass cannot read a record of the expiry index and leaves it there for a build that can. TransactionId: {TransactionId}, Reason: {Reason}, State: {State}",
            transactionId.ToString(),
            reason,
            state);

        return (ExpirySweepOutcome.LeaveForAnotherBuild, null);
    }

    /// <summary>
    /// Builds the key for the main transaction record.
    /// </summary>
    /// <param name="transactionId">String transaction identifier.</param>
    /// <returns>Redis key.</returns>
    private string GetTransactionKey(string transactionId)
    {
        return string.Concat(_options.KeyPrefix, transactionId);
    }

    /// <summary>
    /// Builds the key for the idempotency index.
    /// </summary>
    /// <param name="scope">Idempotency scope.</param>
    /// <param name="key">Idempotency key.</param>
    /// <returns>Redis index key.</returns>
    private string GetIdempotencyKey(string scope, string key)
    {
        return string.Concat(
            _options.KeyPrefix,
            IdempotencyIndexSegment,
            scope,
            KeySegmentSeparator,
            key);
    }

    /// <summary>
    /// Builds the key for the expiry sorted set index.
    /// </summary>
    /// <returns>Redis sorted set key.</returns>
    private string GetExpiryIndexKey()
    {
        return string.Concat(_options.KeyPrefix, ExpiryIndexSegment);
    }

    /// <summary>
    /// Computes the effective Unix timestamp for EXPIREAT: the moment the key is deleted, which is
    /// never the moment the transaction expires. A dead transaction — expired or terminal — stays
    /// readable for <c>CompletedRetentionSeconds</c> after the moment it died.
    /// <list type="bullet">
    /// <item>Non-terminal: <c>ExpiresAt + CompletedRetentionSeconds</c>. The transaction is dead at
    /// <c>ExpiresAt</c>, but it is the cleanup pass that records that — reads it back through the expiry
    /// index, moves it to Expired and publishes TransactionExpiredEvent — and the pass runs on its own
    /// interval, so the key has to wait for it. Readers in that window get a non-terminal transaction
    /// whose <c>ExpiresAt</c> is in the past, exactly what the in-memory and EF Core stores return.</item>
    /// <item>Terminal: <c>max(ExpiresAt, UpdatedAt + CompletedRetentionSeconds)</c>, so consumers have
    /// time to read the final status.</item>
    /// </list>
    /// </summary>
    /// <param name="entity">Transaction entity.</param>
    /// <returns>Unix timestamp of the key deletion moment.</returns>
    private long ComputeEffectiveExpiryTimestamp(TransactionEntity entity)
    {
        var baseExpiry = entity.ExpiresAt.ToUnixTimeSeconds();
        var retentionSecs = GetRetentionSeconds();

        if (!IsTerminalState(entity.State))
        {
            return baseExpiry + retentionSecs;
        }

        var retentionExpiry = entity.UpdatedAt.AddSeconds(retentionSecs).ToUnixTimeSeconds();

        return Math.Max(baseExpiry, retentionExpiry);
    }

    /// <summary>
    /// Reads the retention window of a dead transaction — completed, failed or expired alike — from
    /// the engine configuration. One setting for both kinds of death: it is the same "keep it readable
    /// long enough for the outcome to be seen" window, and nothing in this store gives the two a
    /// different length.
    /// </summary>
    /// <returns>Retention in seconds.</returns>
    private int GetRetentionSeconds()
    {
        return _engineOptions.CurrentValue.CompletedRetentionSeconds;
    }

    /// <summary>
    /// Returns true if the given string state name is terminal.
    /// Terminal states: Completed, Failed, Expired.
    /// </summary>
    /// <param name="state">String state name.</param>
    /// <returns>true — the state is terminal.</returns>
    private static bool IsTerminalState(string state)
    {
        return state is nameof(TransactionState.Completed)
            or nameof(TransactionState.Failed)
            or nameof(TransactionState.Expired);
    }

    /// <summary>
    /// Computes the TTL in seconds (integer) for use in Redis Lua scripts (EX/EXPIREAT).
    /// </summary>
    /// <param name="expiresAt">Transaction expiration moment.</param>
    /// <returns>TTL in seconds (≥1), or 0 if the transaction has already expired.</returns>
    private long GetExpirySecs(DateTimeOffset expiresAt)
    {
        var ttlSecs = (expiresAt - _timeProvider.GetUtcNow()).TotalSeconds;
        return ttlSecs > 0 ? (long)Math.Ceiling(ttlSecs) : 0L;
    }

    /// <summary>
    /// Gets and deserializes a transaction entity from Redis by key.
    /// </summary>
    /// <param name="key">Redis key.</param>
    /// <returns>Transaction entity, or null if not found.</returns>
    private async Task<TransactionEntity?> GetEntityAsync(string key)
    {
        // Get the JSON from Redis and deserialize into TransactionEntity
        var value = await _db.StringGetAsync(key);

        if (value.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize<TransactionEntity>(
            value.ToString(),
            TransactionEntityMapper.SerializerOptions);
    }

    /// <summary>
    /// Serializes a TransactionEntity into a JSON string for storage in Redis.
    /// </summary>
    /// <param name="entity">Transaction entity.</param>
    /// <returns>JSON string.</returns>
    private static string SerializeEntity(TransactionEntity entity)
    {
        return JsonSerializer.Serialize(entity, TransactionEntityMapper.SerializerOptions);
    }

    /// <summary>
    /// What the expiry pass may do with one record it found in the expiry index. The index is shared by
    /// every instance working against the same Redis, so an id removed from it is expired by no
    /// instance ever again — which is why "read it" and "drop it" are not the same decision.
    /// </summary>
    private enum ExpirySweepOutcome
    {
        /// <summary>
        /// The record is read and can still become Expired: it goes to the result of the pass.
        /// </summary>
        Expire,

        /// <summary>
        /// The pass has nothing left to do with the record — it is gone, or it is read and past the
        /// point of expiry in the lifecycle. Its id leaves the index.
        /// </summary>
        DropFromIndex,

        /// <summary>
        /// This build could not read the record. That says something about the build, not about the
        /// record, so the id stays in the index for a build that reads it.
        /// </summary>
        LeaveForAnotherBuild
    }
}
