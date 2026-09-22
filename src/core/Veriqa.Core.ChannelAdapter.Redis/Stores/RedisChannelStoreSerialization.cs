// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;

namespace Veriqa.Core.ChannelAdapter.Redis.Stores;

/// <summary>
/// Serialization settings shared by the Redis channel stores. A single instance because
/// <see cref="JsonSerializerOptions"/> caches its metadata — creating one per call would re-run
/// contract resolution on every store operation.
/// </summary>
internal static class RedisChannelStoreSerialization
{
    /// <summary>
    /// Settings used to write and read stored records. Property names are kept as declared so the
    /// payload stays readable in <c>redis-cli</c> and stable across releases.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };
}
