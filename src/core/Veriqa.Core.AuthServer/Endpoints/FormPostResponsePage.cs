// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Text;
using Veriqa.Core.AuthServer.UI;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;

namespace Veriqa.Core.AuthServer.Endpoints;

/// <summary>
/// Auto-posting page of the <c>form_post</c> response mode (OAuth 2.0 Form Post Response Mode): it
/// carries the authorization response parameters to the relying party's <c>redirect_uri</c> in a POST
/// body instead of a URL. The page is Veriqa's own rather than the one the library ships, and that is
/// the point of it: the shipped page holds an inline script with no nonce, which this deployment's
/// content security policy blocks outright — and <c>&lt;noscript&gt;</c> does not save it, because the
/// fallback shows only when scripts are DISABLED, not when a script is refused by policy. Rendering the
/// page here lets the script take the nonce of this very request through the ordinary path, so
/// <c>script-src</c> is not relaxed at all.
/// <para>
/// The user normally never sees it: the script posts the form as soon as the document loads. The
/// submit button is the manual way through whenever the script does not run — scripts switched off,
/// or a script refused by a policy this page did not get a nonce from.
/// </para>
/// <para>
/// The page resolves neither its language nor its branding — the CALLER passes both, because the two
/// callers stand in different contexts: on the code-issuing path the transaction is already deleted
/// (SPEC-007 UI-102), while on the <c>access_denied</c> path it is still there in its terminal state
/// and brands the page by its own levels (UI-101).
/// </para>
/// </summary>
internal static class FormPostResponsePage
{
    /// <summary>
    /// Page title Natural Key.
    /// </summary>
    private const string PageTitle = "Completing sign-in";

    /// <summary>
    /// Body-message Natural Key.
    /// </summary>
    private const string Message =
        "Returning you to the application. If nothing happens, use the button below.";

    /// <summary>
    /// Submit-button Natural Key (the manual way through whenever the script does not run).
    /// </summary>
    private const string SubmitButtonText = "Continue";

    /// <summary>
    /// Identifier of the auto-posted form — the anchor the inline script submits by.
    /// </summary>
    private const string FormElementId = "veriqa-form-post";

    /// <summary>
    /// Builds the auto-posting page. Every attribute value and every text is HTML-encoded; the form
    /// targets the <c>redirect_uri</c> the caller passes, which OpenIddict validated against the client
    /// registration before the response existed.
    /// </summary>
    /// <param name="action">Form target — the relying party's validated redirect URI.</param>
    /// <param name="fields">Authorization response parameters, one entry per value: a multi-valued
    /// parameter becomes several hidden fields of the same name, which is how a form states one.</param>
    /// <param name="cspNonce">CSP nonce of this request. The script goes out either way — the nonce
    /// attribute is simply left off without one, as every other inline script of the contour does;
    /// missing nonce also moves the submit button out of <c>&lt;noscript&gt;</c>, see the remarks on
    /// the two placements below.</param>
    /// <param name="localizer">Locale-file localizer (Natural Key → localized text).</param>
    /// <param name="language">Detected language code (ru, en, zh, …).</param>
    /// <param name="branding">Effective branding, already resolved by the caller over the context that
    /// page actually has (SPEC-007 UI-101, UI-102).</param>
    /// <returns>HTML page.</returns>
    public static string Build(
        string action,
        IEnumerable<KeyValuePair<string, string>> fields,
        string? cspNonce,
        IConfirmationPromptLocalizer localizer,
        string language,
        CorePageBranding? branding)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(fields);

        // WCAG 3.1.1: the page is either fully in the requested language or fully on the English base
        // text, never mixed, so the lang attribute stays truthful — the same rule the other pages of
        // the contour follow.
        var contentLanguage = AuthPageLocalization.ResolveContentLanguage(localizer, PageTitle, language);
        var pageTitle = AuthPageLocalization.Localize(localizer, PageTitle, contentLanguage);
        var message = AuthPageLocalization.Localize(localizer, Message, contentLanguage);
        var submitButtonText = AuthPageLocalization.Localize(localizer, SubmitButtonText, contentLanguage);

        var hiddenFields = new StringBuilder();
        foreach (var field in fields)
        {
            hiddenFields
                .Append("      <input type=\"hidden\" name=\"")
                .Append(WebUtility.HtmlEncode(field.Key))
                .Append("\" value=\"")
                .Append(WebUtility.HtmlEncode(field.Value))
                .Append("\" />\n");
        }

        // The script goes out with or without a nonce — the same rule the other pages of the contour
        // follow (DefaultAuthPageRenderer, EmailPushComposePage): the attribute is dropped, the script
        // is not. In this deployment a missing nonce means SecurityHeadersMiddleware is out of the
        // pipeline, so there is no policy of ours to refuse the script, and omitting it would leave the
        // response with nothing that carries the parameters to the relying party.
        var nonceAttribute = string.IsNullOrEmpty(cspNonce)
            ? string.Empty
            : $" nonce=\"{WebUtility.HtmlEncode(cspNonce)}\"";

        var submitButton =
            $"""<button type="submit" class="veriqa-btn">{WebUtility.HtmlEncode(submitButtonText)}</button>""";

        // Where the button goes depends on what backs the script. With a nonce the script is covered by
        // the very policy that issued it, so the only case left is scripts switched off — <noscript>.
        // Without a nonce nothing guarantees the script runs (a host may serve a policy of its own that
        // this page knows nothing about), and <noscript> is NOT rendered while scripts are on: the
        // button then stays in the document as the visible way through.
        var submitControl = string.IsNullOrEmpty(cspNonce)
            ? submitButton
            : $"<noscript>{submitButton}</noscript>";

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
                <form id="{{FormElementId}}" method="post" action="{{WebUtility.HtmlEncode(action)}}">
            {{hiddenFields.ToString().TrimEnd('\n')}}
                  {{submitControl}}
                </form>
              </main>
              {{CorePageAttribution.BuildLine(contentLanguage)}}
              <script{{nonceAttribute}}>document.getElementById('{{FormElementId}}').submit();</script>
            </body>
            </html>
            """;
    }
}
