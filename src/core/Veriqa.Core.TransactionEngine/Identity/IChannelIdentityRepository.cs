// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.Identity;

/// <summary>
/// Repository for persisting <see cref="ChannelIdentity"/>.
/// The contract is limited to save operations only (create-or-update / upsert).
/// Uniqueness is defined by the triple (TenantId, ChannelType, ChannelUserId) — the tenant travels
/// as a field of the snapshot, so the signature states only what a caller must supply.
/// A repeated save with the same key updates the mutable fields.
/// Storage is indefinite — no delete operations or TTL are provided.
///
/// NOT IMPLEMENTED IN THE SHIPPED ASSEMBLY:
///   Veriqa registers no implementation of this port, so by default nothing is stored. What travels
///   with the transaction is the resolved identity snapshot, and the sign-in works without a single
///   write. Storing the channel identity — PII (channel user id, phone, email, display name) kept
///   indefinitely by the contract above — is therefore the integrator's own decision: register an
///   implementation and every entry becomes an upsert again. A reference in-memory implementation
///   lives in the inproc sample host.
///
/// LIFETIME:
///   Any lifetime works, Scoped included. The resolver asks for this port once per operation from a
///   scope of its own rather than holding it, so an implementation over a Scoped DbContext is not
///   captured by the singleton that consumes it.
///
/// SCOPE LIMITATION (TASK-002):
///   Search, filtering, aggregation and matching methods are deliberately NOT added.
///   A full-fledged identity system with extended operations is out of scope for the current task.
/// </summary>
public interface IChannelIdentityRepository
{
    /// <summary>
    /// Saves a <see cref="ChannelIdentity"/> with upsert semantics.
    /// If a record with the triple (TenantId, ChannelType, ChannelUserId) already exists — updates the
    /// mutable fields.
    /// If it does not exist — creates a new record.
    /// </summary>
    /// <param name="snapshot">Channel data snapshot to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved or updated <see cref="ChannelIdentity"/> entity.</returns>
    Task<ChannelIdentity> SaveAsync(ChannelIdentitySnapshot snapshot, CancellationToken cancellationToken = default);
}
