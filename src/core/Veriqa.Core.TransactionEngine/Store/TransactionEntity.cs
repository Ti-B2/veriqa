// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Store;

/// <summary>
/// Flat EF Core entity for storing a transaction in the database.
/// Complex types (snapshots, collections) are stored as JSON strings.
/// Not sealed: EF Core requires the ability to create proxy-derived classes.
/// </summary>
internal class TransactionEntity
{
    /// <summary>
    /// Transaction identifier (Base62, 43 characters).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Transaction type (login, confirmation, etc.).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Current transaction state (string name of the TransactionState enum).
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Reason code of the terminal state. Null for non-terminal states.
    /// </summary>
    public string? StateReasonCode { get; set; }

    /// <summary>
    /// Moment the transaction was created (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Moment of the last state update (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// TTL expiration moment (UTC).
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Client request identifier for tracing.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Idempotency key.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Idempotency scope.
    /// </summary>
    public string? IdempotencyScope { get; set; }

    /// <summary>
    /// Channel type explicitly requested by the client.
    /// </summary>
    public string? RequestedChannelType { get; set; }

    /// <summary>
    /// Arbitrary client data (raw JSON string).
    /// </summary>
    public string? ClientContext { get; set; }

    /// <summary>
    /// Allowed channel types (JSON string array).
    /// </summary>
    public string AllowedChannelTypesJson { get; set; } = string.Empty;

    /// <summary>
    /// Confirmation data snapshot (JSON).
    /// </summary>
    public string? ConfirmationSnapshotJson { get; set; }

    /// <summary>
    /// Channel identity snapshot (JSON).
    /// </summary>
    public string? ChannelIdentitySnapshotJson { get; set; }

    /// <summary>
    /// Final resolved identity snapshot (JSON).
    /// </summary>
    public string? ResolvedIdentitySnapshotJson { get; set; }

    /// <summary>
    /// Completion data snapshot (JSON).
    /// </summary>
    public string? CompletionSnapshotJson { get; set; }

    /// <summary>
    /// Optimistic concurrency token.
    /// </summary>
    public string ConcurrencyToken { get; set; } = string.Empty;

    /// <summary>
    /// OIDC request context (JSON).
    /// </summary>
    public string? OidcContextJson { get; set; }

    /// <summary>
    /// Request context of a non-OIDC transaction (JSON, SPEC-039 C19). Immutable after creation.
    /// </summary>
    public string? RequestContextJson { get; set; }

    /// <summary>
    /// Confirming-party check container (JSON, SPEC-039 C23). Holds protected, opaque values.
    /// </summary>
    public string? IdentityMatchJson { get; set; }

    /// <summary>
    /// Initiator context snapshot (JSON, SPEC-017). Immutable after creation.
    /// </summary>
    public string? InitiatorContextSnapshotJson { get; set; }
}
