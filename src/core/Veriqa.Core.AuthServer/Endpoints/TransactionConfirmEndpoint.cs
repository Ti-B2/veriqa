// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.AuthServer.UI;
using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Events;
using Veriqa.Core.TransactionEngine.Identity;
using Veriqa.Core.TransactionEngine.Services;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// Confirmation surface of the core (SPEC-039 R37): the page that ASKS what a confirmation
/// transaction is about, takes the user's answer and reports how it ended.
/// <para>
/// It is the surface of the branch where the question is not asked in the channel — a channel that
/// carries no in-channel confirmation, or a deployment that chose the web page. The channel is still
/// what identifies the user: the answer here is signed by the channel identity snapshot the channel
/// attached earlier, and a transaction with no snapshot yet has nobody to answer for it.
/// </para>
/// <para>
/// The page owns neither of the two things it shows. The subject is worded by the confirmation
/// context factory (one engine of substitution and protections for the channel and for this page
/// alike), and the terminal line comes from the receipt port. Finalization is the server-side
/// sequence every other path uses. What is genuinely this endpoint's own is the precondition of
/// answering and the reading of a refused write.
/// </para>
/// </summary>
public static class TransactionConfirmEndpoint
{
    /// <summary>
    /// Everything the three handlers need to RENDER, gathered once per request: which localizer and
    /// branding resolver to use, and the two languages the surface speaks in.
    /// </summary>
    /// <remarks>
    /// The two languages are separate on purpose and neither may stand in for the other. The pages of
    /// a found transaction speak the language the relying party stated when creating it, while the
    /// neutral refusal always speaks the language of the REQUEST — a refusal translated by the levels
    /// of a found transaction would answer the very question the single wording hides.
    /// </remarks>
    /// <param name="Localizer">Locale-file localizer (Natural Key → localized text).</param>
    /// <param name="BrandingResolver">Resolver of the effective page branding.</param>
    /// <param name="RequestLanguage">Language detected from the request.</param>
    /// <param name="PageLanguage">Language of the pages of a found transaction; equal to
    /// <paramref name="RequestLanguage"/> until a transaction is resolved.</param>
    private readonly record struct ConfirmSurface(
        IConfirmationPromptLocalizer Localizer,
        CorePageBrandingResolver BrandingResolver,
        string RequestLanguage,
        string PageLanguage);

    /// <summary>
    /// What the precondition of answering found for the addressed identifier.
    /// </summary>
    private enum PagePrecondition
    {
        /// <summary>
        /// The identifier names no transaction of ours — it does not parse, or nothing is stored
        /// under it. Nothing is written down about it: a journal entry is keyed by a transaction.
        /// </summary>
        Unknown,

        /// <summary>
        /// A transaction is there, but this page is not where it is answered: it is not a
        /// confirmation, it carries no channel identity to sign an answer with, or it is over for a
        /// reason other than its time — a decision on it is already recorded.
        /// </summary>
        NotAnswerable,

        /// <summary>
        /// A confirmation this page was addressed for, whose lifetime has run out — either still
        /// Pending with its expiry behind it, or already moved into the Expired state by the cleanup.
        /// </summary>
        Expired,

        /// <summary>
        /// A confirmation waiting for its answer here, still in time.
        /// </summary>
        Answerable
    }

    /// <summary>
    /// Outcome of the precondition together with the transaction it was read over.
    /// </summary>
    /// <param name="Precondition">What was found for the addressed identifier.</param>
    /// <param name="Transaction">The transaction found, or null when there is none to speak of.</param>
    private readonly record struct AnswerableLookup(PagePrecondition Precondition, Transaction? Transaction);

    /// <summary>
    /// What it takes to record a refusal of this surface: the log it is written to, the request the
    /// audit publisher is resolved from, and the clock the occurrence is dated by.
    /// </summary>
    /// <param name="HttpContext">Request the refusal happened on.</param>
    /// <param name="TimeProvider">Clock of the engine — the same one the TTL is measured by.</param>
    /// <param name="Logger">Logger of this endpoint.</param>
    private readonly record struct RefusalJournal(
        HttpContext HttpContext,
        TimeProvider TimeProvider,
        ILogger Logger);

