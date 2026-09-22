// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.TransactionEngine.Store;

/// <summary>
/// Mapper between the Transaction domain model and the TransactionEntity EF Core entity.
/// Also used by the Redis store for serialization/deserialization via TransactionEntity.
/// </summary>
internal static class TransactionEntityMapper
{
    /// <summary>
    /// JSON serialization options for snapshots and collections.
    /// PropertyNameCaseInsensitive is enabled for reliable deserialization.
    /// </summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Cache of JSON representations of immutable snapshots (ConfirmationSnapshot,
    /// InitiatorContextSnapshot, ChannelIdentitySnapshot, ResolvedIdentitySnapshot)
    /// keyed by instance identity. Snapshot instances are immutable (init-only fields,
    /// frozen collections) and over the transaction lifecycle are replaced wholesale
    /// (a new instance is assigned on Confirm/Complete) rather than mutated in place —
    /// so the JSON of each instance is computed exactly once: the service validates
    /// the size, and the store reuses the same JSON when writing to the entity.
    /// </summary>
    private static readonly ConditionalWeakTable<object, string> ImmutableSnapshotJsonCache = new();

    /// <summary>
    /// Serializes an immutable snapshot to JSON with per-instance memoization.
    /// Apply only to snapshots with immutable instances replaced wholesale
    /// (ConfirmationSnapshot, InitiatorContextSnapshot, ChannelIdentitySnapshot,
    /// ResolvedIdentitySnapshot) — for snapshots mutated in place, a per-instance
    /// cache would yield stale JSON.
    /// </summary>
    /// <param name="snapshot">Immutable snapshot.</param>
    /// <returns>JSON representation of the snapshot (store's SerializerOptions).</returns>
    internal static string SerializeImmutableSnapshot(object snapshot)
    {
        return ImmutableSnapshotJsonCache.GetValue(
            snapshot,
            static value => JsonSerializer.Serialize(value, SerializerOptions));
    }

    /// <summary>
    /// Converts the Transaction domain model into a TransactionEntity EF Core entity.
    /// </summary>
    /// <param name="transaction">Transaction domain model.</param>
    /// <returns>Flat EF Core entity.</returns>
    internal static TransactionEntity ToEntity(Transaction transaction)
    {
        // Take the whole transaction as one object first: the same published path a store written
        // outside the Veriqa assemblies uses, so the shipped mapper and a third-party one see exactly
        // the same field perimeter and a field added to the domain cannot be flattened here alone.
        var snapshot = transaction.ToSnapshot();

        // Serialize collections and snapshots into JSON strings for database storage
        return new TransactionEntity
        {
            Id = snapshot.Id.ToString(),
            Type = snapshot.Type,
            State = snapshot.State.ToString(),
            StateReasonCode = snapshot.StateReasonCode,
            CreatedAt = snapshot.CreatedAt,
            UpdatedAt = snapshot.UpdatedAt,
            ExpiresAt = snapshot.ExpiresAt,
            CorrelationId = snapshot.CorrelationId,
            IdempotencyKey = snapshot.IdempotencyKey,
            IdempotencyScope = snapshot.IdempotencyScope,
            RequestedChannelType = snapshot.RequestedChannelType,
            ClientContext = snapshot.ClientContext,
            AllowedChannelTypesJson = JsonSerializer.Serialize(
                snapshot.AllowedChannelTypes,
                SerializerOptions),
            ConfirmationSnapshotJson = snapshot.ConfirmationSnapshot is not null
                ? SerializeImmutableSnapshot(snapshot.ConfirmationSnapshot)
                : null,
            ChannelIdentitySnapshotJson = snapshot.ChannelIdentitySnapshot is not null
                ? SerializeImmutableSnapshot(snapshot.ChannelIdentitySnapshot)
                : null,
            ResolvedIdentitySnapshotJson = snapshot.ResolvedIdentitySnapshot is not null
                ? SerializeImmutableSnapshot(snapshot.ResolvedIdentitySnapshot)
                : null,
            CompletionSnapshotJson = snapshot.CompletionSnapshot is not null
                ? JsonSerializer.Serialize(snapshot.CompletionSnapshot, SerializerOptions)
                : null,
            ConcurrencyToken = snapshot.ConcurrencyToken,
            OidcContextJson = snapshot.OidcContext is not null
                ? JsonSerializer.Serialize(snapshot.OidcContext, SerializerOptions)
                : null,
            RequestContextJson = snapshot.RequestContext is not null
                ? JsonSerializer.Serialize(snapshot.RequestContext, SerializerOptions)
                : null,
            IdentityMatchJson = snapshot.IdentityMatch is not null
                ? JsonSerializer.Serialize(snapshot.IdentityMatch, SerializerOptions)
                : null,
            InitiatorContextSnapshotJson = snapshot.InitiatorContextSnapshot is not null
                ? SerializeImmutableSnapshot(snapshot.InitiatorContextSnapshot)
                : null
        };
    }

