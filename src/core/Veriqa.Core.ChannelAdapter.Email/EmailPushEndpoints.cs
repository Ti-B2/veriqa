// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.Email.Abstractions;
using Veriqa.Core.ChannelAdapter.Email.Configuration;
using Veriqa.Core.ChannelAdapter.Email.Constants;
using Veriqa.Core.ChannelAdapter.Email.Domain;
using Veriqa.Core.ChannelAdapter.Email.Enums;
using Veriqa.Core.ChannelAdapter.Email.Services;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Events;
using Veriqa.Core.TransactionEngine.Identity;
using Veriqa.Core.TransactionEngine.MessageTemplates;
using Veriqa.Core.TransactionEngine.Services;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.ChannelAdapter.Email;

/// <summary>
/// Email Push mode endpoints (SPEC-016 §5): the compose-helper page and the inbound webhook.
/// Push mode is enabled only when <see cref="EmailOptions.PushEnabled"/> = true and requires
/// an external inbound provider. At the code level, the contract and orchestration are implemented.
/// </summary>
public static partial class EmailAuthEndpoints
{
    /// <summary>
    /// Renders the Push mode compose-helper page.
    /// When <c>session_id</c> is passed, mints a new correlation token for the transaction;
    /// when <c>token</c> is passed, renders the page for an already existing correlation.
    /// The page requires no external services to render and does not expose PII.
    /// </summary>
    private static async Task<IResult> HandleEmailPushComposeAsync(HttpContext httpContext)
    {
        // The method builds a page with a mailto button, copyable fields, and a manual Pull fallback

        var services = httpContext.RequestServices;
        var cancellationToken = httpContext.RequestAborted;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(EmailAuthEndpoints));

        // Served page localization (ICC-050): the compose page and error pages render in the request language.
        var pageLocalizer = services.GetRequiredService<IConfirmationPromptLocalizer>();
        var servedLocale = ChannelRequestLocale.Detect(httpContext.Request.Headers.AcceptLanguage.ToString());

        // The compose page is reached either by session_id (first entry) or by an already issued
        // correlation token (re-entry), and both name the transaction whose application and ui_config
        // record brand this page (SPEC-007 UI-101). The correlation entry is read here, once, and
        // reused below instead of being read a second time.
        var token = httpContext.Request.Query[EmailAdapterConstants.TokenQueryParam].ToString();
        var sessionId = httpContext.Request.Query[EmailAdapterConstants.SessionIdQueryParam].ToString();
        var correlation = string.IsNullOrWhiteSpace(token)
            ? null
            : await services.GetRequiredService<IEmailPushCorrelationStore>().GetAsync(token, cancellationToken);
        // The transaction is read ONCE and answers two questions of this page at the same ownership:
        // what brands it, and what wording the prefilled mail below is resolved with (SPEC-036
        // TPL-116). A page with no readable transaction has the core level and nothing else (UI-090).
        var pageTransaction = await TryLoadBrandingTransactionAsync(
            services,
            string.IsNullOrWhiteSpace(token) ? sessionId : correlation?.TransactionId,
            cancellationToken);
        var ownership = TransactionResolutionContext.For(pageTransaction);
        var branding = await services.GetRequiredService<CorePageBrandingResolver>()
            .ResolveAsync(ownership, cancellationToken);

        // The tenant of this page is the one its transaction states, and the Email credentials read
        // below (Inbound.*, TokenTtl) belong to that tenant (CA-162/CA-164). The scope opens as soon
        // as the transaction is on hand — before the first of those reads — and closes with the
        // request; a page with no readable transaction gets the default implicit tenant, which is the
        // self-hosted behaviour unchanged.
        using var tenantScope = ChannelTenantContext.BeginScope(pageTransaction?.GetTenantId());

        // Enabled — the channels_enabled axis (§17.7); PushEnabled — core-transport (CFG-230):
        // both stay global by ownership. Read through the adapter's guarded entry point, so that a
        // section broken by a deployment-side edit degrades instead of throwing (TryReadEmailOptions).
        var emailOptions = TryReadEmailOptions(services, logger);
        if (emailOptions is null)
        {
            // The channel cannot be served, and the reason is neither an expired page nor a disabled
            // mode — say what is true rather than reusing the invalid-token text.
            var unreadableHtml = BuildConfirmPageHtml(
                pageLocalizer, servedLocale, branding,
                isError: true,
                errorMessage: EmailAdapterMailStrings.ApiErrorChannelUnavailable);
            return Results.Content(unreadableHtml, "text/html", Encoding.UTF8,
                StatusCodes.Status503ServiceUnavailable);
        }

        // Push must be enabled explicitly (disabled by default — EM-051)
        if (!emailOptions.Enabled || !emailOptions.PushEnabled)
        {
            logger.LogWarning("Email Push compose requested but the adapter or Push mode is disabled.");
            var disabledHtml = BuildPushInvalidPageHtml(pageLocalizer, servedLocale, branding);
            return Results.Content(disabledHtml, "text/html", Encoding.UTF8,
                StatusCodes.Status503ServiceUnavailable);
        }

