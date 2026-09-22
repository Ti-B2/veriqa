// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.Events;

/// <summary>
/// Base class of a transaction event.
/// All events contain the transaction identifier and the moment of occurrence.
/// </summary>
public abstract record TransactionEvent
{
    /// <summary>
    /// Identifier of the transaction the event occurred for.
    /// </summary>
    public required TransactionId TransactionId { get; init; }

    /// <summary>
    /// Moment the event occurred (UTC).
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Attribution of the transaction captured at publication time; null when the publisher has no
    /// transaction at hand. A subscriber reads the attributes from here instead of the store: the
    /// transaction may already be gone by the time the event is handled.
    /// </summary>
    public TransactionEventContext? Context { get; init; }
}

/// <summary>
/// Transaction created.
/// </summary>
public sealed record TransactionCreatedEvent : TransactionEvent
{
    /// <summary>
    /// Type of the created transaction.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Transaction TTL expiration time.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Transaction activated (State → Pending).
/// </summary>
public sealed record TransactionActivatedEvent : TransactionEvent;

/// <summary>
/// Transaction confirmed by the user (State → Confirmed).
/// </summary>
public sealed record TransactionConfirmedEvent : TransactionEvent
{
    /// <summary>
    /// Type of the channel through which the confirmation occurred.
    /// </summary>
    public required string ChannelType { get; init; }
}

/// <summary>
/// Channel identity attached to a transaction that stays Pending: the core accepted the user's
/// action in the channel and now waits for the confirming answer on its own web page.
/// Additive event — the lifecycle state does not change.
/// </summary>
public sealed record TransactionChannelIdentityAttachedEvent : TransactionEvent
{
    /// <summary>
    /// Type of the channel the identity came from.
    /// </summary>
    public required string ChannelType { get; init; }
}

/// <summary>
/// Transaction completed successfully (State → Completed).
/// </summary>
public sealed record TransactionCompletedEvent : TransactionEvent
{
    /// <summary>
    /// Final user identifier (sub claim).
    /// </summary>
    public string? Subject { get; init; }
}

/// <summary>
/// Transaction ended with an error (State → Failed).
/// </summary>
public sealed record TransactionFailedEvent : TransactionEvent
{
    /// <summary>
    /// Failure reason code.
    /// </summary>
    public required string ReasonCode { get; init; }
}

/// <summary>
/// Transaction expired by TTL (State → Expired).
/// </summary>
public sealed record TransactionExpiredEvent : TransactionEvent
{
    /// <summary>
    /// Reason code of the terminal state, stated by the publisher that performed the transition —
    /// symmetric with <see cref="TransactionFailedEvent.ReasonCode"/>.
    /// </summary>
    public required string ReasonCode { get; init; }

    /// <summary>
    /// TTL deadline the transaction ended at — the moment of the expiry itself, symmetric with
    /// <see cref="TransactionCreatedEvent.ExpiresAt"/>.
    /// </summary>
    /// <remarks>
    /// Stated separately from <see cref="TransactionEvent.OccurredAt"/> because the two are not the
    /// same moment here: the transition is performed by a sweep running on its own interval, so the
    /// moment of publication is the moment the expiry was NOTICED and lies up to one pass after the
    /// deadline it reports. A subscriber that tells the user when the transaction ended reads this
    /// one; a subscriber journalling when the event happened reads <c>OccurredAt</c>.
    /// </remarks>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Channel-specific intermediate transaction status changed (SPEC-016 §10.1).
/// An additive event: it does not change the transaction lifecycle state and is published
/// by a channel adapter to inform the UI about intermediate steps
/// (e.g. email: "message sent", "compose page opened").
/// </summary>
public sealed record TransactionChannelStatusChangedEvent : TransactionEvent
{
    /// <summary>
    /// Type of the channel that published the status (e.g. "email").
    /// </summary>
    public required string ChannelType { get; init; }

    /// <summary>
    /// Channel-specific status code (e.g. "sent", "compose_opened").
    /// The code semantics are defined by the channel adapter.
    /// </summary>
    public required string ChannelStatus { get; init; }
}

/// <summary>
/// Channel audit event: a security-significant occurrence that does not change
/// the transaction lifecycle state (SPEC-016 EM-032, EM-066).
/// Consumers — external audit consumers (e.g. via the RabbitMQ publisher) and logs.
/// </summary>
public sealed record TransactionChannelAuditEvent : TransactionEvent
{
    /// <summary>
    /// Type of the channel that published the event (e.g. "email").
    /// </summary>
    public required string ChannelType { get; init; }

    /// <summary>
    /// Audit event code (e.g. "email_sender_verification_failed").
    /// </summary>
    public required string EventCode { get; init; }

    /// <summary>
    /// Safe event details (no PII in the clear — e.g. masked email, policy).
    /// </summary>
    public string? Details { get; init; }
}