    /// <summary>
    /// Updates the mutable fields of an existing EF Core entity from the domain model.
    /// Called on UpdateAsync — updates only the fields that can change.
    /// </summary>
    /// <param name="entity">Existing EF Core entity to update.</param>
    /// <param name="transaction">Updated domain model.</param>
    internal static void UpdateEntity(TransactionEntity entity, Transaction transaction)
    {
        // Update only the mutable fields (fields with internal set in the domain)
        entity.State = transaction.State.ToString();
        entity.StateReasonCode = transaction.StateReasonCode;
        entity.UpdatedAt = transaction.UpdatedAt;
        entity.ChannelIdentitySnapshotJson = transaction.ChannelIdentitySnapshot is not null
            ? SerializeImmutableSnapshot(transaction.ChannelIdentitySnapshot)
            : null;
        entity.ResolvedIdentitySnapshotJson = transaction.ResolvedIdentitySnapshot is not null
            ? SerializeImmutableSnapshot(transaction.ResolvedIdentitySnapshot)
            : null;
        entity.CompletionSnapshotJson = transaction.CompletionSnapshot is not null
            ? JsonSerializer.Serialize(transaction.CompletionSnapshot, SerializerOptions)
            : null;

        // The identity-match container changes over the lifecycle — the verdict and the erased
        // expectations on completion, the issuance mark after it — so it is rewritten on every update
        // exactly as it is written on creation.
        entity.IdentityMatchJson = transaction.IdentityMatch is not null
            ? JsonSerializer.Serialize(transaction.IdentityMatch, SerializerOptions)
            : null;
        entity.ConcurrencyToken = transaction.ConcurrencyToken;
    }

    /// <summary>
    /// Converts a TransactionEntity EF Core entity back into the Transaction domain model.
    /// </summary>
    /// <param name="entity">Flat EF Core entity.</param>
    /// <returns>Transaction domain model.</returns>
    internal static Transaction ToDomain(TransactionEntity entity)
    {
        // Parse TransactionId from the string
        if (!TransactionId.TryParse(entity.Id, out var transactionId))
        {
            throw new InvalidOperationException(
                $"Invalid TransactionId in the store: '{entity.Id}'");
        }

        // Parse AllowedChannelTypes from the JSON array
        var allowedChannelTypes = DeserializeAllowedChannelTypes(entity.AllowedChannelTypesJson);

        // Restore the domain model from the flat data through the published rehydration path:
        // State, UpdatedAt and ConcurrencyToken are stated, not defaulted, and the transaction comes
        // back exactly as it was stored.
        return Transaction.Restore(new TransactionSnapshot
        {
            Id = transactionId,
            Type = entity.Type,
            State = Enum.Parse<TransactionState>(entity.State),
            StateReasonCode = entity.StateReasonCode,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            ExpiresAt = entity.ExpiresAt,
            CorrelationId = entity.CorrelationId,
            IdempotencyKey = entity.IdempotencyKey,
            IdempotencyScope = entity.IdempotencyScope,
            RequestedChannelType = entity.RequestedChannelType,
            AllowedChannelTypes = allowedChannelTypes,
            ClientContext = entity.ClientContext,
            ConfirmationSnapshot = entity.ConfirmationSnapshotJson is not null
                ? JsonSerializer.Deserialize<ConfirmationSnapshot>(
                    entity.ConfirmationSnapshotJson,
                    SerializerOptions)
                : null,
            ChannelIdentitySnapshot = entity.ChannelIdentitySnapshotJson is not null
                ? JsonSerializer.Deserialize<ChannelIdentitySnapshot>(
                    entity.ChannelIdentitySnapshotJson,
                    SerializerOptions)
                : null,
            ResolvedIdentitySnapshot = entity.ResolvedIdentitySnapshotJson is not null
                ? JsonSerializer.Deserialize<ResolvedIdentitySnapshot>(
                    entity.ResolvedIdentitySnapshotJson,
                    SerializerOptions)
                : null,
            CompletionSnapshot = entity.CompletionSnapshotJson is not null
                ? JsonSerializer.Deserialize<CompletionSnapshot>(
                    entity.CompletionSnapshotJson,
                    SerializerOptions)
                : null,
            ConcurrencyToken = entity.ConcurrencyToken,
            OidcContext = entity.OidcContextJson is not null
                ? JsonSerializer.Deserialize<OidcContext>(
                    entity.OidcContextJson,
                    SerializerOptions)
                : null,
            RequestContext = entity.RequestContextJson is not null
                ? JsonSerializer.Deserialize<TransactionRequestContext>(
                    entity.RequestContextJson,
                    SerializerOptions)
                : null,
            IdentityMatch = entity.IdentityMatchJson is not null
                ? JsonSerializer.Deserialize<IdentityMatchState>(
                    entity.IdentityMatchJson,
                    SerializerOptions)
                : null,
            InitiatorContextSnapshot = entity.InitiatorContextSnapshotJson is not null
                ? JsonSerializer.Deserialize<InitiatorContextSnapshot>(
                    entity.InitiatorContextSnapshotJson,
                    SerializerOptions)
                : null
        });
    }

    /// <summary>
    /// Deserializes a JSON string array into a FrozenSet for AllowedChannelTypes.
    /// </summary>
    /// <param name="json">JSON string containing an array of channel types.</param>
    /// <returns>Immutable set of allowed channel types.</returns>
    private static IReadOnlySet<string> DeserializeAllowedChannelTypes(string json)
    {
        // Deserialize the JSON array into a string array, then create a FrozenSet
        if (string.IsNullOrEmpty(json))
        {
            return Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);
        }

        var channelTypes = JsonSerializer.Deserialize<string[]>(json, SerializerOptions);

        return (channelTypes ?? Array.Empty<string>())
            .ToFrozenSet(StringComparer.Ordinal);
    }
}
