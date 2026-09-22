// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using OpenIddict.Abstractions;
using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.AuthServer.UI;
using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Events;
using Veriqa.Core.TransactionEngine.Identity;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// Authorization callback endpoint (GET /connect/authorize/callback).
/// Invoked after authentication completes in the mobile channel.
/// Creates an intermediate Cookie and redirects to /connect/authorize
/// to issue the authorization code via OpenIddict.
/// <para>
/// When the effective confirmation surface is <see cref="ConfirmationSurface.OnWebPage"/>
/// (SPEC-012 §4.4, SPEC-007 UI-038), GET does NOT sign in automatically: it returns a page asking
/// the user to confirm, with both answers guarded by an AntiForgery token. Sign-in completes only on
/// a POST to <see cref="OidcEndpoints.AuthorizeCallbackConfirm"/> ("Yes"); a POST to
/// <see cref="OidcEndpoints.AuthorizeCallbackDecline"/> ("No") declines it instead.
/// </para>
/// </summary>
public static class AuthorizeCallbackEndpoint
{
    /// <summary>
    /// Cache-Control of a response whose body carries the outcome of a sign-in attempt.
    /// </summary>
    private const string ResponseNoStore = "no-store";

    /// <summary>
    /// Pragma of that same response, for HTTP/1.0 caches that ignore Cache-Control.
    /// </summary>
    private const string ResponseNoCache = "no-cache";

    /// <summary>
    /// Registers the authorization callback endpoint (GET) and the web-confirmation endpoint (POST).
    /// </summary>
    /// <param name="endpoints">The endpoint router.</param>
    /// <param name="rateLimitPolicyName">The rate limiting policy name (null — no limiting).</param>
    /// <returns>The router for chaining.</returns>
    public static IEndpointRouteBuilder MapAuthorizeCallbackEndpoint(this IEndpointRouteBuilder endpoints, string? rateLimitPolicyName = null)
    {
        var getBuilder = endpoints.MapGet(OidcEndpoints.AuthorizeCallback, HandleCallbackAsync);
        var postBuilder = endpoints.MapPost(OidcEndpoints.AuthorizeCallbackConfirm, HandleWebConfirmAsync);
        var declineBuilder = endpoints.MapPost(OidcEndpoints.AuthorizeCallbackDecline, HandleWebDeclineAsync);
        if (!string.IsNullOrEmpty(rateLimitPolicyName))
        {
            getBuilder.RequireRateLimiting(rateLimitPolicyName);
            postBuilder.RequireRateLimiting(rateLimitPolicyName);
            declineBuilder.RequireRateLimiting(rateLimitPolicyName);
        }
        return endpoints;
    }

    /// <summary>
    /// Handles the callback after authentication via the channel completes (GET).
    /// On the OnWebPage surface it returns the confirmation page; otherwise it completes the sign-in.
    /// </summary>
    private static async Task<IResult> HandleCallbackAsync(
        HttpContext httpContext,
        ITransactionService transactionService,
        IEffectiveConfirmationSurfaceResolver surfaceResolver,
        IEnumerable<IChannelAdapter> channelAdapters,
        IAntiforgery antiforgery,
        IConfirmationPromptLocalizer promptLocalizer,
        IOptions<VeriqaOptions> veriqaOptions,
        CorePageBrandingResolver brandingResolver,
        IAuthPageLanguageRegistry languageRegistry)
    {
        // Detect the page language up front (Accept-Language + configured default), so both the
        // web-confirmation page and the friendly "session expired" page localize consistently (TASK-060).
        var language = DetectCallbackLanguage(httpContext, veriqaOptions, languageRegistry);

        // 1. Transaction validation (session_id from query, state, nonce, snapshots). A stale/expired
        //    transaction (not found / not completed) is surfaced as a friendly localized page, not a raw
        //    ProblemDetails (product-mode handling).
        var sessionIdStr = httpContext.Request.Query[OidcConstants.SessionIdParameterName].FirstOrDefault();
        var validation = await ValidateCallbackAsync(
            httpContext, sessionIdStr, transactionService, promptLocalizer, language,
            brandingResolver);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        var transaction = validation.Transaction!;
        var transactionId = validation.TransactionId!.Value;

        // 2. Effective confirmation surface (SPEC-012 §4.4.2): OnWebPage → page with a button,
        //    sign-in is deferred until an explicit POST confirmation (UI-038). None/InChannel — sign-in immediately
        //    (confirmation is either not required or already done in the channel).
        //    A transaction the core is already waiting on skips the resolution entirely: the surface
        //    decision was made when the channel event arrived, and re-deciding it here could only
        //    contradict it.
        if (transaction.IsAwaitingWebConfirmation()
            || await ResolveSurfaceAsync(
                transaction, surfaceResolver, channelAdapters, httpContext.RequestAborted)
                is ConfirmationSurface.OnWebPage)
        {
            var tokens = antiforgery.GetAndStoreTokens(httpContext);

            // The transaction carries the language the sign-in page was rendered in, so the
            // confirmation page continues in that language rather than falling back to the browser's.
            var confirmPageLanguage = AuthPageLocalization.ResolveTransactionLanguage(
                transaction,
                language,
                veriqaOptions.Value.Localization.DefaultLanguage,
                languageRegistry.SupportedLanguages);

            // The confirmation page is branded by the levels of ITS OWN transaction — the application
            // and the ui_config record the sign-in window was rendered with (SPEC-007 UI-101).
            var confirmPageBranding = await brandingResolver.ResolveAsync(
                TransactionResolutionContext.For(transaction), httpContext.RequestAborted);

            var page = WebConfirmPage.Build(
                sessionId: httpContext.Request.Query[OidcConstants.SessionIdParameterName].FirstOrDefault(),
                antiforgeryFormFieldName: tokens.FormFieldName,
                antiforgeryToken: tokens.RequestToken,
                pathBase: httpContext.Request.PathBase.Value ?? string.Empty,
                localizer: promptLocalizer,
                language: confirmPageLanguage,
                branding: confirmPageBranding);
            return Results.Content(page, "text/html", Encoding.UTF8);
        }

        // 3. None/InChannel — complete the sign-in immediately
        return await CompleteSignInAsync(httpContext, transaction, transactionId);
    }

