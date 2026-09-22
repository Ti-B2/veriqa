// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Identity;

namespace Veriqa.Sample.DotNet.Inproc.Login;

/// <summary>
/// Reference implementation of <see cref="IChannelIdentityRepository"/> — the port Veriqa ships
/// without an implementation on purpose: a channel identity is PII (channel user id, phone, email,
/// display name), the contract stores it indefinitely, and where that data lives is the
/// integrator's decision. Register your own implementation to keep the identities; register none
/// and Veriqa keeps nothing, the sign-in itself works either way.
/// <para>
/// This one keeps them in memory: thread-safe (ConcurrentDictionary), everything lost on restart,
/// no deletion and no TTL. Good enough for a sample or a development run; a real deployment writes
/// to a store of its own and answers for the retention of what it wrote.
/// </para>
/// </summary>
public sealed class InMemoryChannelIdentityRepository : IChannelIdentityRepository
{
    /// <summary>
    /// Entity store. Key — the composite key (tenant, channel_type, channel_user_id). The tenant is
    /// part of the key because two tenants seeing the same channel user own separate records; an
    /// unstated tenant is spelled one fixed way (<see cref="TenantKey.Segment"/>), so null and an
    /// empty string never split one default tenant into two.
    /// </summary>
    private readonly ConcurrentDictionary<(string Tenant, string ChannelType, string ChannelUserId), ChannelIdentity> _store = new();

    /// <summary>
    /// Clock the timestamps of the stored records are taken from.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates an in-memory repository instance.
    /// </summary>
    /// <param name="timeProvider">Time provider.</param>
    public InMemoryChannelIdentityRepository(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<ChannelIdentity> SaveAsync(ChannelIdentitySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var key = (TenantKey.Segment(snapshot.TenantId), snapshot.ChannelType, snapshot.ChannelUserId);

        // One reading of the clock per upsert: the update delegate may run repeatedly, and a record
        // must not end up with timestamps taken at different moments of the same save.
        var now = _timeProvider.GetUtcNow();

        // ConcurrentDictionary.AddOrUpdate guarantees atomicity only at the slot level;
        // the update delegate may be invoked repeatedly. Therefore we return a NEW instance
        // preserving the immutable fields (Id, CreatedAt) with an atomic reference swap —
        // this rules out interleaving fields of different snapshots within one entity.
        var result = _store.AddOrUpdate(
            key,
            // Factory creating a new record
            _ => ChannelIdentity.FromSnapshot(snapshot, now),
            // Update factory: creates a new ChannelIdentity, preserving Id and CreatedAt.
            (_, existing) => ChannelIdentity.FromSnapshot(snapshot, now, existing.Id, existing.CreatedAt));

        return Task.FromResult(result);
    }
}
