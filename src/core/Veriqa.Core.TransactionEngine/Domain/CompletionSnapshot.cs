// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;

using Veriqa.Core.Contracts;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Final transaction completion data: resulting claims, metadata.
/// Populated on transition to Completed. Immutable after being written.
/// Maximum size: 128 KB (validated in TransactionService).
/// </summary>
public sealed class CompletionSnapshot
{
    /// <summary>
    /// Transaction completion time (UTC).
    /// </summary>
    [JsonPropertyName("completed_at")]
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Type of the channel through which the confirmation was received.
    /// </summary>
    [JsonPropertyName(VeriqaClaimTypes.ChannelType)]
    public required string ChannelType { get; init; }

    /// <summary>
    /// Final claims for token issuance.
    /// </summary>
    [JsonPropertyName("claims")]
    public IReadOnlyDictionary<string, string>? Claims { get; init; }
}