        // Inbound.* and TokenTtl are tenant-credential fields — resolve them through the seam (CA-162)
        var credentialsResult = await EmailCredentialsResolver.ResolveAsync(
            services.GetRequiredService<IConfigurationResolver>(),
            logger,
            cancellationToken);
        if (credentialsResult.IsFailure)
        {
            logger.LogWarning(
                "Failed to resolve the tenant Email credentials for the Push compose page. Error: {ErrorCode}",
                credentialsResult.Error.Code);
            var unavailableHtml = BuildPushInvalidPageHtml(pageLocalizer, servedLocale, branding);
            return Results.Content(unavailableHtml, "text/html", Encoding.UTF8,
                StatusCodes.Status503ServiceUnavailable);
        }

        var credentials = credentialsResult.Value;

        string correlationToken;
        string transactionIdString;

        if (!string.IsNullOrWhiteSpace(token))
        {
            // Re-entry using an already issued correlation token — the entry was read above, when the
            // page resolved the transaction it is branded for.
            var existing = correlation;

            if (existing is null
                || existing.IsExpired(services.GetRequiredService<TimeProvider>().GetUtcNow())
                || existing.IsConsumed)
            {
                var invalidHtml = BuildPushInvalidPageHtml(pageLocalizer, servedLocale, branding);
                return Results.Content(invalidHtml, "text/html", Encoding.UTF8,
                    StatusCodes.Status410Gone);
            }

            correlationToken = token;
            transactionIdString = existing.TransactionId;

            // Re-validate the transaction the same way as at initial issuance (Pending + Email channel),
            // so we don't show a working compose page for a transaction that has left Pending (review feedback).
            var reentryTransactionId = ParseTransactionId(transactionIdString);
            if (reentryTransactionId is null
                || !await IsPushTransactionActiveAsync(
                    services.GetRequiredService<ITransactionService>(),
                    reentryTransactionId.Value,
                    logger,
                    cancellationToken))
            {
                var invalidHtml = BuildPushInvalidPageHtml(pageLocalizer, servedLocale, branding);
                return Results.Content(invalidHtml, "text/html", Encoding.UTF8,
                    StatusCodes.Status410Gone);
            }
        }
        else
        {
            // First entry by session_id — issue a new correlation token
            var minted = await MintPushCorrelationAsync(
                services,
                EmailCredentialsResolver.ResolveTokenTtl(credentials, emailOptions.TokenTtl),
                sessionId,
                logger,
                cancellationToken);
            if (minted is null)
            {
                var invalidHtml = BuildPushInvalidPageHtml(pageLocalizer, servedLocale, branding);
                return Results.Content(invalidHtml, "text/html", Encoding.UTF8,
                    StatusCodes.Status400BadRequest);
            }

            correlationToken = minted.Value.Token;
            transactionIdString = minted.Value.TransactionId;
        }

        // Publish the email-specific "compose_opened" status (SPEC-016 §10.1) only on the
        // first opening of the compose page: a refresh must not duplicate the signal on the auth page
        var composeOpenedFirstTime = await services
            .GetRequiredService<IEmailPushCorrelationStore>()
            .TryMarkComposeOpenedAsync(correlationToken, cancellationToken);
        if (composeOpenedFirstTime
            && TransactionId.TryParse(transactionIdString, out var composeTransactionId))
        {
            await PublishEmailChannelStatusAsync(
                services,
                composeTransactionId,
                EmailChannelStatuses.PushComposeOpened,
                logger);
        }

        // The subject and the body of the prefilled mail are ONE message of this channel — the same kind
        // WhatsApp prefills its deep link with, told apart by the channel dimension (SPEC-036 §4.6).
        var prefill = await services.GetRequiredService<IMessageTemplateAccessor>()
            .RequireAsync(
                MessageKinds.DeeplinkPrefill,
                MessageSurfaces.InChannel,
                ChannelTypes.Email,
                ownership,
                cancellationToken);

