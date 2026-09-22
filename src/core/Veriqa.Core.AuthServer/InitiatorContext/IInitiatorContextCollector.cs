// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.InitiatorContext;

/// <summary>
/// Collector of the transaction initiator context (SPEC-017 §5).
/// Called once at transaction creation — in the Authorization Endpoint,
/// where the HttpContext of the initiating browser is available.
/// </summary>
public interface IInitiatorContextCollector
{
    /// <summary>
    /// Collects the initiator context from the HTTP request (best-effort, ICC-011):
    /// unavailability of any field does not block transaction creation.
    /// </summary>
    /// <param name="httpContext">HTTP context of the initiator's request.</param>
    /// <param name="clientId">OIDC client identifier (source of the application name).</param>
    /// <returns>Snapshot of the initiator context, or null (collection disabled or context not formed).</returns>
    InitiatorContextSnapshot? Collect(HttpContext httpContext, string? clientId);
}
