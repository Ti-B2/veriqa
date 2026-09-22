// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Mapping utility between the external session_id and the internal TransactionId.
/// The mapping is a direct 1:1 identity: session_id = TransactionId.
/// </summary>
public static class SessionIdMapper
{
    /// <summary>
    /// Converts a TransactionId to an external session_id.
    /// </summary>
    /// <param name="id">Internal transaction identifier.</param>
    /// <returns>External session_id.</returns>
    public static string ToSessionId(TransactionId id)
    {
        // Direct conversion (1:1 identity mapping).
        return id.ToString();
    }

    /// <summary>
    /// Converts an external session_id to a TransactionId.
    /// </summary>
    /// <param name="sessionId">External session_id.</param>
    /// <returns>TransactionId, or null if the format is invalid.</returns>
    public static TransactionId? ToTransactionId(string? sessionId)
    {
        // Direct parsing via TryParse (1:1 identity mapping).
        if (TransactionId.TryParse(sessionId, out var id))
        {
            return id;
        }

        return null;
    }
}
