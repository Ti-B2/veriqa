// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using Veriqa.Core.AuthServer.UI;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// Friendly "sign-in session expired" page shown by the authorization callback when the transaction
/// is no longer valid for sign-in — it was not found (cleaned up after its retention window) or is not
/// in the Completed state. In product mode this replaces a raw RFC ProblemDetails 400 ("Transaction not
/// found"), which read as a bare core error to the end user (e.g. answering/refreshing the
/// web-confirmation page after the completed transaction was reaped, or opening a stale callback link).
///
/// The page is deliberately RP-agnostic: at this point the transaction (and its redirect_uri) may already
/// be gone, so there is no reliable, safe restart target to link to. It states plainly that the session
/// expired and asks the user to start the sign-in again from the application. Standalone core page that
/// shares the service-page stylesheet and branding of the contour with <see cref="WebConfirmPage"/>.
/// </summary>
internal static class CallbackExpiredPage
{
    /// <summary>
    /// Page title Natural Key.
    /// </summary>
    private const string PageTitle = "Sign-in session expired";

    /// <summary>
    /// Body-message Natural Key.
    /// </summary>
    private const string Message =
        "This sign-in link has expired or was already used. Please start the sign-in again from the application.";

    /// <summary>
    /// Builds the expired-session page HTML, localized into the recipient's language (the English base
    /// text — the key — is the fallback; the page is either fully in the requested language or fully
    /// in English, never mixed, so the html lang attribute stays truthful, WCAG 3.1.1).
    /// </summary>
    /// <param name="localizer">Locale-file localizer (Natural Key → localized text).</param>
    /// <param name="language">Detected language code (ru, en, zh, …).</param>
    /// <param name="branding">Effective branding, already resolved by the caller. On a FOUND
    /// transaction it carries that transaction's own levels (SPEC-007 UI-101); on a transaction that
    /// was not found there is no context to resolve against and the branding is the global one, which
    /// is the norm here rather than a fallback (UI-090).</param>
    /// <returns>HTML page.</returns>
    public static string Build(
        IConfirmationPromptLocalizer localizer,
        string language,
        CorePageBranding? branding)
    {
        var contentLanguage = AuthPageLocalization.ResolveContentLanguage(localizer, PageTitle, language);
        var pageTitle = AuthPageLocalization.Localize(localizer, PageTitle, contentLanguage);
        var message = AuthPageLocalization.Localize(localizer, Message, contentLanguage);

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
                <p class="veriqa-instruction">{{WebUtility.HtmlEncode(message)}}</p>
              </main>
              {{CorePageAttribution.BuildLine(contentLanguage)}}
            </body>
            </html>
            """;
    }
}
