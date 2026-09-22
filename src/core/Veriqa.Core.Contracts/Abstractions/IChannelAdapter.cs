// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Abstractions;

/// <summary>
/// Unified channel adapter interface (SPEC-003 §6).
/// Each implementation serves one specific channel (Telegram, WhatsApp, Max, etc.).
/// The division of labour is fixed: the core states the intent, the adapter picks the platform
/// operations that carry it out and declares what the channel can do
/// (<see cref="Capabilities"/>) instead of answering "cannot" with a stub.
/// </summary>
public interface IChannelAdapter
{
    /// <summary>
    /// Channel type served by this adapter.
    /// </summary>
    string ChannelType { get; }

    /// <summary>
    /// Facts the channel declares about itself (SPEC-003 §6.1, SPEC-012 §4.4.1).
    /// Every implementation declares them explicitly; a new fact is a new property of
    /// <see cref="ChannelCapabilities"/> with a safe default, never a new interface member.
    /// </summary>
    ChannelCapabilities Capabilities { get; }

    /// <summary>
    /// Processes an inbound event from the channel platform. The adapter parses its own wire
    /// format from the neutral envelope — the core imposes no body format.
    /// </summary>
    /// <param name="request">Neutral inbound request envelope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of processing the incoming event, typed by intent.</returns>
    Task<Result<ChannelInboundResult>> ProcessInboundEventAsync(
        ChannelInboundRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers, as cheaply as the question allows, whether this inbound event belongs to Veriqa
    /// (SPEC-003 §6.5, CA-195). It exists for a router that owns the channel transport and hands
    /// updates out between its own bot and Veriqa: that router needs the answer BEFORE the event
    /// is processed, and parsing the same event twice is what this member saves it.
    /// </summary>
    /// <param name="request">Neutral inbound request envelope.</param>
    /// <returns><see langword="true"/> — the body carries a Veriqa ownership marker;
    /// <see langword="false"/> — it carries none. The answer is deliberately one-sided, and that is
    /// part of the contract: an event produced by a Veriqa button or deep link always answers
    /// <see langword="true"/>, while <see langword="true"/> on its own does not promise the event is
    /// actionable — the precise answer still comes from <see cref="ProcessInboundEventAsync"/>.</returns>
    /// <remarks>
    /// The default body scans the raw bytes of <see cref="ChannelInboundRequest.Body"/> for any
    /// marker of <see cref="CallbackDataPrefixes"/>. It reads nothing else — not the headers, not
    /// the query — asks neither the platform nor the transaction store anything, and throws for no
    /// body content whatsoever: an empty or a binary body simply answers <see langword="false"/>.
    /// False positives are allowed by the contract for two reasons: the search is substring-based,
    /// so <see cref="CallbackDataPrefixes.Auth"/> — the one marker without a vendor prefix — also
    /// matches inside unrelated fields (a user's message containing "oauth_", say); and any marker
    /// can simply be typed out by a user. Override the member with a precise check of your own wire
    /// format when a false positive costs you something.
    /// </remarks>
    bool OwnsInboundEvent(ChannelInboundRequest request)
    {
        var body = request.Body.Span;
        return ContainsMarker(body, CallbackDataPrefixes.Confirm)
            || ContainsMarker(body, CallbackDataPrefixes.Decline)
            || ContainsMarker(body, CallbackDataPrefixes.Auth);

        // Ordinal byte-wise search: the markers are ASCII by construction, so their UTF-8 encoding
        // is byte-identical to their characters and the body needs no decoding — which is what
        // keeps the check allocation-free on a path the router walks for every single update.
        static bool ContainsMarker(ReadOnlySpan<byte> body, string marker)
        {
            Span<byte> markerBytes = stackalloc byte[marker.Length];
            for (var i = 0; i < marker.Length; i++)
            {
                markerBytes[i] = (byte)marker[i];
            }

            return body.IndexOf(markerBytes) >= 0;
        }
    }

    /// <summary>
    /// Sends an authentication confirmation prompt to the user in the channel.
    /// The confirmation context is always present (SPEC-003 §6.2, SPEC-017 §7.1): either the
    /// initiator details or the explicit "no details" value, which the adapter renders with its own
    /// default text (ICC-042). The recipient locale travels inside the context and nowhere else.
    /// </summary>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="channelUserId">User identifier in the channel.</param>
    /// <param name="promptContext">Confirmation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reference to the sent prompt, or null when the channel has no addressable one.
    /// An adapter declaring <c>SupportsMessageUpdate</c> must return a non-null reference here.
    /// A failure carrying <see cref="Contracts.VeriqaErrorCodes.ChannelCannotContinue"/> says "I cannot carry
    /// this confirmation out", and the core ends the transaction on it instead of leaving it to a
    /// retry or its TTL — every other failure code keeps the latter, waiting behaviour.</returns>
    Task<Result<ChannelMessageRef?>> SendConfirmationPromptAsync(
        TransactionId transactionId,
        string channelUserId,
        ConfirmationPromptContext promptContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to the user in the channel.
    /// </summary>
    /// <param name="message">Message to deliver; its <c>Kind</c> is guaranteed to be within
    /// <c>Capabilities.SupportedMessageKinds</c> — the core never passes anything else.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reference to the sent message; null in a success means the message was delivered but
    /// the channel has no addressable reference for it. A failure means it was not delivered.</returns>
    Task<Result<ChannelMessageRef?>> SendMessageAsync(
        ChannelMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an inbound webhook request from the channel platform.
    /// </summary>
    /// <param name="request">Neutral inbound request envelope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the request is valid.</returns>
    Task<Result<bool>> ValidateWebhookAsync(
        ChannelInboundRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the channel's health status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Channel health status.</returns>
    Task<Result<ChannelHealthStatus>> GetHealthAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a deep link to start authentication in the channel.
    /// </summary>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deep link URL.</returns>
    Task<Result<string>> GetDeepLinkAsync(
        TransactionId transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows the user that the transaction reached a terminal state (SPEC-003 §6.2, CA-142).
    /// The core only states the intent; the composition and the order of platform calls are the
    /// adapter's choice. Called only when the channel declared <c>DeliversOutcomeNotice</c>.
    /// The notice also carries the DESIRED display intent of the deployment
    /// (<see cref="Domain.TransactionOutcomeNotice.DisplayIntent"/>) — replace the message that asked
    /// the question, or send a new one — and honouring it is the adapter's choice as well: an intent
    /// the platform cannot serve is left unhonoured, which is neither a refusal nor a failure.
    /// </summary>
    /// <param name="notice">What to show and everything needed to address it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>Success(true)</c> — the outcome was shown to the user;
    /// <c>Success(false)</c> — the channel deliberately showed nothing (no surface, channel policy);
    /// <c>Failure</c> — the attempt to show the outcome did not succeed.</returns>
    Task<Result<bool>> ReportOutcomeAsync(
        TransactionOutcomeNotice notice,
        CancellationToken cancellationToken = default);
}
