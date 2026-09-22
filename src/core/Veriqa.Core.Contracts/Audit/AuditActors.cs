// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Contracts.Audit;

/// <summary>
/// Well-known values of <see cref="AuditRecord.Actor"/> that name no concrete principal.
/// </summary>
public static class AuditActors
{
    /// <summary>
    /// Actor of a lifecycle transition initiated neither by a user nor by an application
    /// (expiry, background finalization) — and the fallback when the initiator is unknown.
    /// </summary>
    public const string System = "system";
}
