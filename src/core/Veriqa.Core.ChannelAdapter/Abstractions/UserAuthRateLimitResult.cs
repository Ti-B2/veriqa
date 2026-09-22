// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Abstractions;

/// <summary>
/// Result of a per-user authentication rate limit check.
/// Contains the allow flag and the recommended wait time on rejection.
/// TASK-016, SPEC-007 §6.1.
/// </summary>
public readonly record struct UserAuthRateLimitResult
{
    /// <summary>
    /// Singleton "allowed" result.
    /// </summary>
    public static readonly UserAuthRateLimitResult Allowed = new(true, TimeSpan.Zero);

    /// <summary>
    /// Initializes a rate limit check result.
    /// </summary>
    /// <param name="isAllowed">Whether the request is allowed.</param>
    /// <param name="retryAfter">Recommended wait time (TimeSpan.Zero when allowed).</param>
    public UserAuthRateLimitResult(bool isAllowed, TimeSpan retryAfter)
    {
        IsAllowed = isAllowed;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// <c>true</c> — the request is allowed;
    /// <c>false</c> — the limit is exceeded.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Recommended wait time until the next attempt.
    /// Relevant only when <see cref="IsAllowed"/> = <c>false</c>.
    /// Obtained from the limiter lease metadata (MetadataName.RetryAfter).
    /// </summary>
    public TimeSpan RetryAfter { get; init; }

    /// <summary>
    /// Creates a rejected result with the wait time from the limiter metadata.
    /// </summary>
    /// <param name="retryAfter">Actual time until the restriction is lifted.</param>
    /// <returns>Rejected result.</returns>
    public static UserAuthRateLimitResult Rejected(TimeSpan retryAfter) => new(false, retryAfter);
}
