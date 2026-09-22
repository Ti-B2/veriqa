// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Services;

/// <summary>
/// Store for Email Push mode correlation tokens (SPEC-016 §5).
/// Ensures single use of correlation tokens and idempotent
/// deduplication of inbound emails by provider message-id (EM-023, EM-127).
/// </summary>
public interface IEmailPushCorrelationStore
{
    /// <summary>
    /// Stores a correlation token in the store.
    /// </summary>
    /// <param name="token">String correlation token (URL-safe Base64).</param>
    /// <param name="correlation">Correlation data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(string token, EmailPushCorrelation correlation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves correlation data by its string token.
    /// </summary>
    /// <param name="token">String correlation token to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Correlation data, or null if not found.</returns>
    Task<EmailPushCorrelation?> GetAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads several correlations in one round trip — the question "which of these candidates is a live
    /// token" an inbound mail asks about every token-shaped run of its text at once.
    /// </summary>
    /// <remarks>
    /// The default implementation asks <see cref="GetAsync"/> for one token after another, so a store
    /// that implements only <see cref="GetAsync"/> needs nothing more; a store able to read
    /// a batch (Redis pipelining or MGET, a SQL <c>WHERE token IN (…)</c>) overrides it and answers in
    /// one round trip. Which of the live candidates wins is not the store's business: the result is a
    /// map, and the order of the candidates stays with the caller that read them out of the mail.
    /// </remarks>
    /// <param name="tokens">Candidate tokens; an empty list asks the storage nothing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The correlations found, keyed by token. A token the store does not hold is ABSENT from the map
    /// rather than present with a null value, and a token repeated in the list yields one entry.
    /// </returns>
    async Task<IReadOnlyDictionary<string, EmailPushCorrelation>> GetManyAsync(
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var found = new Dictionary<string, EmailPushCorrelation>(tokens.Count, StringComparer.Ordinal);
        var asked = new HashSet<string>(StringComparer.Ordinal);

        // A token repeated in the list is asked about once whether or not it is found: "one question
        // per candidate" is what the caller counts on, and a candidate the store does not hold is a
        // candidate all the same.
        foreach (var token in tokens)
        {
            if (asked.Add(token) && await GetAsync(token, cancellationToken) is { } correlation)
            {
                found[token] = correlation;
            }
        }

        return found;
    }

    /// <summary>
    /// Atomically marks a correlation token as consumed (single use).
    /// </summary>
    /// <param name="token">String correlation token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// true — the token was successfully marked consumed by the current call;
    /// false — the token was not found, has expired, or was already consumed by another call.
    /// </returns>
    Task<bool> TryConsumeAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks the first opening of the compose page for a correlation token
    /// (to publish the "compose_opened" status only once, SPEC-016 §10.1).
    /// </summary>
    /// <remarks>
    /// CONTRACT for production implementations (Redis/EF, etc.): the operation MUST be
    /// atomic (compare-and-swap / SET NX / transaction) — exactly one concurrent call
    /// gets true; for an expired/consumed correlation — false.
    /// The default implementation returns true ON EVERY call (no deduplication) and is kept
    /// only for backward compatibility with custom stores: without an override, the
    /// "compose_opened" status will be duplicated on every refresh of the compose page.
    /// </remarks>
    /// <param name="token">String correlation token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// true — the opening was recorded for the first time (the status should be published);
    /// false — the page was opened earlier, or the token was not found.
    /// </returns>
    Task<bool> TryMarkComposeOpenedAsync(string token, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Idempotently registers a correlation token for its transaction: when the transaction already has
    /// a live (neither consumed nor expired) token, that token is returned and nothing is stored.
    /// </summary>
    /// <remarks>
    /// CONTRACT for production implementations (Redis/EF, etc.): the capture of the
    /// "transaction → live token" index MUST be atomic (compare-and-swap / SET NX / transaction) —
    /// out of concurrent calls for one transaction exactly one token stays alive and every caller
    /// gets that same token back. The TTL of a reused token MUST NOT be extended: otherwise a token
    /// would stay alive for as long as the page keeps being re-rendered.
    /// The default implementation stores the supplied token unconditionally and returns it, preserving
    /// today's behavior and backward compatibility with custom stores: without an override, the direct
    /// <c>mailto:</c> Push mode issues a new correlation token on every render of the sign-in page.
    /// </remarks>
    /// <param name="token">Freshly minted correlation token (lower-case Base32, the recipient local part) — a candidate for registration.</param>
    /// <param name="correlation">
    /// Correlation data; <see cref="EmailPushCorrelation.TransactionId"/> is the key of the index.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The token the caller must use: the supplied one if it was registered, or the token already
    /// live for this transaction.
    /// </returns>
    async Task<string> StoreOrReuseAsync(string token, EmailPushCorrelation correlation, CancellationToken cancellationToken = default)
    {
        await StoreAsync(token, correlation, cancellationToken);
        return token;
    }

    /// <summary>
    /// Checks whether an email with the given message-id has already been successfully processed,
    /// without registering a new message-id (read-only deduplication check).
    /// </summary>
    /// <param name="messageId">Email identifier (Message-ID or provider event id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// true — an email with this message-id has already been processed (it should be ignored);
    /// false — the message-id has not been registered yet.
    /// </returns>
    Task<bool> IsMessageProcessedAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers the processing of an inbound email by its message-id for idempotent
    /// deduplication of redelivery (EM-023, EM-127). Called only after
    /// the transaction has completed successfully — otherwise a transient failure would "lose" the email,
    /// marking it processed before it actually completed (review feedback).
    /// </summary>
    /// <param name="messageId">Email identifier (Message-ID or provider event id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// true — the message-id was registered for the first time (the email should be processed);
    /// false — an email with this message-id has already been processed (it should be ignored).
    /// </returns>
    Task<bool> TryRegisterProcessedMessageAsync(string messageId, CancellationToken cancellationToken = default);
}
