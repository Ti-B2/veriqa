// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// Result of processing an inbound channel event (SPEC-003 §6.3).
/// The intent is the type: every intent brings its own mandatory set, so "which field is valid for
/// which intent" is no longer a rule each consumer has to remember.
/// </summary>
/// <remarks>
/// The hierarchy is closed (a private protected constructor) and carries its type through the
/// standard <c>System.Text.Json</c> polymorphism, with the discriminator named <c>intent</c> —
/// the replay perimeter of the sandbox deserializes this shape from disk and from a request body.
/// A consumer must have an "unknown result type" branch (warning plus no-op) so that a sixth intent
/// does not break the runtime.
/// <para>
/// That sixth intent is why the type is marked experimental: the roadmap items that add one — claim
/// enrichment, a shared contact, unlinking a channel, a chain of channels — are still ahead, so this
/// hierarchy may gain a variant in a minor version instead of waiting for a major one.
/// </para>
/// </remarks>
[Experimental(VeriqaDiagnosticIds.UnsettledChannelSpiShape)]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "intent")]
[JsonDerivedType(typeof(ChannelAuthStartResult), ChannelInboundIntents.AuthStart)]
[JsonDerivedType(typeof(ChannelAuthConfirmResult), ChannelInboundIntents.AuthConfirm)]
[JsonDerivedType(typeof(ChannelAuthDeclineResult), ChannelInboundIntents.AuthDecline)]
[JsonDerivedType(typeof(ChannelPhoneSharedResult), ChannelInboundIntents.PhoneShared)]
[JsonDerivedType(typeof(ChannelUnrelatedResult), ChannelInboundIntents.Unrelated)]
[JsonDerivedType(typeof(ChannelUnaddressedResult), ChannelInboundIntents.Unaddressed)]
public abstract record ChannelInboundResult
{
    /// <summary>
    /// Closes the hierarchy to this assembly.
    /// </summary>
    private protected ChannelInboundResult()
    {
    }

    /// <summary>
    /// Original platform event, kept for logging.
    /// </summary>
    public object? RawEvent { get; init; }
}

/// <summary>
/// The user initiated the start of authentication (deep link followed, prefilled message sent).
/// </summary>
public sealed record ChannelAuthStartResult : ChannelInboundResult
{
    /// <summary>
    /// Transaction identifier the event belongs to, already parsed by the adapter: the raw string
    /// lives in the channel event, so the channel is where it is read out (SPEC-003 §6.3). An event
    /// whose identifier does not parse is not ours — the adapter answers with
    /// <see cref="ChannelUnrelatedResult"/> instead of constructing this intent.
    /// </summary>
    public required TransactionId TransactionId { get; init; }

    /// <summary>
    /// Snapshot of the user's identity in the channel.
    /// </summary>
    public required ChannelIdentitySnapshot Identity { get; init; }
}

/// <summary>
/// The user confirmed authentication.
/// </summary>
public sealed record ChannelAuthConfirmResult : ChannelInboundResult
{
    /// <summary>
    /// Transaction identifier the event belongs to, already parsed by the adapter: the raw string
    /// lives in the channel event, so the channel is where it is read out (SPEC-003 §6.3). An event
    /// whose identifier does not parse is not ours — the adapter answers with
    /// <see cref="ChannelUnrelatedResult"/> instead of constructing this intent.
    /// </summary>
    public required TransactionId TransactionId { get; init; }

    /// <summary>
    /// Snapshot of the user's identity in the channel.
    /// </summary>
    public required ChannelIdentitySnapshot Identity { get; init; }

    /// <summary>
    /// Opaque token of the interaction, handed back to the adapter with the outcome notice.
    /// Absent when the event carries no interactive context (polling, a channel without buttons).
    /// </summary>
    public ChannelInboundToken? InboundToken { get; init; }
}

/// <summary>
/// The user declined authentication.
/// </summary>
public sealed record ChannelAuthDeclineResult : ChannelInboundResult
{
    /// <summary>
    /// Transaction identifier the event belongs to, already parsed by the adapter: the raw string
    /// lives in the channel event, so the channel is where it is read out (SPEC-003 §6.3). An event
    /// whose identifier does not parse is not ours — the adapter answers with
    /// <see cref="ChannelUnrelatedResult"/> instead of constructing this intent.
    /// </summary>
    public required TransactionId TransactionId { get; init; }

    /// <summary>
    /// Snapshot of the user's identity in the channel. Mandatory on this branch too: the outcome
    /// notice needs the recipient, and an optional snapshot would degrade into a silent skip.
    /// </summary>
    public required ChannelIdentitySnapshot Identity { get; init; }

    /// <summary>
    /// Opaque token of the interaction, handed back to the adapter with the outcome notice.
    /// </summary>
    public ChannelInboundToken? InboundToken { get; init; }
}

/// <summary>
/// The user shared a phone number (CA-004).
/// </summary>
public sealed record ChannelPhoneSharedResult : ChannelInboundResult
{
    /// <summary>
    /// Shared phone number.
    /// </summary>
    public required string PhoneNumber { get; init; }

    /// <summary>
    /// Snapshot of the user's identity in the channel.
    /// </summary>
    public required ChannelIdentitySnapshot Identity { get; init; }
}

/// <summary>
/// The event is unrelated to the authentication process.
/// </summary>
public sealed record ChannelUnrelatedResult : ChannelInboundResult;

/// <summary>
/// A direct message to the bot from a user the adapter already vetted, that names no transaction
/// (a bare /start, free text). Unlike <see cref="ChannelUnrelatedResult"/> it carries the sender, so
/// a reply is possible when the registration declares one.
/// </summary>
/// <remarks>
/// The vetting is the one an inbound message already goes through — a private chat, a sender that is
/// present and is not a bot. An event that fails it, one from a group chat, and a callback that is
/// none of ours stay <see cref="ChannelUnrelatedResult"/>: they carry no sender the core may answer
/// (SPEC-003 CA-193).
/// </remarks>
public sealed record ChannelUnaddressedResult : ChannelInboundResult
{
    /// <summary>
    /// Snapshot of the sender's identity in the channel. Mandatory: an answer without a recipient is
    /// no answer, and this intent exists precisely to carry one.
    /// </summary>
    public required ChannelIdentitySnapshot Identity { get; init; }
}
