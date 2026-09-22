// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Action data for confirmation (confirmation transaction type). It is the transport of the
/// caller-supplied slot values of the declared confirmation message kind (SPEC-039 C16):
/// no free-form text of the relying party is carried or shown.
/// Stored as serialized JSON, not interpreted by the Transaction Engine.
/// Maximum size: 128 KB (validated in TransactionService).
/// </summary>
public sealed class ConfirmationSnapshot
{
    /// <summary>
    /// Identifier of the declared confirmation message kind being confirmed — the allowlisted
    /// action type (SPEC-039 C18).
    /// </summary>
    [JsonPropertyName("action_type")]
    public string? ActionType { get; init; }

    /// <summary>
    /// Caller-supplied slot values: slot name → value in the canonical textual form of its declared
    /// type (SPEC-039 C14/C16). Null or empty both mean "no caller values"; per-slot obligations are
    /// enforced by the schema on the way in, not here.
    /// </summary>
    [JsonPropertyName("slot_values")]
    public IReadOnlyDictionary<string, string>? SlotValues { get; init; }
}
