// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.MultiTenancy;

namespace Veriqa.Core.AuthServer.Configuration.Resolution;

/// <summary>
/// Outcome of picking the <c>ui_config</c> record of a request.
/// </summary>
/// <param name="Selector">Effective record selector; null — render with the global defaults.</param>
/// <param name="Invalid">The request explicitly named a record the application may not use — the
/// caller answers with <c>ui_config_invalid</c>.</param>
internal readonly record struct UiConfigSelection(string? Selector, bool Invalid);

/// <summary>
/// The single home of the <c>ui_config</c> selection rules (SPEC-012 CFG-203, CFG-231): which record
/// a request ends up with, and when naming one is an error.
/// <para>
/// Validity of a selector is "the record exists and is assigned to the application" — which is exactly
/// what the presence of the <c>ui_config</c> LEVEL of the resolver means, so the check needs no second
/// path to the record store. The three rules are:
/// </para>
/// <list type="bullet">
/// <item>the parameter is stated but the level stays unset → invalid (a strict check);</item>
/// <item>the parameter is not stated → the client's <c>DefaultUiConfig</c> is tried, and a default
/// that does not resolve degrades to the global defaults rather than failing the request;</item>
/// <item>neither → the global defaults.</item>
/// </list>
/// <para>
/// Both entry points that create a transaction ask here — the browser authorization endpoint and the
/// server-to-server confirmation entry (SPEC-039 C14/L20). A copy of these rules on the second entry
/// would be a second home for them, and the two would drift the first time one of them is amended.
/// The rules stay free of HTTP: what an invalid selector looks like on the wire is the endpoint's
/// business, and the two endpoints answer in their own shapes.
/// </para>
/// </summary>
internal static class UiConfigSelectorResolver
{
    /// <summary>
    /// What a caller tells the relying party when the selector is refused. Deliberately free of any
    /// detail about the catalog: separate wordings would let a client discover its contents by
    /// probing (SPEC-039 C17).
    /// </summary>
    public const string InvalidSelectorMessage = "Invalid or not allowed UI configuration code.";

    /// <summary>
    /// Determines the effective <c>ui_config</c> selector of a request.
    /// </summary>
    /// <param name="tenantId">Tenant of the calling client, as the entry point resolved it; null — none
    /// is stated. A <c>ui_config</c> record is an artifact of a tenant (CFG-203), so the level is asked
    /// in that tenant's context: without it a store that scopes records by tenant answers for none.</param>
    /// <param name="clientId">Application the request is attributed to (<c>client_id</c>).</param>
    /// <param name="requestedSelector">Value the request states for the parameter; null — the request
    /// does not state it at all. An empty or blank value is a stated one, and an invalid one.</param>
    /// <param name="clientsSnapshot">OIDC clients snapshot served by the guarded accessor.</param>
    /// <param name="pageSettingsResolver">Resolver of the level-owned page settings — the one reader
    /// behind the <c>ui_config</c> level.</param>
    /// <param name="logger">Logger of the selection outcome.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The selection outcome.</returns>
    public static async Task<UiConfigSelection> ResolveAsync(
        string? tenantId,
        string? clientId,
        string? requestedSelector,
        OidcClientsOptions clientsSnapshot,
        AuthPageSettingsResolver pageSettingsResolver,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // The method picks the ui_config selector of the request and validates it against the level

        var explicitlyRequested = requestedSelector is not null;

        // The parameter is explicitly passed but empty/whitespace — invalid
        if (explicitlyRequested && string.IsNullOrWhiteSpace(requestedSelector))
        {
            return new UiConfigSelection(null, Invalid: true);
        }

        // Determine the selector: an explicit parameter takes priority, otherwise — the client's DefaultUiConfig
        var clientConfig = clientsSnapshot.Clients
            .FirstOrDefault(c => string.Equals(c.ClientId, clientId, StringComparison.Ordinal));

        var selector = explicitlyRequested ? requestedSelector : clientConfig?.DefaultUiConfig;

        // No selector (neither parameter nor default) — render with global defaults
        if (string.IsNullOrWhiteSpace(selector))
        {
            return new UiConfigSelection(null, Invalid: false);
        }

        // Ask the ui_config level about the candidate selector, in the ownership context of this request —
        // the tenant included, since the record is looked up within the (tenant, application) scope.
        var candidate = TransactionResolutionContext.ForClient(tenantId, clientId, selector);
        var assigned = await pageSettingsResolver.IsSelectorAssignedAsync(candidate, cancellationToken);

        // Audit of the ui_config selection: selector + applicationId + outcome.
        // The record contents are NOT logged (only the outcome). client_id/selector come from the client's allowlist.
        logger.LogInformation(
            "ui_config resolve: selector={Selector}, applicationId={ApplicationId}, result={Result}",
            selector,
            clientId,
            assigned ? "assigned" : "not_assigned");

        if (assigned)
        {
            return new UiConfigSelection(selector, Invalid: false);
        }

        // Explicitly requested but not allowed code — strict refusal.
        // DefaultUiConfig that does not resolve — render with global defaults.
        return new UiConfigSelection(null, Invalid: explicitlyRequested);
    }
}
