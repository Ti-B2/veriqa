// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Host;

/// <summary>
/// Durable audit sink resolved from Veriqa:Logging:Store with the per-key fallback to
/// Veriqa:TransactionEngine:Store (SPEC-012 CFG-252). The resolver returns null when the effective
/// connection string is empty, i.e. when the journal stays in memory.
/// </summary>
/// <param name="Provider">Effective relational provider of the sink.</param>
/// <param name="ConnectionString">Effective non-empty connection string of the sink.</param>
internal sealed record AuditStoreSelection(RelationalStoreProvider Provider, string ConnectionString);
