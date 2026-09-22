// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// String names of transaction statuses for SignalR messages (SPEC-007 §5.3).
/// </summary>
public static class TransactionStatusNames
{
    /// <summary>
    /// The user confirmed the action in the channel.
    /// </summary>
    public const string Confirmed = "Confirmed";

    /// <summary>
    /// The core accepted the user's action in the channel and now asks the confirming question on
    /// its own page: the browser must go to the callback instead of waiting for a terminal status.
    /// Not a lifecycle state — the transaction stays Pending.
    /// </summary>
    public const string AwaitingWebConfirmation = "AwaitingWebConfirmation";

    /// <summary>
    /// Authentication completed successfully.
    /// </summary>
    public const string Completed = "Completed";

    /// <summary>
    /// The transaction expired by TTL.
    /// </summary>
    public const string Expired = "Expired";

    /// <summary>
    /// Error or rejection.
    /// </summary>
    public const string Failed = "Failed";
}
