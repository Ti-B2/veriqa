// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Net;

using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.UI;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// The two screens of the core confirmation surface (SPEC-039 R37): the question a confirmation
/// transaction is answered on, and the receipt of how it ended.
/// <para>
/// The page words NOTHING of its own beyond its title and the two button labels. The question comes
/// already worded from the confirmation context of the transaction, and the terminal line comes from
/// the receipt port — so a subject is never replaced by a sign-in text (E28) and the outcome is never
/// spelled twice in two places.
/// </para>
/// <para>
/// The sign-in confirmation page <see cref="WebConfirmPage"/> is a different surface with different
/// texts and different routes: it belongs to the OIDC path, and a transaction created
/// server-to-server never travels it (SPEC-039 §1.2).
/// </para>
/// </summary>
internal static class TransactionConfirmPage
{
    /// <summary>
    /// Page title Natural Key. Neutral to the type of the transaction and carrying no sign-in
    /// semantics: what exactly is being confirmed is said by the question below it, and that text
    /// belongs to the calling party rather than to this page.
    /// </summary>
    private const string PageTitle = "Confirmation required";

    /// <summary>
    /// Confirmation button Natural Key — the one the sign-in confirmation page already uses.
    /// </summary>
    private const string ConfirmButtonText = "Yes";

    /// <summary>
    /// Decline button Natural Key — the one the sign-in confirmation page already uses.
    /// </summary>
    private const string DeclineButtonText = "No";

