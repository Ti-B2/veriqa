// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.DependencyInjection;
using Veriqa.Core.ChannelAdapter.Diagnostics;
using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;
using Veriqa.Core.Contracts;
using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Constants;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Events;
using Veriqa.Core.TransactionEngine.Identity;
using Veriqa.Core.TransactionEngine.MessageTemplates;
using Veriqa.Core.TransactionEngine.Services;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Processing pipeline for channel adapter webhooks.
/// The adapter does NOT call TransactionEngine directly (CA-012) — orchestration lives here.
/// Always returns HTTP 200 for valid webhooks (CA-070).
/// The wire format of an inbound event is parsed by the adapter, not here: the pipeline builds one
/// channel-neutral envelope per request and hands the same envelope to validation and to processing.
/// </summary>
public static class ChannelWebhookPipeline
{
    /// <summary>
    /// Body size limit of every webhook route (1 MB): protects against DoS by large request
    /// bodies (webhook updates are usually &lt;100 KB). A single constant for the built-in and the
    /// generic third-party routes — the protective parity is guaranteed by construction.
    /// The limit is enforced by the envelope's own copy of the body, so nothing is reserved for a
    /// request rejected before the body is read, and an over-limit body stops the copy instead of
    /// being materialized first. The name keeps the word "buffer" for continuity with the earlier
    /// naming; no buffering wrapper is involved in reading the body.
    /// </summary>
    private const int WebhookBodyBufferLimitBytes = 1_048_576;

    /// <summary>
    /// Message of the exception raised when the webhook body crosses the size limit.
    /// </summary>
    private const string BodyOverLimitMessage = "The webhook request body exceeds the size limit.";

    /// <summary>
    /// Separator joining the values of a multi-valued header or query parameter into one envelope value.
    /// </summary>
    private const string MultiValueSeparator = ", ";

    /// <summary>
    /// Registers the endpoints of every registered channel.
    /// Besides the base webhook paths (default tenant, CA-168), registers optional routes with
    /// a tenant segment <c>…/webhook/{tenant}</c> (tenant demux, opt-in, CA-167), and lets each
    /// channel contribute endpoints of its own (<see cref="IChannelEndpointRegistrar"/>).
    /// </summary>
    /// <param name="endpoints">Endpoint router.</param>
    /// <param name="rateLimitPolicyName">Rate limiting policy name for webhook endpoints (null — no limit).</param>
    /// <returns>Router for chaining.</returns>
    public static IEndpointRouteBuilder MapChannelWebhookEndpoints(this IEndpointRouteBuilder endpoints, string? rateLimitPolicyName = null)
        => MapChannelWebhookEndpoints(
            endpoints,
            new ChannelEndpointPolicies { WebhookPolicyName = rateLimitPolicyName });

    /// <summary>
    /// Registers the endpoints of every registered channel, with the full set of rate-limit policies
    /// the host offers — the webhook budget and the budget of the user-facing pages a channel serves.
    /// </summary>
    /// <param name="endpoints">Endpoint router.</param>
    /// <param name="policies">Rate-limit policy names the host offers to the channel endpoints.</param>
    /// <returns>Router for chaining.</returns>
    public static IEndpointRouteBuilder MapChannelWebhookEndpoints(
        this IEndpointRouteBuilder endpoints,
        ChannelEndpointPolicies policies)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(policies);

        // Webhook routes of the channels registered through ChannelAdapterBuilder.AddChannel — the
        // shipped ones and the third-party ones alike. Same protective layers for every channel: one
        // rate-limit policy and the same 1 MB body size limit inside the handler.
        MapRegisteredChannelWebhooks(endpoints, policies.WebhookPolicyName);

        // Endpoints a channel maps for itself: a page of its own, an inbound route outside the path
        // convention. Nothing here knows which channel contributes what.
        MapChannelOwnEndpoints(endpoints, policies);

