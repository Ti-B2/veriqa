// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text;

using Microsoft.Extensions.Options;

using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Configuration.Enums;
using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.AuthServer.UI;
using Veriqa.Core.AuthServer.UI.Services;
using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// Entry page of an already created confirmation transaction (GET /auth/transaction, SPEC-039 C15) —
/// the second kind of entry next to the channel material returned by the creation answer. The page
/// shows the way INTO the channel and nothing else: no subject matter on it and none in its URL.
/// <para>
/// The transaction is addressed by its public identifier, and the identifier is what makes the page
/// unguessable (256-bit CSPRNG, <see cref="TransactionId.NewId"/>) — there is no second token to
/// carry. Opening the page again before the transaction ends is normal: it is an entry point, not a
/// single-use nonce, and the transaction's own protections still admit exactly one confirmation.
/// </para>
/// <para>
/// The page never leaves itself. A confirmation transaction has no browser callback of its own
/// (SPEC-039 C19), so a terminal outcome is shown in place — the browser stays on this URL, the
/// transaction stays in the store, and the relying party still reads the outcome through its own
/// surfaces (R49).
/// </para>
/// </summary>
public static class TransactionEntryEndpoint
{
    /// <summary>
    /// Registers the transaction entry page.
    /// </summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <param name="rateLimitPolicyName">Rate limiting policy name (null — no limiting).</param>
    /// <returns>The route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapTransactionEntryEndpoint(
        this IEndpointRouteBuilder endpoints,
        string? rateLimitPolicyName = null)
    {
        var builder = endpoints.MapGet(OidcEndpoints.TransactionEntryPage, HandleEntryPageAsync);

        if (!string.IsNullOrEmpty(rateLimitPolicyName))
        {
            builder.RequireRateLimiting(rateLimitPolicyName);
        }

        return endpoints;
    }

    /// <summary>
    /// Answers with the channel entry page of the addressed transaction, or with the neutral page.
    /// </summary>
    private static async Task<IResult> HandleEntryPageAsync(
        HttpContext httpContext,
        ITransactionService transactionService,
        IChannelDisplayService channelDisplayService,
        IAuthPageRenderer authPageRenderer,
        AuthPageSettingsResolver pageSettingsResolver,
        IConfirmationPromptLocalizer promptLocalizer,
        IConfirmationPromptContextFactory promptContextFactory,
        IOutcomeReceiptText receiptText,
        IConfigurationResolver configurationResolver,
        IOptions<VeriqaOptions> veriqaOptions,
        CorePageBrandingResolver brandingResolver,
        IAuthPageLanguageRegistry languageRegistry,
        CorePageResourceScope resourceScope,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        // The method resolves the addressed transaction and renders either its entry page or the
        // neutral one — the three refusals being indistinguishable from the outside

        var cancellationToken = httpContext.RequestAborted;
        var defaultLanguage = veriqaOptions.Value.Localization.DefaultLanguage;
        var supportedLanguages = languageRegistry.SupportedLanguages;

        // The language of the REQUEST. It is what the neutral page uses in every one of its cases,
        // including the one where a transaction was found — see TransactionEntryUnavailablePage.
        var requestLanguage = AuthPageStrings.DetectLanguage(
            httpContext.Request.Headers.AcceptLanguage.ToString(), defaultLanguage, supportedLanguages);

        var transactionId = SessionIdMapper.ToTransactionId(
            httpContext.Request.Query[OidcConstants.SessionIdParameterName].FirstOrDefault());

        if (transactionId is null)
        {
            return await UnavailableAsync(
                promptLocalizer, requestLanguage, brandingResolver, cancellationToken);
        }

        var txResult = await transactionService.GetTransactionAsync(transactionId.Value, cancellationToken);
        if (txResult.IsFailure)
        {
            return await UnavailableAsync(
                promptLocalizer, requestLanguage, brandingResolver, cancellationToken);
        }

        var transaction = txResult.Value;

        // A transaction of another type is refused with the same page as a missing one: this route
        // addresses confirmations, and signing in has a route of its own.
        if (!string.Equals(transaction.Type, TransactionTypes.Confirmation, StringComparison.Ordinal)
            || transaction.IsTerminal())
        {
            return await UnavailableAsync(
                promptLocalizer, requestLanguage, brandingResolver, cancellationToken);
        }

        // From here on the page belongs to a live transaction, and every level-owned setting is
        // resolved over ITS ownership context — the same one the channel messages of this transaction
        // resolve over.
        var resolution = TransactionResolutionContext.For(transaction);
        var pageSettings = await pageSettingsResolver.ResolveAsync(resolution, cancellationToken);

        // The page speaks the language of the transaction, which is the language the relying party
        // stated when it created it; a transaction that stated none falls back to the request.
        var language = AuthPageLocalization.ResolveTransactionLanguage(
            transaction, requestLanguage, defaultLanguage, supportedLanguages);

        // Per-request channel display override from the ui_config record (CFG-211) — same narrowing
        // rule as on the sign-in path.
        var displayOverride = pageSettings.Channels is null && pageSettings.ChannelDisplayMode is null
            ? null
            : new ChannelDisplayOverride(pageSettings.Channels, pageSettings.ChannelDisplayMode);

        // The effective external stylesheet and script of this page, stated for its CSP before the
        // response starts (SPEC-007 UI-054, UI-040).
        resourceScope.Declare(pageSettings.Design.CustomCssPath, pageSettings.Design.CustomJsPath);

        var channelDataResult = await channelDisplayService.GetChannelDisplayDataAsync(
            transaction.Id,
            transaction.AllowedChannelTypes,
            resolution,
            displayOverride,
            cancellationToken);

        if (channelDataResult.IsFailure)
        {
            loggerFactory.CreateLogger(typeof(TransactionEntryEndpoint)).LogError(
                "Failed to prepare channel data for transaction {TransactionId}: {Error}",
                transaction.Id.ToString(),
                channelDataResult.Error.Message);

            return Results.Problem(
                detail: "Failed to prepare authentication channel data",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: OidcErrorCodes.ChannelDisplayFailed);
        }

        var sessionId = SessionIdMapper.ToSessionId(transaction.Id);

        // What is being confirmed — shown here only when the deployment put this surface into the set
        // of subject-display surfaces (SPEC-039 R38, CFG-104). The question itself stays on the page
        // that asks it: this one shows the subject and takes no answer (R36).
        var showsSubject = await configurationResolver.ShowsSubjectOnAsync(
            resolution, ConfirmationSubjectSurface.InteractionPage, cancellationToken);

        var subjectText = showsSubject
            ? await ResolveSubjectTextAsync(promptContextFactory, transaction, language, cancellationToken)
            : null;

        // The terminal lines of this page. It never leaves itself for a confirmation transaction, so
        // its status line is the LAST screen the user sees and may not word a sign-in that did not
        // happen (SPEC-039 R51): the wording comes from the one port that words outcomes.
        var terminalTexts = await ResolveTerminalTextsAsync(
            receiptText, promptLocalizer, transaction, showsSubject, language, cancellationToken);

        var renderContext = new AuthPageRenderContext
        {
            SessionId = sessionId,
            ExpiresAt = transaction.ExpiresAt,
            // The countdown runs on the server's remainder here for the same reason it does on the
            // sign-in path: the browser's clock is not a shared one, and a transaction must not end
            // on this page because the device it is opened on runs fast.
            RemainingSeconds = Math.Max(0, (transaction.ExpiresAt - timeProvider.GetUtcNow()).TotalSeconds),
            // No way out to offer: a confirmation transaction is created server-to-server and has no
            // request of its own to repeat, so the expired page states the fact and nothing more —
            // starting over is the calling application's to arrange (SPEC-039 C15).
            RestartUrl = null,
            Channels = channelDataResult.Value,
            Language = language,
            CspNonce = httpContext.Items[SecurityHeaderConstants.CspNonceItemKey] as string,
            Design = pageSettings.Design,
            Title = pageSettings.Title,
            Instruction = pageSettings.Instruction,
            PathBase = httpContext.Request.PathBase.Value ?? string.Empty,
            QrVisibility = pageSettings.QrVisibility,
            // No callback URL and no navigation: this transaction is finalized on the server, and the
            // callback of the sign-in path would end it as a stale session instead.
            CallbackUrl = null,
            NavigatesAway = false,
            // The one place this page does take the browser to: the core's own question, once the
            // channel has let the user in and the core is waiting for the answer (SPEC-039 E29). It is
            // stated here, from the constants of the routes, rather than taken out of a status message —
            // so both delivery paths of that status lead to the same page. Root-relative: the path base
            // is applied by the page.
            ConfirmationQuestionUrl =
                $"{OidcEndpoints.TransactionConfirmPage}"
                + $"?{OidcConstants.SessionIdParameterName}={Uri.EscapeDataString(sessionId)}",
            SubjectText = subjectText,
            TerminalConfirmedText = terminalTexts.Confirmed,
            TerminalExpiredText = terminalTexts.Expired,
            TerminalDeclinedText = terminalTexts.Declined,
            TerminalErrorText = terminalTexts.Error
        };

        var html = authPageRenderer.RenderAuthPage(renderContext);

        return Results.Content(html, AuthPageConstants.HtmlContentType, Encoding.UTF8);
    }

    /// <summary>
    /// The four lines this page ends on, worded and localized.
    /// </summary>
    /// <param name="Confirmed">The transaction was confirmed.</param>
    /// <param name="Expired">The transaction ran out of time.</param>
    /// <param name="Declined">The USER refused it.</param>
    /// <param name="Error">Anything else that failed it — including a failure that states no
    /// reason.</param>
    private readonly record struct TerminalTexts(
        string Confirmed,
        string Expired,
        string Declined,
        string Error);

    /// <summary>
    /// Resolves the subject of the confirmation for this page. Asked only once the deployment put this
    /// surface into the set of subject-display surfaces (SPEC-012 §4.11).
    /// </summary>
    /// <remarks>
    /// The wording is asked of the SAME factory the page that asks the question asks: one engine of
    /// substitution, one set of render protections, one place they have to be right (SPEC-039 R8). The
    /// channel parameters of that contract are stated empty here — the subject branch does not read
    /// them, and a second entry point "give me the subject text" is the second implementation this
    /// must not create.
    /// <para>
    /// A subject that cannot be worded is NOT fatal here and does not end the transaction: the entry
    /// page shows a way into the channel, the showing of the subject is an addition to it, and E28
    /// belongs to the surface that would otherwise ask a question with nothing in it.
    /// </para>
    /// </remarks>
    /// <param name="promptContextFactory">Factory of the confirmation context.</param>
    /// <param name="transaction">Transaction the page belongs to.</param>
    /// <param name="language">Language of the page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subject as plain text, or null when it cannot be worded.</returns>
    private static async Task<string?> ResolveSubjectTextAsync(
        IConfirmationPromptContextFactory promptContextFactory,
        Transaction transaction,
        string language,
        CancellationToken cancellationToken)
    {
        var promptContext = await promptContextFactory.CreateAsync(
            transaction,
            channelType: string.Empty,
            channelUserId: string.Empty,
            language,
            cancellationToken);

        return promptContext is SubjectConfirmationPromptContext subject ? subject.PromptText : null;
    }

    /// <summary>
    /// Resolves the lines this page ends on: three receipts of an outcome, plus the neutral wording of
    /// a failure that is not the user's refusal.
    /// </summary>
    /// <remarks>
    /// The three receipts are three addresses — one per outcome — and therefore three resolutions;
    /// they are asked of the port, not worded here, so this page introduces no terminal text of its
    /// own (SPEC-036 TPL-123). The fourth line is not a receipt at all: a failure has no receipt kind,
    /// and the page says what its own neutral page says, through the localizer of its own keys.
    /// </remarks>
    /// <param name="receiptText">Port the terminal wording comes from.</param>
    /// <param name="promptLocalizer">Locale-file localizer (for the page's own neutral key).</param>
    /// <param name="transaction">Transaction the page belongs to.</param>
    /// <param name="showsSubject">Whether the deployment shows the subject on this page.</param>
    /// <param name="language">Language of the page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The four terminal lines.</returns>
    private static async Task<TerminalTexts> ResolveTerminalTextsAsync(
        IOutcomeReceiptText receiptText,
        IConfirmationPromptLocalizer promptLocalizer,
        Transaction transaction,
        bool showsSubject,
        string language,
        CancellationToken cancellationToken)
    {
        return new TerminalTexts(
            await ReceiptAsync(TransactionOutcome.Confirmed),
            await ReceiptAsync(TransactionOutcome.Expired),
            await ReceiptAsync(TransactionOutcome.Declined),
            AuthPageLocalization.Localize(
                promptLocalizer, TransactionEntryUnavailablePage.PageTitle, language));

        ValueTask<string> ReceiptAsync(TransactionOutcome outcome) => receiptText.RenderAsync(
            new OutcomeReceiptAddress(
                outcome,
                OutcomeReceiptSurfaces.InteractionPage,
                transaction.Type,
                transaction.ConfirmationSnapshot?.ActionType,
                // The channel is named only once one has taken part: before the user enters one, this
                // page's receipt narrows by no channel, exactly as any unnamed axis does.
                transaction.ChannelIdentitySnapshot?.ChannelType),
            language,
            // The zone the transaction states, beside the language of the page: the pair by which this
            // recipient reads a moment (TPL-016).
            transaction.GetUiTimeZone(),
            // No moment: this page words all three receipts BEFORE any of them happens — the script
            // shows whichever one the outcome turns out to be — so there is no moment of an outcome to
            // state here. The clock of the request would be the moment the page was built, which is
            // not what an outcome receipt reports.
            outcomeMoment: null,
            // The receipts are worded into the page before any decision, so they may name the subject
            // only where the page already shows it: a caller value in a receipt discloses no more than
            // the subject line beside it (SPEC-036 TPL-123, SPEC-039 R38). Outside that set the page
            // carries no caller value at all — only what the core states about the transaction
            // (TPL-124).
            showsSubject
                ? TransactionSlotSource.From(transaction)
                : TransactionSlotSource.From(transaction).WithoutCallerValues(),
            // The four terminal lines of one transaction are worded over that transaction's ownership,
            // so a wording an application or a ui_config record declares reaches the page (TPL-116).
            TransactionResolutionContext.For(transaction),
            cancellationToken);
    }

    /// <summary>
    /// Builds the neutral answer — the single response for "no such transaction", "already over" and
    /// "another type".
    /// </summary>
    /// <param name="promptLocalizer">Locale-file localizer.</param>
    /// <param name="language">Language detected from the request.</param>
    /// <param name="brandingResolver">Resolver of the effective page branding.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The neutral page.</returns>
    private static async Task<IResult> UnavailableAsync(
        IConfirmationPromptLocalizer promptLocalizer,
        string language,
        CorePageBrandingResolver brandingResolver,
        CancellationToken cancellationToken)
    {
        // The branding is resolved over the CORE level even when a transaction was found: resolving it
        // over the transaction's levels would make the look of the answer a readable state of somebody
        // else's transaction. Status 200 — this is a page, not an API.
        var branding = await brandingResolver.ResolveAsync(ResolutionContext.Core, cancellationToken);
        var html = TransactionEntryUnavailablePage.Build(promptLocalizer, language, branding);

        return Results.Content(html, AuthPageConstants.HtmlContentType, Encoding.UTF8);
    }
}
