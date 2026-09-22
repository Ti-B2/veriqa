// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Redis.Stores;

/// <summary>
/// Lua scripts shared by the Redis channel stores. A script runs as one indivisible step on the
/// server, which is what turns a read-then-write into a genuine compare-and-swap across replicas.
/// </summary>
internal static class RedisChannelStoreScripts
{
    /// <summary>
    /// Records a "first time" marker on an existing hash, once.
    /// Both Email ports publish a status exactly once — the token's "opened" and the correlation's
    /// "compose_opened" — and the rule is the same for both: the record must still be alive (a used,
    /// consumed or expired one has no hash left), and only the first writer of the field wins.
    /// KEYS[1] — record hash key.
    /// ARGV[1] — hash field to set.
    /// ARGV[2] — timestamp value.
    /// Returns: 1 — recorded by this call; 0 — already recorded, or the record is gone.
    /// </summary>
    public const string MarkFieldOnce = @"
if redis.call('EXISTS', KEYS[1]) == 0 then
    return 0
end
return redis.call('HSETNX', KEYS[1], ARGV[1], ARGV[2])";
}