    /// <summary>
    /// Registers the question page (GET) and the two answers (POST).
    /// </summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <param name="rateLimitPolicyName">Rate limiting policy name (null — no limiting). The three
    /// routes share ONE budget: asking the question and answering it are one act, and a route of them
    /// left with a budget of its own would be the way around the other two.</param>
    /// <returns>The route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapTransactionConfirmEndpoint(
        this IEndpointRouteBuilder endpoints,
        string? rateLimitPolicyName = null)
    {
        var question = endpoints.MapGet(OidcEndpoints.TransactionConfirmPage, HandleQuestionAsync);
        var accept = endpoints.MapPost(OidcEndpoints.TransactionConfirmAccept, HandleAcceptAsync);
        var decline = endpoints.MapPost(OidcEndpoints.TransactionConfirmDecline, HandleDeclineAsync);

        if (!string.IsNullOrEmpty(rateLimitPolicyName))
        {
            question.RequireRateLimiting(rateLimitPolicyName);
            accept.RequireRateLimiting(rateLimitPolicyName);
            decline.RequireRateLimiting(rateLimitPolicyName);
        }

        return endpoints;
    }

    /// <summary>
    /// Answers with the question of the addressed transaction, or with the neutral page.
    /// </summary>
    private static async Task<IResult> HandleQuestionAsync(
        HttpContext httpContext,
        ITransactionService transactionService,
        ITransactionStore transactionStore,
        IConfirmationPromptContextFactory promptContextFactory,
        IAntiforgery antiforgery,
        IConfirmationPromptLocalizer promptLocalizer,
        IOptions<VeriqaOptions> veriqaOptions,
        CorePageBrandingResolver brandingResolver,
        IAuthPageLanguageRegistry languageRegistry,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        var cancellationToken = httpContext.RequestAborted;
        var surface = BuildSurface(httpContext, promptLocalizer, veriqaOptions, brandingResolver, languageRegistry);
        var journal = new RefusalJournal(
            httpContext, timeProvider, loggerFactory.CreateLogger(typeof(TransactionConfirmEndpoint)));

        var lookup = await ResolveAnswerableAsync(
            httpContext.Request.Query[OidcConstants.SessionIdParameterName].FirstOrDefault(),
            transactionStore,
            timeProvider,
            cancellationToken);

        if (lookup.Transaction is not { } transaction)
        {
            return await UnavailableAsync(surface, cancellationToken);
        }

        // The question is not asked after the TTL either — but the refusal stays the neutral page,
        // the same one an unknown identifier gets. A receipt here would answer, to whoever holds an
        // identifier and never decided anything, the question of whether the transaction exists
        // (SPEC-039 R51, C15); the fact of the expiry is told to the integrator's journal instead.
        if (lookup.Precondition is not PagePrecondition.Answerable)
        {
            await RecordPageRefusalAsync(journal, transaction, AuditCodeOf(lookup.Precondition), null);

            return await UnavailableAsync(surface, cancellationToken);
        }

        surface = ForTransaction(surface, transaction, veriqaOptions, languageRegistry);

        // The channel parameters come off the snapshot the channel attached. The subject branch of the
        // factory does not read them — it words the question out of the transaction — but the contract
        // of the factory asks for them, and a second entry point "give me the subject text" is exactly
        // the second implementation this task must not create.
        var promptContext = await promptContextFactory.CreateAsync(
            transaction,
            transaction.ChannelIdentitySnapshot?.ChannelType ?? string.Empty,
            transaction.ChannelIdentitySnapshot?.ChannelUserId ?? string.Empty,
            surface.PageLanguage,
            cancellationToken);

        if (promptContext is not SubjectConfirmationPromptContext subject)
        {
            // No wording of the subject — the question is not asked at all, and a sign-in text must
            // never stand in for it (SPEC-039 E28). The transaction ends here rather than living out
            // its TTL as answerable.
            await FailForSubjectUnavailableAsync(transaction, transactionService, journal.Logger);

            return await UnavailableAsync(surface, cancellationToken);
        }

        var branding = await surface.BrandingResolver.ResolveAsync(
            TransactionResolutionContext.For(transaction), cancellationToken);

        var tokens = antiforgery.GetAndStoreTokens(httpContext);

        var html = TransactionConfirmPage.BuildQuestion(
            sessionId: SessionIdMapper.ToSessionId(transaction.Id),
            antiforgeryFormFieldName: tokens.FormFieldName,
            antiforgeryToken: tokens.RequestToken,
            promptText: subject.PromptText,
            pathBase: httpContext.Request.PathBase.Value ?? string.Empty,
            localizer: surface.Localizer,
            language: surface.PageLanguage,
            branding: branding);

        return Results.Content(html, AuthPageConstants.HtmlContentType, Encoding.UTF8);
    }

    /// <summary>
    /// Handles the "Yes" answer: finalizes the transaction on the server and shows the receipt.
    /// </summary>
    private static async Task<IResult> HandleAcceptAsync(
        HttpContext httpContext,
        ITransactionService transactionService,
        ITransactionStore transactionStore,
        IIdentityResolutionService identityResolutionService,
        IAntiforgery antiforgery,
        IOutcomeReceiptText receiptText,
        IConfirmationPromptLocalizer promptLocalizer,
        IOptions<VeriqaOptions> veriqaOptions,
        CorePageBrandingResolver brandingResolver,
        IAuthPageLanguageRegistry languageRegistry,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        var antiforgeryFailure = await ValidateAntiforgeryAsync(antiforgery, httpContext);
        if (antiforgeryFailure is not null)
        {
            return antiforgeryFailure;
        }

        var cancellationToken = httpContext.RequestAborted;
        var surface = BuildSurface(httpContext, promptLocalizer, veriqaOptions, brandingResolver, languageRegistry);
        var journal = new RefusalJournal(
            httpContext, timeProvider, loggerFactory.CreateLogger(typeof(TransactionConfirmEndpoint)));

        var lookup = await ResolveAnswerableAsync(
            await ReadSessionIdAsync(httpContext), transactionStore, timeProvider, cancellationToken);

        if (lookup.Transaction is not { } transaction)
        {
            return await UnavailableAsync(surface, cancellationToken);
        }

        surface = ForTransaction(surface, transaction, veriqaOptions, languageRegistry);

        if (lookup.Precondition is not PagePrecondition.Answerable)
        {
            return await AnswerRefusedPreconditionAsync(
                lookup.Precondition, transaction, receiptText, surface, journal, cancellationToken);
        }

        // The one server-side finalization sequence: confirm → resolve identity → complete, with the
        // compensation that belongs to it. The snapshot is the channel's, taken when the user entered
        // the channel; "Yes" is what authorizes turning it into a completed transaction.
        var finalizeResult = await ChannelTransactionFinalizer.ConfirmAndCompleteAsync(
            transaction.ChannelIdentitySnapshot!,
            transaction.Id,
            transactionService,
            identityResolutionService,
            transaction.ConcurrencyToken,
            journal.Logger,
            cancellationToken);

        if (finalizeResult.IsFailure)
        {
            return await AnswerRefusedDecisionAsync(
                finalizeResult.Error.Code,
                transaction,
                closeOnDownstreamRefusal: true,
                transactionService,
                receiptText,
                surface,
                journal,
                cancellationToken);
        }

        // The moment of the outcome is the one the completion wrote down: the finalizer returns the
        // fresh instance, and the one read before the call is the stale one.
        return await ReceiptAsync(
            TransactionOutcome.Confirmed, transaction, finalizeResult.Value.UpdatedAt, receiptText,
            surface, cancellationToken);
    }

    /// <summary>
    /// Handles the "No" answer: records the refusal and shows the receipt.
    /// </summary>
    private static async Task<IResult> HandleDeclineAsync(
        HttpContext httpContext,
        ITransactionService transactionService,
        ITransactionStore transactionStore,
        IAntiforgery antiforgery,
        IOutcomeReceiptText receiptText,
        IConfirmationPromptLocalizer promptLocalizer,
        IOptions<VeriqaOptions> veriqaOptions,
        CorePageBrandingResolver brandingResolver,
        IAuthPageLanguageRegistry languageRegistry,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        var antiforgeryFailure = await ValidateAntiforgeryAsync(antiforgery, httpContext);
        if (antiforgeryFailure is not null)
        {
            return antiforgeryFailure;
        }

        var cancellationToken = httpContext.RequestAborted;
        var surface = BuildSurface(httpContext, promptLocalizer, veriqaOptions, brandingResolver, languageRegistry);
        var journal = new RefusalJournal(
            httpContext, timeProvider, loggerFactory.CreateLogger(typeof(TransactionConfirmEndpoint)));

        var lookup = await ResolveAnswerableAsync(
            await ReadSessionIdAsync(httpContext), transactionStore, timeProvider, cancellationToken);

        if (lookup.Transaction is not { } transaction)
        {
            return await UnavailableAsync(surface, cancellationToken);
        }

        surface = ForTransaction(surface, transaction, veriqaOptions, languageRegistry);

        // Expiry is answered BEFORE the "No" is written down, because writing it does not check: the
        // decline path fails a transaction that is merely not terminal, so a "No" pressed after the
        // TTL would go down as a decision the user made in time. The check itself lives in the
        // precondition all three routes share — the confirmation path needs it just as much, and the
        // clock it reads is the one the engine measures the TTL by.
        if (lookup.Precondition is not PagePrecondition.Answerable)
        {
            return await AnswerRefusedPreconditionAsync(
                lookup.Precondition, transaction, receiptText, surface, journal, cancellationToken);
        }

        // The reason code is the one every other decline uses — one meaning, one code, one audit
        // trail. CancellationToken.None: the refusal must be recorded even if the browser goes away.
        var failResult = await transactionService.FailTransactionAsync(
            transaction.Id,
            TransactionErrorCodes.DeclinedByUser,
            transaction.ConcurrencyToken,
            CancellationToken.None);

        if (failResult.IsFailure)
        {
            // Nothing was moved, so there is nothing to close after: a second write with another code
            // would be refused by whatever refused this one.
            return await AnswerRefusedDecisionAsync(
                failResult.Error.Code,
                transaction,
                closeOnDownstreamRefusal: false,
                transactionService,
                receiptText,
                surface,
                journal,
                cancellationToken);
        }

        // The refusal has just been written down; its moment is the one that write recorded.
        return await ReceiptAsync(
            TransactionOutcome.Declined, transaction, failResult.Value.UpdatedAt, receiptText,
            surface, cancellationToken);
    }

    /// <summary>
    /// Gathers what the surface renders with, before anything is known about a transaction.
    /// </summary>
    /// <param name="httpContext">Request context.</param>
    /// <param name="promptLocalizer">Locale-file localizer.</param>
    /// <param name="veriqaOptions">Product options (the configured default language).</param>
    /// <param name="brandingResolver">Resolver of the effective page branding.</param>
    /// <param name="languageRegistry">Data-driven registry of supported languages.</param>
    /// <returns>The render context of this request.</returns>
    private static ConfirmSurface BuildSurface(
        HttpContext httpContext,
        IConfirmationPromptLocalizer promptLocalizer,
        IOptions<VeriqaOptions> veriqaOptions,
        CorePageBrandingResolver brandingResolver,
        IAuthPageLanguageRegistry languageRegistry)
    {
        var requestLanguage = AuthPageStrings.DetectLanguage(
            httpContext.Request.Headers.AcceptLanguage.ToString(),
            veriqaOptions.Value.Localization.DefaultLanguage,
            languageRegistry.SupportedLanguages);

        return new ConfirmSurface(promptLocalizer, brandingResolver, requestLanguage, requestLanguage);
    }

    /// <summary>
    /// Narrows the render context to a found transaction: its pages speak the language the relying
    /// party stated when it created the transaction, falling back to the request's when it stated
    /// none. The language of the neutral refusal is left alone.
    /// </summary>
    /// <param name="surface">Render context of the request.</param>
    /// <param name="transaction">Transaction the pages belong to.</param>
    /// <param name="veriqaOptions">Product options (the configured default language).</param>
    /// <param name="languageRegistry">Data-driven registry of supported languages.</param>
    /// <returns>The render context of this transaction's pages.</returns>
    private static ConfirmSurface ForTransaction(
        ConfirmSurface surface,
        Transaction transaction,
        IOptions<VeriqaOptions> veriqaOptions,
        IAuthPageLanguageRegistry languageRegistry)
    {
        return surface with
        {
            PageLanguage = AuthPageLocalization.ResolveTransactionLanguage(
                transaction,
                surface.RequestLanguage,
                veriqaOptions.Value.Localization.DefaultLanguage,
                languageRegistry.SupportedLanguages)
        };
    }

    /// <summary>
    /// Validates the AntiForgery token of a state-changing POST.
    /// </summary>
    /// <param name="antiforgery">AntiForgery service.</param>
    /// <param name="httpContext">Request context.</param>
    /// <returns>The refusal to return, or null when the token is valid.</returns>
    private static async Task<IResult?> ValidateAntiforgeryAsync(IAntiforgery antiforgery, HttpContext httpContext)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            // A ProblemDetails and not the neutral page: this is a malformed request rather than an
            // answer to a question, and the same refusal the sign-in confirmation returns.
            return Results.Problem(
                detail: "Invalid or missing confirmation token.",
                statusCode: StatusCodes.Status400BadRequest,
                title: OidcErrorCodes.OidcRequestInvalid);
        }
    }

    /// <summary>
    /// Reads the session_id out of the posted form.
    /// </summary>
    /// <remarks>
    /// A request that carries no form is answered with "no identifier" rather than by reading one:
    /// the token may have travelled in a header on a deployment that configured one, and asking a
    /// non-form body for a field throws instead of answering.
    /// </remarks>
    /// <param name="httpContext">Request context.</param>
    /// <returns>The value, or null when the request carries none.</returns>
    private static async Task<string?> ReadSessionIdAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.HasFormContentType)
        {
            return null;
        }

        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);

        return form[OidcConstants.SessionIdParameterName].FirstOrDefault();
    }

    /// <summary>
    /// The single precondition of answering, shared by the question and by both answers: the
    /// identifier parses, the transaction exists, it is a confirmation, the core is waiting for an
    /// answer on its own page for it, and its lifetime has not run out.
    /// </summary>
    /// <remarks>
    /// The transaction is read from the STORE rather than from the transaction service, because the
    /// service refuses an expired one without handing over the object — and this surface has to tell
    /// an expiry from an unknown identifier in order to answer a decision submitted too late with the
    /// truth. Reading past the TTL gate is the same thing the hub and the deep-link prefill reader do,
    /// and it is why the service is not given a member of its own for it.
    /// <para>
    /// The distinctions made here are for the JOURNAL, never for the page: the caller answers every
    /// refusal but a late decision with the one neutral page, because telling "no such transaction"
    /// from "there is one, and it is not to be answered here" is exactly what a holder of somebody
    /// else's identifier must not be able to do (SPEC-039 C15).
    /// </para>
    /// </remarks>
    /// <param name="sessionId">Public identifier as the request stated it.</param>
    /// <param name="transactionStore">Transaction store.</param>
    /// <param name="timeProvider">Clock of the engine — the same one the TTL is measured by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was found, with the transaction it was found over.</returns>
    private static async Task<AnswerableLookup> ResolveAnswerableAsync(
        string? sessionId,
        ITransactionStore transactionStore,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var transactionId = SessionIdMapper.ToTransactionId(sessionId);
        if (transactionId is null)
        {
            return new AnswerableLookup(PagePrecondition.Unknown, null);
        }

        var transaction = await transactionStore.GetByIdAsync(transactionId.Value, cancellationToken);
        if (transaction is null)
        {
            return new AnswerableLookup(PagePrecondition.Unknown, null);
        }

        // A sign-in has its own path and its own confirmation page; and a confirmation nobody entered
        // a channel for has no identity to sign the answer with, so this page was never addressed for
        // it. Asked BEFORE the state, and deliberately: whatever the transaction has since run into,
        // an expiry of THIS page is not what happened to a transaction it was never asked about.
        if (!string.Equals(transaction.Type, TransactionTypes.Confirmation, StringComparison.Ordinal)
            || transaction.ChannelIdentitySnapshot is null)
        {
            return new AnswerableLookup(PagePrecondition.NotAnswerable, transaction);
        }

        // WHY the transaction is over is what the two codes tell apart. The cleanup moves a transaction
        // whose TTL ran out into Expired and only drops it a retention later, so that state is itself
        // the record of an expiry; every other terminal state means a decision was recorded, which is
        // not one. Both readings of the same fact are kept, because they are the same fact seen on
        // either side of the cleanup pass.
        if (transaction.State is TransactionState.Expired)
        {
            return new AnswerableLookup(PagePrecondition.Expired, transaction);
        }

        if (!transaction.IsAwaitingWebConfirmation())
        {
            return new AnswerableLookup(PagePrecondition.NotAnswerable, transaction);
        }

        return transaction.IsExpired(timeProvider.GetUtcNow())
            ? new AnswerableLookup(PagePrecondition.Expired, transaction)
            : new AnswerableLookup(PagePrecondition.Answerable, transaction);
    }

    /// <summary>
    /// Answers a request the precondition refused, and records that refusal once.
    /// </summary>
    /// <remarks>
    /// A decision submitted after the TTL is the one refusal that is told truthfully: the visitor
    /// decided something, and the receipt of an expiry is the answer to what they did. Everything
    /// else — including opening the question page after the TTL — gets the neutral page.
    /// </remarks>
    /// <param name="precondition">What the precondition found; never
    /// <see cref="PagePrecondition.Answerable"/>.</param>
    /// <param name="transaction">Transaction the request named.</param>
    /// <param name="receiptText">Port the terminal wording comes from.</param>
    /// <param name="surface">Render context of this transaction's pages.</param>
    /// <param name="journal">Where the refusal is written down.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page to answer with.</returns>
    private static async Task<IResult> AnswerRefusedPreconditionAsync(
        PagePrecondition precondition,
        Transaction transaction,
        IOutcomeReceiptText receiptText,
        ConfirmSurface surface,
        RefusalJournal journal,
        CancellationToken cancellationToken)
    {
        await RecordPageRefusalAsync(journal, transaction, AuditCodeOf(precondition), null);

        return precondition is PagePrecondition.Expired
            // Nothing was written for an expiry — the moment being reported is the TTL itself, which
            // is when this transaction ended, not when the visitor found out.
            ? await ReceiptAsync(
                TransactionOutcome.Expired, transaction, transaction.ExpiresAt, receiptText, surface,
                cancellationToken)
            : await UnavailableAsync(surface, cancellationToken);
    }

    /// <summary>
    /// Names the audit code of a refused precondition.
    /// </summary>
    /// <param name="precondition">What the precondition found; never
    /// <see cref="PagePrecondition.Answerable"/> and never <see cref="PagePrecondition.Unknown"/>,
    /// which leaves no trace at all.</param>
    /// <returns>The code the journal reads the reason off.</returns>
    private static string AuditCodeOf(PagePrecondition precondition) =>
        precondition is PagePrecondition.Expired
            ? ConfirmationPageAuditCodes.Expired
            : ConfirmationPageAuditCodes.NotAnswerable;

    /// <summary>
    /// Records a refusal of this surface on a transaction it FOUND: one log entry and one audit event
    /// on the transaction bus (SPEC-039 E48).
    /// </summary>
    /// <remarks>
    /// This is the single point where a refusal is written down, so exactly one event goes out per
    /// refused request whichever route was asked and wherever the refusal was decided. The reason is
    /// told to the integrator's journal alone — the visitor's answer is chosen by the caller and does
    /// not depend on it (SPEC-039 C15).
    /// <para>
    /// The event goes out through <see cref="ITransactionEventPublisher"/> rather than straight into
    /// the audit sink: the publisher is registered by the transaction engine itself, so it resolves on
    /// every host that has transactions at all, while the sink exists only where the audit component
    /// was added. A host without it simply has no subscriber for the event, and the page works as
    /// before.
    /// </para>
    /// </remarks>
    /// <param name="journal">Where the refusal is written down.</param>
    /// <param name="transaction">Transaction the request named.</param>
    /// <param name="auditCode">Reason the journal reads.</param>
    /// <param name="refusedWriteErrorCode">Code the write of the decision failed with, when the
    /// refusal was decided there rather than by the precondition.</param>
    /// <returns>The completed operation.</returns>
    private static async Task RecordPageRefusalAsync(
        RefusalJournal journal,
        Transaction transaction,
        string auditCode,
        string? refusedWriteErrorCode)
    {
        // transaction.Id.ToString() is Base62 — no log injection. Nothing of the request or of the
        // snapshot is named here (SPEC-039 N31).
        journal.Logger.LogInformation(
            "The confirmation page refused the request. TransactionId: {TransactionId}, Reason: {Reason}, ErrorCode: {ErrorCode}",
            transaction.Id.ToString(),
            auditCode,
            refusedWriteErrorCode);

        // The audit event states the channel of the transaction. An empty type means a transaction
        // nobody has entered a channel for: the event is then not published at all rather than naming
        // an invented channel — the log entry above still stands.
        var channelType = transaction.ChannelIdentitySnapshot?.ChannelType
            ?? transaction.RequestedChannelType;
        if (string.IsNullOrEmpty(channelType))
        {
            return;
        }

        try
        {
            var eventPublisher = journal.HttpContext.RequestServices
                .GetRequiredService<ITransactionEventPublisher>();

            // CancellationToken.None — the occurrence must reach the journal even if the browser goes
            // away; otherwise abandoning the request would suppress its own trace. No attribution is
            // attached: the visitor here is unauthenticated by definition, and the identity the
            // transaction carries belongs to its owner, who did not act.
            await eventPublisher.PublishAsync(
                new TransactionChannelAuditEvent
                {
                    TransactionId = transaction.Id,
                    OccurredAt = journal.TimeProvider.GetUtcNow(),
                    ChannelType = channelType,
                    EventCode = auditCode,
                    Details = null
                },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // An unavailable journal does not turn into a denial of the page: the visitor gets the
            // same answer either way.
            journal.Logger.LogWarning(ex,
                "Failed to publish audit event {EventCode}. TransactionId: {TransactionId}",
                auditCode,
                transaction.Id.ToString());
        }
    }

    /// <summary>
    /// Ends a transaction whose subject cannot be worded (SPEC-039 E28).
    /// </summary>
    /// <param name="transaction">Transaction the question was asked for.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The completed operation.</returns>
    private static async Task FailForSubjectUnavailableAsync(
        Transaction transaction,
        ITransactionService transactionService,
        ILogger logger)
    {
        // CancellationToken.None — the transaction must be closed even if the browser goes away.
        var failResult = await transactionService.FailTransactionAsync(
            transaction.Id,
            TransactionErrorCodes.ConfirmationTemplateUnavailable,
            transaction.ConcurrencyToken,
            CancellationToken.None);

        if (failResult.IsSuccess)
        {
            return;
        }

        // transaction.Id.ToString() is Base62 — no log injection. Nothing of the subject is named
        // here or anywhere on this path (SPEC-039 N31).
        if (failResult.Error.Code is TransactionErrorCodes.InvalidStateTransition)
        {
            logger.LogDebug(
                "The transaction decision is already recorded — the missing subject changes nothing. TransactionId: {TransactionId}",
                transaction.Id.ToString());
            return;
        }

        logger.LogWarning(
            "Failed to move the transaction to Failed (the subject of the confirmation is unavailable). TransactionId: {TransactionId}, ErrorCode: {ErrorCode}",
            transaction.Id.ToString(),
            failResult.Error.Code);
    }

    /// <summary>
    /// Answers a decision the store refused to record, by what the refusal means.
    /// </summary>
    /// <remarks>
    /// The classification is read off the error code rather than assumed, because the three meanings
    /// need three different answers — and one of them needs the transaction closed. A downstream
    /// refusal on the confirmation path leaves the transaction exactly as it was: still Pending, still
    /// carrying the snapshot, still answerable until the TTL, while the relying party would read
    /// <c>Expired</c> where a failure happened. Closing it is best effort — if the token has already
    /// moved, whoever moved it decides the outcome.
    /// </remarks>
    /// <param name="errorCode">Code the write failed with.</param>
    /// <param name="transaction">Transaction as it was read before the write.</param>
    /// <param name="closeOnDownstreamRefusal">Whether an unclassified refusal leaves something to
    /// close: the confirmation path may have moved the transaction, the refusal path never does.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="receiptText">Port the terminal wording comes from.</param>
    /// <param name="surface">Render context of this transaction's pages.</param>
    /// <param name="journal">Where the refusal is written down.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page to answer with.</returns>
    private static async Task<IResult> AnswerRefusedDecisionAsync(
        string errorCode,
        Transaction transaction,
        bool closeOnDownstreamRefusal,
        ITransactionService transactionService,
        IOutcomeReceiptText receiptText,
        ConfirmSurface surface,
        RefusalJournal journal,
        CancellationToken cancellationToken)
    {
        switch (DecisionRefusalClassifier.Classify(errorCode))
        {
            case DecisionRefusalClass.Expired:
                // The window between reading the transaction and writing the decision: the same
                // occurrence the precondition names, recorded under the same reason.
                await RecordPageRefusalAsync(
                    journal, transaction, ConfirmationPageAuditCodes.Expired, errorCode);

                // Same occurrence as the precondition names, so the same moment: the TTL the
                // transaction ended at.
                return await ReceiptAsync(
                    TransactionOutcome.Expired, transaction, transaction.ExpiresAt, receiptText,
                    surface, cancellationToken);

            case DecisionRefusalClass.Race:
                await RecordPageRefusalAsync(
                    journal, transaction, ConfirmationPageAuditCodes.NotAnswerable, errorCode);

                return await UnavailableAsync(surface, cancellationToken);

            default:
                // A refusal of ours, not of the visitor's request: it is not what the audit codes of
                // this surface are about, and the transaction being closed leaves its own trace.
                journal.Logger.LogError(
                    "Failed to record the answer to the confirmation. TransactionId: {TransactionId}, ErrorCode: {ErrorCode}",
                    transaction.Id.ToString(),
                    errorCode);

                if (closeOnDownstreamRefusal)
                {
                    await CloseAfterDownstreamRefusalAsync(transaction, transactionService, journal.Logger);
                }

                return await UnavailableAsync(surface, cancellationToken);
        }
    }

    /// <summary>
    /// Closes a transaction left open by a downstream refusal, so it stops offering the same question.
    /// </summary>
    /// <param name="transaction">Transaction as it was read before the write.</param>
    /// <param name="transactionService">Transaction service.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The completed operation.</returns>
    private static async Task CloseAfterDownstreamRefusalAsync(
        Transaction transaction,
        ITransactionService transactionService,
        ILogger logger)
    {
        // CancellationToken.None — the transaction must be closed even if the browser goes away.
        var failResult = await transactionService.FailTransactionAsync(
            transaction.Id,
            TransactionErrorCodes.DownstreamFinalizeFailed,
            transaction.ConcurrencyToken,
            CancellationToken.None);

        // An invalid transition here means the transaction is already terminal — the compensation
        // inside the finalizer got there first, which is exactly the outcome wanted.
        if (failResult.IsFailure
            && !string.Equals(
                failResult.Error.Code, TransactionErrorCodes.InvalidStateTransition, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Failed to close the transaction after a refused finalization. TransactionId: {TransactionId}, ErrorCode: {ErrorCode}",
                transaction.Id.ToString(),
                failResult.Error.Code);
        }
    }

    /// <summary>
    /// Builds the receipt page of one outcome. The wording is asked of the port and never spelled
    /// here: the receipt is a message of the template mechanism, addressed by what this render point
    /// knows about it (SPEC-036 TPL-123).
    /// </summary>
    /// <param name="outcome">Terminal outcome to report.</param>
    /// <param name="transaction">Transaction the receipt belongs to.</param>
    /// <param name="outcomeMoment">Moment this outcome happened — the one the decision was written
    /// down at, or the TTL an expiry is reported against.</param>
    /// <param name="receiptText">Port the terminal wording comes from.</param>
    /// <param name="surface">Render context of this transaction's pages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The receipt page.</returns>
    private static async Task<IResult> ReceiptAsync(
        TransactionOutcome outcome,
        Transaction transaction,
        DateTimeOffset outcomeMoment,
        IOutcomeReceiptText receiptText,
        ConfirmSurface surface,
        CancellationToken cancellationToken)
    {
        // The receipt belongs to a transaction that WAS found and answered, so both its wording and its
        // branding are resolved over that transaction's own ownership — the same context the question
        // page carried, assembled once here (SPEC-036 TPL-116).
        var ownership = TransactionResolutionContext.For(transaction);

        var text = await receiptText.RenderAsync(
            new OutcomeReceiptAddress(
                outcome,
                OutcomeReceiptSurfaces.CorePage,
                transaction.Type,
                transaction.ConfirmationSnapshot?.ActionType,
                transaction.ChannelIdentitySnapshot?.ChannelType),
            surface.PageLanguage,
            // The zone the transaction states, beside the language the page is served in: one pair,
            // read off the transaction the same way every other consumer of it reads the zone.
            transaction.GetUiTimeZone(),
            outcomeMoment,
            // Every receipt of this page answers a decision submitted from the question this page
            // showed, so it may name the subject that question named (SPEC-036 TPL-123), beside what
            // the core states about the transaction (TPL-124).
            TransactionSlotSource.From(transaction),
            ownership,
            cancellationToken);

        var branding = await surface.BrandingResolver.ResolveAsync(ownership, cancellationToken);

        var html = TransactionConfirmPage.BuildReceipt(
            text, surface.Localizer, surface.PageLanguage, branding);

        return Results.Content(html, AuthPageConstants.HtmlContentType, Encoding.UTF8);
    }

    /// <summary>
    /// Builds the neutral answer — the single response of this surface to everything it refuses.
    /// </summary>
    /// <remarks>
    /// The very page the entry surface answers with, on purpose: a user turned away by one of the two
    /// pages of a confirmation must not be able to tell which one turned them away. Its branding is
    /// resolved over the CORE level even when a transaction was found, and its language is the
    /// request's, so neither the look nor the wording of the answer becomes a readable state of
    /// somebody else's transaction. Status 200 — this is a page, not an API.
    /// </remarks>
    /// <param name="surface">Render context of the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The neutral page.</returns>
    private static async Task<IResult> UnavailableAsync(
        ConfirmSurface surface,
        CancellationToken cancellationToken)
    {
        var branding = await surface.BrandingResolver.ResolveAsync(ResolutionContext.Core, cancellationToken);
        var html = TransactionEntryUnavailablePage.Build(
            surface.Localizer, surface.RequestLanguage, branding);

        return Results.Content(html, AuthPageConstants.HtmlContentType, Encoding.UTF8);
    }
}
