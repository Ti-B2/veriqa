// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// OIDC endpoint paths of the authorization server.
/// </summary>
public static class OidcEndpoints
{
    /// <summary>
    /// Authorization path (Authorization Endpoint).
    /// </summary>
    public const string Authorize = "/connect/authorize";

    /// <summary>
    /// Authorization callback path (callback after the transaction completes).
    /// </summary>
    public const string AuthorizeCallback = "/connect/authorize/callback";

    /// <summary>
    /// Login confirmation path on the web page (POST), LoginConfirmationMode.OnWebPage mode
    /// (SPEC-012 §4.4, SPEC-007 UI-038). The "Yes" button with an AntiForgery token completes the login.
    /// </summary>
    public const string AuthorizeCallbackConfirm = "/connect/authorize/callback/confirm";

    /// <summary>
    /// Login decline path on the web page (POST), the "No" answer to the same confirmation question
    /// (LoginConfirmationMode.OnWebPage). Fails the transaction as declined by the user and returns the
    /// standard OAuth 2.0 <c>access_denied</c> error to the client.
    /// </summary>
    public const string AuthorizeCallbackDecline = "/connect/authorize/callback/decline";

    /// <summary>
    /// Token exchange path (Token Endpoint).
    /// </summary>
    public const string Token = "/connect/token";

    /// <summary>
    /// User information path (UserInfo Endpoint).
    /// </summary>
    public const string UserInfo = "/connect/userinfo";

    /// <summary>
    /// Token revocation path (Revocation Endpoint, RFC 7009).
    /// </summary>
    public const string Revoke = "/connect/revoke";

    /// <summary>
    /// Token introspection path (Introspection Endpoint, RFC 7662). The response is formed by
    /// OpenIddict itself and carries token metadata only; claims are read from the userinfo endpoint.
    /// </summary>
    public const string Introspect = "/connect/introspect";

    /// <summary>
    /// Base path for the transaction status polling endpoint (SPEC-007 §5.4).
    /// Full path: /api/transaction/{id}/status
    /// </summary>
    public const string TransactionStatusBase = "/api/transaction";

    /// <summary>
    /// Server-to-server creation of a confirmation transaction (SPEC-039 C14). Same base path as the
    /// status surface, because it addresses the same resource — a transaction.
    /// </summary>
    public const string ConfirmationTransaction = TransactionStatusBase + "/confirmation";

    /// <summary>
    /// Channel-agnostic entry page of an ALREADY CREATED confirmation transaction (SPEC-039 C15),
    /// addressed by its public identifier. The <c>/auth/…</c> base is the one the user-facing pages of
    /// the contour already live under; the page is not part of the OIDC protocol surface, which is why
    /// it is not under <c>/connect/…</c>.
    /// </summary>
    public const string TransactionEntryPage = "/auth/transaction";

    /// <summary>
    /// Page of the core that ASKS the subject of a confirmation transaction and takes the user's
    /// answer (SPEC-039 R37). It sits under the entry page it continues, and outside
    /// <c>/connect/…</c> for the same reason: a transaction created server-to-server has no OIDC
    /// request behind it.
    /// </summary>
    public const string TransactionConfirmPage = TransactionEntryPage + "/confirm";

    /// <summary>
    /// The "Yes" answer to the question of <see cref="TransactionConfirmPage"/> (POST): finalizes the
    /// transaction on the server. Carries the AntiForgery token and the session_id.
    /// </summary>
    public const string TransactionConfirmAccept = TransactionConfirmPage + "/accept";

    /// <summary>
    /// The "No" answer to the same question (POST): records the refusal. Guarded exactly like the
    /// confirmation — declining changes state too.
    /// </summary>
    public const string TransactionConfirmDecline = TransactionConfirmPage + "/decline";
}
