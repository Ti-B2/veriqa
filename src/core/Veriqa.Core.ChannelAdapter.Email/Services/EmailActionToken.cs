// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Services;

/// <summary>
/// Email Pull-mode action token data (SPEC-016 §4.3).
/// A single-use token: confirms that the user clicked the magic link.
/// </summary>
/// <param name="TransactionId">String representation of the transaction identifier.</param>
/// <param name="NormalizedEmail">The user's normalized email address.</param>
/// <param name="ExpiresAt">Token expiration time (UTC).</param>
/// <param name="UsedAt">Token usage time (UTC). Null if not used.</param>
/// <param name="OpenedAt">
/// Time the confirm page was first opened via the token (UTC). Null if the page has not
/// been opened yet. Used for a single publication of the "opened" status —
/// repeat views and mail scanner prefetch do not duplicate the signal (SPEC-016 §4.4, §10.1).
/// </param>
public sealed record EmailActionToken(
    string TransactionId,
    string NormalizedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt = null,
    DateTimeOffset? OpenedAt = null)
{
    /// <summary>
    /// Returns whether the token has been used.
    /// </summary>
    public bool IsUsed => UsedAt is not null;

    /// <summary>
    /// Returns whether the token has expired at the given moment.
    /// </summary>
    /// <param name="now">Current time (UTC), supplied by the caller's time provider.</param>
    /// <returns><c>true</c> when the expiration moment has been reached.</returns>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
