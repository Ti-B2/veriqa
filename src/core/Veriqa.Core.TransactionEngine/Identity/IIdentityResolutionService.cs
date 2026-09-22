// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.Identity;

/// <summary>
/// Channel identity resolution service.
/// Accepts a <see cref="ChannelIdentitySnapshot"/>, persists the <see cref="ChannelIdentity"/>,
/// builds a <see cref="ResolvedIdentity"/> and returns a <see cref="ResolvedIdentitySnapshot"/>.
///
/// Extension point: the implementation can be replaced via DI for a custom resolution strategy.
///
/// Data transformation chain:
///   ChannelIdentitySnapshot → ChannelIdentity → IIdentityResolutionService → ResolvedIdentity → ResolvedIdentitySnapshot.
/// </summary>
public interface IIdentityResolutionService
{
    /// <summary>
    /// Resolves the channel identity: persists the channel data and builds
    /// the resulting <see cref="ResolvedIdentitySnapshot"/> with subject and claims.
    /// </summary>
    /// <param name="snapshot">Channel data snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Resolved identity with subject and claims, or the reason the identity could not be resolved.
    /// </returns>
    /// <remarks>
    /// A refusal is a result, not an exception: a replacing implementation that reaches into a
    /// directory of its own — the very reason this is an extension point — answers "I cannot" the
    /// same way the rest of the channel boundary does. The caller reaches this method with the
    /// transaction already confirmed, so a refusal is a failed finalization and is compensated as
    /// one; the error travels out unchanged, because the only party that knows why the identity was
    /// refused is the implementation that refused it.
    ///
    /// Because the error travels out unchanged, the code carried by it lands in the same string the
    /// callers of the finalization use to tell a lost race from a downstream failure. Four codes are
    /// therefore reserved to the transaction lifecycle and must not be returned from here:
    /// <c>transaction_not_found</c>, <c>transaction_expired</c>, <c>invalid_state_transition</c> and
    /// <c>concurrency_conflict</c>. An implementation that returns one of them tells the caller the
    /// session is no longer answerable, and its refusal is answered with a "start again" page instead
    /// of the failure it actually was. Any other code — including one of the implementation's own —
    /// is read as a refusal to resolve and answered as such.
    /// </remarks>
    Task<Result<ResolvedIdentitySnapshot>> ResolveAsync(
        ChannelIdentitySnapshot snapshot,
        CancellationToken cancellationToken = default);
}
