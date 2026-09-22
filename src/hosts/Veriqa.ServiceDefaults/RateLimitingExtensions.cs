// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.ServiceDefaults;

/// <summary>
/// Shared rate limiter constants for Veriqa hosts.
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// The <c>QueueLimit</c> value that disables the limiter's wait queue: a request over the limit
    /// is rejected immediately rather than queued. The default for Veriqa guardrail limiters.
    /// </summary>
    public const int NoQueue = 0;
}
