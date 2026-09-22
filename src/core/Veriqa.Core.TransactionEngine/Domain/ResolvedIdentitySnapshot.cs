// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Final resolved identity — the user identifier and claims
/// obtained after Identity Resolution. Populated on the transition to Completed.
/// Immutable after being written. Maximum size: 128 KB (validated in TransactionService).
/// </summary>
public sealed class ResolvedIdentitySnapshot
{
    /// <summary>
    /// Final user identifier (sub claim).
    /// </summary>
    [JsonPropertyName("sub")]
    public required string Subject { get; init; }

    /// <summary>
    /// Set of claims associated with the user.
    /// Key — claim name, value — claim value.
    /// </summary>
    [JsonPropertyName("claims")]
    public IReadOnlyDictionary<string, string>? Claims { get; init; }

    /// <summary>
    /// Returns an instance with guaranteed-immutable <see cref="Claims"/>:
    /// with non-empty claims — a copy with <see cref="FrozenDictionary{TKey,TValue}"/>
    /// (the caller may have passed a live mutable dictionary cast to
    /// IReadOnlyDictionary), otherwise — the same instance.
    /// The copy enumerates all properties of the type and is located next to them —
    /// when adding a property, update the copy as well.
    /// The claims are copied key for key and spelling for spelling: freezing must not change what
    /// the set contains. A custom <c>IIdentityResolutionService</c> may return a case-sensitive
    /// dictionary holding two names that differ only in letter case, and re-keying them here by
    /// claim-name identity would drop one of them silently — after which the size validation would
    /// be measuring a set the caller never built.
    /// </summary>
    /// <returns>Instance with frozen claims.</returns>
    public ResolvedIdentitySnapshot WithFrozenClaims()
    {
        if (Claims is null)
        {
            return this;
        }

        return new ResolvedIdentitySnapshot
        {
            Subject = Subject,
            Claims = Claims.ToFrozenDictionary(StringComparer.Ordinal)
        };
    }
}
