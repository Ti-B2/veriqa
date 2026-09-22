// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Entity for storing channel identity in the DB (persistence/entity layer).
/// Represents a technical channel artifact, not a global user model.
/// Uniqueness is defined by the triple (<see cref="TenantId"/>, <see cref="ChannelType"/>,
/// <see cref="ChannelUserId"/>).
/// Lifecycle: indefinite storage, create-or-update (upsert), without delete/TTL.
///
/// Data transformation chain:
///   ChannelIdentitySnapshot → ChannelIdentity → IIdentityResolutionService → ResolvedIdentity → ResolvedIdentitySnapshot.
///
/// Scope limitation:
///   A full-fledged identity resolution system (linking, matching, merging multiple channels
///   of one real person) is deliberately not implemented.
///   The entity is intended solely for capturing data of an individual channel.
/// </summary>
public sealed class ChannelIdentity
{
    /// <summary>
    /// Unique record identifier (generated on creation).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Tenant the identity belongs to. Null — the default implicit tenant of a self-hosted install.
    /// Part of the unique key: (TenantId, ChannelType, ChannelUserId).
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Channel type (telegram, max, whatsapp, email, etc.).
    /// Part of the unique key: (TenantId, ChannelType, ChannelUserId).
    /// </summary>
    public required string ChannelType { get; init; }

    /// <summary>
    /// User identifier within the channel.
    /// Part of the unique key: (TenantId, ChannelType, ChannelUserId).
    /// </summary>
    public required string ChannelUserId { get; init; }

    /// <summary>
    /// Bot flag: true if the source is a bot rather than a real user.
    /// </summary>
    public required bool IsBot { get; set; }

    /// <summary>
    /// The user's display name in the channel.
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// The user's first name (if available from the channel).
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// The user's last name (if available from the channel).
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// The user's username (in the channel, if available).
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// The user's email address (if available from the channel).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Phone number (if available from the channel).
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// The user's avatar URL (if available from the channel).
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// The user's language/locale (if available from the channel).
    /// </summary>
    public string? Locale { get; set; }

    /// <summary>
    /// Arbitrary metadata from the channel (JSON).
    /// </summary>
    public JsonElement? RawMetadata { get; set; }

    /// <summary>
    /// Version of the channel adapter that captured the data.
    /// </summary>
    public required string AdapterVersion { get; set; }

    /// <summary>
    /// Date and time when the identity first appeared (UTC).
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Date and time of the identity's last update (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Creates a <see cref="ChannelIdentity"/> from a <see cref="ChannelIdentitySnapshot"/>.
    /// </summary>
    /// <param name="snapshot">Channel data snapshot.</param>
    /// <param name="now">Current time (UTC), supplied by the caller's time provider.</param>
    /// <param name="existingId">Existing record identifier (for upsert). If <c>null</c> — a new one is generated.</param>
    /// <param name="createdAt">Time of first appearance (for upsert). If <c>null</c> — <paramref name="now"/> is used.</param>
    /// <returns>A new entity with data from the snapshot.</returns>
    public static ChannelIdentity FromSnapshot(
        ChannelIdentitySnapshot snapshot,
        DateTimeOffset now,
        string? existingId = null,
        DateTimeOffset? createdAt = null)
    {
        return new ChannelIdentity
        {
            Id = existingId ?? Guid.NewGuid().ToString("N"),
            TenantId = snapshot.TenantId,
            ChannelType = snapshot.ChannelType,
            ChannelUserId = snapshot.ChannelUserId,
            IsBot = snapshot.IsBot,
            DisplayName = snapshot.DisplayName,
            FirstName = snapshot.FirstName,
            LastName = snapshot.LastName,
            Username = snapshot.Username,
            Email = snapshot.Email,
            PhoneNumber = snapshot.PhoneNumber,
            AvatarUrl = snapshot.AvatarUrl,
            Locale = snapshot.Locale,
            RawMetadata = snapshot.RawMetadata,
            AdapterVersion = snapshot.AdapterVersion,
            CreatedAt = createdAt ?? now,
            UpdatedAt = now,
        };
    }
}
