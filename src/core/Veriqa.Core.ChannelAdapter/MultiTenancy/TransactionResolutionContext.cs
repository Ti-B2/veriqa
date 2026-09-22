// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// The single point where the ownership context of a transaction is assembled: the ambient tenant
/// (SPEC-003 CA-164 — the polling paths have neither an HTTP request nor a DI scope), the application
/// (<c>client_id</c>) and the <c>ui_config</c> record selector, all three carried by the transaction
/// itself (SPEC-012 §10.3, CFG-210).
/// <para>
/// It exists for the same reason as <see cref="ResolutionContext.ForTenant"/>: a context assembled by
/// hand at every call site is a place where one of the three dimensions is quietly forgotten, and a
/// forgotten dimension does not fail — it silently resolves the setting of somebody else's level.
/// </para>
/// </summary>
public static class TransactionResolutionContext
{
    /// <summary>
    /// Builds the ownership context of a transaction. A null transaction, or one carrying no request
    /// context at all, leaves the application and record levels unset — the transaction states neither,
    /// and the resolution then runs over the tenant and core levels alone. Which container carries the
    /// dimensions (the OIDC context or the server-to-server one) is not this method's business:
    /// the accessor of the transaction context answers that in one place.
    /// </summary>
    /// <remarks>
    /// The context is assembled by that accessor rather than here, so the transaction has exactly one
    /// reader. What this method still decides is where the tenant comes from: the ambient scope keeps
    /// priority, and the tenant carried by the transaction is the fallback for a caller running
    /// outside a scope — the sign-in and callback paths, which never had one and therefore resolved
    /// past the tenant level entirely.
    /// </remarks>
    /// <param name="transaction">Transaction of the request; null — there is none.</param>
    /// <returns>Resolution context of that transaction.</returns>
    public static ResolutionContext For(Transaction? transaction) =>
        transaction is null
            ? ResolutionContext.Of(ChannelTenantContext.CurrentTenantId, applicationId: null, uiConfigSelector: null)
            : transaction.ToResolutionContext(ChannelTenantContext.CurrentTenantId);

    /// <summary>
    /// Builds the ownership context of a request that has no transaction yet, for a caller that has
    /// already resolved the tenant of the calling client — the HTTP entry points, which run outside
    /// any ambient tenant scope and would otherwise resolve past the tenant level entirely.
    /// </summary>
    /// <remarks>
    /// The precedence is the one <see cref="For"/> applies to a transaction: the ambient scope keeps
    /// priority, and the client's tenant is the fallback. A caller that stores the same client tenant
    /// on the transaction it creates therefore gets back the very context this method returned when a
    /// later reader rebuilds it through <see cref="For"/>. Blank values are normalized to null, so a
    /// blank <c>client_id</c> never creates a phantom application level; when none of the three
    /// dimensions is set — self-hosted with no levels — the shared <see cref="ResolutionContext.Core"/>
    /// instance is returned, which keeps the self-hosted invariant "the resolver reads the core level
    /// alone" (CFG-202).
    /// </remarks>
    /// <param name="clientTenantId">Tenant of the calling client; null — the client states none.</param>
    /// <param name="applicationId">Application identifier (OIDC <c>client_id</c>).</param>
    /// <param name="uiConfigSelector"><c>ui_config</c> record selector.</param>
    /// <returns>Resolution context carrying the tenant, the application and the record selector.</returns>
    public static ResolutionContext ForClient(
        string? clientTenantId,
        string? applicationId,
        string? uiConfigSelector) =>
        ResolutionContext.Of(
            ChannelTenantContext.CurrentTenantId ?? clientTenantId,
            applicationId,
            uiConfigSelector);
}