    /// <summary>
    /// Builds the question page: the subject of the confirmation and the two answers.
    /// </summary>
    /// <remarks>
    /// The subject arrives as PLAIN text and is HTML-encoded here — that, and only that, is this
    /// page's share of the render protections: the substitution, the sanitizing and the degradation
    /// between wording variants all happened inside the engine that produced
    /// <paramref name="promptText"/>, and a second render here would be a second place to keep them
    /// correct (SPEC-039 R8).
    /// </remarks>
    /// <param name="sessionId">Public identifier of the transaction, carried into both POSTs.</param>
    /// <param name="antiforgeryFormFieldName">AntiForgery token field name.</param>
    /// <param name="antiforgeryToken">AntiForgery request token value.</param>
    /// <param name="promptText">The question, already worded and localized.</param>
    /// <param name="pathBase">Path base the application is hosted under (empty — the site root).</param>
    /// <param name="localizer">Locale-file localizer (Natural Key → localized text).</param>
    /// <param name="language">Language of the page (the transaction's, or the request's).</param>
    /// <param name="branding">Effective branding of the page's own transaction.</param>
    /// <returns>HTML page.</returns>
    public static string BuildQuestion(
        string sessionId,
        string? antiforgeryFormFieldName,
        string? antiforgeryToken,
        string promptText,
        string pathBase,
        IConfirmationPromptLocalizer localizer,
        string language,
        CorePageBranding? branding)
    {
        // WCAG 3.1.1: html lang must match the language of the rendered text, so the language is
        // resolved once and used both for the attribute and for every string of the page.
        var contentLanguage = AuthPageLocalization.ResolveContentLanguage(localizer, PageTitle, language);
        var pageTitle = AuthPageLocalization.Localize(localizer, PageTitle, contentLanguage);
        var confirmButtonText = AuthPageLocalization.Localize(localizer, ConfirmButtonText, contentLanguage);
        var declineButtonText = AuthPageLocalization.Localize(localizer, DeclineButtonText, contentLanguage);

        var encodedSessionId = WebUtility.HtmlEncode(sessionId);
        var encodedFieldName = WebUtility.HtmlEncode(antiforgeryFormFieldName ?? string.Empty);
        var encodedToken = WebUtility.HtmlEncode(antiforgeryToken ?? string.Empty);
        var encodedSessionParam = WebUtility.HtmlEncode(OidcConstants.SessionIdParameterName);

        // The form targets carry the path base: this application may be hosted under a sub-path, and
        // a root-relative action would post past it.
        var encodedAcceptAction = WebUtility.HtmlEncode(pathBase + OidcEndpoints.TransactionConfirmAccept);
        var encodedDeclineAction = WebUtility.HtmlEncode(pathBase + OidcEndpoints.TransactionConfirmDecline);

        return $$"""
            <!DOCTYPE html>
            <html lang="{{contentLanguage}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <meta name="robots" content="noindex" />
              <title>{{WebUtility.HtmlEncode(pageTitle)}}</title>
              {{CorePageHead.BuildServicePageHead(branding)}}
            </head>
            <body>
              <main class="veriqa-card">
                <h1 class="veriqa-title">{{WebUtility.HtmlEncode(pageTitle)}}</h1>
                <p class="veriqa-instruction">{{WebUtility.HtmlEncode(promptText)}}</p>
                <div class="veriqa-actions">
                  <form method="post" action="{{encodedAcceptAction}}">
                    <input type="hidden" name="{{encodedSessionParam}}" value="{{encodedSessionId}}" />
                    <input type="hidden" name="{{encodedFieldName}}" value="{{encodedToken}}" />
                    <button type="submit" class="veriqa-btn">{{WebUtility.HtmlEncode(confirmButtonText)}}</button>
                  </form>
                  <form method="post" action="{{encodedDeclineAction}}">
                    <input type="hidden" name="{{encodedSessionParam}}" value="{{encodedSessionId}}" />
                    <input type="hidden" name="{{encodedFieldName}}" value="{{encodedToken}}" />
                    <button type="submit" class="veriqa-btn veriqa-btn-decline">{{WebUtility.HtmlEncode(declineButtonText)}}</button>
                  </form>
                </div>
              </main>
              {{CorePageAttribution.BuildLine(contentLanguage)}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Builds the receipt page — one card carrying one line: how the transaction ended.
    /// </summary>
    /// <remarks>
    /// One neutral styling for all three outcomes, and the same text in the heading and in the
    /// title. A confirmed outcome painted differently from a declined one would be this page
    /// stating an outcome of its own beside the wording, and the wording is the only thing that may
    /// state it (SPEC-039 R51): a receipt is a report, not a verdict of the surface it lands on.
    /// <para>
    /// That single line is the page heading itself — one element carrying both the typographic
    /// heading and the status line, because the outcome may be spelled only once on the page
    /// (SPEC-015 §4.15: exactly one &lt;h1&gt; per page, and it is the page heading; §4.4: the
    /// receipt states its outcome by that one line, in the neutral state).
    /// </para>
    /// <para>
    /// The language is probed on the page's own key exactly as on the question page: both the page
    /// keys and the receipt wording are resolved out of the same host locale files by the same
    /// localizer, so a language the host carries no translation set for degrades both alike.
    /// </para>
    /// </remarks>
    /// <param name="outcomeText">Receipt text, already localized, as plain text.</param>
    /// <param name="localizer">Locale-file localizer (used only to probe the content language).</param>
    /// <param name="language">Language the receipt was rendered for.</param>
    /// <param name="branding">Effective branding of the page's own transaction.</param>
    /// <returns>HTML page.</returns>
    public static string BuildReceipt(
        string outcomeText,
        IConfirmationPromptLocalizer localizer,
        string language,
        CorePageBranding? branding)
    {
        var contentLanguage = AuthPageLocalization.ResolveContentLanguage(localizer, PageTitle, language);
        var encodedOutcome = WebUtility.HtmlEncode(outcomeText);

        return $$"""
            <!DOCTYPE html>
            <html lang="{{contentLanguage}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <meta name="robots" content="noindex" />
              <title>{{encodedOutcome}}</title>
              {{CorePageHead.BuildServicePageHead(branding)}}
            </head>
            <body>
              <main class="veriqa-card">
                <h1 class="veriqa-title veriqa-status">{{encodedOutcome}}</h1>
              </main>
              {{CorePageAttribution.BuildLine(contentLanguage)}}
            </body>
            </html>
            """;
    }
}
