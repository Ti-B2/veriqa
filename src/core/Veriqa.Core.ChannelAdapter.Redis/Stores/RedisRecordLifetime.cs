// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Redis.Stores;

/// <summary>
/// Turns the expiry moment an entity carries into the TTL of its Redis record. The lifetime of a
/// stored record is the lifetime of the entity — this package holds no lifetime constants of its own.
/// </summary>
internal static class RedisRecordLifetime
{
    /// <summary>
    /// Computes the TTL of a record from the entity's expiry moment.
    /// </summary>
    /// <param name="expiresAt">Expiry moment of the entity (UTC).</param>
    /// <param name="now">Current time (UTC), supplied by the caller's time provider.</param>
    /// <param name="entityDescription">
    /// What is being stored, for the exception message (e.g. "Email action token").
    /// </param>
    /// <returns>Record TTL.</returns>
    /// <exception cref="InvalidOperationException">
    /// The entity has already expired, so there is no lifetime to give its record. Redis has no
    /// "store an already-dead key" mode, and silently writing it with a one-second TTL would hide a
    /// caller that mints expired tokens — the same stance the Redis transaction store takes.
    /// </exception>
    public static TimeSpan FromExpiry(DateTimeOffset expiresAt, DateTimeOffset now, string entityDescription)
    {
        var lifetime = expiresAt - now;

        if (lifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{entityDescription} has already expired (ExpiresAt={expiresAt:O}) and cannot be stored in Redis.");
        }

        return lifetime;
    }
}
