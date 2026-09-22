// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Store.Redis;

/// <summary>
/// Options of the Redis transaction store.
/// Configured via the fluent API in TransactionEngineBuilder.
/// </summary>
public sealed class RedisTransactionStoreOptions
{
    /// <summary>
    /// Default key prefix.
    /// </summary>
    public const string DefaultKeyPrefix = "veriqa:tx:";

    /// <summary>
    /// Redis connection string (e.g., "localhost:6379").
    /// </summary>
    public string Configuration { get; set; } = string.Empty;

    /// <summary>
    /// Prefix for all transaction keys in Redis.
    /// Default: "veriqa:tx:".
    /// </summary>
    public string KeyPrefix { get; set; } = DefaultKeyPrefix;
}
