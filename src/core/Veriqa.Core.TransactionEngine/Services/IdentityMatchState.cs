// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;

namespace Veriqa.Core.TransactionEngine.Services;

/// <summary>
/// Container of the "who confirmed vs whom the relying party expected" check on a transaction
/// (SPEC-039 C23). It deliberately lives outside <c>ConfirmationSnapshot</c>: the snapshot is the
/// transport of the prompt's caller values, while expectations are never shown to anyone.
/// <para>
/// The values are stored <b>already protected</b> — the protection is applied by the writer of the
/// container, so from the store's point of view they are opaque strings. This type declares no
/// protection port and performs no comparison of its own.
/// </para>
/// <para>
/// The container also holds the fact that the identity of the confirming party was handed to the
/// client that created the transaction (SPEC-039 C51): the one-time issuance is a lifecycle fact of
/// the same check, not a parameter of the request.
/// </para>
/// </summary>
public sealed class IdentityMatchState
{
    /// <summary>
    /// Expectations of the relying party: declared comparable type name → protected expected value.
    /// At most one value per type. Null — the relying party stated no expectations.
    /// The map that reaches the store is immutable: it is frozen on creation by
    /// <c>TransactionService</c>, which is what lets the store share the container by reference.
    /// </summary>
    [JsonPropertyName("expectations")]
    public IReadOnlyDictionary<string, string>? Expectations { get; init; }

    /// <summary>
    /// Verdict of the check — the name of the declared type whose value matched; null means either
    /// "no match" or "the verdict has not been computed yet", which is the normal state of a
    /// transaction that has not been finalized.
    /// </summary>
    [JsonPropertyName("matched_type")]
    public string? MatchedType { get; init; }

    /// <summary>
    /// Moment the identity token of the confirming party was issued to the client that created the
    /// transaction (SPEC-039 C51); null — not issued. Written once: the transaction is redeemed a
    /// single time.
    /// </summary>
    [JsonPropertyName("identity_token_issued_at")]
    public DateTimeOffset? IdentityTokenIssuedAt { get; init; }
}
