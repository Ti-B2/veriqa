// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Contracts;

/// <summary>
/// Error codes that cross the boundary of the free SPI package — the value of
/// <c>TransactionError.Code</c> a third-party adapter is expected to produce and to recognize.
/// Consolidates the codes that were previously declared only in the MPL-2.0 core assemblies,
/// where an adapter built against this package cannot see them at all.
///
/// A code belongs here when at least one of the two holds: an adapter returns it through
/// <c>IChannelAdapter</c>, or the core branches on it in behaviour observable outside the package.
/// Codes that satisfy neither stay internal to the core. Values are part of the public wire
/// contract (they are also telemetry tags) and must not change.
/// </summary>
public static class VeriqaErrorCodes
{
    // Group 1 — returned by an adapter through IChannelAdapter.

    /// <summary>
    /// The webhook authenticity check threw — returned by the adapter from
    /// <c>ValidateWebhookAsync</c> only when verifying the secret token or the signature fails with
    /// an unexpected exception. Every controlled rejection carries no code: a secret that is
    /// missing from the request, does not match, cannot be resolved or is not configured is
    /// reported as a check that ran and returned <c>false</c>. The body is not its subject
    /// either — a body the adapter cannot parse is <see cref="UnsupportedEventType"/>.
    /// </summary>
    public const string WebhookValidationFailed = "webhook_validation_failed";

    /// <summary>
    /// Failed to extract the user identity from the channel event — returned by the adapter.
    /// </summary>
    public const string IdentityExtractionFailed = "identity_extraction_failed";

    /// <summary>
    /// Deep link generation error — returned by the adapter.
    /// </summary>
    public const string DeepLinkGenerationFailed = "deep_link_generation_failed";

    /// <summary>
    /// Failed to send a message to the channel — returned by the adapter.
    /// </summary>
    public const string MessageSendFailed = "message_send_failed";

    /// <summary>
    /// Invalid message or chat identifier — returned by the adapter.
    /// </summary>
    public const string InvalidMessageId = "invalid_message_id";

    /// <summary>
    /// Phone number extraction error — returned by the adapter.
    /// </summary>
    public const string PhoneExtractionFailed = "phone_extraction_failed";

    /// <summary>
    /// Unsupported channel event type — returned by the adapter from inbound event processing for
    /// a body it cannot parse into an update it supports. An update the adapter does parse but has
    /// no reason to act on is not an error and carries no code: the adapter reports it as a
    /// channel-unrelated result.
    /// </summary>
    public const string UnsupportedEventType = "unsupported_event_type";

    // Group 2 — the core branches on these codes in behaviour observable outside the package.

    /// <summary>
    /// The channel type does not resolve. Returned by an adapter, and the core branches on it
    /// when it composes the refusal text of the channel client factory.
    /// </summary>
    public const string ChannelNotAvailable = "channel_not_available";

    /// <summary>
    /// Credentials for the tenant/channel are not set. The core branches on it when it composes
    /// the refusal text of the channel client factory.
    /// </summary>
    public const string ChannelCredentialsMissing = "channel_credentials_missing";

    /// <summary>
    /// A transaction with the given identifier was not found. The core branches on it to stop
    /// retrying an inbound event, so an adapter can rely on the code being terminal.
    /// </summary>
    public const string TransactionNotFound = "transaction_not_found";

    /// <summary>
    /// The transaction expired by TTL. The core branches on it to stop retrying an inbound event.
    /// </summary>
    public const string TransactionExpired = "transaction_expired";

    /// <summary>
    /// The channel declares it cannot carry THIS confirmation out — a runtime signal about the
    /// adapter's own inability and nothing more. The adapter names no core surface and does not
    /// choose what happens next: it states "not me, not now", the core decides.
    /// <para>
    /// The code is meant to cover three classes of situation, told apart by what the core does with
    /// the signal — not by a reason the adapter would report, because the core neither asks for
    /// reasons nor interprets them:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// A critical situation — there is no way to continue at all, so the transaction ends in
    /// failure with this code as its reason. <b>Implemented.</b>
    /// </description></item>
    /// <item><description>
    /// A signal for resolution — "this possibility is unreachable right now", on which the core
    /// recomputes its effective decision by its own rules (for example, taking the confirmation
    /// elsewhere). <i>Intent of the contract, not existing behaviour.</i>
    /// </description></item>
    /// <item><description>
    /// A non-fatal situation — whether to continue is decided by the core's policy, not by the
    /// adapter. <i>Intent of the contract, not existing behaviour.</i>
    /// </description></item>
    /// </list>
    /// <para>
    /// Only the first class is implemented today; the other two describe what the code is designed
    /// to carry, so that an adapter returning it does not have to guess a second code later.
    /// </para>
    /// </summary>
    public const string ChannelCannotContinue = "channel_cannot_continue";
}