        return endpoints;
    }

    /// <summary>
    /// Maps the webhook endpoints of the registered channels. The registry is absent when no channel
    /// is registered — then nothing is mapped.
    /// </summary>
    /// <param name="endpoints">Endpoint router.</param>
    /// <param name="rateLimitPolicyName">Rate limiting policy name (null — no limit).</param>
    private static void MapRegisteredChannelWebhooks(
        IEndpointRouteBuilder endpoints,
        string? rateLimitPolicyName)
    {
        var registry = endpoints.ServiceProvider.GetService<CustomChannelRegistry>();
        if (registry is null)
        {
            return;
        }

        // The registration ↔ adapter cross-check does not live here: it is bound to the host start
        // (CustomChannelStartupValidator), so a host that maps no webhooks at all is diagnosed too.
        foreach (var registration in registry.Registrations)
        {
            // The registration is captured per channel — the handlers are keyed by what it declares.
            var channelType = registration.ChannelType;
            var options = registration.Options;

            if (options.MapWebhook)
            {
                ChannelTenantDemux.MapWithOptionalTenant(
                    endpoints,
                    HttpMethods.Post,
                    registration.WebhookPath,
                    ([FromServices] ChannelWebhookSupport? support, HttpContext httpContext) =>
                        HandleChannelWebhookAsync(
                            httpContext, support ?? ChannelWebhookSupport.None, channelType, options),
                    rateLimitPolicyName);
            }

            // The platform's verification handshake, when the channel declared one: a GET on the very
            // same path, answered with the challenge the platform sent.
            if (options.WebhookVerificationQueryKey is { Length: > 0 } challengeQueryKey)
            {
                ChannelTenantDemux.MapWithOptionalTenant(
                    endpoints,
                    HttpMethods.Get,
                    registration.WebhookPath,
                    ([FromServices] ChannelWebhookSupport? support, HttpContext httpContext) =>
                        HandleWebhookVerificationAsync(
                            httpContext, support ?? ChannelWebhookSupport.None, channelType, challengeQueryKey),
                    rateLimitPolicyName);
            }
        }
    }

    /// <summary>
    /// Lets every channel that registered an endpoint registrar map the endpoints of its own.
    /// </summary>
    /// <param name="endpoints">Endpoint router.</param>
    /// <param name="policies">Rate-limit policy names the host offers to the channel endpoints.</param>
    private static void MapChannelOwnEndpoints(
        IEndpointRouteBuilder endpoints,
        ChannelEndpointPolicies policies)
    {
        var registrars = endpoints.ServiceProvider.GetServices<IChannelEndpointRegistrar>();

        foreach (var registrar in registrars)
        {
            registrar.MapEndpoints(endpoints, policies);
        }
    }

    /// <summary>
    /// Builds the channel-neutral inbound envelope from the ASP.NET Core request.
    /// </summary>
    /// <remarks>
    /// The body is carried as raw bytes on purpose: a signature is computed over exactly those
    /// bytes, so no decoding step here (a text decode would swallow a byte-order mark) can make an
    /// adapter hash something other than what arrived. Decoding is the adapter's business.
    /// </remarks>
    /// <param name="request">HTTP request; its body is read here, and only here.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The neutral inbound envelope.</returns>
    private static async Task<ChannelInboundRequest> BuildInboundRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var body = HttpMethods.IsGet(request.Method)
            ? ReadOnlyMemory<byte>.Empty
            : await ReadBodyAsync(request.Body, cancellationToken);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(MultiValueSeparator, header.Value.Where(value => value is not null));
        }

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in request.Query)
        {
            query[parameter.Key] = string.Join(MultiValueSeparator, parameter.Value.Where(value => value is not null));
        }

        return new ChannelInboundRequest
        {
            Method = request.Method.ToUpperInvariant(),
            Body = body,
            Headers = headers,
            Query = query
        };
    }

    /// <summary>
    /// Reads the request body in full, refusing to hold more than
    /// <see cref="WebhookBodyBufferLimitBytes"/>.
    /// </summary>
    /// <remarks>
    /// The limit lives here, in the copy itself, rather than in a buffering wrapper around the
    /// stream: the body is read exactly once per request — by this method — so a rewindable buffer
    /// would serve no second reader while reserving its whole ceiling on every request, including
    /// the ones the route rejects before reading anything. Reading it in chunks also means an
    /// over-limit body is refused as it arrives instead of being accepted in full first, and the
    /// request never touches a temp file, so a temp volume cannot fail a webhook read. The chunked
    /// copy itself is the component's shared one (<see cref="BoundedBodyReader"/>); what stays here
    /// is what belongs to this route — its limit and the answer an over-limit body gets.
    /// </remarks>
    /// <param name="body">Request body stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The body bytes.</returns>
    /// <exception cref="BadHttpRequestException">The body is over the limit.</exception>
    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(Stream body, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        await BoundedBodyReader.CopyAsync(
            body,
            buffer,
            WebhookBodyBufferLimitBytes,
            static () => new BadHttpRequestException(BodyOverLimitMessage, StatusCodes.Status413PayloadTooLarge),
            cancellationToken);

        return buffer.ToArray();
    }

    /// <summary>
    /// Handles an incoming POST webhook of a channel registered through
    /// <c>ChannelAdapterBuilder.AddChannel</c> — the shipped channels and the third-party ones alike.
    /// The chain is: tenant demux (an unknown tenant is rejected with 403
    /// before the adapter is reached) → the neutral envelope, whose body is read once under the
    /// 1 MB limit → <c>ValidateWebhookAsync</c> over that envelope (rejection → 403) →
    /// <c>ProcessInboundEventAsync</c> over the same envelope, whose raw body bytes the adapter
    /// parses itself (the core imposes no body format) → the standard post-processing.
    /// Adapter code is never called outside a catch: an exception from <c>ValidateWebhookAsync</c>
    /// is treated as a rejection (fail-closed 403), and everything after a successful validation —
    /// the adapter's processing and the post-processing — always answers HTTP 200 (CA-070), including
    /// a Failure result and an unhandled exception: the details go to the log only, never to the
    /// response body. A body that cannot be read (over the size limit) answers 200 too — that is
    /// not a rejection verdict (CA-185).
    /// The catches are what the route owes the CA-070 invariant: an unhandled exception would turn
    /// into a 500 (with a stack trace on a Development host) and break it. They apply to every
    /// channel — a shipped adapter has no more licence to break the invariant than a third-party one,
    /// and one handler for all of them is what keeps the two from drifting apart.
    /// </summary>
    /// <param name="httpContext">HTTP request context.</param>
    /// <param name="support">Optional dependencies of the pipeline.</param>
    /// <param name="channelType">Channel type of the registered channel.</param>
    /// <param name="options">Registration settings of the channel (its status texts).</param>
    /// <returns>HTTP result of the webhook.</returns>
    private static async Task<IResult> HandleChannelWebhookAsync(
        HttpContext httpContext,
        ChannelWebhookSupport support,
        string channelType,
        ChannelRegistrationOptions options)
    {
        // Get dependencies from DI
        var adapters = httpContext.RequestServices.GetRequiredService<IEnumerable<IChannelAdapter>>();
        var transactionService = httpContext.RequestServices.GetRequiredService<ITransactionService>();
        // The store and the clock travel beside the service: a decision reported by a channel is
        // answered for a transaction whose time may already be up, and the TTL gate of the service
        // hides exactly that transaction (SPEC-003 CA-192).
        var transactionStore = httpContext.RequestServices.GetRequiredService<ITransactionStore>();
        var timeProvider = httpContext.RequestServices.GetRequiredService<TimeProvider>();
        var identityResolutionService = httpContext.RequestServices.GetRequiredService<IIdentityResolutionService>();
        var logger = GetLogger(httpContext);
        // The instrument comes from the request's services, the same way the logger does. It is not
        // the only seam: the background and polling paths have no HttpContext and get it from their
        // own container, which is why it travels onwards as a parameter.
        var metrics = httpContext.RequestServices.GetRequiredService<ChannelAdapterMetrics>();
        // The terminal wording of a transaction is the core's, not the registration's: it is resolved
        // from the levelled configuration, so it is a required service and is taken exactly the way
        // the other required ones above are — not through the bundle of OPTIONAL dependencies.
        var receiptText = httpContext.RequestServices.GetRequiredService<IOutcomeReceiptText>();
        // How the deployment wants the receipt to appear in the conversation. Taken exactly like the
        // wording above and for the same reason: the value lives in the levelled configuration, so
        // there is no default for this route to stand in with.
        var displayIntentSource = httpContext.RequestServices.GetRequiredService<IOutcomeNoticeDisplayIntentSource>();
        // The journal of the interaction surface (SPEC-039 E48). Optional: the publisher belongs to
        // the transaction engine, and a composition wired without the engine has no journal to write
        // to — the refusals are then still logged, they simply reach no audit consumer.
        var eventPublisher = httpContext.RequestServices.GetService<ITransactionEventPublisher>();
        // Per-user rate limiter — optional (not registered in tests / polling mode)
        var userAuthRateLimiter = support.UserAuthRateLimiter;

        // The fact of the call is worth a span whether or not it turns out to address a live
        // transaction, so it opens here and not after the lookups. It belongs to the trace of THIS
        // request — the messenger propagates no trace context, and the sign-in trace that sent the
        // prompt ended long ago; the two are stitched by the correlation attribute set further down.
        // This span is the one place that has to mark an exception itself: the CA-070 invariant makes
        // the handler swallow every one of them and answer 200, so nothing upstream — not the host's
        // request instrumentation, not the caller — is left to record that the event failed. Hence
        // the SetStatus in each catch below; the status discipline the three catches follow is stated
        // once on ChannelActivitySource.
        using var inboundActivity = ChannelActivitySource.Source.StartActivity(ChannelTelemetry.InboundActivityName);
        inboundActivity?.SetTag(ChannelTelemetry.ChannelTypeTag, channelType);

        var cancellationToken = httpContext.RequestAborted;

        // Tenant demux (CA-167/168): determine the tenant BEFORE selecting credentials/adapter.
        var tenantResolution = await ChannelTenantDemux.ResolveTenantAsync(
            httpContext, support.ConfigurationResolver, channelType, logger, cancellationToken);

        if (!tenantResolution.Allowed)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var tenantId = tenantResolution.TenantId;

        // Tenant context for the processing path (CA-164): credentials/client are resolved per tenant.
        using var tenantScope = ChannelTenantContext.BeginScope(tenantId);

        // Find the adapter by (tenant, ChannelType). No adapter — there is nobody to
        // check the signature with, so the request is rejected before it is read: 403 (CA-185).
        var adapter = FindAdapter(adapters, channelType);
        if (adapter is null)
        {
            logger.LogError(
                "Adapter of channel {ChannelType} is not registered. " +
                "Webhook rejected — cannot validate the request without an adapter",
                channelType);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Building the envelope reads the body — the single read of the request, and the first one
        // to touch it. A body over the limit throws here. A failed read is not a validation verdict,
        // so it answers 200 (CA-185), with the known limitation that a connection dropped mid-body
        // lands here too and there is no connection left to answer it on.
        ChannelInboundRequest inboundRequest;
        try
        {
            inboundRequest = await BuildInboundRequestAsync(httpContext.Request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            inboundActivity?.SetStatus(ActivityStatusCode.Error);

            logger.LogWarning(
                ex,
                "Failed to read the webhook body of channel {ChannelType} — event dropped",
                channelType);
            return Results.Ok();
        }

        // Validate the webhook (CA-030) with the resolved tenant's credentials.
        // Validation code may throw (parsing a signature header is a classic case);
        // an exception is treated exactly like a Failure — fail-closed 403, details to the log only,
        // never a 500 that would expose a stack trace on an unauthenticated endpoint.
        // Only a real cancellation of THIS request is rethrown: a bare type check would also let
        // through the TaskCanceledException an HttpClient/Polly timeout inside the adapter raises
        // while the client connection is alive — and that would escape the handler as a 500.
        bool isWebhookValid;
        try
        {
            var validationResult = await adapter.ValidateWebhookAsync(inboundRequest, cancellationToken);
            isWebhookValid = validationResult.IsSuccess && validationResult.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            inboundActivity?.SetStatus(ActivityStatusCode.Error);

            logger.LogError(
                ex,
                "Unhandled exception while validating a webhook of channel {ChannelType}",
                channelType);
            isWebhookValid = false;
        }

        if (!isWebhookValid)
        {
            logger.LogWarning("Invalid webhook of channel {ChannelType} — rejected", channelType);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Everything past a successful validation answers 200 (CA-070): an adapter — which
        // may both return a Failure and throw — may not turn the webhook into a non-2xx answer.
        try
        {
            var inboundResult = await adapter.ProcessInboundEventAsync(inboundRequest, cancellationToken);
            if (inboundResult.IsFailure)
            {
                inboundActivity?.SetStatus(ActivityStatusCode.Error);
                inboundActivity?.SetTag(ChannelTelemetry.ErrorCodeTag, inboundResult.Error.Code);

                logger.LogWarning(
                    "Error processing an inbound event. ChannelType: {ChannelType}, Error: {ErrorCode}",
                    channelType,
                    inboundResult.Error.Code);
                return Results.Ok();
            }

            await ProcessInboundResultAsync(
                inboundResult.Value, adapter, transactionService, transactionStore, timeProvider,
                identityResolutionService, logger,
                metrics,
                userAuthRateLimiter,
                support.PromptOrchestrator,
                support.PromptLocalizer,
                support.PromptMessageStore,
                receiptText,
                displayIntentSource,
                eventPublisher,
                new ChannelReplyTexts(
                    options.ErrorMessageText,
                    options.StaleLinkReplyText,
                    options.UnaddressedReplyText),
                cancellationToken);
        }
        // As above: only a real cancellation of this request escapes; a spurious timeout of a
        // network call inside the adapter stays inside and keeps the CA-070 "always 200" invariant.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            inboundActivity?.SetStatus(ActivityStatusCode.Error);

            logger.LogError(
                ex,
                "Unhandled exception while processing a webhook of channel {ChannelType}",
                channelType);
        }

        // CA-070: always 200 for valid webhooks
        return Results.Ok();
    }

    /// <summary>
    /// Handles the platform's webhook verification handshake: a GET on the webhook path carrying a
    /// challenge the platform expects echoed back. Which query parameter holds the challenge is the
    /// channel's own declaration (<see cref="ChannelRegistrationOptions.WebhookVerificationQueryKey"/>);
    /// what makes the handshake genuine — a verify token, its spelling, the mode — is checked by the
    /// adapter's <c>ValidateWebhookAsync</c>, exactly as on the POST route.
    /// Adapter code is never called outside a catch here either: an exception while building the
    /// envelope or inside <c>ValidateWebhookAsync</c> is a failed handshake, answered fail-closed
    /// with 403 — never a 500 that would expose a stack trace on an unauthenticated endpoint. The
    /// route is open to any registered channel, a third-party one included, so its defence may not
    /// depend on how well-behaved the adapter is.
    /// </summary>
    /// <param name="httpContext">HTTP request context.</param>
    /// <param name="support">Optional dependencies of the pipeline.</param>
    /// <param name="channelType">Channel type of the registered channel.</param>
    /// <param name="challengeQueryKey">Query parameter carrying the challenge to echo back.</param>
    /// <returns>HTTP result of the handshake.</returns>
    private static async Task<IResult> HandleWebhookVerificationAsync(
        HttpContext httpContext,
        ChannelWebhookSupport support,
        string channelType,
        string challengeQueryKey)
    {
        // Get dependencies from DI.
        var adapters = httpContext.RequestServices.GetRequiredService<IEnumerable<IChannelAdapter>>();
        var logger = GetLogger(httpContext);
        var cancellationToken = httpContext.RequestAborted;

        // Tenant demux (CA-167/168): determine the tenant BEFORE selecting credentials/adapter.
        var tenantResolution = await ChannelTenantDemux.ResolveTenantAsync(
            httpContext, support.ConfigurationResolver, channelType, logger, cancellationToken);

        if (!tenantResolution.Allowed)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var tenantId = tenantResolution.TenantId;

        using var tenantScope = ChannelTenantContext.BeginScope(tenantId);

        // Find the adapter by (tenant, ChannelType).
        var adapter = FindAdapter(adapters, channelType);
        if (adapter is null)
        {
            logger.LogError(
                "Adapter of channel {ChannelType} is not registered. " +
                "Webhook verification rejected — cannot validate the verify token.",
                channelType);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Build the envelope and validate the handshake under one guard: on this route both steps
        // have the same failed outcome — the handshake is not proven, so it is refused (403).
        // An exception is treated exactly like a Failure, details to the log only.
        // Only a real cancellation of THIS request is rethrown: a bare type check would also let
        // through the TaskCanceledException an HttpClient/Polly timeout inside the adapter raises
        // while the client connection is alive — and that would escape the handler as a 500.
        bool isHandshakeValid;
        try
        {
            var inboundRequest = await BuildInboundRequestAsync(httpContext.Request, cancellationToken);
            var validationResult = await adapter.ValidateWebhookAsync(inboundRequest, cancellationToken);
            isHandshakeValid = validationResult.IsSuccess && validationResult.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "Unhandled exception while validating a webhook verification of channel {ChannelType}",
                channelType);
            isHandshakeValid = false;
        }

        if (!isHandshakeValid)
        {
            logger.LogWarning(
                "Invalid webhook verification of channel {ChannelType} — rejected", channelType);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Return the challenge upon successful validation.
        var challenge = httpContext.Request.Query[challengeQueryKey].FirstOrDefault();
        if (string.IsNullOrEmpty(challenge))
        {
            logger.LogWarning(
                "Webhook verification of channel {ChannelType} does not contain the challenge parameter",
                channelType);
            return Results.BadRequest();
        }

        return Results.Text(challenge);
    }

    /// <summary>
    /// Processes the inbound event result through the pipeline logic.
    /// Used by polling services for routing in dev mode (without a webhook).
    /// </summary>
    /// <param name="inboundResult">Inbound event processing result.</param>
    /// <param name="adapter">Channel adapter.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="transactionStore">
    /// Transaction store. A decision reported by a channel is read through it rather than through
    /// the TTL gate of the service: an expiry is an outcome this pipeline answers with a receipt of
    /// its own (SPEC-003 CA-192), and the gate hides the very transaction that receipt is worded
    /// from.
    /// </param>
    /// <param name="timeProvider">
    /// Clock the deadline of a transaction is read against. Stated by the caller and never taken
    /// from the ambient system clock: the answer to a decision made after the deadline is the same
    /// before and after the cleanup pass, and that is only observable on a clock the caller drives.
    /// </param>
    /// <param name="identityResolutionService">Channel identity resolution service (TASK-002).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics (from DI) a failed user notification is counted into.</param>
    /// <param name="userAuthRateLimiter">Per-user rate limiter (null — no limit).</param>
    /// <param name="confirmationPromptOrchestrator">Confirmation prompt orchestrator (null — AuthStart auto-confirmation, SPEC-003 §4.8).</param>
    /// <param name="promptLocalizer">Channel message localizer (null — status texts stay in the base language, 1:1).</param>
    /// <param name="promptMessageStore">Store of sent-prompt coordinates (null — the outcome notice carries no prompt reference).</param>
    /// <param name="receiptText">
    /// Terminal text of the transaction: the caller states the address of the receipt and the core
    /// answers with the wording (SPEC-036 TPL-123). MANDATORY — the wordings live in the levelled
    /// configuration of the deployment rather than in a compiled table, so there is no default to
    /// take when it is absent.
    /// </param>
    /// <param name="displayIntentSource">
    /// How the deployment wants the receipt of a terminal outcome to appear in the conversation —
    /// in place of the question that was asked, or as a message of its own. MANDATORY for the reason
    /// <paramref name="receiptText"/> is: the value lives in the levelled configuration of the
    /// deployment, so a caller that stated nothing would have no default to fall back on.
    /// </param>
    /// <param name="eventPublisher">
    /// Bus the refusals of this surface are written to (SPEC-039 E48); null — the composition has no
    /// transaction engine and therefore no journal, and the refusals reach the log alone.
    /// </param>
    /// <param name="replyTexts">
    /// Texts the channel states for itself: the error, and the replies it declares for a sign-in link
    /// that cannot be served and for a message that names no transaction.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The three prompt dependencies are nullable and have no default, exactly like
    /// <paramref name="userAuthRateLimiter"/>: null keeps its declared meaning, but a caller that has
    /// no prompt to send states it by hand, so the confirmation question can no longer be switched
    /// off by an argument nobody wrote (SPEC-039 E41).
    /// </remarks>
    public static async Task ProcessInboundResultAsync(
        ChannelInboundResult inboundResult,
        IChannelAdapter adapter,
        ITransactionService transactionService,
        ITransactionStore transactionStore,
        TimeProvider timeProvider,
        IIdentityResolutionService identityResolutionService,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        IUserAuthRateLimiter? userAuthRateLimiter,
        IConfirmationPromptOrchestrator? confirmationPromptOrchestrator,
        IConfirmationPromptLocalizer? promptLocalizer,
        IChannelPromptMessageStore? promptMessageStore,
        IOutcomeReceiptText receiptText,
        IOutcomeNoticeDisplayIntentSource displayIntentSource,
        ITransactionEventPublisher? eventPublisher,
        ChannelReplyTexts replyTexts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receiptText);
        ArgumentNullException.ThrowIfNull(displayIntentSource);
        ArgumentNullException.ThrowIfNull(replyTexts);

        var errorText = replyTexts.ErrorMessageText;

        // Routing by result type (SPEC-003 §13): each intent brings its own mandatory set, so there
        // is no "which field is valid here" rule left to remember.
        switch (inboundResult)
        {
            case ChannelAuthStartResult start:
                await HandleAuthStartAsync(
                    start, adapter, transactionService, timeProvider, identityResolutionService,
                    logger, metrics,
                    userAuthRateLimiter, confirmationPromptOrchestrator, receiptText,
                    displayIntentSource, eventPublisher,
                    replyTexts, promptLocalizer, cancellationToken);
                break;

            case ChannelAuthConfirmResult confirm:
                await HandleAuthConfirmAsync(
                    confirm, adapter, transactionService, transactionStore, timeProvider,
                    identityResolutionService, logger, metrics,
                    userAuthRateLimiter, confirmationPromptOrchestrator,
                    receiptText, displayIntentSource, eventPublisher, errorText, promptLocalizer,
                    promptMessageStore, cancellationToken);
                break;

            case ChannelAuthDeclineResult decline:
                await HandleAuthDeclineAsync(
                    decline, adapter, transactionService, transactionStore, timeProvider, logger,
                    metrics, confirmationPromptOrchestrator,
                    receiptText, displayIntentSource, eventPublisher, errorText, promptLocalizer,
                    promptMessageStore, cancellationToken);
                break;

            case ChannelPhoneSharedResult phoneShared:
                await HandlePhoneSharedAsync(phoneShared, logger);
                break;

            case ChannelUnaddressedResult unaddressed:
                await HandleUnaddressedAsync(
                    unaddressed, adapter, replyTexts.UnaddressedReplyText, userAuthRateLimiter,
                    promptLocalizer, logger, metrics, cancellationToken);
                break;

            case ChannelUnrelatedResult:
                break;

            default:
                // A further intent added later must not bring the runtime down here.
                logger.LogWarning(
                    "Unknown inbound result type — no action taken. ChannelType: {ChannelType}, ResultType: {ResultType}",
                    adapter.ChannelType,
                    inboundResult.GetType().Name);
                break;
        }
    }

    /// <summary>
    /// Handles the start of authentication (the user followed the deep link).
    /// The mode is determined by the pipeline configuration (SPEC-003 §4.8, §13.2):
    /// AutoConfirm — following the deep link is treated as confirmation of intent;
    /// ConfirmationPrompt — a confirmation message with buttons and initiator context
    /// (SPEC-017) is sent, and the user makes the decision.
    /// An event whose transaction cannot be served at all is answered before any of that
    /// (SPEC-003 CA-200) — with the one text the registration declares for it, or with silence when
    /// it declares none.
    /// This handler shows no outcome itself; it carries the display intent onwards because the
    /// web-confirmation branch it hands over to does answer a closed window with a receipt.
    /// </summary>
    private static async Task HandleAuthStartAsync(
        ChannelAuthStartResult result,
        IChannelAdapter adapter,
        ITransactionService transactionService,
        TimeProvider timeProvider,
        IIdentityResolutionService identityResolutionService,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        IUserAuthRateLimiter? userAuthRateLimiter,
        IConfirmationPromptOrchestrator? confirmationPromptOrchestrator,
        IOutcomeReceiptText receiptText,
        IOutcomeNoticeDisplayIntentSource displayIntentSource,
        ITransactionEventPublisher? eventPublisher,
        ChannelReplyTexts replyTexts,
        IConfirmationPromptLocalizer? promptLocalizer,
        CancellationToken cancellationToken)
    {
        var errorText = replyTexts.ErrorMessageText;

        // The identifier arrives already parsed: the adapter is the one that reads it out of the
        // channel event, so an unparseable value never reaches this point (SPEC-003 §6.3).
        var transactionId = result.TransactionId;

        // Get the transaction for its ConcurrencyToken
        var txResult = await transactionService.GetTransactionAsync(transactionId, cancellationToken);
        if (txResult.IsFailure)
        {
            logger.LogWarning(
                "Failed to get the transaction for AuthStart. TransactionId: {TransactionId}, Error: {ErrorCode} — {ErrorMessage}",
                result.TransactionId,
                txResult.Error.Code,
                txResult.Error.Message);

            metrics.RecordUnservedStart(adapter.ChannelType);

            // A refused read is not the same as a missing record. The engine hands back
            // transaction_expired for a transaction it FOUND whose deadline is behind it and which
            // the cleanup pass has not promoted yet, and transaction_not_found only for an
            // identifier no record answers to. The journal is keyed by the transaction, so the found
            // one is written down under the same code the promoted one gets below — the two are the
            // two sides of that pass (SPEC-039 E48) — while an identifier with nothing behind it
            // writes nothing, or a stranger sending random ones would be the one filling the journal.
            // The counter above is what makes that second branch visible.
            if (txResult.Error.Code is TransactionErrorCodes.TransactionExpired)
            {
                await PublishChannelRefusalAsync(
                    eventPublisher, timeProvider, transactionId, adapter.ChannelType,
                    ChannelInteractionAuditCodes.LinkExpired, txResult.Error.Code, logger);
            }

            // The transaction is not in hand here even when it exists, so the recipient-locale chain
            // (B17) collapses to the channel snapshot locale and then to the base language — the same
            // chain a transaction found in Expired takes below, so neither the side of the cleanup
            // pass nor the existence of the transaction shows through in the language of the answer.
            await ReplyToUnservedStartAsync(
                adapter, result.Identity, replyTexts.StaleLinkReplyText, result.Identity.Locale,
                userAuthRateLimiter, promptLocalizer, logger, metrics, transactionId,
                cancellationToken);
            return;
        }

        TagCorrelatingTrace(txResult.Value);

        // Recipient-locale chain (B17): channel snapshot locale → initiation-page language stored in
        // the transaction → base language (the localizer applies the base fallback for null).
        var recipientLocale = result.Identity.Locale ?? txResult.Value.GetUiLocale();

        // The cleanup pass has already written the expiry down. It is the same occurrence the failed
        // read above answers — the two are simply the two sides of that pass — so it gets the same
        // text (SPEC-003 CA-200). It stands here, before the rate-limit block below, for two reasons:
        // the budget is spent once per event, inside the helper, and the limiter's FailTransactionAsync
        // has nothing to write on a transaction that is already terminal.
        // No other state is looked at: what a transaction that was already decided is answered with
        // belongs to the orchestrator and to the surface it chose.
        if (txResult.Value.State is TransactionState.Expired)
        {
            // The branch leaves a trace of its own whether or not it answers: a channel that declares
            // no text is silent, and without this line the event would leave nothing behind at all.
            logger.LogWarning(
                "AuthStart on a transaction the cleanup pass has already expired. " +
                "TransactionId: {TransactionId}, State: {State}",
                transactionId,
                txResult.Value.State);

            metrics.RecordUnservedStart(adapter.ChannelType);

            // The transaction was found, so this refusal reaches the journal (SPEC-039 E48). No
            // engine code went with it — the state itself is the reason — hence no details.
            await PublishChannelRefusalAsync(
                eventPublisher, timeProvider, transactionId, adapter.ChannelType,
                ChannelInteractionAuditCodes.LinkExpired, refusalErrorCode: null, logger);

            // The snapshot locale alone, not the recipientLocale chain above: the answer must read
            // the same on both sides of the cleanup pass, and the side that has not been promoted yet
            // has no transaction in hand to take a language off. Preferring the better language here
            // would let the recipient tell a transaction that exists from one that never did — the
            // very distinction the single text is there to withhold (SPEC-039 C15 / R51).
            await ReplyToUnservedStartAsync(
                adapter, result.Identity, replyTexts.StaleLinkReplyText, result.Identity.Locale,
                userAuthRateLimiter, promptLocalizer, logger, metrics, transactionId,
                cancellationToken);
            return;
        }

        // Everything below acts on the transaction for this sender: the question with the subject is
        // sent, the transaction is failed (an exhausted budget, a channel that cannot continue, a
        // subject that cannot be worded), the identity is attached, or the sign-in is finalized — and
        // the finalization succeeds on its idempotent branch without asking who is writing. So the
        // sender is gated once, here, for the whole start. A refused sender is answered with silence
        // on every surface: a sender the transaction does not admit must not be able to make the core
        // send it a message (SPEC-003 CA-200 states it for the web-page surface, and which surface
        // applies is resolved only below). The budget is not taken either — there is nothing to send,
        // and the limiter's own refusal fails the transaction, which is exactly what this sender may
        // not cause.
        if (!await IsSenderAdmittedAsync(
                txResult.Value, result.Identity, adapter, transactionService, eventPublisher,
                timeProvider, logger, cancellationToken))
        {
            metrics.RecordUnservedStart(adapter.ChannelType);
            return;
        }

        // Per-user rate limiting: check before confirmation. The partition is scoped to the
        // auth-start operation so this check does not drain the confirmation/token budgets
        // of the same user (see UserAuthRateLimitOperations).
        if (userAuthRateLimiter is not null)
        {
            var identitySnapshot = result.Identity;
            var userKey = NormalizeUserKey(identitySnapshot);
            var rateLimitResult = await userAuthRateLimiter.TryAcquireAsync(
                UserAuthRateLimitOperations.AuthStart + userKey, cancellationToken);

            if (!rateLimitResult.IsAllowed)
            {
                logger.LogWarning(
                    "Per-user rate limit exceeded in AuthStart. TransactionId: {TransactionId}, UserKeyHash: {UserKeyHash}",
                    result.TransactionId,
                    LogMasking.Fingerprint(userKey));

                var failRlResult = await transactionService.FailTransactionAsync(
                    transactionId,
                    TransactionErrorCodes.UserRateLimitExceeded,
                    txResult.Value.ConcurrencyToken,
                    CancellationToken.None);

                if (failRlResult.IsFailure)
                {
                    logger.LogWarning(
                        "Failed to move the transaction to Failed (rate limit, AuthStart). TransactionId: {TransactionId}",
                        result.TransactionId);
                }

                // AuthStart is not a transaction outcome, so this stays a plain message and is not
                // gated by the outcome capability — a channel that shows no outcome still gets it.
                await SendUserMessageAsync(
                    adapter,
                    result.Identity.ChannelUserId,
                    Localize(promptLocalizer, MessageTemplateNaturalKeys.OutcomeTooManyAttempts, recipientLocale),
                    logger,
                    metrics,
                    cancellationToken);
                return;
            }
        }

        // The orchestrator resolves the effective surface (SPEC-012 §4.4) and returns what to do:
        // InChannel — a confirmation message with buttons and initiator context (SPEC-017) has been
        // sent, the transaction will be confirmed after a button press (AuthConfirm); OnWebPage — the
        // question belongs to the core page, so the channel gets nothing while it is answered there
        // (a transaction that refuses the event is a different matter — CA-200); None — the previous
        // auto-confirmation applies.
        if (confirmationPromptOrchestrator is not null)
        {
            var outcome = await confirmationPromptOrchestrator.HandleAuthStartConfirmationAsync(
                txResult.Value,
                adapter,
                result.Identity.ChannelUserId,
                recipientLocale,
                cancellationToken);

            if (outcome is AuthStartConfirmationOutcome.HandledInChannel)
            {
                return;
            }

            if (outcome is AuthStartConfirmationOutcome.AwaitingWebConfirmation)
            {
                await AttachIdentityForWebConfirmationAsync(
                    result.Identity, txResult.Value, adapter, transactionService, timeProvider,
                    recipientLocale, replyTexts.StaleLinkReplyText, receiptText, displayIntentSource,
                    promptLocalizer, eventPublisher, logger, metrics, cancellationToken);
                return;
            }

            if (outcome is AuthStartConfirmationOutcome.ChannelCannotContinue)
            {
                await FailForChannelCannotContinueAsync(
                    transactionId, transactionService, txResult.Value.ConcurrencyToken, logger);

                // Nothing is sent to the user in the channel — it has just declared it cannot carry
                // the confirmation out — and auto-confirmation is not performed either. The user
                // sees the error in the browser through the existing Failed-status branch.
                return;
            }

            if (outcome is AuthStartConfirmationOutcome.ConfirmationSubjectUnavailable)
            {
                // There is no wording of the subject, so the question was not asked (SPEC-039 E28).
                // Waiting out the TTL would leave the user in front of a channel that answered
                // nothing, and auto-confirming would complete an action nobody was shown: the
                // transaction ends here, and the user gets the channel's own neutral error text —
                // the one its options carry, not a wording of this particular cause.
                await FailForConfirmationSubjectUnavailableAsync(
                    transactionId, transactionService, txResult.Value.ConcurrencyToken, logger);

                await SendUserMessageAsync(
                    adapter,
                    result.Identity.ChannelUserId,
                    Localize(promptLocalizer, errorText, recipientLocale),
                    logger,
                    metrics,
                    cancellationToken);
                return;
            }
        }

        // Following the deep link IS the decision on this path (SPEC-003 §4.8), so it passes the same
        // gate as a decision reported later (SPEC-039 E41). The gate matters here precisely when the
        // branch above made no decision at all — no confirmation orchestration is wired, so nothing
        // asked the question and nothing can establish that the subject was shown. The transaction is
        // then left as it is, to its TTL: auto-confirming it would complete the action the relying
        // party named without ever showing it, which is what E41 exists to prevent.
        if (!await AcceptsChannelDecisionAsync(
                txResult.Value, adapter, confirmationPromptOrchestrator, logger, cancellationToken))
        {
            return;
        }

        // Automatically confirm and complete the transaction (SPEC-003 §4.8)
        var finalizeResult = await ChannelTransactionFinalizer.ConfirmAndCompleteAsync(
            result.Identity, transactionId, transactionService,
            identityResolutionService, txResult.Value.ConcurrencyToken, logger, cancellationToken);
        var completed = finalizeResult.IsSuccess;

        // Send a text notification (without buttons). A confirmed outcome is a RECEIPT — the core
        // states its wording; an error stays the channel's own text and is localized here.
        var outcomeText = completed
            ? await receiptText.RenderAsync(
                ReceiptAddressOf(TransactionOutcome.Confirmed, adapter, txResult.Value),
                recipientLocale,
                txResult.Value.GetUiTimeZone(),
                // The moment of the outcome is the one the completion wrote down, read off the
                // instance the finalizer returned: the one held before the call is the stale one.
                finalizeResult.Value.UpdatedAt,
                // The gate above accepted this channel, so the question stood before this user here.
                TransactionSlotSource.From(txResult.Value),
                TransactionResolutionContext.For(txResult.Value),
                cancellationToken)
            : Localize(promptLocalizer, errorText, recipientLocale);

        await SendUserMessageAsync(
            adapter,
            result.Identity.ChannelUserId,
            outcomeText,
            logger,
            metrics,
            cancellationToken);
    }

    /// <summary>
    /// The address of a receipt shown INSIDE a channel for a transaction the point has loaded: what
    /// the render point knows, and nothing more. The wording of that address is chosen by the
    /// resolution (SPEC-036 TPL-116), never here.
    /// </summary>
    /// <param name="outcome">Terminal outcome being reported.</param>
    /// <param name="adapter">Channel adapter the receipt travels through.</param>
    /// <param name="transaction">Transaction the decision was made on.</param>
    /// <returns>The address of the receipt.</returns>
    private static OutcomeReceiptAddress ReceiptAddressOf(
        TransactionOutcome outcome,
        IChannelAdapter adapter,
        Transaction transaction) =>
        new(outcome,
            OutcomeReceiptSurfaces.ForChannel(adapter),
            transaction.Type,
            transaction.ConfirmationSnapshot?.ActionType,
            adapter.ChannelType);

    /// <summary>
    /// The address of a receipt shown inside a channel when the transaction could NOT be read
    /// (SPEC-003 CA-192): the type of a transaction that is not there is unknowable, so the address
    /// states none and the wording that narrows by no type answers instead
    /// (SPEC-036 TPL-107 — the one deliberate departure from the byte-for-byte rule TPL-098).
    /// </summary>
    /// <param name="outcome">Terminal outcome being reported.</param>
    /// <param name="adapter">Channel adapter the receipt travels through.</param>
    /// <returns>The address of the receipt.</returns>
    private static OutcomeReceiptAddress ReceiptAddressWithoutTransaction(
        TransactionOutcome outcome,
        IChannelAdapter adapter) =>
        new(outcome,
            OutcomeReceiptSurfaces.ForChannel(adapter),
            ChannelType: adapter.ChannelType);

    /// <summary>
    /// Reads the transaction a channel decision names, from the STORE — past the TTL gate of the
    /// transaction service, which answers an expiry by withholding the transaction.
    /// </summary>
    /// <remarks>
    /// A store that cannot answer must not leave a pressed button unanswered (SPEC-003 CA-192), so a
    /// failure of the read is turned into the same "nothing to answer with" a missing transaction
    /// produces; why it failed stays in the deployment's log. Only a real cancellation of the request
    /// escapes.
    /// </remarks>
    /// <param name="transactionStore">Transaction store.</param>
    /// <param name="transactionId">Transaction identifier the decision names.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transaction, or null when there is none to answer with.</returns>
    private static async Task<Transaction?> ReadForDecisionAsync(
        ITransactionStore transactionStore,
        TransactionId transactionId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transactionStore.GetByIdAsync(transactionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Failed to read the transaction a channel decision names. TransactionId: {TransactionId}",
                transactionId);
            return null;
        }
    }

    /// <summary>
    /// The receipt of a closed sign-in window, worded for the transaction that closed. The ONE place
    /// its address is built, so the pre-check and the refusal of a write answer the same occurrence
    /// with the same wording.
    /// </summary>
    /// <param name="adapter">Channel adapter the receipt travels through.</param>
    /// <param name="transaction">Transaction whose window closed.</param>
    /// <param name="slotSource">Source of the values the receipt may be worded with — of the same
    /// transaction, carrying its caller values where the question of the transaction stood before this
    /// user in this channel and none where it never did (SPEC-036 TPL-123, TPL-124). Stated by the
    /// caller because only the caller knows which of the two it is.</param>
    /// <param name="recipientLocale">Recipient locale (null — base language).</param>
    /// <param name="receiptText">Port the terminal wording comes from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The receipt text, already localized.</returns>
    private static ValueTask<string> ExpiredReceiptAsync(
        IChannelAdapter adapter,
        Transaction transaction,
        TransactionSlotSource slotSource,
        string? recipientLocale,
        IOutcomeReceiptText receiptText,
        CancellationToken cancellationToken)
        => receiptText.RenderAsync(
            ReceiptAddressOf(TransactionOutcome.Expired, adapter, transaction),
            recipientLocale,
            transaction.GetUiTimeZone(),
            // The moment of THIS outcome is the deadline the transaction ran into, not the moment the
            // button was pressed — the same moment the confirmation page states for the same event.
            transaction.ExpiresAt,
            slotSource,
            TransactionResolutionContext.For(transaction),
            cancellationToken);

    /// <summary>
    /// Reads a refused write of the user's decision by its class (SPEC-003 CA-192) and states what
    /// the sender is answered with.
    /// </summary>
    /// <remarks>
    /// The REASON of the refusal is never disclosed to the sender; what the transaction ended in is.
    /// An expiry is told as an expiry because that is what happened to the transaction, and a
    /// transaction that a parallel decision has already finished is told by the receipt of that
    /// outcome — pressing the other button earns the same answer pressing the same one twice does.
    /// Everything left is the channel's own error text. The code goes to the deployment's log
    /// instead — at a level that says whose problem it is: a closed window is a normal end, a
    /// decision recorded in parallel is a benign race, and a refusal of ours is a warning.
    /// <para>
    /// The expiry and the race are also the refusals the integrator's journal records (SPEC-039 E48),
    /// and this is the one place they are written from, so a press reaches the journal once. A race
    /// that turns out to be a repeat of the very decision the press asked for is not a refusal — the
    /// same press delivered twice — and writes nothing, exactly as a repeated confirmation, which the
    /// engine answers with success, writes nothing. A refusal of ours is not a refusal of the surface
    /// and stays in the log.
    /// </para>
    /// </remarks>
    /// <param name="errorCode">Code the write of the decision failed with.</param>
    /// <param name="pressedDecision">Outcome the pressed button asked for — tells a repeat of the same
    /// decision from a decision that someone else recorded.</param>
    /// <param name="transaction">Transaction as it was read before the write.</param>
    /// <param name="adapter">Channel adapter the decision arrived through.</param>
    /// <param name="transactionStore">Transaction store the single re-read goes to.</param>
    /// <param name="eventPublisher">Bus the refusal is written to (null — no journal).</param>
    /// <param name="timeProvider">Clock the moment of the audit event is read off.</param>
    /// <param name="recipientLocale">Recipient locale (null — base language).</param>
    /// <param name="receiptText">Port the terminal wording comes from.</param>
    /// <param name="promptLocalizer">Channel message localizer (null — base language).</param>
    /// <param name="errorText">Message text shown to the user on error.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What to show the sender.</returns>
    private static async Task<RefusedDecisionAnswer> AnswerToRefusedDecisionAsync(
        string errorCode,
        TransactionOutcome pressedDecision,
        Transaction transaction,
        IChannelAdapter adapter,
        ITransactionStore transactionStore,
        ITransactionEventPublisher? eventPublisher,
        TimeProvider timeProvider,
        string? recipientLocale,
        IOutcomeReceiptText receiptText,
        IConfirmationPromptLocalizer? promptLocalizer,
        string errorText,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        switch (DecisionRefusalClassifier.Classify(errorCode))
        {
            case DecisionRefusalClass.Expired:
                // The window closed between the read and the write: the same occurrence the pre-check
                // answers, and a normal end of a sign-in rather than a failure of anything.
                logger.LogInformation(
                    "The decision arrived after the sign-in window closed. TransactionId: {TransactionId}, Error: {ErrorCode}",
                    transaction.Id,
                    errorCode);

                await PublishChannelRefusalAsync(
                    eventPublisher, timeProvider, transaction.Id, adapter.ChannelType,
                    ChannelInteractionAuditCodes.LinkExpired, errorCode, logger);

                // A write is attempted only past the decision gate, so the question of this transaction
                // stood before this user in this channel and the receipt may name its subject.
                return new RefusedDecisionAnswer(
                    TransactionOutcome.Expired,
                    await ExpiredReceiptAsync(
                        adapter, transaction, TransactionSlotSource.From(transaction),
                        recipientLocale, receiptText, cancellationToken));

            case DecisionRefusalClass.Race:
                {
                    // A decision was recorded in parallel — a second delivery of the same press, the web
                    // page answering at the same moment, the other button of a question already answered.
                    // What the transaction has BECOME decides the answer, so it is re-read: once, and the
                    // sender is told the outcome it actually ended in. In this delivery that answer is
                    // the same whichever button was pressed — a decision that stands is not re-opened by
                    // pressing the other one, and reporting a failure instead told the user their own
                    // finished sign-in had gone wrong.
                    logger.LogDebug(
                        "The decision was refused — one is already recorded. TransactionId: {TransactionId}, Error: {ErrorCode}",
                        transaction.Id,
                        errorCode);

                    var current = await ReadForDecisionAsync(
                        transactionStore, transaction.Id, logger, cancellationToken);
                    var ending = current is null ? null : TerminalTransactionOutcome.Of(current);

                    // The journal is told what refused the press: an expiry is the same occurrence on
                    // either side of the cleanup pass, so a transaction the pass promoted under the
                    // write keeps the expiry code; any other recorded decision — or one not readable
                    // at all — is a transaction that no longer accepts the event. A repeat of the very
                    // decision this press asked for is no refusal and writes nothing.
                    if (ending != pressedDecision)
                    {
                        await PublishChannelRefusalAsync(
                            eventPublisher, timeProvider, transaction.Id, adapter.ChannelType,
                            ending == TransactionOutcome.Expired
                                ? ChannelInteractionAuditCodes.LinkExpired
                                : ChannelInteractionAuditCodes.LinkNotAnswerable,
                            errorCode, logger);
                    }

                    // The answer for every case below in which there is no outcome to state — the same
                    // one the sender got before this branch learned to state any.
                    var errorAnswer = new RefusedDecisionAnswer(
                        TransactionOutcome.Failed, Localize(promptLocalizer, errorText, recipientLocale));

                    // The re-read found nothing to read an outcome off.
                    if (current is null)
                    {
                        return errorAnswer;
                    }

                    // Nothing has ended yet (a decision is written but not finalized), or it ended in a
                    // failure. A failure has no receipt of its own — OutcomeReceiptAddress.Kind is
                    // declared for the other three and throws on this one — so the channel's error text
                    // stays the answer for both.
                    if (ending is null || ending.Value == TransactionOutcome.Failed)
                    {
                        return errorAnswer;
                    }

                    var endedIn = ending.Value;

                    if (endedIn == TransactionOutcome.Expired)
                    {
                        return new RefusedDecisionAnswer(
                            TransactionOutcome.Expired,
                            await ExpiredReceiptAsync(
                                adapter, current, TransactionSlotSource.From(current),
                                recipientLocale, receiptText, cancellationToken));
                    }

                    // Confirmed or declined — what someone's decision put on the transaction. Telling a
                    // sender that is a disclosure, and it is made here only because both callers gated
                    // the sender before the write, with the engine's own admissibility rule: a sender
                    // the transaction does not admit never reaches this branch.
                    logger.LogInformation(
                        "Repeated decision — the sender is answered with the receipt of the recorded outcome. TransactionId: {TransactionId}, Outcome: {Outcome}",
                        current.Id,
                        endedIn.Value);

                    // A write is attempted only past the decision gate, so the question of this
                    // transaction stood before this user in this channel and the receipt may name its
                    // subject — the same reading the expiry branch above states.
                    return new RefusedDecisionAnswer(
                        endedIn,
                        await receiptText.RenderAsync(
                            ReceiptAddressOf(endedIn, adapter, current),
                            recipientLocale,
                            current.GetUiTimeZone(),
                            // The moment of the outcome is the one the decision wrote down, read off the
                            // re-read instance: the one held before the write is the stale one.
                            current.UpdatedAt,
                            TransactionSlotSource.From(current),
                            TransactionResolutionContext.For(current),
                            cancellationToken));
                }

            default:
                // Something of ours refused — the store, the identity resolver, a channel the
                // transaction does not admit. The sender is told that it did not work and nothing
                // about why; the code is here.
                logger.LogWarning(
                    "Failed to record the decision reported by the channel. TransactionId: {TransactionId}, Error: {ErrorCode}",
                    transaction.Id,
                    errorCode);

                return new RefusedDecisionAnswer(
                    TransactionOutcome.Failed, Localize(promptLocalizer, errorText, recipientLocale));
        }
    }

    /// <summary>
    /// Records the decision the channel forced (SPEC-003 CA-191) on the transaction itself: the
    /// terminal state plus the reason code, which is what both late readers — the sign-in page
    /// polling and the web callback — already know how to show.
    /// </summary>
    /// <remarks>
    /// <see cref="CancellationToken.None"/> for the same reason as the rate-limit branch: the
    /// decision is already made, and a cancelling request must not leave the transaction hanging
    /// until its TTL. A refused transition means a concurrent delivery recorded the decision first —
    /// a benign race that must not add noise to monitoring, hence a level below Warning; any other
    /// failure leaves the transaction to expire and IS worth a warning.
    /// </remarks>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="concurrencyToken">Concurrency token of the read transaction.</param>
    /// <param name="logger">Logger.</param>
    private static async Task FailForChannelCannotContinueAsync(
        TransactionId transactionId,
        ITransactionService transactionService,
        string concurrencyToken,
        ILogger logger)
    {
        var failResult = await transactionService.FailTransactionAsync(
            transactionId,
            TransactionErrorCodes.ChannelCannotContinue,
            concurrencyToken,
            CancellationToken.None);

        if (failResult.IsSuccess)
        {
            return;
        }

        if (failResult.Error.Code is TransactionErrorCodes.InvalidStateTransition)
        {
            logger.LogDebug(
                "The transaction decision is already recorded — the channel refusal changes nothing. TransactionId: {TransactionId}",
                transactionId);
            return;
        }

        logger.LogWarning(
            "Failed to move the transaction to Failed (the channel cannot continue). TransactionId: {TransactionId}, Error: {ErrorCode}",
            transactionId,
            failResult.Error.Code);
    }

    /// <summary>
    /// Records the core's own refusal to ask a question it has no wording for (SPEC-039 E28) on the
    /// transaction itself: the terminal state plus the reason code both late readers — the status
    /// poll of the relying party and the web callback — already know how to show.
    /// </summary>
    /// <remarks>
    /// The twin of <see cref="FailForChannelCannotContinueAsync"/>, down to the treatment of a refused
    /// transition as a benign race: the decision is already made, so a cancelling request must not
    /// leave the transaction hanging until its TTL.
    /// </remarks>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="concurrencyToken">Concurrency token of the read transaction.</param>
    /// <param name="logger">Logger.</param>
    private static async Task FailForConfirmationSubjectUnavailableAsync(
        TransactionId transactionId,
        ITransactionService transactionService,
        string concurrencyToken,
        ILogger logger)
    {
        var failResult = await transactionService.FailTransactionAsync(
            transactionId,
            TransactionErrorCodes.ConfirmationTemplateUnavailable,
            concurrencyToken,
            CancellationToken.None);

        if (failResult.IsSuccess)
        {
            return;
        }

        if (failResult.Error.Code is TransactionErrorCodes.InvalidStateTransition)
        {
            logger.LogDebug(
                "The transaction decision is already recorded — the missing subject changes nothing. TransactionId: {TransactionId}",
                transactionId);
            return;
        }

        logger.LogWarning(
            "Failed to move the transaction to Failed (the subject of the confirmation is unavailable). TransactionId: {TransactionId}, Error: {ErrorCode}",
            transactionId,
            failResult.Error.Code);
    }

    /// <summary>
    /// The caller slot values an expiry receipt answered BEFORE the decision gate may be worded with:
    /// the transaction's own when its question stood before the user in this channel, null otherwise
    /// (SPEC-036 TPL-123).
    /// </summary>
    /// <remarks>
    /// The expiry pre-check answers ahead of the gate on purpose — a pressed button is always answered
    /// (SPEC-003 CA-192) — so it also answers a channel the question was never sent to (SPEC-039 E41).
    /// That intersection is resolved by the receipt staying neutral to the subject, so the values are
    /// handed over only where the channel is the one the question was asked in, and the answer comes
    /// from the same gate every decision passes. The gate is asked only when there is something to
    /// name: a sign-in transaction and a confirmation without caller values render the same text either
    /// way, and no question to the surface is spent on them. A path wiring no confirmation orchestration
    /// never asked a confirmation question at all, so it has no values to hand over either.
    /// </remarks>
    /// <param name="transaction">Transaction whose window closed.</param>
    /// <param name="adapter">Channel adapter the decision arrived through.</param>
    /// <param name="confirmationPromptOrchestrator">Confirmation prompt orchestrator (null — none is
    /// wired on this path).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The caller slot values, or null.</returns>
    private static async ValueTask<IReadOnlyDictionary<string, string>?> ShownCallerSlotValuesAsync(
        Transaction transaction,
        IChannelAdapter adapter,
        IConfirmationPromptOrchestrator? confirmationPromptOrchestrator,
        CancellationToken cancellationToken)
    {
        if (transaction.ConfirmationSnapshot?.SlotValues is not { Count: > 0 } values
            || confirmationPromptOrchestrator is null)
        {
            return null;
        }

        return await confirmationPromptOrchestrator.AcceptsChannelDecisionAsync(
                transaction, adapter, cancellationToken)
            ? values
            : null;
    }

    /// <summary>
    /// Whether a decision reported by a channel may be acted on (SPEC-039 E41). The guarantee "the
    /// subject is always shown before it is confirmed" is held by the core for EVERY channel alike,
    /// third-party ones included, and never by the discipline of an adapter's own code.
    /// </summary>
    /// <remarks>
    /// Every way a channel decides the fate of a transaction passes here — the reported confirmation,
    /// the reported decline, and the auto-confirmation a followed deep link stands for. A sign-in
    /// transaction is out of scope in every branch: nothing about its paths changes.
    /// </remarks>
    /// <param name="transaction">Transaction the decision arrived for.</param>
    /// <param name="adapter">Channel adapter the decision arrived through.</param>
    /// <param name="confirmationPromptOrchestrator">Confirmation prompt orchestrator (null — the
    /// caller wired no confirmation orchestration at all).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> — the decision may be acted on.</returns>
    private static async ValueTask<bool> AcceptsChannelDecisionAsync(
        Transaction transaction,
        IChannelAdapter adapter,
        IConfirmationPromptOrchestrator? confirmationPromptOrchestrator,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (confirmationPromptOrchestrator is not null)
        {
            return await confirmationPromptOrchestrator.AcceptsChannelDecisionAsync(
                transaction, adapter, cancellationToken);
        }

        // No confirmation orchestration is wired here, so this path never asked the question and has
        // no way of establishing that the subject was shown. A sign-in transaction is unaffected —
        // it is accepted, and the caller's auto-confirmation applies to it.
        if (string.Equals(transaction.Type, TransactionTypes.Confirmation, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "A decision on a confirmation arrived on a path that asks no confirmation question — the decision is dropped. ChannelType: {ChannelType}, TransactionId: {TransactionId}",
                adapter.ChannelType,
                transaction.Id.ToString());

            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles authentication confirmation (an inline-keyboard button press).
    /// </summary>
    /// <param name="result">Inbound event result with confirmation data.</param>
    /// <param name="adapter">Channel adapter.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="transactionStore">Transaction store the decision is read through.</param>
    /// <param name="timeProvider">Clock the deadline of the transaction is read against.</param>
    /// <param name="identityResolutionService">Channel identity resolution service.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics a failed user notification is counted into.</param>
    /// <param name="userAuthRateLimiter">Per-user rate limiter (null — no limit).</param>
    /// <param name="confirmationPromptOrchestrator">Confirmation prompt orchestrator — the point that
    /// answers whether this channel was the one asked to show the question (SPEC-039 E41).</param>
    /// <param name="receiptText">Terminal text of the transaction (SPEC-036 TPL-123).</param>
    /// <param name="displayIntentSource">Port the desired display intent of the receipt comes from.</param>
    /// <param name="eventPublisher">Bus the refusals of the press are written to — a late press, a
    /// decision already recorded, a sender not admitted (SPEC-039 E48; null — no journal).</param>
    /// <param name="errorText">Message text shown to the user on error.</param>
    /// <param name="promptLocalizer">Channel message localizer (null — base language).</param>
    /// <param name="promptMessageStore">Store of sent-prompt coordinates (null — no prompt reference).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task HandleAuthConfirmAsync(
        ChannelAuthConfirmResult result,
        IChannelAdapter adapter,
        ITransactionService transactionService,
        ITransactionStore transactionStore,
        TimeProvider timeProvider,
        IIdentityResolutionService identityResolutionService,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        IUserAuthRateLimiter? userAuthRateLimiter,
        IConfirmationPromptOrchestrator? confirmationPromptOrchestrator,
        IOutcomeReceiptText receiptText,
        IOutcomeNoticeDisplayIntentSource displayIntentSource,
        ITransactionEventPublisher? eventPublisher,
        string errorText,
        IConfirmationPromptLocalizer? promptLocalizer,
        IChannelPromptMessageStore? promptMessageStore,
        CancellationToken cancellationToken)
    {
        // The identifier arrives already parsed: the adapter is the one that reads it out of the
        // channel event, so an unparseable value never reaches this point (SPEC-003 §6.3).
        var transactionId = result.TransactionId;

        // Read the transaction for its ConcurrencyToken — through the STORE, past the TTL gate of
        // the service: what happens to a transaction whose time is up is decided below, and a gate
        // that answers "expired" by withholding it would leave the receipt nothing to be worded from.
        var transaction = await ReadForDecisionAsync(
            transactionStore, transactionId, logger, cancellationToken);

        if (transaction is null)
        {
            logger.LogWarning(
                "No transaction to answer the AuthConfirm with. TransactionId: {TransactionId}",
                result.TransactionId);

            // A pressed button is always answered (SPEC-003 §13.3, CA-192): a transaction that is
            // not there is an outcome too, and leaving here would keep the platform's loading
            // indicator spinning until its own timeout. "Never existed" and "reaped long ago" are
            // not told apart — to the user both mean the same thing. There is no transaction, so the
            // recipient-locale chain (B17) collapses to the channel snapshot locale and then to the
            // base language.
            await ShowOutcomeAsync(
                adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
                TransactionOutcome.Expired,
                await receiptText.RenderAsync(
                    ReceiptAddressWithoutTransaction(TransactionOutcome.Expired, adapter),
                    result.Identity.Locale,
                    // Neither the zone nor the moment of the outcome has a source on this branch: the
                    // transaction they are both read off is the one that could not be read. Stating
                    // "now" instead would be this handler's own clock passed off as the moment the
                    // transaction ended, so nothing is stated and a moment falls back to UTC.
                    recipientTimeZone: null,
                    outcomeMoment: null,
                    // Nor any value of the transaction: they are read off the same transaction
                    // (SPEC-036 TPL-107).
                    transaction: null,
                    // There is no transaction to take ownership from — that is this branch. The only
                    // level left is the tenant of the ambient channel scope, which the single context
                    // builder puts in for a null transaction.
                    TransactionResolutionContext.For(null),
                    cancellationToken),
                promptMessageStore,
                displayIntentSource,
                // The same context the wording above is addressed with: the receipt and the way it is
                // shown are one answer about one notice.
                TransactionResolutionContext.For(null),
                logger, metrics, cancellationToken);
            return;
        }

        // Recipient-locale chain (B17): channel snapshot locale → initiation-page language → base.
        var recipientLocale = result.Identity.Locale ?? transaction.GetUiLocale();

        // The window closed before the button was pressed. The answer is the receipt of an expiry —
        // the SAME one on both sides of the cleanup pass, which is the whole point of reading past
        // the gate. Nothing is written down and no state is changed: the transaction is already over,
        // and the cleanup will record it in its own time. Asked before the decision gate and before
        // the rate limiter: a transaction whose time is up spends not the user's budget, and asks the
        // surface only whether its receipt may name the subject — and only when there is a subject
        // to name (SPEC-036 TPL-123).
        if (transaction.HasRunOutOfTime(timeProvider.GetUtcNow()))
        {
            // The transaction was found, so the late press reaches the journal (SPEC-039 E48). No
            // engine code went with it — the deadline or the state itself is the reason.
            await PublishChannelRefusalAsync(
                eventPublisher, timeProvider, transactionId, adapter.ChannelType,
                ChannelInteractionAuditCodes.LinkExpired, refusalErrorCode: null, logger);

            await ShowOutcomeAsync(
                adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
                TransactionOutcome.Expired,
                await ExpiredReceiptAsync(
                    adapter,
                    transaction,
                    // The gate of the caller values cuts off what the calling party supplied and
                    // nothing the core states about the transaction.
                    TransactionSlotSource.From(transaction) with
                    {
                        CallerSlotValues = await ShownCallerSlotValuesAsync(
                            transaction, adapter, confirmationPromptOrchestrator, cancellationToken)
                    },
                    recipientLocale,
                    receiptText,
                    cancellationToken),
                promptMessageStore,
                displayIntentSource,
                // The context the expiry helper addresses its wording with — the transaction in hand.
                TransactionResolutionContext.For(transaction),
                logger, metrics, cancellationToken);
            return;
        }

        TagCorrelatingTrace(transaction);

        // A confirmation is only answered by the channel its subject was shown in (SPEC-039 E41).
        // Nothing is changed and nothing is sent back: the channel was never asked, so it gets no
        // reply either.
        if (!await AcceptsChannelDecisionAsync(
                transaction, adapter, confirmationPromptOrchestrator, logger, cancellationToken))
        {
            return;
        }

        // A transaction that has already answered is finalized SUCCESSFULLY by the step below: the
        // idempotent branch of the write returns the recorded state without asking whether this sender
        // may act on the transaction at all, and the receipt built afterwards states that recorded
        // outcome — a disclosure. The limiter below, when the budget is spent, fails the transaction —
        // an action on it. So the gate every write of this transaction passes is applied here,
        // explicitly, before either. It is applied whatever state was read: the transaction may be
        // decided elsewhere between the read above and the write below, and a gate keyed on the state
        // read would miss exactly that case. What it checks — the allow-list and the application the
        // bot policy resolves for — does not change over the life of a transaction, so the instance
        // read above answers it. A sender the gate refuses gets the same answer the write itself would
        // have produced for them; a pressed button is answered regardless (SPEC-003 CA-192).
        if (!await IsSenderAdmittedAsync(
                transaction, result.Identity, adapter, transactionService, eventPublisher,
                timeProvider, logger, cancellationToken))
        {
            await ShowOutcomeAsync(
                adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
                TransactionOutcome.Failed,
                Localize(promptLocalizer, errorText, recipientLocale),
                promptMessageStore,
                displayIntentSource,
                TransactionResolutionContext.For(transaction),
                logger, metrics, cancellationToken);
            return;
        }

        // Per-user rate limiting (SPEC-002): check BEFORE ConfirmTransactionAsync.
        // The partition is scoped to the confirmation operation (see UserAuthRateLimitOperations).
        if (userAuthRateLimiter is not null)
        {
            var identitySnapshot = result.Identity;
            var userKey = NormalizeUserKey(identitySnapshot);
            var rateLimitResult = await userAuthRateLimiter.TryAcquireAsync(
                UserAuthRateLimitOperations.AuthConfirm + userKey, cancellationToken);

            if (!rateLimitResult.IsAllowed)
            {
                logger.LogWarning(
                    "Per-user rate limit exceeded. TransactionId: {TransactionId}, UserKeyHash: {UserKeyHash}",
                    result.TransactionId,
                    LogMasking.Fingerprint(userKey));

                var failRlResult = await transactionService.FailTransactionAsync(
                    transactionId,
                    TransactionErrorCodes.UserRateLimitExceeded,
                    transaction.ConcurrencyToken,
                    CancellationToken.None);

                if (failRlResult.IsFailure)
                {
                    logger.LogWarning(
                        "Failed to move the transaction to Failed (rate limit). TransactionId: {TransactionId}, Error: {ErrorCode}",
                        result.TransactionId,
                        failRlResult.Error.Code);
                }

                // The transaction was moved to Failed, so the user is shown the error outcome — with
                // the wording of an exhausted budget rather than the channel's generic error: what
                // to do next after this refusal is to wait, and telling them to start again would
                // spend the budget they have just run out of.
                await ShowOutcomeAsync(
                    adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
                    TransactionOutcome.Failed,
                    Localize(promptLocalizer, MessageTemplateNaturalKeys.OutcomeTooManyAttempts, recipientLocale),
                    promptMessageStore,
                    displayIntentSource,
                    TransactionResolutionContext.For(transaction),
                    logger, metrics, cancellationToken);
                return;
            }
        }

        // Confirm and complete the transaction
        var finalizeResult = await ChannelTransactionFinalizer.ConfirmAndCompleteAsync(
            result.Identity, transactionId, transactionService,
            identityResolutionService, transaction.ConcurrencyToken, logger, cancellationToken);

        if (finalizeResult.IsFailure)
        {
            // A refusal is read by its class, not reduced to a flag: the code is right here, and an
            // expiry that slipped through between the read and the write is the same occurrence the
            // pre-check answers, so it gets the same answer.
            var refusal = await AnswerToRefusedDecisionAsync(
                finalizeResult.Error.Code, TransactionOutcome.Confirmed, transaction, adapter,
                transactionStore, eventPublisher, timeProvider, recipientLocale, receiptText,
                promptLocalizer, errorText, logger, cancellationToken);

            await ShowOutcomeAsync(
                adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
                refusal.Outcome, refusal.Text,
                promptMessageStore,
                displayIntentSource,
                TransactionResolutionContext.For(transaction),
                logger, metrics, cancellationToken);
            return;
        }

        // Show the user the terminal outcome: the adapter picks the platform operations. A confirmed
        // outcome is a RECEIPT and comes back already localized.
        await ShowOutcomeAsync(
            adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
            TransactionOutcome.Confirmed,
            await receiptText.RenderAsync(
                ReceiptAddressOf(TransactionOutcome.Confirmed, adapter, transaction),
                recipientLocale,
                transaction.GetUiTimeZone(),
                // The moment of the outcome is the one the completion wrote down, read off the
                // instance the finalizer returned: the one held before the call is the stale one.
                finalizeResult.Value.UpdatedAt,
                // Past the decision gate: the question stood before this user in this channel.
                TransactionSlotSource.From(transaction),
                TransactionResolutionContext.For(transaction),
                cancellationToken),
            promptMessageStore,
            displayIntentSource,
            TransactionResolutionContext.For(transaction),
            logger, metrics, cancellationToken);
    }

    /// <summary>
    /// Attaches the channel identity to the still-Pending transaction so the browser waiting on the
    /// sign-in screen can be taken to the core's confirmation question. Nothing is sent to the
    /// channel and the transaction is not completed — the user answers on the web page.
    /// </summary>
    /// <remarks>
    /// A failure is never turned into an auto-confirmation: that would silently replace the chosen
    /// confirmation surface with a silent sign-in. The transaction is left Pending and expires by TTL.
    /// What the sender is told about a refusal is read off its class (SPEC-003 CA-200): a closed
    /// window is answered by the receipt of an expiry, a decision already recorded by the one neutral
    /// text the registration declares. Silence is left for the single case it was always meant for —
    /// a sender the transaction does not admit must not be able to make the core reply. On this
    /// surface the channel says nothing for the whole sign-in, so such a reply is the first message
    /// the sender gets rather than a second copy of an answer already given.
    /// The reply goes out WITHOUT taking the rate limit: the budget of this event was already spent
    /// by the limiter block of the caller, and taking it twice would charge one event two attempts.
    /// </remarks>
    /// <param name="identity">Channel identity snapshot of the user who started the sign-in.</param>
    /// <param name="transaction">Transaction as it was read before the attach.</param>
    /// <param name="adapter">Channel adapter the event arrived through.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="timeProvider">Clock the moment of the audit event is read off.</param>
    /// <param name="recipientLocale">Recipient locale of the receipt, which the transaction is
    /// allowed to improve (null — base language); the neutral text is localized off the snapshot
    /// alone, so that it reads the same as the one a missing transaction gets.</param>
    /// <param name="staleLinkReplyText">Declared reply to an unservable sign-in link (null — silence).</param>
    /// <param name="receiptText">Port the terminal wording comes from.</param>
    /// <param name="displayIntentSource">Port the desired display intent of the receipt comes from.</param>
    /// <param name="promptLocalizer">Channel message localizer (null — base language).</param>
    /// <param name="eventPublisher">Bus the refusal is written to (null — no journal).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics the unserved event is counted into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task AttachIdentityForWebConfirmationAsync(
        ChannelIdentitySnapshot identity,
        Transaction transaction,
        IChannelAdapter adapter,
        ITransactionService transactionService,
        TimeProvider timeProvider,
        string? recipientLocale,
        string? staleLinkReplyText,
        IOutcomeReceiptText receiptText,
        IOutcomeNoticeDisplayIntentSource displayIntentSource,
        IConfirmationPromptLocalizer? promptLocalizer,
        ITransactionEventPublisher? eventPublisher,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        CancellationToken cancellationToken)
    {
        var transactionId = transaction.Id;

        var attachResult = await transactionService.AttachChannelIdentityAsync(
            transactionId,
            identity,
            transaction.ConcurrencyToken,
            cancellationToken);

        if (attachResult.IsSuccess)
        {
            logger.LogInformation(
                "Channel identity accepted; the confirmation question is asked on the core web page. " +
                "TransactionId: {TransactionId}, ChannelType: {ChannelType}",
                transactionId,
                identity.ChannelType);
            return;
        }

        var refusalCode = attachResult.Error.Code;

        // The event named a transaction that was found and was not served, whichever way it refused:
        // the branch is counted before it is answered.
        metrics.RecordUnservedStart(adapter.ChannelType);

        // A late delivery of the same channel event (the transaction has already been declined or has
        // expired) and a sender the transaction does not admit (a channel outside its allow-list, a
        // bot) are expected refusals, not operational errors — hence Warning for those codes and
        // Error for the rest. An expiry is reported by two codes, not one: past the TTL the engine
        // answers with the deadline while the transaction is still Pending, and with the state only
        // after the cleanup pass has promoted it — the classifier reads both as one class.
        switch (DecisionRefusalClassifier.Classify(refusalCode))
        {
            case DecisionRefusalClass.Expired:
                logger.LogWarning(
                    "Channel identity not attached — the sign-in window has closed. " +
                    "TransactionId: {TransactionId}, Error: {ErrorCode}",
                    transactionId,
                    refusalCode);

                await PublishChannelRefusalAsync(
                    eventPublisher, timeProvider, transactionId, adapter.ChannelType,
                    ChannelInteractionAuditCodes.LinkExpired, refusalCode, logger);

                // The transaction is in hand, so the expiry is worded for it in full — the same
                // receipt the same occurrence gets on every other surface.
                await ShowOutcomeAsync(
                    adapter, transactionId, identity.ChannelUserId, inboundToken: null,
                    TransactionOutcome.Expired,
                    await ExpiredReceiptAsync(
                        adapter,
                        transaction,
                        // The question of this surface is asked on the core page only AFTER the
                        // identity is attached, and it was not: nothing of the transaction was shown to
                        // this user yet, so its receipt names no subject (SPEC-036 TPL-123).
                        TransactionSlotSource.From(transaction).WithoutCallerValues(),
                        recipientLocale,
                        receiptText,
                        cancellationToken),
                    // No prompt was ever sent into this channel on this surface — the question lives
                    // on the core page — so there are no prompt coordinates to consume.
                    promptMessageStore: null,
                    displayIntentSource,
                    // The context the expiry helper addresses its wording with — the transaction in hand.
                    TransactionResolutionContext.For(transaction),
                    logger, metrics, cancellationToken);
                return;

            case DecisionRefusalClass.Race:
                logger.LogWarning(
                    "Channel identity not attached — the transaction does not accept this event. " +
                    "TransactionId: {TransactionId}, Error: {ErrorCode}",
                    transactionId,
                    refusalCode);

                await PublishChannelRefusalAsync(
                    eventPublisher, timeProvider, transactionId, adapter.ChannelType,
                    ChannelInteractionAuditCodes.LinkNotAnswerable, refusalCode, logger);

                // What became of the transaction is not disclosed (SPEC-039 C15): the sender gets the
                // same neutral text a missing transaction gets, down to the language it is written
                // in — the snapshot locale and then the base one, never the language the transaction
                // knows, or the answer would read differently depending on what the sender is not
                // being told. The reason stays in the journal.
                await SendDeclaredReplyAsync(
                    adapter, identity, staleLinkReplyText, identity.Locale, promptLocalizer, logger,
                    metrics, cancellationToken);
                return;

            default:
                // The transaction refused the sender, or something of ours refused: either way the
                // sender is told nothing at all. A sender a transaction does not admit must not be
                // able to make the core send it a message.
                if (refusalCode is TransactionErrorCodes.ChannelNotAllowed
                    or TransactionErrorCodes.BotRejected)
                {
                    logger.LogWarning(
                        "Channel identity not attached — the transaction does not admit this sender. " +
                        "TransactionId: {TransactionId}, Error: {ErrorCode}",
                        transactionId,
                        refusalCode);
                }
                else
                {
                    logger.LogError(
                        "Failed to attach the channel identity for web confirmation. " +
                        "TransactionId: {TransactionId}, Error: {ErrorCode}",
                        transactionId,
                        refusalCode);
                }

                await PublishChannelRefusalAsync(
                    eventPublisher, timeProvider, transactionId, adapter.ChannelType,
                    ChannelInteractionAuditCodes.SenderNotAdmitted, refusalCode, logger);
                return;
        }
    }

    /// <summary>
    /// Answers a sign-in start the core could not serve with the text the registration declares for
    /// it, spending one attempt of the sender's budget on the way (SPEC-003 CA-200).
    /// </summary>
    /// <remarks>
    /// The single place that both checks the declaration and takes the limit, so every branch that
    /// ends in "this start cannot be served" charges the event exactly once and stays silent under
    /// exactly the same condition. A channel that declares no text costs the budget nothing: there is
    /// nothing to send, so nothing is taken.
    /// A refused limit is not written onto the transaction: there may be none, and one that is there
    /// is already terminal — the branch this helper serves is the branch where nothing is left to fail.
    /// </remarks>
    /// <param name="adapter">Channel adapter the reply travels through.</param>
    /// <param name="identity">Identity snapshot of the sender.</param>
    /// <param name="replyText">Declared reply text (null or blank — the channel stays silent).</param>
    /// <param name="recipientLocale">Recipient locale (null — base language).</param>
    /// <param name="userAuthRateLimiter">Per-user rate limiter (null — no limit).</param>
    /// <param name="promptLocalizer">Channel message localizer (null — base language).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics a failed notification is counted into.</param>
    /// <param name="transactionId">Transaction the event named, for the log entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task ReplyToUnservedStartAsync(
        IChannelAdapter adapter,
        ChannelIdentitySnapshot identity,
        string? replyText,
        string? recipientLocale,
        IUserAuthRateLimiter? userAuthRateLimiter,
        IConfirmationPromptLocalizer? promptLocalizer,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        TransactionId transactionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(replyText))
        {
            return;
        }

        if (userAuthRateLimiter is not null)
        {
            var userKey = NormalizeUserKey(identity);
            var rateLimitResult = await userAuthRateLimiter.TryAcquireAsync(
                UserAuthRateLimitOperations.AuthStart + userKey, cancellationToken);

            if (!rateLimitResult.IsAllowed)
            {
                logger.LogWarning(
                    "Per-user rate limit exceeded — the unserved sign-in start stays unanswered. " +
                    "TransactionId: {TransactionId}, UserKeyHash: {UserKeyHash}",
                    transactionId,
                    LogMasking.Fingerprint(userKey));
                return;
            }
        }

        await SendDeclaredReplyAsync(
            adapter, identity, replyText, recipientLocale, promptLocalizer, logger, metrics,
            cancellationToken);
    }

    /// <summary>
    /// Sends the text a registration declared for an occurrence, if it declared one. The single point
    /// where a declared reply leaves the pipeline: an undeclared and a blank text mean the same thing
    /// — the channel stays silent — and that reading lives here rather than at each call site.
    /// </summary>
    /// <param name="adapter">Channel adapter the reply travels through.</param>
    /// <param name="identity">Identity snapshot of the recipient.</param>
    /// <param name="replyText">Declared reply text (null or blank — nothing is sent).</param>
    /// <param name="recipientLocale">Recipient locale (null — base language).</param>
    /// <param name="promptLocalizer">Channel message localizer (null — base language).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics a failed notification is counted into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static Task SendDeclaredReplyAsync(
        IChannelAdapter adapter,
        ChannelIdentitySnapshot identity,
        string? replyText,
        string? recipientLocale,
        IConfirmationPromptLocalizer? promptLocalizer,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(replyText))
        {
            return Task.CompletedTask;
        }

        // Not an outcome of a transaction, so it stays a plain message and is not gated by the
        // outcome capability — a channel that shows no outcome still gets it.
        return SendUserMessageAsync(
            adapter,
            identity.ChannelUserId,
            Localize(promptLocalizer, replyText, recipientLocale),
            logger,
            metrics,
            cancellationToken);
    }

    /// <summary>
    /// Asks the engine's own admissibility rule whether the sender of a channel event may act on the
    /// transaction the event names — the channel is in the transaction's allow-list, the sender is not
    /// a bot the application rejects — and writes a refusal into the integrator's journal under
    /// <see cref="ChannelInteractionAuditCodes.SenderNotAdmitted"/> (SPEC-039 E48).
    /// </summary>
    /// <remarks>
    /// The engine applies the same rule on its writes only while the transaction is still awaiting a
    /// decision: the idempotent success of a confirmation, the failure a spent budget writes and the
    /// question sent into the channel do not pass it. So every channel path that acts on a found
    /// transaction for a sender asks this first. What the refused sender is answered with stays with
    /// the caller — a pressed button is always answered, a followed link is not.
    /// </remarks>
    /// <param name="transaction">Transaction the event named.</param>
    /// <param name="identity">Identity snapshot of the sender.</param>
    /// <param name="adapter">Channel adapter the event arrived through.</param>
    /// <param name="transactionService">Transaction service — the one implementation of the rule.</param>
    /// <param name="eventPublisher">Bus the refusal is written to (null — no journal).</param>
    /// <param name="timeProvider">Clock the moment of the audit event is read off.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> — the sender is admitted; <c>false</c> — refused, logged and journaled.</returns>
    private static async Task<bool> IsSenderAdmittedAsync(
        Transaction transaction,
        ChannelIdentitySnapshot identity,
        IChannelAdapter adapter,
        ITransactionService transactionService,
        ITransactionEventPublisher? eventPublisher,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var admissibility = await transactionService.ValidateChannelAdmissibilityAsync(
            transaction, identity, cancellationToken);

        if (admissibility.IsSuccess)
        {
            return true;
        }

        logger.LogWarning(
            "The sender is not admitted by the transaction — the channel event is not acted on and no outcome is disclosed. TransactionId: {TransactionId}, ChannelType: {ChannelType}, Error: {ErrorCode}",
            transaction.Id,
            adapter.ChannelType,
            admissibility.Error.Code);

        await PublishChannelRefusalAsync(
            eventPublisher, timeProvider, transaction.Id, adapter.ChannelType,
            ChannelInteractionAuditCodes.SenderNotAdmitted, admissibility.Error.Code, logger);

        return false;
    }

    /// <summary>
    /// Writes a refusal of the channel side of the interaction surface into the integrator's journal
    /// (SPEC-039 E48): one audit event on the transaction bus, for a transaction that WAS found.
    /// </summary>
    /// <remarks>
    /// The twin of the page's own record of the same class of occurrence, down to the treatment of an
    /// unavailable journal: a failed publish never changes what the sender is answered with, and the
    /// reason of the refusal is told to the journal alone (SPEC-039 C15).
    /// <see cref="CancellationToken.None"/> for the publish: the occurrence must reach the journal
    /// even if the platform's delivery attempt is being cancelled, or abandoning a request would
    /// suppress its own trace.
    /// </remarks>
    /// <param name="eventPublisher">Bus the event goes to (null — the composition has no journal).</param>
    /// <param name="timeProvider">Clock the moment of the event is read off.</param>
    /// <param name="transactionId">Transaction the event named.</param>
    /// <param name="channelType">Channel the event arrived through.</param>
    /// <param name="auditCode">Reason the journal reads (see <see cref="ChannelInteractionAuditCodes"/>).</param>
    /// <param name="refusalErrorCode">Code the engine refused with, when there was one.</param>
    /// <param name="logger">Logger.</param>
    private static async Task PublishChannelRefusalAsync(
        ITransactionEventPublisher? eventPublisher,
        TimeProvider timeProvider,
        TransactionId transactionId,
        string channelType,
        string auditCode,
        string? refusalErrorCode,
        ILogger logger)
    {
        if (eventPublisher is null)
        {
            return;
        }

        try
        {
            await eventPublisher.PublishAsync(
                new TransactionChannelAuditEvent
                {
                    TransactionId = transactionId,
                    OccurredAt = timeProvider.GetUtcNow(),
                    ChannelType = channelType,
                    EventCode = auditCode,
                    Details = refusalErrorCode
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to publish audit event {EventCode}. TransactionId: {TransactionId}",
                auditCode,
                transactionId);
        }
    }

    /// <summary>
    /// Handles a direct message to the bot that names no transaction (SPEC-003 CA-201): a bare start
    /// command, free text from a sender the adapter has already vetted.
    /// </summary>
    /// <remarks>
    /// Nothing is looked up and nothing is written down — there is no transaction to look up. The
    /// answer is the one the registration declares, under the same per-user budget as any other
    /// sign-in start, and the shipped channels declare none: the mechanism exists, the behaviour of
    /// the product does not change until an installation states a text.
    /// </remarks>
    /// <param name="result">The inbound result carrying the sender.</param>
    /// <param name="adapter">Channel adapter the message arrived through.</param>
    /// <param name="replyText">Declared reply text (null — no reply).</param>
    /// <param name="userAuthRateLimiter">Per-user rate limiter (null — no limit).</param>
    /// <param name="promptLocalizer">Channel message localizer (null — base language).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics a failed notification is counted into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task HandleUnaddressedAsync(
        ChannelUnaddressedResult result,
        IChannelAdapter adapter,
        string? replyText,
        IUserAuthRateLimiter? userAuthRateLimiter,
        IConfirmationPromptLocalizer? promptLocalizer,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(replyText))
        {
            logger.LogDebug(
                "A message naming no transaction arrived; the channel declares no reply to it. ChannelType: {ChannelType}",
                adapter.ChannelType);
            return;
        }

        if (userAuthRateLimiter is not null)
        {
            var userKey = NormalizeUserKey(result.Identity);
            var rateLimitResult = await userAuthRateLimiter.TryAcquireAsync(
                UserAuthRateLimitOperations.AuthStart + userKey, cancellationToken);

            if (!rateLimitResult.IsAllowed)
            {
                logger.LogWarning(
                    "Per-user rate limit exceeded — the message naming no transaction stays unanswered. " +
                    "UserKeyHash: {UserKeyHash}",
                    LogMasking.Fingerprint(userKey));
                return;
            }
        }

        // There is no transaction, so the recipient-locale chain (B17) collapses to the channel
        // snapshot locale and then to the base language.
        await SendDeclaredReplyAsync(
            adapter, result.Identity, replyText, result.Identity.Locale, promptLocalizer, logger,
            metrics, cancellationToken);
    }

    /// <summary>
    /// Handles the user declining authentication.
    /// </summary>
    /// <param name="result">Inbound event result with decline data.</param>
    /// <param name="adapter">Channel adapter.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="transactionStore">Transaction store the decision is read through.</param>
    /// <param name="timeProvider">Clock the deadline of the transaction is read against.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics a failed user notification is counted into.</param>
    /// <param name="confirmationPromptOrchestrator">Confirmation prompt orchestrator — the point that
    /// answers whether this channel was the one asked to show the question (SPEC-039 E41).</param>
    /// <param name="receiptText">Terminal text of the transaction (SPEC-036 TPL-123).</param>
    /// <param name="displayIntentSource">Port the desired display intent of the receipt comes from.</param>
    /// <param name="eventPublisher">Bus the refusals of the press are written to — a late press, a
    /// decision already recorded, a sender not admitted (SPEC-039 E48; null — no journal).</param>
    /// <param name="errorText">Message text shown to the user on error.</param>
    /// <param name="promptLocalizer">Channel message localizer (null — base language).</param>
    /// <param name="promptMessageStore">Store of sent-prompt coordinates (null — no prompt reference).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task HandleAuthDeclineAsync(
        ChannelAuthDeclineResult result,
        IChannelAdapter adapter,
        ITransactionService transactionService,
        ITransactionStore transactionStore,
        TimeProvider timeProvider,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        IConfirmationPromptOrchestrator? confirmationPromptOrchestrator,
        IOutcomeReceiptText receiptText,
        IOutcomeNoticeDisplayIntentSource displayIntentSource,
        ITransactionEventPublisher? eventPublisher,
        string errorText,
        IConfirmationPromptLocalizer? promptLocalizer,
        IChannelPromptMessageStore? promptMessageStore,
        CancellationToken cancellationToken)
    {
        // The identifier arrives already parsed: the adapter is the one that reads it out of the
        // channel event, so an unparseable value never reaches this point (SPEC-003 §6.3).
        var transactionId = result.TransactionId;

        // Read the transaction for its ConcurrencyToken — through the STORE, past the TTL gate, for
        // the reason the twin branch of the confirmation handler states.
        var transaction = await ReadForDecisionAsync(
            transactionStore, transactionId, logger, cancellationToken);

        if (transaction is null)
        {
            logger.LogWarning(
                "No transaction to answer the AuthDecline with. TransactionId: {TransactionId}",
                result.TransactionId);

            // A pressed button is always answered (SPEC-003 §13.3, CA-192) — see the twin branch of
            // the confirmation handler for why the outcome is Expired and why a transaction that is
            // not there is not told apart from one that never was.
            await ShowOutcomeAsync(
                adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
                TransactionOutcome.Expired,
                await receiptText.RenderAsync(
                    ReceiptAddressWithoutTransaction(TransactionOutcome.Expired, adapter),
                    result.Identity.Locale,
                    // Neither the zone nor the moment of the outcome has a source on this branch: the
                    // transaction they are both read off is the one that could not be read. Stating
                    // "now" instead would be this handler's own clock passed off as the moment the
                    // transaction ended, so nothing is stated and a moment falls back to UTC.
                    recipientTimeZone: null,
                    outcomeMoment: null,
                    // Nor any value of the transaction: they are read off the same transaction
                    // (SPEC-036 TPL-107).
                    transaction: null,
                    // There is no transaction to take ownership from — that is this branch. The only
                    // level left is the tenant of the ambient channel scope, which the single context
                    // builder puts in for a null transaction.
                    TransactionResolutionContext.For(null),
                    cancellationToken),
                promptMessageStore,
                displayIntentSource,
                // The same context the wording above is addressed with: the receipt and the way it is
                // shown are one answer about one notice.
                TransactionResolutionContext.For(null),
                logger, metrics, cancellationToken);
            return;
        }

        // Recipient-locale chain (B17): channel snapshot locale → initiation-page language → base.
        var recipientLocale = result.Identity.Locale ?? transaction.GetUiLocale();

        // The window closed before the button was pressed — the twin of the confirmation pre-check,
        // and deliberately the same answer: a decision reported after the deadline is answered by
        // what happened to the transaction, not by which button was pressed.
        if (transaction.HasRunOutOfTime(timeProvider.GetUtcNow()))
        {
            // The transaction was found, so the late press reaches the journal (SPEC-039 E48). No
            // engine code went with it — the deadline or the state itself is the reason.
            await PublishChannelRefusalAsync(
                eventPublisher, timeProvider, transactionId, adapter.ChannelType,
                ChannelInteractionAuditCodes.LinkExpired, refusalErrorCode: null, logger);

            await ShowOutcomeAsync(
                adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
                TransactionOutcome.Expired,
                await ExpiredReceiptAsync(
                    adapter,
                    transaction,
                    // The gate of the caller values cuts off what the calling party supplied and
                    // nothing the core states about the transaction.
                    TransactionSlotSource.From(transaction) with
                    {
                        CallerSlotValues = await ShownCallerSlotValuesAsync(
                            transaction, adapter, confirmationPromptOrchestrator, cancellationToken)
                    },
                    recipientLocale,
                    receiptText,
                    cancellationToken),
                promptMessageStore,
                displayIntentSource,
                // The context the expiry helper addresses its wording with — the transaction in hand.
                TransactionResolutionContext.For(transaction),
                logger, metrics, cancellationToken);
            return;
        }

        TagCorrelatingTrace(transaction);

        // The same gate as on the confirmation (SPEC-039 E41): a channel that was not shown the
        // subject does not decide its fate either way.
        if (!await AcceptsChannelDecisionAsync(
                transaction, adapter, confirmationPromptOrchestrator, logger, cancellationToken))
        {
            return;
        }

        // The same gate of the sender as on the confirmation, for the reason stated there: failing a
        // transaction checks the state and the concurrency token, not who asks, so a sender the
        // transaction does not admit would otherwise end someone else's sign-in and be answered with
        // the receipt of that decline.
        if (!await IsSenderAdmittedAsync(
                transaction, result.Identity, adapter, transactionService, eventPublisher,
                timeProvider, logger, cancellationToken))
        {
            await ShowOutcomeAsync(
                adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
                TransactionOutcome.Failed,
                Localize(promptLocalizer, errorText, recipientLocale),
                promptMessageStore,
                displayIntentSource,
                TransactionResolutionContext.For(transaction),
                logger, metrics, cancellationToken);
            return;
        }

        // Decline the transaction
        var failResult = await transactionService.FailTransactionAsync(
            transactionId,
            TransactionErrorCodes.DeclinedByUser,
            transaction.ConcurrencyToken,
            cancellationToken);

        if (failResult.IsFailure)
        {
            // The refusal is read by its class, exactly as on the confirmation: the user is still
            // shown a terminal state even on error — the stale buttons must go — and which state
            // that is depends on what refused the write.
            var refusal = await AnswerToRefusedDecisionAsync(
                failResult.Error.Code, TransactionOutcome.Declined, transaction, adapter,
                transactionStore, eventPublisher, timeProvider, recipientLocale, receiptText,
                promptLocalizer, errorText, logger, cancellationToken);

            await ShowOutcomeAsync(
                adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
                refusal.Outcome, refusal.Text,
                promptMessageStore,
                displayIntentSource,
                TransactionResolutionContext.For(transaction),
                logger, metrics, cancellationToken);
            return;
        }

        // Show the decline outcome (CA-142) — a receipt, already localized by the port.
        await ShowOutcomeAsync(
            adapter, transactionId, result.Identity.ChannelUserId, result.InboundToken,
            TransactionOutcome.Declined,
            await receiptText.RenderAsync(
                ReceiptAddressOf(TransactionOutcome.Declined, adapter, transaction),
                recipientLocale,
                transaction.GetUiTimeZone(),
                // The refusal has just been written down; its moment is the one the write recorded,
                // read off the instance that write returned.
                failResult.Value.UpdatedAt,
                // Past the decision gate: the question stood before this user in this channel.
                TransactionSlotSource.From(transaction),
                TransactionResolutionContext.For(transaction),
                cancellationToken),
            promptMessageStore,
            displayIntentSource,
            TransactionResolutionContext.For(transaction),
            logger, metrics, cancellationToken);
    }

    /// <summary>
    /// Localizes a status Natural Key into the recipient's language (ICC-050); when the localizer or a
    /// translation is unavailable, returns the key's base text (<see cref="NaturalKeyContext.BaseTextOf"/>).
    /// </summary>
    /// <param name="promptLocalizer">Channel message localizer (null — base language).</param>
    /// <param name="naturalKey">Status text Natural Key (base language, English).</param>
    /// <param name="recipientLocale">Recipient locale (IETF tag; null — base language).</param>
    /// <returns>The translated text, or the key's base text when there is no translation.</returns>
    private static string Localize(
        IConfirmationPromptLocalizer? promptLocalizer,
        string naturalKey,
        string? recipientLocale)
        => promptLocalizer?.ResolveOrBaseText(naturalKey, recipientLocale)
            ?? NaturalKeyContext.BaseTextOf(naturalKey);

    /// <summary>
    /// Handles receiving a phone number from the user (CA-004).
    /// Logs the received number for further processing.
    /// Full integration with Identity Resolution (snapshot update)
    /// requires an additional Identity Resolution layer spec.
    /// </summary>
    private static Task HandlePhoneSharedAsync(
        ChannelPhoneSharedResult result,
        ILogger logger)
    {
        logger.LogInformation(
            "Phone number received. ChannelType: {ChannelType}",
            result.Identity.ChannelType);

        // The phone number is extracted and available in result.PhoneNumber.
        // Persisting it onto the ChannelIdentitySnapshot in the Transaction Engine is outside the
        // current identity-resolution behaviour (CA-020): the snapshot is not updated here.
        // CA-121: snapshot contents (including the phone) are PII, log only at Debug
        logger.LogDebug(
            "Phone number extracted from PhoneShared. ChannelUserId: {ChannelUserId}, PhonePresent: true",
            result.Identity.ChannelUserId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends a plain message to the user through the shared outbound seam, which gates the content
    /// kind, logs a failure with the channel type and the error code, and counts it.
    /// </summary>
    /// <param name="adapter">Channel adapter.</param>
    /// <param name="channelUserId">Recipient within the channel.</param>
    /// <param name="text">Already localized message text.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics a failed notification is counted into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static Task SendUserMessageAsync(
        IChannelAdapter adapter,
        string channelUserId,
        string text,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        CancellationToken cancellationToken)
    {
        var message = new ChannelMessage
        {
            ChannelUserId = channelUserId,
            Text = text
        };

        return ChannelOutbound.SendMessageAsync(adapter, message, logger, metrics, cancellationToken);
    }

    /// <summary>
    /// States the core's intent "show the user the terminal state of this transaction" (SPEC-003 §6.2).
    /// The prompt reference is taken from the store with remove-on-read, exactly like the TTL path —
    /// the two paths differ only in what triggers them. The desired display intent is resolved here
    /// too, so every outcome this route shows states the one intent of the deployment, whichever
    /// branch it came from.
    /// </summary>
    /// <param name="adapter">Channel adapter.</param>
    /// <param name="transactionId">Transaction identifier.</param>
    /// <param name="channelUserId">Recipient within the channel.</param>
    /// <param name="inboundToken">Opaque token of the live interaction (null — no interaction).</param>
    /// <param name="outcome">Terminal outcome to show.</param>
    /// <param name="statusText">Already localized status text.</param>
    /// <param name="promptMessageStore">Store of sent-prompt coordinates (null — no prompt reference).</param>
    /// <param name="displayIntentSource">Port the desired display intent of the receipt comes from.</param>
    /// <param name="resolutionContext">
    /// Ownership context the intent is resolved for — the one the caller states for the wording of
    /// this very notice, so the text and the way it is shown are answered at one level.
    /// </param>
    /// <param name="logger">Logger.</param>
    /// <param name="metrics">Component metrics a failed notification is counted into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task ShowOutcomeAsync(
        IChannelAdapter adapter,
        TransactionId transactionId,
        string channelUserId,
        ChannelInboundToken? inboundToken,
        TransactionOutcome outcome,
        string statusText,
        IChannelPromptMessageStore? promptMessageStore,
        IOutcomeNoticeDisplayIntentSource displayIntentSource,
        ResolutionContext resolutionContext,
        ILogger logger,
        ChannelAdapterMetrics metrics,
        CancellationToken cancellationToken)
    {
        // Resolved with the token of the call and BEFORE the stored reference is consumed: a
        // cancellation here leaves that reference in the store for the background expiry path,
        // instead of spending it on a notice this call is no longer going to build.
        var displayIntent = await displayIntentSource.ResolveAsync(resolutionContext, cancellationToken);

        ChannelMessageRef? promptMessage = null;
        if (promptMessageStore is not null)
        {
            // CancellationToken.None: the transaction has already reached a terminal state, so the
            // stored reference must be consumed even if the request is being cancelled.
            var stored = await promptMessageStore.TakeAsync(transactionId, CancellationToken.None);
            promptMessage = stored?.Message;
        }

        var notice = new TransactionOutcomeNotice
        {
            TransactionId = transactionId,
            Outcome = outcome,
            ChannelUserId = channelUserId,
            StatusText = statusText,
            InboundToken = inboundToken,
            PromptMessage = promptMessage,
            DisplayIntent = displayIntent
        };

        await ChannelOutbound.NotifyOutcomeAsync(adapter, notice, logger, metrics, cancellationToken);
    }

    /// <summary>
    /// Stitches the inbound trace to the trace that created the transaction, by hanging the creating
    /// trace's identifier on the inbound span as an attribute. Navigation in a dashboard is then a
    /// search by that attribute.
    /// </summary>
    /// <remarks>
    /// An attribute rather than a trace link: a link is built out of an <c>ActivityContext</c> and
    /// therefore needs a span id as well, while the transaction stores only a trace id. A link
    /// assembled on a zero span id would build, yet point nowhere — backends drop such a reference or
    /// render it broken.
    /// The current activity is checked against the inbound span by name on purpose: the same handlers
    /// also run on the polling loops, where no inbound span was opened and the ambient activity, if
    /// any, belongs to something else entirely and must not be tagged.
    /// An empty correlation (a host without telemetry, a transaction created before this ever worked)
    /// leaves the span untagged — there is nothing to stitch to.
    /// </remarks>
    /// <param name="transaction">Transaction the inbound event addresses.</param>
    private static void TagCorrelatingTrace(Transaction transaction)
    {
        if (string.IsNullOrEmpty(transaction.CorrelationId))
        {
            return;
        }

        if (Activity.Current is { OperationName: ChannelTelemetry.InboundActivityName } inbound)
        {
            inbound.SetTag(TransactionTelemetry.CorrelationTraceIdTag, transaction.CorrelationId);
        }
    }

    /// <summary>
    /// Creates the pipeline logger from the ILoggerFactory of the current request.
    /// </summary>
    private static ILogger GetLogger(HttpContext httpContext)
        => httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(ChannelWebhookPipeline));

    /// <summary>
    /// Finds the adapter by channel type via a linear scan over the current request's adapters (there are only a few).
    /// An adapter is a stateless singleton per channel type (CA-014/CA-164): a tenant does not multiply adapter
    /// instances; instead, the seam determines which credentials/client it uses during processing. The tenant
    /// is carried into processing via the ambient context (<see cref="ChannelTenantContext"/>) set by the
    /// caller before this method, not via an adapter selection parameter.
    /// Deliberately no static cache: a cache would be built from the adapters of the FIRST request and would leak
    /// between different DI containers in one process (multiple WebApplicationFactory instances in tests,
    /// a multi-tenant host) — review feedback. The scan cost is negligible next to JSON parsing and the DB.
    /// </summary>
    /// <param name="adapters">Registered adapters of the current request.</param>
    /// <param name="channelType">Channel type.</param>
    /// <returns>Adapter of the channel type or null if not registered.</returns>
    private static IChannelAdapter? FindAdapter(IEnumerable<IChannelAdapter> adapters, string channelType)
    {
        foreach (var adapter in adapters)
        {
            if (string.Equals(adapter.ChannelType, channelType, StringComparison.Ordinal))
            {
                return adapter;
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes the user key for the rate limiter.
    /// Format: {tenant}:{channel_type}:{channel_user_id} — lowercase, trimmed.
    /// Prevents bypass via letter case or whitespace.
    /// </summary>
    /// <remarks>
    /// The tenant leads the key so that tenants do not share one user's budget: the same
    /// <c>channel_user_id</c> under two tenants is two users of two installations, and a shared
    /// counter would let one tenant's traffic throttle another's. An unstated tenant takes the one
    /// fixed spelling of <see cref="TenantKey.Segment"/> — the same the identity record uses, so the
    /// counter and the record of one user always belong to the same tenant.
    /// </remarks>
    /// <param name="identity">Channel identity snapshot the key is built from.</param>
    /// <returns>Normalized partition key.</returns>
    internal static string NormalizeUserKey(ChannelIdentitySnapshot identity)
        => $"{TenantKey.Segment(identity.TenantId).Trim().ToLowerInvariant()}"
           + $":{identity.ChannelType.Trim().ToLowerInvariant()}"
           + $":{identity.ChannelUserId.Trim().ToLowerInvariant()}";

    /// <summary>
    /// What a refused write of the user's decision is answered with: which terminal state to show
    /// and the text that goes with it.
    /// </summary>
    /// <param name="Outcome">Terminal state shown to the user.</param>
    /// <param name="Text">Already localized text of that state.</param>
    private readonly record struct RefusedDecisionAnswer(TransactionOutcome Outcome, string Text);
}
