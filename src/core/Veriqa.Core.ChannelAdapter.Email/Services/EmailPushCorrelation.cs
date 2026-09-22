// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Services;

/// <summary>
/// Correlation-token data for Email Push mode (SPEC-016 §5).
/// Unlike <see cref="EmailActionToken"/>, at creation time the user's email is not yet known —
/// it is determined once an inbound message from a verified sender is received.
/// Single-use token: links an inbound email to a specific transaction.
/// </summary>
/// <param name="TransactionId">String representation of the transaction identifier.</param>
/// <param name="ExpiresAt">Expiration moment of the correlation token (UTC).</param>
/// <param name="ConsumedAt">Moment the token was consumed (UTC). Null if not consumed.</param>
/// <param name="ComposeOpenedAt">
/// Moment the compose page was first opened (UTC). Null if the page has not been opened yet.
/// Used to publish the "compose_opened" status only once — a page refresh
/// does not duplicate the signal (SPEC-016 §10.1).
/// </param>
public sealed record EmailPushCorrelation(
    string TransactionId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt = null,
    DateTimeOffset? ComposeOpenedAt = null)
{
    /// <summary>
    /// Returns whether the correlation token has been consumed.
    /// </summary>
    public bool IsConsumed => ConsumedAt is not null;

    /// <summary>
    /// Returns whether the correlation token has expired at the given moment.
    /// </summary>
    /// <param name="now">Current time (UTC), supplied by the caller's time provider.</param>
    /// <returns><c>true</c> when the expiration moment has been reached.</returns>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
