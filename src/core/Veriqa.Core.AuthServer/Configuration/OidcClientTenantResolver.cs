// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Shipped <see cref="IClientTenantResolver"/> of the auth server: the tenant of a client is the
/// <see cref="OidcClientOptions.TenantId"/> of the client's own configuration entry, and nothing
/// else is consulted.
/// </summary>
/// <remarks>
/// <para>
/// There is no separate tenancy section and no map of its own: the entry that already describes the
/// client is the one place where a fact about that client belongs. An entry stating no tenant, and a
/// client with no entry at all, both answer null — the single default tenant of a self-hosted
/// installation, which is the behaviour of a deployment that never heard of tenants.
/// </para>
/// <para>
/// Registration happens in <c>AddVeriqaOpenIddict</c> — the one call both entry points of the auth
/// server pass through — and displaces the engine's shipped null answer instead of queueing behind
/// it, so this resolver answers wherever the auth server is present, no matter when the Transaction
/// Engine was wired. A host that registers its own resolver — a multi-tenant one deriving the tenant
/// from a catalog, a route, or a paid contour of its own — wins over this one whatever the call
/// order, when it registers with <c>AddSingleton</c> or <c>Replace</c>; a <c>TryAdd</c> of its own
/// wins only while nothing has answered yet.
/// </para>
/// <para>
/// The lookup reads through <see cref="OidcClientsOptionsAccessor"/> rather than
/// <c>IOptions&lt;OidcClientsOptions&gt;</c>, so an edit of the section applies without a restart and
/// a broken edit degrades to the last valid snapshot instead of throwing into the request pipeline —
/// the same discipline every other reader of the clients section follows.
/// </para>
/// </remarks>
internal sealed class OidcClientTenantResolver : IClientTenantResolver
{
    /// <summary>
    /// Guarded view of the clients section.
    /// </summary>
    private readonly OidcClientsOptionsAccessor _clients;

    /// <summary>
    /// Creates the resolver.
    /// </summary>
    /// <param name="clients">Guarded view of the clients section.</param>
    public OidcClientTenantResolver(OidcClientsOptionsAccessor clients)
    {
        ArgumentNullException.ThrowIfNull(clients);

        _clients = clients;
    }

    /// <inheritdoc />
    public ValueTask<string?> ResolveTenantAsync(
        string? clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            return ValueTask.FromResult<string?>(null);
        }

        // Ordinal, as everywhere else a client_id is matched in the core: the identifier is the one
        // the client was registered under, and two spellings of it are two different clients.
        var tenant = _clients.Current.Clients
            .FirstOrDefault(entry => string.Equals(entry.ClientId, clientId, StringComparison.Ordinal))
            ?.TenantId;

        // A blank tenant is an entry that states none, not a tenant named by whitespace.
        return ValueTask.FromResult(string.IsNullOrWhiteSpace(tenant) ? null : tenant);
    }
}