    /// <summary>
    /// Handles the web sign-in confirmation (POST, OnWebPage mode). Validates the AntiForgery token,
    /// re-validates the transaction and completes the sign-in (SPEC-007 UI-038).
    /// </summary>
    private static async Task<IResult> HandleWebConfirmAsync(
        HttpContext httpContext,
        ITransactionService transactionService,
        IIdentityResolutionService identityResolutionService,
        IAntiforgery antiforgery,
        IConfirmationPromptLocalizer promptLocalizer,
        IOptions<VeriqaOptions> veriqaOptions,
        CorePageBrandingResolver brandingResolver,
        IAuthPageLanguageRegistry languageRegistry)
    {
        // 1. AntiForgery token validation (UI-038): missing/invalid → 400, sign-in is not performed
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                detail: "Invalid or missing confirmation token.",
                statusCode: StatusCodes.Status400BadRequest,
                title: OidcErrorCodes.OidcRequestInvalid);
        }

        // Detect the page language for a possible friendly "session expired" page (consistent with GET).
        var language = DetectCallbackLanguage(httpContext, veriqaOptions, languageRegistry);

        // 2. Transaction re-validation (session_id from the form, state, nonce, snapshots) — as in GET.
        //    A stale transaction reaped between showing the question and this POST (e.g. the user waited past
        //    its retention window) is surfaced as a friendly page instead of a raw "Transaction not found".
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var sessionIdStr = form[OidcConstants.SessionIdParameterName].FirstOrDefault();
        var validation = await ValidateCallbackAsync(
            httpContext, sessionIdStr, transactionService, promptLocalizer, language,
            brandingResolver);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        var transaction = validation.Transaction!;
        var transactionId = validation.TransactionId!.Value;

        // 3. A transaction the core was waiting on is only now finalized: the channel data has been
        //    sitting on it since the user acted in the channel, and "Yes" is what authorizes turning
        //    it into a sign-in. The sequence is the webhook path's own (confirm → resolve identity →
        //    complete, compensating a refused resolution and a failed completion alike) — one
        //    implementation, two callers.
        if (transaction.IsAwaitingWebConfirmation())
        {
            var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(AuthorizeCallbackEndpoint));

            var finalizeResult = await ChannelTransactionFinalizer.ConfirmAndCompleteAsync(
                transaction.ChannelIdentitySnapshot!,
                transactionId,
                transactionService,
                identityResolutionService,
                transaction.ConcurrencyToken,
                logger,
                httpContext.RequestAborted);

            if (finalizeResult.IsFailure)
            {
                // The finalizer compensates only what it moved: a refusal at the confirmation step
                // returns before any compensation and leaves the transaction exactly as it was —
                // still waiting for an answer on this page. So the response is chosen by the error
                // instead of assumed, and the waiting state is never left open behind it.
                // The page this may end in belongs to a transaction that WAS found and validated, so
                // it is branded by that transaction's own levels (SPEC-007 UI-101).
                var failureBranding = await brandingResolver.ResolveAsync(
                    TransactionResolutionContext.For(transaction), httpContext.RequestAborted);

                return await HandleFailedWebFinalizationAsync(
                    finalizeResult.Error.Code,
                    transaction.ConcurrencyToken,
                    transactionId,
                    transactionService,
                    promptLocalizer,
                    language,
                    failureBranding,
                    logger);
            }

            // The finalized instance is the one carrying ResolvedIdentitySnapshot and
            // CompletionSnapshot; the pre-finalization one validated above carries neither.
            transaction = finalizeResult.Value;
        }

        // 4. Complete the sign-in
        return await CompleteSignInAsync(httpContext, transaction, transactionId);
    }

    /// <summary>
    /// Answers a failed finalization of a transaction the core was waiting on, and makes sure that
    /// transaction does not keep offering the same question afterwards.
    /// </summary>
    /// <remarks>
    /// Two classes of failure arrive here. A lost race — the transaction expired, was reaped, left the
    /// waiting state, or its concurrency token moved under a repeat delivery of the channel event — is
    /// a stale session from the user's side: they get the same friendly page the validation shows,
    /// with a way to start again, and the transaction is left alone (a still-valid one is finalized by
    /// the next answer). Anything else is a downstream failure of ours: the user gets the opaque 500
    /// the claims-mapping failure returns, and the transaction is closed so an unexplained refusal
    /// cannot leave it waiting and hand the user that 500 on every further attempt. Closing it is best
    /// effort — if the token has already moved, whoever moved it decides the outcome.
    /// </remarks>
    private static async Task<IResult> HandleFailedWebFinalizationAsync(
        string errorCode,
        string concurrencyToken,
        TransactionId transactionId,
        ITransactionService transactionService,
        IConfirmationPromptLocalizer promptLocalizer,
        string language,
        CorePageBranding branding,
        ILogger logger)
    {
        // These four belong to the transaction lifecycle, and IIdentityResolutionService reserves them
        // for it: a resolver refusal travels out with its own code, so the classification below stays
        // a classification of lifecycle outcomes and not of arbitrary extension-point strings.
        // transactionId.ToString() — a Base62 string, log injection is excluded
        if (errorCode is TransactionErrorCodes.TransactionNotFound
            or TransactionErrorCodes.TransactionExpired
            or TransactionErrorCodes.InvalidStateTransition
            or TransactionErrorCodes.ConcurrencyConflict)
        {
            logger.LogWarning(
                "Web confirmation was not finalized — the session is no longer answerable here. " +
                "TransactionId: {TransactionId}, ErrorCode: {ErrorCode}",
                transactionId.ToString(),
                errorCode);

            return ExpiredSessionResult(promptLocalizer, language, branding);
        }

        logger.LogError(
            "Failed to finalize the web confirmation. TransactionId: {TransactionId}, ErrorCode: {ErrorCode}",
            transactionId.ToString(),
            errorCode);

        // CancellationToken.None — the transaction must be closed even if the client goes away.
        var failResult = await transactionService.FailTransactionAsync(
            transactionId,
            TransactionErrorCodes.DownstreamFinalizeFailed,
            concurrencyToken,
            CancellationToken.None);

        // An invalid transition here means the transaction is already terminal — the compensation
        // inside the finalizer got there first, which is exactly the outcome wanted.
        if (failResult.IsFailure
            && !string.Equals(
                failResult.Error.Code, TransactionErrorCodes.InvalidStateTransition, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Failed to close the transaction after an unsuccessful web finalization. " +
                "TransactionId: {TransactionId}, ErrorCode: {ErrorCode}",
                transactionId.ToString(),
                failResult.Error.Code);
        }

        return Results.Problem(
            detail: "Failed to complete authentication. Try again later.",
            statusCode: StatusCodes.Status500InternalServerError,
            title: OidcErrorCodes.ClaimsMappingFailed);
    }

    /// <summary>
    /// Handles the "No" answer on the web confirmation page (POST, OnWebPage mode). Validates the
    /// AntiForgery token, re-validates the transaction, records the refusal and returns the standard
    /// OAuth 2.0 <c>access_denied</c> error to the client.
    /// </summary>
    private static async Task<IResult> HandleWebDeclineAsync(
        HttpContext httpContext,
        ITransactionService transactionService,
        IAntiforgery antiforgery,
        IConfirmationPromptLocalizer promptLocalizer,
        IOptions<VeriqaOptions> veriqaOptions,
        CorePageBrandingResolver brandingResolver,
        IAuthPageLanguageRegistry languageRegistry)
    {
        // 1. AntiForgery (CFG-045): declining changes state too, so it is guarded exactly like the confirm
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                detail: "Invalid or missing confirmation token.",
                statusCode: StatusCodes.Status400BadRequest,
                title: OidcErrorCodes.OidcRequestInvalid);
        }

        var language = DetectCallbackLanguage(httpContext, veriqaOptions, languageRegistry);

        // 2. Transaction re-validation — the same path as the confirm POST
        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var sessionIdStr = form[OidcConstants.SessionIdParameterName].FirstOrDefault();
        var validation = await ValidateCallbackAsync(
            httpContext, sessionIdStr, transactionService, promptLocalizer, language,
            brandingResolver);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        var transaction = validation.Transaction!;
        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AuthorizeCallbackEndpoint));

        // 3. Record the refusal under the same reason code the in-channel decline uses
        //    (ChannelWebhookPipeline.HandleAuthDeclineAsync): one meaning, one code, one audit trail.
        //    CancellationToken.None — the refusal must be recorded even if the client goes away.
        //    The transaction is left in its terminal Failed state (not deleted), so a status
        //    consumer still polling learns the outcome; retention reaps it afterwards.
        var failResult = await transactionService.FailTransactionAsync(
            validation.TransactionId!.Value,
            TransactionErrorCodes.DeclinedByUser,
            transaction.ConcurrencyToken,
            CancellationToken.None);

        if (failResult.IsFailure)
        {
            // The user's answer still stands — we do not sign them in either way, so the refusal is
            // reported to the client regardless. transactionId.ToString() is Base62 — no log injection.
            logger.LogWarning(
                "Failed to move the declined transaction to Failed. TransactionId: {TransactionId}, ErrorCode: {ErrorCode}",
                validation.TransactionId!.Value.ToString(),
                failResult.Error.Code);
        }

        // 4. Delete the browser nonce cookie (Path matches the one set in AuthorizeEndpoint):
        //    this sign-in attempt is over regardless of the answer.
        httpContext.Response.Cookies.Delete(OidcConstants.BrowserNonceCookieName, new CookieOptions
        {
            Path = $"{httpContext.Request.PathBase}{OidcEndpoints.AuthorizeCallback}"
        });

        // 5. Return the refusal to the client, in the response mode the request asked for. This path
        //    assembles the response by hand — it never reaches OpenIddict — so the mode has to be
        //    honoured here as well: a client that reads ONLY a POST would otherwise not see the
        //    refusal either, and would show a failed sign-in instead of a declined one.
        var oidcContext = transaction.OidcContext!;
        if (string.IsNullOrEmpty(oidcContext.RedirectUri))
        {
            // Defensive: an accepted authorize request always carries a redirect_uri, so there is
            // nowhere to send the user only if the stored context is corrupt.
            return Results.Problem(
                detail: "Sign-in was declined, but the client redirect URI is unavailable.",
                statusCode: StatusCodes.Status400BadRequest,
                title: OidcErrorCodes.OidcRequestInvalid);
        }

        if (string.Equals(
                oidcContext.ResponseMode,
                OpenIddictConstants.ResponseModes.FormPost,
                StringComparison.Ordinal))
        {
            return await AccessDeniedFormPostResultAsync(
                httpContext, transaction, oidcContext, promptLocalizer, language, veriqaOptions,
                brandingResolver, languageRegistry);
        }

        var useFragment = string.Equals(
            oidcContext.ResponseMode,
            OpenIddictConstants.ResponseModes.Fragment,
            StringComparison.Ordinal);

        return Results.Redirect(BuildAccessDeniedRedirectUrl(oidcContext, useFragment));
    }

    /// <summary>
    /// Returns the refusal as the auto-posting page of the <c>form_post</c> response mode: the same
    /// <c>error</c> and <c>state</c> parameters, delivered in a POST body instead of a URL.
    /// </summary>
    /// <remarks>
    /// Unlike the code-issuing path, the transaction is still here — <c>HandleWebDeclineAsync</c> leaves
    /// it in its terminal Failed state rather than deleting it — so the page speaks the language of the
    /// sign-in session and is branded by that transaction's own levels (SPEC-007 UI-101), not by the
    /// browser's header and the application level alone.
    /// </remarks>
    /// <param name="httpContext">HTTP context of the declining POST.</param>
    /// <param name="transaction">The declined transaction.</param>
    /// <param name="oidcContext">Its stored OIDC request context.</param>
    /// <param name="promptLocalizer">Locale-file localizer.</param>
    /// <param name="language">Language detected from the request headers (the fallback of the chain).</param>
    /// <param name="veriqaOptions">Veriqa options (the configured default language).</param>
    /// <param name="brandingResolver">Resolver of the service-page branding.</param>
    /// <param name="languageRegistry">Registry of supported page languages.</param>
    /// <returns>An HTML content result carrying the refusal.</returns>
    private static async Task<IResult> AccessDeniedFormPostResultAsync(
        HttpContext httpContext,
        Transaction transaction,
        OidcContext oidcContext,
        IConfirmationPromptLocalizer promptLocalizer,
        string language,
        IOptions<VeriqaOptions> veriqaOptions,
        CorePageBrandingResolver brandingResolver,
        IAuthPageLanguageRegistry languageRegistry)
    {
        var pageLanguage = AuthPageLocalization.ResolveTransactionLanguage(
            transaction,
            language,
            veriqaOptions.Value.Localization.DefaultLanguage,
            languageRegistry.SupportedLanguages);

        var branding = await brandingResolver.ResolveAsync(
            TransactionResolutionContext.For(transaction), httpContext.RequestAborted);

        // The CSP source of THIS response only — the target the page posts to, which is the
        // redirect_uri OpenIddict validated when it accepted the authorize request (SPEC-007 UI-091).
        var formActionSource = SecurityHeadersMiddleware.GetFormActionSource(oidcContext.RedirectUri);
        if (!string.IsNullOrEmpty(formActionSource))
        {
            httpContext.Items[SecurityHeaderConstants.FormActionTargetItemKey] = formActionSource;
        }

        var html = FormPostResponsePage.Build(
            action: oidcContext.RedirectUri!,
            fields: BuildAccessDeniedParameters(oidcContext),
            cspNonce: httpContext.Items[SecurityHeaderConstants.CspNonceItemKey] as string,
            localizer: promptLocalizer,
            language: pageLanguage,
            branding: branding);

        // The body carries the outcome of a sign-in attempt — it is not cacheable, exactly as the
        // code-issuing form_post response is not.
        httpContext.Response.Headers[HeaderNames.CacheControl] = ResponseNoStore;
        httpContext.Response.Headers[HeaderNames.Pragma] = ResponseNoCache;

        return Results.Content(html, "text/html", Encoding.UTF8);
    }

    /// <summary>
    /// Builds the standard denial response URL for the client (OAuth 2.0 RFC 6749 §4.1.2.1,
    /// OIDC Core §3.1.2.6): <c>access_denied</c> plus the original <c>state</c> on the client's
    /// redirect_uri, so the RP can tell a deliberate refusal from a failed sign-in.
    /// </summary>
    /// <remarks>
    /// The redirect_uri is the one OpenIddict already validated against the client registration when it
    /// accepted the authorize request (<c>GetOpenIddictServerRequest</c> in AuthorizeEndpoint) and stored
    /// on the transaction — not a URI taken from the current request, so this is not an open redirect.
    /// </remarks>
    /// <param name="oidcContext">The OIDC request context from the transaction (redirect_uri set).</param>
    /// <param name="useFragment">Whether the request asked for the <c>fragment</c> response mode: the
    /// same parameters then go after <c>#</c> instead of into the query string.</param>
    /// <returns>The absolute redirect URL.</returns>
    private static string BuildAccessDeniedRedirectUrl(OidcContext oidcContext, bool useFragment)
    {
        var parameters = string.Join(
            '&',
            BuildAccessDeniedParameters(oidcContext)
                .Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

        // A registered redirect_uri may already carry a query string — keep it intact. The fragment
        // mode appends to the URI as a whole, so that query string survives untouched as well.
        var separator = useFragment
            ? '#'
            : oidcContext.RedirectUri!.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        return $"{oidcContext.RedirectUri}{separator}{parameters}";
    }

    /// <summary>
    /// The parameters of the refusal itself, independent of how they are delivered: <c>access_denied</c>
    /// and the original <c>state</c>, in that order. Both the redirect and the auto-posting page state
    /// exactly these, so the two ways out cannot drift apart.
    /// </summary>
    /// <param name="oidcContext">The OIDC request context from the transaction.</param>
    /// <returns>Parameter name/value pairs of the refusal (values unescaped).</returns>
    private static IReadOnlyList<KeyValuePair<string, string>> BuildAccessDeniedParameters(
        OidcContext oidcContext)
    {
        var parameters = new List<KeyValuePair<string, string>>(2)
        {
            new(OpenIddictConstants.Parameters.Error, OpenIddictConstants.Errors.AccessDenied)
        };

        if (!string.IsNullOrEmpty(oidcContext.State))
        {
            parameters.Add(new(OpenIddictConstants.Parameters.State, oidcContext.State));
        }

        return parameters;
    }

    /// <summary>
    /// Resolves the effective confirmation surface for the transaction by its channel (CompletionSnapshot).
    /// When the channel adapter is absent — the safe default is the core's own surface (explicit
    /// confirmation, not a silent sign-in).
    /// </summary>
    private static async ValueTask<ConfirmationSurface> ResolveSurfaceAsync(
        Transaction transaction,
        IEffectiveConfirmationSurfaceResolver surfaceResolver,
        IEnumerable<IChannelAdapter> channelAdapters,
        CancellationToken cancellationToken)
    {
        var channelType = transaction.CompletionSnapshot?.ChannelType
            ?? transaction.ChannelIdentitySnapshot?.ChannelType;

        var adapter = channelType is null
            ? null
            : channelAdapters.FirstOrDefault(a => string.Equals(a.ChannelType, channelType, StringComparison.Ordinal));

        // The channel is undetermined/the adapter is unavailable — do not guess a silent sign-in:
        // require explicit confirmation on the core's own surface.
        if (adapter is null)
        {
            return ConfirmationSurfacePolicy.CoreDefault;
        }

        return await surfaceResolver.ResolveEffectiveSurfaceAsync(transaction, adapter, cancellationToken);
    }

    /// <summary>
    /// Shared callback validation: session_id, transaction presence/state, browser nonce, snapshots.
    /// Returns either the transaction or a ready error response.
    /// </summary>
    /// <remarks>
    /// Two states are valid here. <c>Completed</c> — the channel path already finalized the sign-in,
    /// so the completion snapshots must be present. "Awaiting a web answer" — the core accepted the
    /// channel event and holds the transaction Pending until the user answers on this very page; it
    /// carries no completion snapshots yet, and the snapshot check does not apply to it. The browser
    /// nonce check is identical for both.
    /// </remarks>
    private static async Task<(Transaction? Transaction, TransactionId? TransactionId, IResult? Error)> ValidateCallbackAsync(
        HttpContext httpContext,
        string? sessionIdStr,
        ITransactionService transactionService,
        IConfirmationPromptLocalizer promptLocalizer,
        string language,
        CorePageBrandingResolver brandingResolver)
    {
        // 1. session_id validation (source — query for GET, form for the POST confirmation)
        var transactionId = SessionIdMapper.ToTransactionId(sessionIdStr);
        if (transactionId is null)
        {
            return (null, null, Results.Problem(
                detail: "Invalid or missing session_id.",
                statusCode: StatusCodes.Status400BadRequest,
                title: OidcErrorCodes.OidcRequestInvalid));
        }

        // 2. Retrieve the transaction. A missing transaction is not an attack/malformed request but a
        //    natural stale state (it was reaped after its retention window, or the callback link is old):
        //    show a friendly localized "session expired" page instead of a raw ProblemDetails so product
        //    users get a clear next step ("start again") rather than a bare core error.
        var txResult = await transactionService.GetTransactionAsync(
            transactionId.Value,
            httpContext.RequestAborted);

        if (txResult.IsFailure)
        {
            // The transaction was NOT found: there is no context to resolve against — no tenant, no
            // application, no record — so the page is rendered from the core level alone. That is the
            // norm for this branch, not a fallback (SPEC-007 UI-090).
            var globalBranding = await brandingResolver.ResolveAsync(
                ResolutionContext.Core, httpContext.RequestAborted);

            return (null, null, ExpiredSessionResult(promptLocalizer, language, globalBranding));
        }

        var transaction = txResult.Value;

        // 3. State check — either the sign-in is already finalized by the channel path, or the core is
        //    waiting for the answer on this page. Anything else (it expired before the channel
        //    confirmed, it was already consumed, or the callback was opened without any channel action
        //    at all) is a stale session from the user's point of view — same friendly page as the
        //    not-found case.
        var awaitingWebConfirmation = transaction.IsAwaitingWebConfirmation();
        if (transaction.State is not TransactionState.Completed && !awaitingWebConfirmation)
        {
            // The transaction WAS found — expired or no longer answerable, but it still names its
            // application and its ui_config record, so this page is branded like any other page of
            // that sign-in (SPEC-007 UI-101; UI-090 covers only the not-found case above).
            var branding = await brandingResolver.ResolveAsync(
                TransactionResolutionContext.For(transaction), httpContext.RequestAborted);

            return (null, null, ExpiredSessionResult(promptLocalizer, language, branding));
        }

        // 3a. A transaction with no OIDC context is not a subject of the browser callback AT ALL: it
        //     was created server-to-server (SPEC-039 C19), it is finalized on the server, and the
        //     relying party learns its outcome through its own surfaces. Whoever opened this URL holds
        //     the entry material of that transaction — the identifier is visible in the page URL and
        //     encoded in the QR — and without this guard the request would fall into the browser-nonce
        //     branch below, which answers a transaction that never had a nonce as if a nonce had
        //     failed to match, and records that as a security occurrence.
        //     The answer is the same "session expired" page the two refusals above give, and the
        //     transaction is left exactly as it is. The check sits AFTER the state check and BEFORE
        //     the nonce one, and it does not silence the sign-in path: a login transaction always
        //     carries an OIDC context, so a missing or foreign nonce cookie on one still refuses
        //     below (SPEC-002 §4.3, §10.7).
        //     This is the server-side layer of the requirement; the renderer's no-navigation mode is
        //     the first one, and it holds only for the shipped renderer.
        if (transaction.OidcContext is null)
        {
            var serverSideBranding = await brandingResolver.ResolveAsync(
                TransactionResolutionContext.For(transaction), httpContext.RequestAborted);

            return (null, null, ExpiredSessionResult(promptLocalizer, language, serverSideBranding));
        }

        // 4. Browser nonce check
        var browserNonce = httpContext.Request.Cookies[OidcConstants.BrowserNonceCookieName];
        if (string.IsNullOrEmpty(browserNonce) ||
            transaction.OidcContext is null ||
            !string.Equals(browserNonce, transaction.OidcContext.BrowserNonce, StringComparison.Ordinal))
        {
            // A mismatch is refused, not destructive (SPEC-002 §10.7): the transaction is left exactly
            // as it was and lives to its TTL, so a caller who only knows the session_id cannot end
            // someone else's sign-in without authenticating. The owner's browser, arriving with the
            // right nonce afterwards, still completes. The refusal is a 400 carrying the
            // browser_nonce_mismatch code; the occurrence is always logged as a Warning and, where the
            // audit component is configured, also publishes an audit event on the transaction bus.
            // The response shape still differs from the two refusals above ("not found" and "wrong
            // state" answer with the "session expired" HTML page, this one with ProblemDetails), so a
            // caller who knows the session_id can tell a live transaction from a missing one. That is
            // decided and accepted, not an oversight: the machine code has to live in a problem+json
            // body, and equalizing the shapes would cost either that code or the branding of the
            // expired page (SPEC-002 §10.7 p. 2 — the refusal must not disclose the transaction or
            // prove ownership of it, which it does not).
            await RecordBrowserNonceMismatchAsync(httpContext, transaction, transactionId.Value);

            return (null, null, Results.Problem(
                detail: "Browser nonce does not match.",
                statusCode: StatusCodes.Status400BadRequest,
                title: OidcErrorCodes.BrowserNonceMismatch));
        }

        // 5. Snapshot presence check — only for an already finalized transaction. The waiting one has
        //    not been through the finalization yet; its snapshots appear when the user answers "Yes".
        if (!awaitingWebConfirmation
            && (transaction.ResolvedIdentitySnapshot is null || transaction.CompletionSnapshot is null))
        {
            return (null, null, Results.Problem(
                detail: "Missing data for claims mapping.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: OidcErrorCodes.ClaimsMappingFailed));
        }

        return (transaction, transactionId, null);
    }

    /// <summary>
    /// Records a browser nonce mismatch: a <c>Warning</c> log entry and an audit event on the
    /// transaction bus (SPEC-002 §10.7 p. 3, §13.2). Since the mismatch no longer destroys the
    /// transaction, this record is the only trace the occurrence leaves.
    /// </summary>
    /// <remarks>
    /// Neither the nonce value nor any other security token is written (SPEC-002 §13.3): the record
    /// carries the fact and the transaction identifier, and the identifier is the parsed Base62 value,
    /// not the raw request parameter — so no caller-controlled text reaches the log.
    /// <para>
    /// The event goes out through <see cref="ITransactionEventPublisher"/> rather than straight into
    /// the audit sink: the publisher is registered by the transaction engine itself, so it resolves on
    /// every host that has transactions at all, while the sink exists only where the audit component
    /// was added. A host without it simply has no subscriber for the event, and the sign-in works as
    /// before.
    /// </para>
    /// </remarks>
    /// <param name="httpContext">Current request — the source of the services and of the log.</param>
    /// <param name="transaction">Transaction the callback was opened for.</param>
    /// <param name="transactionId">Identifier of that transaction.</param>
    private static async Task RecordBrowserNonceMismatchAsync(
        HttpContext httpContext,
        Transaction transaction,
        TransactionId transactionId)
    {
        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AuthorizeCallbackEndpoint));

        // transactionId.ToString() — a Base62 string, log injection is excluded
        logger.LogWarning(
            "Browser nonce does not match — the callback was opened by a browser other than the one " +
            "that started the sign-in; the transaction is kept. TransactionId: {TransactionId}",
            transactionId.ToString());

        // The audit event states the channel of the transaction. Both states admitted above carry a
        // channel snapshot, so an empty type here means an unexpected shape: the event is then not
        // published at all rather than naming an invented channel — the Warning above still stands.
        var channelType = transaction.ChannelIdentitySnapshot?.ChannelType
            ?? transaction.RequestedChannelType;
        if (string.IsNullOrEmpty(channelType))
        {
            return;
        }

        try
        {
            var timeProvider = httpContext.RequestServices.GetRequiredService<TimeProvider>();
            var eventPublisher = httpContext.RequestServices.GetRequiredService<ITransactionEventPublisher>();

            // CancellationToken.None — the occurrence must reach the journal even if the caller drops
            // the connection; otherwise aborting the request would suppress its own trace.
            // No attribution is attached: the caller here is unauthenticated by definition, and the
            // identity the transaction carries belongs to its owner, who did not act.
            await eventPublisher.PublishAsync(
                new TransactionChannelAuditEvent
                {
                    TransactionId = transactionId,
                    OccurredAt = timeProvider.GetUtcNow(),
                    ChannelType = channelType,
                    EventCode = OidcErrorCodes.BrowserNonceMismatch,
                    Details = null
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // An unavailable journal does not turn into a denial of the sign-in service: the caller
            // gets its 400 either way.
            logger.LogWarning(ex,
                "Failed to publish audit event {EventCode}. TransactionId: {TransactionId}",
                OidcErrorCodes.BrowserNonceMismatch,
                transactionId.ToString());
        }
    }

    /// <summary>
    /// Completes the browser side of the sign-in: signs in the intermediate cookie, clears the nonce and
    /// builds the redirect to /connect/authorize. A shared point for GET (None/InChannel) and POST (OnWebPage).
    /// </summary>
    /// <remarks>
    /// The cookie carries a reference to the transaction and no claims (SPEC-002 §4.3): the claims are
    /// built from the transaction by the authorization endpoint when it issues the code, and the
    /// transaction is deleted there. A claim set in the cookie would grow with every claim a channel
    /// reports — an avatar image alone exceeds the header limits of common proxies and servers — while
    /// the reference keeps the cookie at a fixed size. The transaction store is already shared state
    /// the callback cannot work without, so the reference adds no new requirement to a deployment.
    /// </remarks>
    private static async Task<IResult> CompleteSignInAsync(
        HttpContext httpContext,
        Transaction transaction,
        TransactionId transactionId)
    {
        // 6. Cookie authentication carrying only the session identifier of the transaction. The value
        //    is protected by the cookie handler, so it can be neither read nor forged by the browser.
        var identity = new ClaimsIdentity(
            [new Claim(OidcConstants.SessionIdParameterName, SessionIdMapper.ToSessionId(transactionId))],
            OidcConstants.CookieAuthScheme);

        await httpContext.SignInAsync(OidcConstants.CookieAuthScheme, new ClaimsPrincipal(identity));

        // 7. Delete the browser nonce cookie (Path matches the one set in AuthorizeEndpoint —
        // including PathBase, otherwise the browser will not match the cookie for deletion)
        httpContext.Response.Cookies.Delete(OidcConstants.BrowserNonceCookieName, new CookieOptions
        {
            Path = $"{httpContext.Request.PathBase}{OidcEndpoints.AuthorizeCallback}"
        });

        // 8. Build the redirect URL to /connect/authorize with the original OIDC parameters.
        // PathBase is required: Location is returned to the browser as-is, and without the prefix behind
        // a sub-path (veriqa.app/demo) the redirect would go past the application.
        var redirectUrl = $"{httpContext.Request.PathBase}{BuildAuthorizeRedirectUrl(transaction.OidcContext!)}";

        return Results.Redirect(redirectUrl);
    }

    /// <summary>
    /// Builds the friendly "sign-in session expired" HTML response (400) for a stale/expired transaction,
    /// replacing a raw ProblemDetails so the product user sees a clear message instead of a bare core error.
    /// 400 is kept (the request references a no-longer-valid session), but the body is a user-facing page.
    /// </summary>
    /// <param name="promptLocalizer">Locale-file localizer.</param>
    /// <param name="language">Detected page language.</param>
    /// <param name="branding">Effective branding of the generated page, already resolved by the
    /// caller over the context that page actually has (SPEC-007 UI-101/UI-090).</param>
    /// <returns>An HTML content result with status 400.</returns>
    private static IResult ExpiredSessionResult(
        IConfirmationPromptLocalizer promptLocalizer,
        string language,
        CorePageBranding branding)
    {
        var html = CallbackExpiredPage.Build(promptLocalizer, language, branding);
        return Results.Content(html, "text/html", Encoding.UTF8, StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Detects the callback page language the same way as the sign-in window (Accept-Language + the
    /// configured default, clamped to the supported-language registry), so the web-confirmation and
    /// expired-session pages localize consistently (TASK-060).
    /// </summary>
    private static string DetectCallbackLanguage(
        HttpContext httpContext,
        IOptions<VeriqaOptions> veriqaOptions,
        IAuthPageLanguageRegistry languageRegistry)
    {
        var acceptLanguage = httpContext.Request.Headers.AcceptLanguage.ToString();
        return AuthPageStrings.DetectLanguage(
            acceptLanguage,
            veriqaOptions.Value.Localization.DefaultLanguage,
            languageRegistry.SupportedLanguages);
    }

    /// <summary>
    /// Builds the redirect URL to authorize with the original OIDC parameters.
    /// </summary>
    /// <param name="oidcContext">The OIDC request context from the transaction.</param>
    /// <returns>The redirect URL.</returns>
    private static string BuildAuthorizeRedirectUrl(OidcContext oidcContext)
    {
        // The method builds the query string from the original OIDC parameters
        var queryParams = new Dictionary<string, string?>
        {
            [OpenIddictConstants.Parameters.ResponseType] = oidcContext.ResponseType,
            [OpenIddictConstants.Parameters.ResponseMode] = oidcContext.ResponseMode,
            [OpenIddictConstants.Parameters.ClientId] = oidcContext.ClientId,
            [OpenIddictConstants.Parameters.RedirectUri] = oidcContext.RedirectUri,
            [OpenIddictConstants.Parameters.Scope] = oidcContext.Scope,
            [OpenIddictConstants.Parameters.State] = oidcContext.State,
            [OpenIddictConstants.Parameters.Nonce] = oidcContext.Nonce,
            [OpenIddictConstants.Parameters.CodeChallenge] = oidcContext.CodeChallenge,
            [OpenIddictConstants.Parameters.CodeChallengeMethod] = oidcContext.CodeChallengeMethod
        };

        var queryString = string.Join("&",
            queryParams
                .Where(kvp => kvp.Value is not null)
                .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value!)}"));

        return $"{OidcEndpoints.Authorize}?{queryString}";
    }
}
