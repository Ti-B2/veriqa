// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;

using StackExchange.Redis;

namespace Veriqa.Core.ChannelAdapter.Redis.Stores;

/// <summary>
/// Reading and writing the moments stored in the Redis channel records. Both Email stores keep the
/// same shape of timestamp, so the format lives in one place rather than in each of them.
/// </summary>
internal static class RedisChannelStoreTimestamps
{
    /// <summary>
    /// Round-trip format specifier: preserves the offset and the sub-second precision.
    /// </summary>
    private const string RoundTripFormat = "O";

    /// <summary>
    /// Formats a moment for storage — round-trippable and culture-independent.
    /// </summary>
    /// <param name="value">Moment to format.</param>
    /// <returns>String representation.</returns>
    public static string Format(DateTimeOffset value)
    {
        return value.ToString(RoundTripFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a stored moment.
    /// </summary>
    /// <param name="value">Stored value.</param>
    /// <returns>The moment, or null when the field is absent or unparseable.</returns>
    public static DateTimeOffset? Parse(RedisValue value)
    {
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }
}
