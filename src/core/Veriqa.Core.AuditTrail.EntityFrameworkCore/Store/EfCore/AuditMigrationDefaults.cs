// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuditTrail.Store.EfCore;

/// <summary>
/// Schema constants of the audit journal shared by everyone who configures its EF Core provider:
/// the host at runtime and the design-time factory of the migrations assembly.
/// </summary>
public static class AuditMigrationDefaults
{
    /// <summary>
    /// Migration history table of the journal. The journal shares its database with the transaction
    /// store, so it keeps a history of its own: a shared one would make the two sets of migrations
    /// overwrite each other's records. The runtime and the CLI tooling must name the same table —
    /// otherwise <c>dotnet ef database update</c> records the migration in one table while the
    /// application, seeing the other one empty, applies it a second time and fails on an already
    /// existing relation.
    /// </summary>
    public const string MigrationsHistoryTable = "__AuditTrailMigrationsHistory";
}
