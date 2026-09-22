// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Services;

/// <summary>
/// Store for Email Pull-mode action tokens (SPEC-016 §4.3).
/// Guarantees single-use of magic link tokens.
/// </summary>
public interface IEmailActionTokenStore
{
    /// <summary>
    /// Persists an action token in the store.
    /// </summary>
    /// <param name="token">String token (URL-safe Base64).</param>
    /// <param name="data">Token data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreTokenAsync(string token, EmailActionToken data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves action token data by its string token.
    /// </summary>
    /// <param name="token">String token to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Token data, or null if the token is not found.
    /// The returned token may be expired (<see cref="EmailActionToken.IsExpired(System.DateTimeOffset)"/>
    /// returns true for the current moment) —
    /// checking validity is the caller's responsibility.
    /// </returns>
    Task<EmailActionToken?> GetTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a token as used, consuming it.
    /// </summary>
    /// <remarks>
    /// CONTRACT for production implementations (Redis/EF, etc.): the operation MUST be atomic
    /// (compare-and-swap / SET NX / DEL / transaction) — exactly one concurrent call receives true,
    /// every other call receives false. Without that guarantee a magic link opened twice at once is
    /// consumed twice, and both openings complete the sign-in.
    /// </remarks>
    /// <param name="token">String token to mark.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>true — the token was marked successfully; false — the token was not found.</returns>
    Task<bool> MarkUsedAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks the first opening of the confirm page for a token (for single
    /// publication of the "opened" status, SPEC-016 §10.1).
    /// </summary>
    /// <remarks>
    /// CONTRACT for production implementations (Redis/EF, etc.): the operation MUST be atomic
    /// (compare-and-swap / SET NX / transaction) — exactly one concurrent call receives true;
    /// for an expired/used token — false.
    /// The default implementation returns true ON EVERY call (without deduplication) and is kept
    /// only for backward compatibility of custom stores: without an override the "opened" status
    /// will be duplicated on every page view and on mail-scanner prefetch.
    /// </remarks>
    /// <param name="token">String token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// true — the opening was recorded for the first time (the status must be published);
    /// false — the page was already opened earlier, or the token was not found.
    /// </returns>
    Task<bool> TryMarkOpenedAsync(string token, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
