// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.UI;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// Web sign-in confirmation page (LoginConfirmationMode.OnWebPage mode, SPEC-007 UI-038).
/// The page asks a question and offers a real choice: "Yes" posts to
/// <see cref="OidcEndpoints.AuthorizeCallbackConfirm"/> and completes the sign-in, "No" posts to
/// <see cref="OidcEndpoints.AuthorizeCallbackDecline"/> and declines it. Both carry the AntiForgery
/// token and session_id. Until the user answers, sign-in is NOT performed (GET does not call SignInAsync).
/// <para>
/// A single "Continue" button was not a confirmation: it offered no way to refuse, so the user could
/// only acknowledge a sign-in that was already decided. The decline branch is what makes this page a
/// confirmation surface rather than an interstitial.
/// </para>
/// </summary>
internal static class WebConfirmPage
{
    /// <summary>
    /// Page title.
    /// </summary>
    private const string PageTitle = "Confirm your sign-in";

    /// <summary>
    /// The confirmation question itself.
    /// </summary>
    private const string Question = "Do you confirm the sign-in?";

    /// <summary>
    /// Confirmation button text.
    /// </summary>
    private const string ConfirmButtonText = "Yes";

    /// <summary>
    /// Decline button text.
    /// </summary>
    private const string DeclineButtonText = "No";

    /// <summary>
    /// Builds the confirmation page HTML. All dynamic values are HTML-encoded. The page texts are
    /// resolved data-driven from the host locale files via <see cref="AuthPageLocalization"/> —
    /// the same rule and the same source the sign-in window uses; the English base text (the key)
    /// is the fallback.
    /// </summary>
    /// <param name="sessionId">Transaction session_id (carried into the POST for re-validation).</param>
    /// <param name="antiforgeryFormFieldName">AntiForgery token field name.</param>
    /// <param name="antiforgeryToken">AntiForgery request token value.</param>
    /// <param name="pathBase">Path base the application is hosted under (empty — the site root).</param>
    /// <param name="localizer">Locale-file localizer (Natural Key → localized text).</param>
    /// <param name="language">Detected language code (ru, en, zh, …).</param>
    /// <param name="branding">Effective branding, already resolved over the context of the page's own
    /// transaction — the same branding the sign-in window carries (SPEC-007 UI-054, UI-101).</param>
    /// <returns>HTML page.</returns>
    public static string Build(
        string? sessionId,
        string? antiforgeryFormFieldName,
        string? antiforgeryToken,
        string pathBase,
        IConfirmationPromptLocalizer localizer,
        string language,
        CorePageBranding? branding)
    {
        // WCAG 3.1.1: the html lang attribute must match the language of the rendered text. The
        // requested locale is honoured only if the localizer actually resolves it — otherwise the
        // page degrades to the English base text and lang must be "en", not the requested code.
        // Resolved once and used for lang and for every string: the page is either fully in the
        // requested language or fully on the English base — never mixed.
        var contentLanguage = AuthPageLocalization.ResolveContentLanguage(localizer, PageTitle, language);
        var pageTitle = AuthPageLocalization.Localize(localizer, PageTitle, contentLanguage);
        var question = AuthPageLocalization.Localize(localizer, Question, contentLanguage);
        var confirmButtonText = AuthPageLocalization.Localize(localizer, ConfirmButtonText, contentLanguage);
        var declineButtonText = AuthPageLocalization.Localize(localizer, DeclineButtonText, contentLanguage);

        var encodedSessionId = WebUtility.HtmlEncode(sessionId ?? string.Empty);
        var encodedFieldName = WebUtility.HtmlEncode(antiforgeryFormFieldName ?? string.Empty);
        var encodedToken = WebUtility.HtmlEncode(antiforgeryToken ?? string.Empty);
        // The form targets carry the path base: this application may be hosted under a sub-path, and
        // a root-relative action would post past it.
        var encodedAction = WebUtility.HtmlEncode(pathBase + OidcEndpoints.AuthorizeCallbackConfirm);
        var encodedDeclineAction = WebUtility.HtmlEncode(pathBase + OidcEndpoints.AuthorizeCallbackDecline);
        var encodedSessionParam = WebUtility.HtmlEncode(OidcConstants.SessionIdParameterName);

        return $$"""
            <!DOCTYPE html>
            <html lang="{{contentLanguage}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>{{WebUtility.HtmlEncode(pageTitle)}}</title>
              {{CorePageHead.BuildServicePageHead(branding)}}
            </head>
            <body>
              <main class="veriqa-card">
                <h1 class="veriqa-title">{{WebUtility.HtmlEncode(pageTitle)}}</h1>
                <p class="veriqa-instruction">{{WebUtility.HtmlEncode(question)}}</p>
                <div class="veriqa-actions">
                  <form method="post" action="{{encodedAction}}">
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
}
