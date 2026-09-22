// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Host;

/// <summary>
/// OpenIddict store choice spoken in Veriqa:OpenIddict:Database:Provider (SPEC-012 CFG-117, CFG-118):
/// either the volatile in-memory store or a relational provider from the host table. An unspoken choice
/// has no instance at all — the resolver returns null for it.
/// </summary>
/// <param name="RelationalProvider">Relational provider, or null for the in-memory store.</param>
internal sealed record OpenIddictStoreSelection(RelationalStoreProvider? RelationalProvider)
{
    /// <summary>
    /// The in-memory store was selected.
    /// </summary>
    public static OpenIddictStoreSelection InMemory { get; } = new(RelationalProvider: null);

    /// <summary>
    /// Whether the in-memory store was selected.
    /// </summary>
    public bool IsInMemory => RelationalProvider is null;
}
