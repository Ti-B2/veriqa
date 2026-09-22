// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.ObjectModel;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Resolved identity entity (persistence/entity layer).
/// The result of IIdentityResolutionService: links a ChannelIdentity
/// with the final user identifier (subject) and a set of claims.
///
/// Data transformation chain:
///   ChannelIdentitySnapshot → ChannelIdentity → IIdentityResolutionService → ResolvedIdentity → ResolvedIdentitySnapshot.
///
/// SCOPE LIMITATION (TASK-002):
///   The current implementation is deterministic: sub = channel_type:channel_user_id.
///   A full-fledged identity resolution with linking, matching, and channel merging
///   is deliberately NOT implemented. The class provides a method to obtain a
///   <see cref="ResolvedIdentitySnapshot"/> for use in the TransactionEngine.
/// </summary>
public sealed class ResolvedIdentity
{
    /// <summary>
    /// Unique record identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The final user identifier (sub claim).
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Identifier of the linked ChannelIdentity.
    /// </summary>
    public required string ChannelIdentityId { get; init; }

    /// <summary>
    /// The set of claims associated with the user.
    /// Key — the claim name, value — the claim value.
    /// Immutable: on assignment it is wrapped in a <see cref="ReadOnlyDictionary{TKey,TValue}"/>.
    /// The copy is faithful — key for key, spelling for spelling. Claim-name identity is decided
    /// where the set is built and the names are checked; re-keying it here would either merge two
    /// case-variant names into one or fail on them, and a transporter is the wrong place to do
    /// either.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Claims
    {
        get => _claims;
        set => _claims = value is null
            ? null
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(value, StringComparer.Ordinal));
    }

    private IReadOnlyDictionary<string, string>? _claims;

    /// <summary>
    /// Date and time when the record was created (UTC).
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Date and time of the record's last update (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Converts the entity into a <see cref="ResolvedIdentitySnapshot"/> for use
    /// in the TransactionEngine.
    /// </summary>
    /// <returns>A DTO snapshot with subject and claims.</returns>
    public ResolvedIdentitySnapshot ToSnapshot()
    {
        return new ResolvedIdentitySnapshot
        {
            Subject = Subject,
            Claims = Claims,
        };
    }
}
