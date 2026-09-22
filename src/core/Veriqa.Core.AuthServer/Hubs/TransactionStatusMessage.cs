// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Hubs;

/// <summary>
/// Transaction status change message model (SPEC-007 §5.3).
/// Sent to clients via SignalR when the transaction state changes.
/// Every holder of the session_id may receive it, so it carries the transaction's process state only —
/// never the channel user's identity, claims or snapshot contents (SPEC-007 UI-095).
/// </summary>
public sealed class TransactionStatusMessage
{
    /// <summary>
    /// External session identifier (session_id) used to subscribe to updates.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Current status: Pending, Confirmed, Completed, Expired, Failed.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Redirect URL (on completion).
    /// </summary>
    public string? RedirectUrl { get; init; }

    /// <summary>
    /// Error code (on failure).
    /// </summary>
    public string? ErrorCode { get; init; }
}
