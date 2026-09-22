// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// The core's intent "show the user that the transaction reached a terminal state" (SPEC-003 §6.2).
/// Which platform calls that takes — answering a callback, editing a message, sending a new one, or
/// nothing at all — is the adapter's decision.
/// </summary>
public sealed record TransactionOutcomeNotice
{
    /// <summary>
    /// Transaction the outcome belongs to.
    /// </summary>
    public required TransactionId TransactionId { get; init; }

    /// <summary>
    /// Terminal outcome of the transaction.
    /// </summary>
    public required TransactionOutcome Outcome { get; init; }

    /// <summary>
    /// Recipient within the channel — needed to send a new message when an update is impossible.
    /// </summary>
    public required string ChannelUserId { get; init; }

    /// <summary>
    /// Terminal status text, already localized by the core into the recipient's language
    /// (the Natural Key itself when no translation exists).
    /// </summary>
    public required string StatusText { get; init; }

    /// <summary>
    /// Opaque token of the inbound interaction: present on a live button press, absent on the TTL path.
    /// </summary>
    public ChannelInboundToken? InboundToken { get; init; }

    /// <summary>
    /// Reference to the previously sent prompt: present when the channel supports updates and the
    /// reference was stored.
    /// </summary>
    public ChannelMessageRef? PromptMessage { get; init; }

    /// <summary>
    /// How the deployment WANTS this receipt to appear in the conversation: in place of the message
    /// that asked the question, or as a message of its own.
    /// <para>
    /// A desired intent, not a guarantee and not an obligation: the adapter may honour it or may have
    /// no way to — a platform without an editable delivered message cannot replace anything. Leaving
    /// the intent unhonoured is not a refusal, is not logged as an error and does not change the
    /// result of <see cref="Abstractions.IChannelAdapter.ReportOutcomeAsync"/>.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Not <c>required</c> on purpose: a notice built without stating the intent — by an adapter or
    /// host compiled against the previous shape of this record — carries the shipped intent and
    /// behaves exactly as it did before the field existed.
    /// </remarks>
    public OutcomeNoticeDisplayIntent DisplayIntent { get; init; }
}
