// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Abstractions;

/// <summary>
/// Contract of the per-user authentication rate limiter.
/// Protects against excessive authentication attempts by the same channel user.
/// Partition key: {channel_type}:{channel_user_id} (lowercase, trimmed).
/// TASK-016, SPEC-002, SPEC-007 §6.1.
/// </summary>
public interface IUserAuthRateLimiter
{
    /// <summary>
    /// Attempts to acquire authentication permission for the user.
    /// </summary>
    /// <param name="userKey">
    /// User key in the {tenant}:{channel_type}:{channel_user_id} format (lowercase, trimmed). The
    /// tenant leads it so tenants do not share one user's budget; a tenant nobody stated takes the
    /// single spelling of <see cref="Veriqa.Core.TransactionEngine.Domain.TenantKey.Default"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="UserAuthRateLimitResult.IsAllowed"/> = <c>true</c> — allowed;
    /// = <c>false</c> — the limit is exceeded, <see cref="UserAuthRateLimitResult.RetryAfter"/>
    /// contains the actual wait time from the limiter metadata.
    /// </returns>
    ValueTask<UserAuthRateLimitResult> TryAcquireAsync(string userKey, CancellationToken cancellationToken = default);
}