        var composeHtml = BuildPushComposePageHtml(
            pageLocalizer, servedLocale, branding, credentials.Inbound, prefill, correlationToken, transactionIdString,
            logger, httpContext.Request.PathBase.Value ?? string.Empty);
        return Results.Content(composeHtml, "text/html", Encoding.UTF8);
    }

    /// <summary>
    /// Mints a new Push mode correlation token for the transaction passed via session_id.
    /// Validates that the transaction exists, is in the Pending state, and allows the Email channel.
    /// </summary>
    /// <param name="services">The request service provider.</param>
    /// <param name="tokenTtl">The effective token lifetime (tenant value or the core default).</param>
    /// <param name="sessionId">The session_id string (TransactionId).</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A (token, transactionId) pair, or null on a validation error.</returns>
    private static async Task<(string Token, string TransactionId)?> MintPushCorrelationAsync(
        IServiceProvider services,
        TimeSpan tokenTtl,
        string? sessionId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // The method validates the transaction and issues a single-use correlation token (EM-022: we do not use the raw transaction_id)
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var transactionId = ParseTransactionId(sessionId);
        if (transactionId is null)
        {
            return null;
        }

        var transactionService = services.GetRequiredService<ITransactionService>();

        // Validate existence, the Pending state, and Email channel availability (single source of truth)
        if (!await IsPushTransactionActiveAsync(transactionService, transactionId.Value, logger, cancellationToken))
        {
            return null;
        }

        // Generate a high-entropy single-use correlation token (URL-safe Base64)
        var token = GenerateUrlSafeToken();
        var expiresAt = services.GetRequiredService<TimeProvider>().GetUtcNow().Add(tokenTtl);

        var correlationStore = services.GetRequiredService<IEmailPushCorrelationStore>();
        var correlation = new EmailPushCorrelation(
            TransactionId: transactionId.Value.ToString(),
            ExpiresAt: expiresAt);

        await correlationStore.StoreAsync(token, correlation, cancellationToken);

        return (token, transactionId.Value.ToString());
    }

    /// <summary>
    /// Verifies that the transaction exists, is in the Pending state, and allows the Email channel.
    /// The single source of truth for compose-page validation: applied both at initial issuance
    /// of the correlation token and on re-entry with an already issued token.
    /// </summary>
    /// <param name="transactionService">The transaction service.</param>
    /// <param name="transactionId">The transaction identifier.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>true — the transaction is active and the Email channel is allowed.</returns>
    private static async Task<bool> IsPushTransactionActiveAsync(
        ITransactionService transactionService,
        TransactionId transactionId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var txResult = await transactionService.GetTransactionAsync(transactionId, cancellationToken);
        if (txResult.IsFailure)
        {
            logger.LogWarning("Push compose: transaction not found.");
            return false;
        }

        var transaction = txResult.Value;

        // The transaction must be in the Pending state
        if (transaction.State is not TransactionState.Pending)
        {
            logger.LogWarning("Push compose: transaction is not in the Pending state.");
            return false;
        }

        // The Email channel must be allowed for the transaction
        if (transaction.AllowedChannelTypes.Count > 0
            && !transaction.AllowedChannelTypes.Contains(ChannelTypes.Email))
        {
            logger.LogWarning("Push compose: the Email channel is not allowed for the transaction.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Processes an incoming email from the Push mode inbound provider.
    /// Validates the webhook secret, deduplicates by message-id, delegates verification
    /// to <see cref="IEmailInboundProcessor"/>, and on success completes the transaction through the pipeline.
    /// Returns generic JSON without disclosing details to the caller.
    /// </summary>
    private static async Task<IResult> HandleEmailInboundWebhookAsync(HttpContext httpContext)
    {
        // The method accepts an inbound email webhook and orchestrates completion of the Push transaction

        var services = httpContext.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(EmailAuthEndpoints));

        // Tenant demux (CA-167/CA-168/CA-185), the shared one: an inbound mailbox of a tenant is
        // configured at the provider with the {tenant} segment in the callback URL, and that segment
        // is what states whose mail this is — the body cannot, it is not read until the request is
        // authenticated. No segment ⇒ the default implicit tenant, behaviour 1:1. A segment naming a
        // tenant the Email channel is not enabled for is refused with 403 and no details. The
        // resolver is asked for optionally: a host that mapped the route without registering the
        // channel configuration has nothing to authorize a segment against, so the demux fails closed.
        var tenantResolution = await ChannelTenantDemux.ResolveTenantAsync(
            httpContext,
            services.GetService<IConfigurationResolver>(),
            ChannelTypes.Email,
            logger,
            httpContext.RequestAborted);

        if (!tenantResolution.Allowed)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Everything below — the Inbound.* group, the webhook secret it is checked against, the
        // identity of the confirming mail — is resolved for this tenant (CA-162/CA-164).
        using var tenantScope = ChannelTenantContext.BeginScope(tenantResolution.TenantId);

        // Enabled — the channels_enabled axis (§17.7); PushEnabled — core-transport (CFG-230).
        // Read through the adapter's guarded entry point (TryReadEmailOptions): the webhook is a live
        // path just like the pages, and here an unhandled exception would also lose the sign-in mail.
        var emailOptions = TryReadEmailOptions(services, logger);
        if (emailOptions is null)
        {
            // A configuration an operator can repair — the same transient class as a failed completion
            // below, so the same answer: 503 + Retry-After keeps the mail queued at the provider. A 404
            // here would read as a permanent rejection and drop the sign-in mail for good.
            httpContext.Response.Headers.RetryAfter = EmailAdapterConstants.InboundRetryAfterSeconds;
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!emailOptions.Enabled || !emailOptions.PushEnabled)
        {
            // The snapshot is fixed at its first read, so a Push mode switched on by a configuration
            // reload after that read is invisible to it (SPEC-016 EM-054). Only the current PushEnabled
            // tells a mode that is really off (404) from one the host was not started with — the latter
            // is the same transient class as a missing processor below, so the same answer: 503 +
            // Retry-After keeps the sign-in mail queued at the provider. Enabled stays the snapshot's:
            // it states the channel registration, which a reload does not change.
            if (emailOptions.Enabled && IsPushEnabledInCurrentSettings(services, logger))
            {
                logger.LogWarning(
                    "Email inbound webhook: Push is enabled in the current configuration but disabled in the "
                    + "settings snapshot this host serves from — Push was most likely switched on after the host "
                    + "started. The channel composition is fixed at startup; restart the host to serve inbound mail.");
                httpContext.Response.Headers.RetryAfter = EmailAdapterConstants.InboundRetryAfterSeconds;
                return Results.Json(
                    new { success = false },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            logger.LogWarning("Email inbound webhook called but the adapter or Push mode is disabled.");
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status404NotFound);
        }

        // Inbound.* is a tenant-credential group field — resolve it through the seam (CA-162).
        // A resolution failure yields 404 without disclosing the reason to the caller (no configuration
        // leak into the response body, core-rules §10).
        var credentialsResult = await EmailCredentialsResolver.ResolveAsync(
            services.GetRequiredService<IConfigurationResolver>(),
            logger,
            httpContext.RequestAborted);
        if (credentialsResult.IsFailure)
        {
            logger.LogWarning(
                "Email inbound webhook: failed to resolve the tenant Email credentials. Error: {ErrorCode}",
                credentialsResult.Error.Code);
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status404NotFound);
        }

        var inbound = credentialsResult.Value.Inbound;

        // The webhook endpoint is active only when Provider=Webhook. For other inbound providers
        // (ImapPolling/SmtpRelay/Custom) the webhook secret is neither validated nor required by the validator,
        // so the endpoint is explicitly unavailable (404) — otherwise it would mask a configuration error
        // with constant 401 "secret not configured" responses (review feedback).
        if (inbound.Provider is not EmailInboundProvider.Webhook)
        {
            logger.LogWarning(
                "Email inbound webhook called but the inbound provider is not Webhook (Provider={Provider}).",
                inbound.Provider);
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status404NotFound);
        }

        // Validate the webhook secret (protection against request forgery, CA-033)
        if (!ValidateInboundWebhookSecret(httpContext.Request, inbound, logger))
        {
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Precondition: the inbound processor must actually be in the container. Resolved optionally,
        // and here rather than at the point of use, because the PushEnabled guard above reads an IOptions
        // snapshot that materializes on first access and may therefore pick up a configuration reloaded
        // after startup, while the processor registration is fixed at startup and stays under PushEnabled
        // (it cannot be registered unconditionally — its required ITransactionEventPublisher comes from
        // the transaction engine, so that would stop a Pull-only host wired without it from starting).
        // Checking it up front keeps the mismatch free of side effects: dedup lookup and the
        // mail_received status below must not run for a request we are about to refuse.
        var processor = services.GetService<IEmailInboundProcessor>();
        if (processor is null)
        {
            // A transient failure awaiting recovery — the same class as a failed completion below, so the
            // same answer: 503 + Retry-After, which keeps the mail queued at the provider instead of
            // dropping it. A 404 here would read as a permanent rejection and lose the sign-in mail for
            // good, and the operator's configuration does say Push is on.
            logger.LogWarning(
                "Email inbound webhook: Push is enabled in the configuration but no inbound processor is "
                + "registered — Push was most likely switched on after the host started. The channel "
                + "composition is fixed at startup; restart the host to serve inbound mail.");
            httpContext.Response.Headers.RetryAfter = EmailAdapterConstants.InboundRetryAfterSeconds;
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Hard limit on the webhook body size (protection against unbounded buffering / DoS).
        // An honest Content-Length is rejected immediately with 413; chunked/forged is handled by capped reading in the parser.
        if (httpContext.Request.ContentLength > EmailAdapterConstants.MaxInboundBodyBytes)
        {
            logger.LogWarning(
                "Email inbound webhook: request body exceeds the {Limit} byte limit.",
                EmailAdapterConstants.MaxInboundBodyBytes);
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        // Parse the request body into InboundEmailMessage (tolerant of missing optional fields)
        InboundEmailMessage? message;
        try
        {
            message = await ParseInboundMessageAsync(
                httpContext.Request,
                services.GetRequiredService<TimeProvider>(),
                httpContext.RequestAborted);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Email inbound webhook: malformed JSON in the request body.");
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (message is null || string.IsNullOrWhiteSpace(message.MessageId))
        {
            logger.LogWarning("Email inbound webhook: required message-id field is missing.");
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationStore = services.GetRequiredService<IEmailPushCorrelationStore>();

        // Idempotent deduplication of redelivery by message-id (EM-023, EM-127).
        // Early read-only check: if the email has already been processed successfully — ignore it.
        // The message-id is registered ONLY after the transaction completes successfully
        // (see below) — otherwise a transient failure would mark the email as processed before it
        // actually completes, and redelivery/replay would become a no-op (review feedback).
        if (await correlationStore.IsMessageProcessedAsync(message.MessageId, httpContext.RequestAborted))
        {
            logger.LogInformation("Email inbound webhook: repeated mail delivery ignored (dedup).");
            return Results.Json(new { success = true });
        }

        // The token of the mail_received status, of the consumption on noRetry and of the consumption
        // after Completed is ONE token, extracted by ONE routine — which is why the extraction lives in
        // a shared method rather than in each step. The processor below runs the same routine again on
        // the same mail (it is reachable through its own SPI and cannot be handed a token through it),
        // so a mail is read twice; the cost is bounded by MaxCorrelationTokenProbes and the two reads
        // answer identically, because neither of them decides anything on its own.
        var correlationToken = await DefaultEmailInboundProcessor.ExtractCorrelationTokenAsync(
            message,
            inbound,
            correlationStore,
            services.GetRequiredService<TimeProvider>().GetUtcNow(),
            httpContext.RequestAborted);

        // Publish the "mail_received" status (SPEC-016 §10.1) if the email correlates
        // with an active transaction — before sender verification
        if (!string.IsNullOrEmpty(correlationToken))
        {
            var receivedCorrelation = await correlationStore.GetAsync(correlationToken, httpContext.RequestAborted);
            if (receivedCorrelation is not null
                && !receivedCorrelation.IsExpired(services.GetRequiredService<TimeProvider>().GetUtcNow())
                && !receivedCorrelation.IsConsumed
                && TransactionId.TryParse(receivedCorrelation.TransactionId, out var receivedTransactionId))
            {
                await PublishEmailChannelStatusAsync(
                    services,
                    receivedTransactionId,
                    EmailChannelStatuses.PushMailReceived,
                    logger);
            }
        }

        // Delegate sender verification and result formation to the processor (resolved as a precondition above)
        var inboundResult = await processor.ProcessInboundEmailAsync(message, httpContext.RequestAborted);

        if (inboundResult is not ChannelAuthConfirmResult confirmResult)
        {
            // The email does not belong to an active Push transaction or failed verification.
            // We intentionally do not register the message-id: a redelivery may arrive after
            // the transaction becomes active, and it should be processed again.
            logger.LogInformation("Email inbound webhook: the mail did not result in a confirmation (Result={ResultType}).",
                inboundResult.GetType().Name);
            return Results.Json(new { success = false });
        }

        // Sender verified — publish the "verified" status (SPEC-016 §10.1)
        await PublishEmailChannelStatusAsync(
            services,
            confirmResult.TransactionId,
            EmailChannelStatuses.PushVerified,
            logger);

        // A CONFIRMATION does not end with the letter that identifies its sender: the letter says WHO,
        // and what is being confirmed is still to be shown and answered on the core page (SPEC-039
        // E29). The mail therefore attaches the identity and leaves the transaction waiting for that
        // answer, exactly as the magic link of the Pull mode does. A sign-in travels its usual path.
        var isConfirmation = await IsConfirmationTransactionAsync(
            services, confirmResult.TransactionId, logger);

        var attachRefusedForGood = false;
        bool completed;

        if (isConfirmation)
        {
            var attachErrorCode = await AttachPushIdentityAsync(services, confirmResult, logger);
            completed = attachErrorCode is null;

            // A sender the transaction does not admit is refused the same way by every redelivery, so
            // the provider is told to stop rather than to retry. An oversized snapshot is refused the
            // same way and for the same reason: the identity is composed from THIS mail, so the next
            // delivery of it measures the same bytes. The set names refusals about THIS MAIL only;
            // refusals about the transaction itself — it ended, or it ran out of time while still
            // Pending — are answered by the check below, which asks the engine for its current state
            // instead of restating the codes here.
            attachRefusedForGood = attachErrorCode is TransactionErrorCodes.ChannelNotAllowed
                or TransactionErrorCodes.BotRejected
                or TransactionErrorCodes.SnapshotTooLarge;
        }
        else
        {
            completed = await CompletePushTransactionAsync(
                services, correlationToken, confirmResult, correlationStore, logger);
        }

        if (!completed)
        {
            // If the transaction is already terminal/unavailable — a retry won't help; return 200 and record the dedup.
            var transactionService = services.GetRequiredService<ITransactionService>();
            var noRetry = attachRefusedForGood;

            var verifyTxResult = await transactionService.GetTransactionAsync(
                confirmResult.TransactionId, CancellationToken.None);

            if (verifyTxResult.IsFailure
                && verifyTxResult.Error.Code is TransactionErrorCodes.TransactionNotFound
                    or TransactionErrorCodes.TransactionExpired)
            {
                noRetry = true;
            }

            // Terminal is asked of the domain (Transaction.IsTerminal) instead of being spelled out
            // here: a transaction that has ENDED accepts nothing from this webhook any more, and a
            // hand-written list of states is what left Completed — the state a confirmation answered
            // on the core page reaches — outside the answer. Neither Confirmed nor Created is
            // terminal, so neither ends the retry by itself: both still move (finalization, handover
            // of the freshly created transaction to the user), and a redelivery may yet be the one
            // that lands. The deadline term narrows that for the confirmed one — and it is no
            // refusal to finalize past the TTL: this handler itself finalizes, through the branch
            // above that hands a non-confirmation mail to CompletePushTransactionAsync, and a
            // redelivery getting through there does drive Confirmed → Completed after the deadline
            // (the TTL of a confirmed transaction decides nothing — SPEC-001 §4.4 item 4). Here that
            // drive has already failed, and what is left to retry into is a wait the TTL does not
            // govern, of a length the store decides — see ITransactionStore.GetStalledConfirmedAsync.
            // So answer 200 and register the dedup rather than hold the provider for all of it.
            if (verifyTxResult.IsSuccess
                && (verifyTxResult.Value.IsTerminal()
                    || verifyTxResult.Value.IsExpired(services.GetRequiredService<TimeProvider>().GetUtcNow())))
            {
                noRetry = true;
            }

            if (noRetry)
            {
                logger.LogInformation(
                    "Email inbound webhook: redelivery not needed (transaction unavailable, terminal or past its deadline). TransactionId: {TransactionId}",
                    confirmResult.TransactionId);

                if (!string.IsNullOrEmpty(correlationToken))
                {
                    _ = await correlationStore.TryConsumeAsync(correlationToken, CancellationToken.None);
                }

                await correlationStore.TryRegisterProcessedMessageAsync(message.MessageId, CancellationToken.None);

                return Results.Json(new { success = false });
            }

            // Transaction completion failed (a pipeline/DB failure, or the transaction did not move to
            // Completed) — an internal transient error awaiting recovery. We intentionally do NOT
            // register the message-id and respond with 503 + Retry-After so the inbound provider retries
            // delivery — otherwise a 200 OK would lose the email (review feedback).
            httpContext.Response.Headers.RetryAfter = EmailAdapterConstants.InboundRetryAfterSeconds;
            return Results.Json(
                new { success = false },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Register the message-id for deduplication only after confirmed transaction completion.
        // We use CancellationToken.None: cancellation/abort of the request after completion must not break
        // idempotency — otherwise the provider could redeliver the event indefinitely (review feedback).
        await correlationStore.TryRegisterProcessedMessageAsync(
            message.MessageId, CancellationToken.None);

        return Results.Json(new { success = true });
    }

    /// <summary>
    /// Answers whether Push mode is enabled in the current Email configuration — the value a reload after
    /// the <see cref="IOptions{TOptions}"/> snapshot was taken has already reached.
    /// </summary>
    /// <remarks>
    /// The read is guarded the same way as <c>TryReadEmailOptions</c>: a reloaded section that no longer
    /// binds or validates must not throw out of the webhook. Such a read answers <see langword="false"/>,
    /// so the snapshot's own answer stands — a switch-on that cannot be read is not reported as one.
    /// </remarks>
    /// <param name="services">Request service provider.</param>
    /// <param name="logger">Logger of the failed read.</param>
    /// <returns><see langword="true"/> — the current configuration has Push mode enabled.</returns>
    private static bool IsPushEnabledInCurrentSettings(IServiceProvider services, ILogger logger)
    {
        try
        {
            return services.GetService<IOptionsMonitor<EmailOptions>>()?.CurrentValue.PushEnabled is true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Email inbound webhook: the current Email settings could not be read, so the settings snapshot decides whether Push is enabled.");

            return false;
        }
    }

    /// <summary>
    /// Answers whether the inbound mail belongs to a CONFIRMATION transaction — the one kind this
    /// path does not finish.
    /// </summary>
    /// <remarks>
    /// A transaction the store can no longer answer for is not called a confirmation: the completion
    /// path below reads it again and reports the real outcome, and answering "yes" on a failed read
    /// would turn an unavailable transaction into a silent success of this webhook.
    /// </remarks>
    /// <param name="services">The request service provider.</param>
    /// <param name="transactionId">Transaction the verified mail names.</param>
    /// <param name="logger">The logger.</param>
    /// <returns><c>true</c> — the transaction is a confirmation.</returns>
    private static async Task<bool> IsConfirmationTransactionAsync(
        IServiceProvider services,
        TransactionId transactionId,
        ILogger logger)
    {
        var txResult = await services.GetRequiredService<ITransactionService>()
            .GetTransactionAsync(transactionId, CancellationToken.None);

        if (txResult.IsFailure)
        {
            logger.LogWarning(
                "Email inbound webhook: failed to read the transaction of the verified mail. TransactionId: {TransactionId}, ErrorCode: {ErrorCode}",
                transactionId,
                txResult.Error.Code);
            return false;
        }

        return string.Equals(
            txResult.Value.Type, TransactionTypes.Confirmation, StringComparison.Ordinal);
    }

    /// <summary>
    /// Attaches the identity the inbound mail proved, leaving the confirmation waiting for its answer
    /// on the core page (SPEC-039 E29). The mirror of what the magic link does in the Pull mode.
    /// </summary>
    /// <remarks>
    /// Neither the correlation token nor the message-id changes hands here for the token's sake: the
    /// token is spent on a terminal success, and there is none — the answer is still ahead. A repeated
    /// delivery of the same mail attaches the same snapshot again, which the attach makes idempotent
    /// (the last delivery wins), and the dedup registration of the caller keeps that from happening
    /// needlessly.
    /// <para>
    /// The user may have no browser open at all, in which case the transaction waits out its TTL: that
    /// is a known limitation of asking the question on a page, and completing it here without ever
    /// showing the subject is the one thing that must not happen instead.
    /// </para>
    /// </remarks>
    /// <param name="services">The request service provider.</param>
    /// <param name="inboundResult">The confirmation result from the processor.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>Null on success; otherwise the code the attach refused with.</returns>
    private static async Task<string?> AttachPushIdentityAsync(
        IServiceProvider services,
        ChannelAuthConfirmResult inboundResult,
        ILogger logger)
    {
        var transactionService = services.GetRequiredService<ITransactionService>();

        // CancellationToken.None, as on the completion path: the mail has arrived and its identity
        // must be recorded even if the provider's connection goes away.
        var txResult = await transactionService.GetTransactionAsync(
            inboundResult.TransactionId, CancellationToken.None);

        if (txResult.IsFailure)
        {
            return txResult.Error.Code;
        }

        var attachResult = await transactionService.AttachChannelIdentityAsync(
            inboundResult.TransactionId,
            inboundResult.Identity,
            txResult.Value.ConcurrencyToken,
            CancellationToken.None);

        if (attachResult.IsSuccess)
        {
            logger.LogInformation(
                "Email inbound webhook: the mailbox identity is attached; the confirmation is answered on the core page. TransactionId: {TransactionId}",
                inboundResult.TransactionId);
            return null;
        }

        logger.LogWarning(
            "Email inbound webhook: the channel identity was not attached. TransactionId: {TransactionId}, ErrorCode: {ErrorCode}",
            inboundResult.TransactionId,
            attachResult.Error.Code);

        return attachResult.Error.Code;
    }

    /// <summary>
    /// Completes the Push transaction through the channel pipeline and consumes the correlation token.
    /// Mirrors the Pull-confirm logic: the correlation is consumed only after the transaction's confirmed
    /// transition to Completed; on failure the correlation remains available within the TTL.
    /// </summary>
    /// <param name="services">The request service provider.</param>
    /// <param name="correlationToken">
    /// The correlation token, extracted from the email once in the calling code
    /// (single point of extraction — review feedback). Null/empty — consumption is skipped.
    /// </param>
    /// <param name="inboundResult">The confirmation result from the processor.</param>
    /// <param name="correlationStore">The correlation token store.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>true — the transaction is completed and the correlation is consumed.</returns>
    private static async Task<bool> CompletePushTransactionAsync(
        IServiceProvider services,
        string? correlationToken,
        ChannelAuthConfirmResult inboundResult,
        IEmailPushCorrelationStore correlationStore,
        ILogger logger)
    {
        // The method orchestrates transaction completion and single-use consumption of the correlation

        var adapters = services.GetRequiredService<IEnumerable<IChannelAdapter>>();
        var adapter = adapters.FirstOrDefault(a => a.ChannelType is ChannelTypes.Email);

        if (adapter is null)
        {
            logger.LogError("Email inbound webhook: Email adapter not found in DI. TransactionId: {TransactionId}",
                inboundResult.TransactionId);
            return false;
        }

        var transactionService = services.GetRequiredService<ITransactionService>();
        var identityResolutionService = services.GetRequiredService<IIdentityResolutionService>();
        var userAuthRateLimiter = services.GetService<IUserAuthRateLimiter>();
        // Required, unlike the rate limiter above: the pipeline counts every failed user notification,
        // and an absent instrument would leave this path silently uncounted.
        var metrics = services.GetRequiredService<ChannelAdapterMetrics>();

        // Process through the pipeline (CancellationToken.None: completion must not be interrupted by request cancellation)
        try
        {
            // Email sends no in-channel prompt, so the outcome reaches the user only through the
            // sign-in page — hence no prompt store is passed either. The terminal wording comes from
            // the core through the receipt port; the error text stays the channel's own.
            await ChannelWebhookPipeline.ProcessInboundResultAsync(
                inboundResult,
                adapter,
                transactionService,
                services.GetRequiredService<ITransactionStore>(),
                services.GetRequiredService<TimeProvider>(),
                identityResolutionService,
                logger,
                metrics,
                userAuthRateLimiter,
                confirmationPromptOrchestrator: null,
                promptLocalizer: null,
                promptMessageStore: null,
                services.GetRequiredService<IOutcomeReceiptText>(),
                services.GetRequiredService<IOutcomeNoticeDisplayIntentSource>(),
                services.GetService<ITransactionEventPublisher>(),
                new ChannelReplyTexts(MessageTemplateNaturalKeys.OutcomeErrorInChannel),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Email inbound webhook: pipeline error while completing the Push transaction. TransactionId: {TransactionId}",
                inboundResult.TransactionId);
            return false;
        }

        // The pipeline does not return a success flag — re-read the transaction state (SPEC-016 §5)
        var verifyTxResult = await transactionService.GetTransactionAsync(
            inboundResult.TransactionId, CancellationToken.None);

        var isCompleted = verifyTxResult.IsSuccess
            && verifyTxResult.Value.State is TransactionState.Completed;

        if (!isCompleted)
        {
            logger.LogWarning(
                "Email inbound webhook: transaction not finished in Completed. TransactionId: {TransactionId}",
                inboundResult.TransactionId);
            return false;
        }

        // Consume the correlation token only after confirmed transaction completion
        // (the token is extracted once in the calling code — single point of extraction)
        if (!string.IsNullOrEmpty(correlationToken))
        {
            var consumed = await correlationStore.TryConsumeAsync(correlationToken, CancellationToken.None);
            if (!consumed)
            {
                var existing = await correlationStore.GetAsync(correlationToken, CancellationToken.None);
                if (existing is not null
                    && !existing.IsExpired(services.GetRequiredService<TimeProvider>().GetUtcNow())
                    && !existing.IsConsumed)
                {
                    logger.LogWarning(
                        "Email inbound webhook: failed to consume the correlation token after Completed. TransactionId: {TransactionId}",
                        inboundResult.TransactionId);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Validates the inbound email webhook secret using a fixed-time comparison (CA-033).
    /// </summary>
    /// <param name="request">The webhook HTTP request.</param>
    /// <param name="inbound">Inbound processing settings.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>true — the secret is valid.</returns>
    private static bool ValidateInboundWebhookSecret(
        HttpRequest request,
        EmailInboundOptions inbound,
        ILogger logger)
    {
        // The method compares the secret header against the configured value in constant time
        var configuredSecret = inbound.WebhookSecretToken;

        // If the secret is not configured — reject the request (inbound webhook protection)
        if (string.IsNullOrEmpty(configuredSecret))
        {
            logger.LogError(
                "Email inbound webhook: secret is not configured. Request rejected. Header {Header}.",
                EmailAdapterConstants.WebhookSecretHeader);
            return false;
        }

        var headerValue = request.Headers[EmailAdapterConstants.WebhookSecretHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(headerValue))
        {
            logger.LogWarning(
                "Email inbound webhook: header {Header} is missing.",
                EmailAdapterConstants.WebhookSecretHeader);
            return false;
        }

        // Compare fixed-length SHA-256 hashes (32 bytes): the comparison is always constant-length,
        // which eliminates leaking the secret length via an early return and preserves constant time (CA-033).
        Span<byte> expectedHash = stackalloc byte[32];
        Span<byte> actualHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(configuredSecret), expectedHash);
        SHA256.HashData(Encoding.UTF8.GetBytes(headerValue), actualHash);

        var isValid = CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        if (!isValid)
        {
            logger.LogWarning("Email inbound webhook: invalid secret.");
        }

        return isValid;
    }

    /// <summary>
    /// Parses the webhook request body into <see cref="InboundEmailMessage"/>.
    /// Tolerant of missing optional fields.
    /// </summary>
    /// <param name="request">The webhook HTTP request.</param>
    /// <param name="timeProvider">Clock supplying the receipt moment when the payload omits it.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The parsed message, or null if the JSON root is not an object. For an empty or
    /// malformed body, a <see cref="JsonException"/> is thrown (handled by the calling code).
    /// </returns>
    private static async Task<InboundEmailMessage?> ParseInboundMessageAsync(
        HttpRequest request,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        // The method reads provider-agnostic JSON and assembles an InboundEmailMessage.
        // We read the body with a hard size limit — protection against unbounded buffering when Content-Length
        // is missing or forged (chunked transfer). The chunked copy under a limit is the component's
        // shared one; the limit and the exception it raises are this endpoint's own: exceeding the
        // limit → JsonException (→ 400).
        await using var bounded = new MemoryStream();
        await BoundedBodyReader.CopyAsync(
            request.Body,
            bounded,
            EmailAdapterConstants.MaxInboundBodyBytes,
            static () => new JsonException("Inbound webhook body exceeds the maximum allowed size."),
            cancellationToken);

        bounded.Position = 0;
        using var document = await JsonDocument.ParseAsync(bounded, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var messageId = ReadString(root, EmailInboundJsonFields.MessageId);
        var from = ReadString(root, EmailInboundJsonFields.From);
        var to = ReadString(root, EmailInboundJsonFields.To);
        var subject = ReadString(root, EmailInboundJsonFields.Subject);

        var receivedAt = ReadDateTimeOffset(root, EmailInboundJsonFields.ReceivedAt)
            ?? timeProvider.GetUtcNow();

        return new InboundEmailMessage(
            MessageId: messageId ?? string.Empty,
            FromAddress: from ?? string.Empty,
            EnvelopeSender: ReadString(root, EmailInboundJsonFields.EnvelopeSender),
            ToAddress: to ?? string.Empty,
            Subject: subject ?? string.Empty,
            Body: ReadString(root, EmailInboundJsonFields.Body),
            ReceivedAt: receivedAt,
            SpfResult: ReadString(root, EmailInboundJsonFields.Spf),
            DkimResult: ReadString(root, EmailInboundJsonFields.Dkim),
            DmarcResult: ReadString(root, EmailInboundJsonFields.Dmarc),
            ProviderVerifiedSender: ReadString(root, EmailInboundJsonFields.ProviderVerifiedSender));
    }

    /// <summary>
    /// Safely reads a JSON string property (with a ValueKind check).
    /// </summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The string value or null.</returns>
    private static string? ReadString(JsonElement element, string propertyName)
    {
        // The method checks ValueKind before calling GetString (protection against malformed data)
        if (element.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    /// <summary>
    /// Safely reads a <see cref="DateTimeOffset"/> value from a JSON string property.
    /// </summary>
    /// <param name="element">The JSON object.</param>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The value, or null if absent or unparseable.</returns>
    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        // The method parses an ISO-8601 string into a DateTimeOffset
        var raw = ReadString(element, propertyName);
        if (raw is not null
            && DateTimeOffset.TryParse(
                raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Generates a high-entropy URL-safe token (Base64Url without padding).
    /// Delegates to the shared generator so the compose and direct-mailto paths mint identical tokens.
    /// </summary>
    /// <returns>The token string.</returns>
    private static string GenerateUrlSafeToken() => EmailPushTokenGenerator.GenerateUrlSafeToken();
}
