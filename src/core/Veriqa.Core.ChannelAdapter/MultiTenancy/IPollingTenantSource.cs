// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Seam "source of active polling tenants" (SPEC-003 §17.4, the CA-165 analog for the polling transport).
/// Returns the set of tenants for which the <c>channelType</c> channel operates in polling mode
/// and is allowed (<c>channels_enabled</c>). <c>null</c> in the set = the default implicit tenant (self-hosted N=1).
/// The core default is the degenerate N=1; the multi-tenant implementation is supplied by the Cloud
/// profile opt-in behind the NuGet boundary (CA-172). There is no second tenant-enumeration mechanism
/// in the core (anti-fork).
/// </summary>
public interface IPollingTenantSource
{
    /// <summary>
    /// Returns a snapshot of the active polling tenant set for the channel type.
    /// </summary>
    /// <param name="channelType">Channel type (<c>ChannelTypes.Telegram</c> / <c>ChannelTypes.Max</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Success with a snapshot of the tenantId set (a <c>null</c> element = the default N=1 tenant; the set may be
    /// empty if polling is not active for the channel), or <c>Result.Failure</c> with code
    /// <see cref="ChannelCredentialErrorCodes.ChannelNotAvailable"/> if the tenant source is unavailable.
    /// </returns>
    Task<Result<IReadOnlyCollection<string?>>> GetActivePollingTenantsAsync(
        string channelType,
        CancellationToken cancellationToken);
}
