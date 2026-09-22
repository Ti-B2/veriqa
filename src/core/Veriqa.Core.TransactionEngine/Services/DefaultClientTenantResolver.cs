// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Services;

/// <summary>
/// Shipped <see cref="IClientTenantResolver"/>: every client belongs to the default implicit tenant,
/// so the answer is always null.
/// </summary>
/// <remarks>
/// This is the correct answer for a self-hosted installation, where there is exactly one tenant and
/// naming it would only create a second spelling of the same thing. It is also the answer only where
/// nothing better is present: the auth server displaces it with a resolver reading the calling
/// client's own configuration entry, and a host may register one of its own. Where neither is wired,
/// tenant-level configuration keys resolve at the default level — a stated limitation for a
/// multi-tenant deployment, and the reason the port exists at all.
/// </remarks>
internal sealed class DefaultClientTenantResolver : IClientTenantResolver
{
    /// <inheritdoc />
    public ValueTask<string?> ResolveTenantAsync(string? clientId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(null);
}
