// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.Email.Configuration;
using Veriqa.Core.ChannelAdapter.Email.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.MessageTemplates;
using Veriqa.Core.TransactionEngine.Store;

namespace Veriqa.Core.ChannelAdapter.Email;

/// <summary>
/// Rendering of Email Push mode HTML pages (SPEC-016 §5): compose-helper, pull form, warning.
/// All pages use the service-page stylesheet of the "Core" contour, so they share tokens and
/// branding with the sign-in window. All interpolations are escaped (XSS protection).
/// </summary>
public static partial class EmailAuthEndpoints
{
    /// <summary>
    /// Resolves the branding of an email-adapter page over the ownership context of ITS OWN
    /// transaction (SPEC-007 UI-101): the tenant, the application and the <c>ui_config</c> record all
    /// come from the transaction the page is serving, exactly as on the sign-in window. A page with no
    /// transaction to name a context — the pull form opened without a <c>session_id</c>, an invalid or
    /// spent magic link — resolves against the core level alone, which is the only level it has
    /// (UI-090).
    /// <para>
    /// The same call states the resolved stylesheet for this request's CSP, so the policy and the
    /// markup cannot name different origins (UI-054, UI-040).
    /// </para>
    /// </summary>
    /// <param name="services">Request service provider.</param>
    /// <param name="transactionIdValue">Transaction identifier the page is serving; null — there is none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Effective branding; all values unset when nothing is branded.</returns>
    private static async Task<CorePageBranding> ResolvePageBrandingAsync(
        IServiceProvider services,
        string? transactionIdValue,
        CancellationToken cancellationToken)
    {
        var transaction = await TryLoadBrandingTransactionAsync(services, transactionIdValue, cancellationToken);

        return await services.GetRequiredService<CorePageBrandingResolver>()
            .ResolveAsync(TransactionResolutionContext.For(transaction), cancellationToken);
    }

    /// <summary>
    /// Loads the transaction whose ownership context brands the page. Never throws over branding: an
    /// unreadable or unknown transaction degrades to null, and the page is then branded by the levels
    /// that do not depend on it.
    /// </summary>
    /// <param name="services">Request service provider.</param>
    /// <param name="transactionIdValue">Transaction identifier as it arrived in the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transaction, or null when there is none to read.</returns>
    private static async Task<Transaction?> TryLoadBrandingTransactionAsync(
        IServiceProvider services,
        string? transactionIdValue,
        CancellationToken cancellationToken)
    {
        if (!TransactionId.TryParse(transactionIdValue, out var transactionId))
        {
            return null;
        }

        try
        {
            return await services.GetRequiredService<ITransactionStore>()
                .GetByIdAsync(transactionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal request cancellation — propagate, this is not a branding failure.
            throw;
        }
        catch (Exception ex)
        {
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(EmailAuthEndpoints))
                .LogWarning(ex, "The transaction that brands the page could not be read; rendering it from the levels below.");

            return null;
        }
    }

    /// <summary>
    /// Renders the standalone pull form page (GET /auth/email/start).
    /// The user arrives here from the compose page via the "Sign in with an emailed link" link.
    /// </summary>
    private static async Task<IResult> HandleEmailPullFormAsync(HttpContext httpContext)
    {
        // The method renders the standalone pull form: enter email → magic link
        var services = httpContext.RequestServices;
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(EmailAuthEndpoints));

        // Served page localization (ICC-050): the form and its messages render in the request language.
        var pageLocalizer = services.GetRequiredService<IConfirmationPromptLocalizer>();
        var servedLocale = ChannelRequestLocale.Detect(httpContext.Request.Headers.AcceptLanguage.ToString());

        // The transaction of this page is named by the session_id of the link the user followed, so
        // the page is branded for its own application and ui_config record (UI-101). Opened without
        // one — the form says so and renders disabled — there is no context and the core level stands
        // alone (UI-090).
        var sessionId = httpContext.Request.Query[EmailAdapterConstants.SessionIdQueryParam].ToString();
        var branding = await ResolvePageBrandingAsync(services, sessionId, httpContext.RequestAborted);

        // GET and POST share the /auth/email/start route, and this GET is the one a user reaches
        // first — so it is this read that materializes the Email section for the whole process. It
        // goes through the adapter's guarded entry point for exactly that reason (TryReadEmailOptions).
        var emailOptions = TryReadEmailOptions(services, logger);
        if (emailOptions is null)
        {
            // The channel cannot be served, and the reason is not "the mode is off": say that instead
            // of the disabled-mode text, which would state something untrue to the user.
            return Results.Content(
                BuildConfirmPageHtml(pageLocalizer, servedLocale, branding, isError: true, errorMessage: EmailAdapterMailStrings.ApiErrorChannelUnavailable),
                "text/html", Encoding.UTF8, StatusCodes.Status503ServiceUnavailable);
        }

        if (!emailOptions.Enabled || !emailOptions.PullEnabled)
        {
            return Results.Content(
                BuildConfirmPageHtml(pageLocalizer, servedLocale, branding, isError: true, errorMessage: EmailAdapterMailStrings.PullFormModeDisabled),
                "text/html", Encoding.UTF8, StatusCodes.Status503ServiceUnavailable);
        }

        // CSP nonce from SecurityHeadersMiddleware: without it the page's inline script
        // is blocked by the script-src policy (review feedback, SPEC-007 §6.3)
        var cspNonce = httpContext.Items[HttpContextItemKeys.CspNonce] as string;

        var html = BuildPullFormPageHtml(
            pageLocalizer,
            servedLocale,
            branding,
            sessionId,
            cspNonce,
            httpContext.Request.PathBase.Value ?? string.Empty);
        return Results.Content(html, "text/html", Encoding.UTF8);
    }

    /// <summary>
    /// Builds the HTML of the standalone pull form page (GET /auth/email/start).
    /// </summary>
    /// <param name="localizer">Localizer of the page texts (Natural Keys).</param>
    /// <param name="servedLocale">The locale the page is served in (null — base language).</param>
    /// <param name="branding">Effective branding of the generated page (null — neutral canon).</param>
    /// <param name="sessionId">The transaction identifier (session_id).</param>
    /// <param name="cspNonce">The CSP nonce for the inline script (null — the attribute is not added).</param>
    /// <param name="pathBase">Path base the application is hosted under (empty — the site root).</param>
    /// <returns>The HTML string of the page with the email input form.</returns>
    private static string BuildPullFormPageHtml(
        IConfirmationPromptLocalizer localizer,
        string? servedLocale,
        CorePageBranding? branding,
        string sessionId,
        string? cspNonce,
        string pathBase)
    {
        // The method renders a minimalist pull form in the white style consistent with the auth page,
        // localized into the request language (ICC-050)
        var htmlLang = ResolveServedHtmlLang(localizer, servedLocale);
        var jsSessionId = JsonSerializer.Serialize(sessionId);
        // The path the form posts to carries the path base: this application may be hosted under a
        // sub-path, and a root-relative fetch would reach past it.
        var jsStartPath = JsonSerializer.Serialize(pathBase + EmailAdapterConstants.PullStartPath);
        // The "link sent" text is an ordinary localized string of this page, not a message of the
        // template mechanism: the address in it is a masked address in a sentence, not a slot. The server
        // resolves the localized text WITH the token in place; the client substitutes the address by
        // token replacement into textContent (TPL-052). Replacing the token — not prefixing — keeps the
        // address in its localized position (in ru/zh it is not at the end) and never reaches markup.
        var jsLinkSentTemplate = JsonSerializer.Serialize(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposePullLinkSent, servedLocale));
        var jsEmailMaskedToken = JsonSerializer.Serialize("{" + SlotNames.EmailMasked + "}");
        var jsSendError = JsonSerializer.Serialize(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposePullError, servedLocale));
        var hasSession = !string.IsNullOrWhiteSpace(sessionId);
        var noSessionWarning = hasSession
            ? ""
            : $"""<p class="veriqa-msg err" style="display:block;margin-bottom:12px;">{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PullFormNoSession, servedLocale))}</p>""";

        // The nonce attribute — as in DefaultAuthPageRenderer: empty when there is no nonce
        var nonceAttr = !string.IsNullOrEmpty(cspNonce)
            ? $" nonce=\"{HtmlEncode(cspNonce)}\""
            : string.Empty;

        return $$"""
            <!DOCTYPE html>
            <html lang="{{htmlLang}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PullFormTitle, servedLocale))}}</title>
              {{CorePageHead.BuildServicePageHead(branding)}}
            </head>
            <body>
              <main class="veriqa-card">
                <h1 class="veriqa-title">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PullFormHeading, servedLocale))}}</h1>
                <p class="veriqa-instruction">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PullFormDescription, servedLocale))}}</p>
                {{noSessionWarning}}
                <input
                  class="veriqa-input"
                  id="veriqa-email"
                  type="email"
                  autocomplete="email"
                  placeholder="{{HtmlEncode(EmailAdapterMailStrings.PullFormEmailPlaceholder)}}"
                  aria-label="{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PullFormEmailAriaLabel, servedLocale))}}"
                  {{(hasSession ? "" : "disabled")}}
                />
                <button type="button" id="veriqa-submit" class="veriqa-btn" {{(hasSession ? "" : "disabled")}}>
                  {{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PullFormSubmitButton, servedLocale))}}
                </button>
                <div id="veriqa-msg" class="veriqa-msg" role="status" aria-live="polite"></div>
              </main>
              {{CorePageAttribution.BuildLine(htmlLang)}}
              <script{{nonceAttr}}>
                (function () {
                  var sessionId = {{jsSessionId}};
                  var startPath = {{jsStartPath}};
                  var linkSentTemplate = {{jsLinkSentTemplate}};
                  var emailMaskedToken = {{jsEmailMaskedToken}};
                  var sendError = {{jsSendError}};
                  var btn = document.getElementById('veriqa-submit');
                  var input = document.getElementById('veriqa-email');
                  var msg = document.getElementById('veriqa-msg');
                  if (!btn || !sessionId) { return; }
                  btn.addEventListener('click', function () {
                    var email = (input.value || '').trim();
                    if (!email) { return; }
                    btn.disabled = true;
                    msg.className = 'veriqa-msg';
                    msg.style.display = 'none';
                    fetch(startPath, {
                      method: 'POST',
                      headers: { 'Content-Type': 'application/json' },
                      body: JSON.stringify({ email: email, session_id: sessionId })
                    })
                      .then(function (r) { return r.json().catch(function () { return {}; }).then(function (d) { return { ok: r.ok, data: d }; }); })
                      .then(function (res) {
                        if (res.ok && res.data && res.data.success) {
                          msg.className = 'veriqa-msg ok';
                          var maskedAddr = (res.data.maskedEmail || email);
                          msg.textContent = linkSentTemplate.replace(emailMaskedToken, function () { return maskedAddr; });
                          msg.style.display = 'block';
                          btn.disabled = true;
                          input.disabled = true;
                        } else {
                          msg.className = 'veriqa-msg err';
                          msg.textContent = sendError;
                          msg.style.display = 'block';
                          btn.disabled = false;
                        }
                      })
                      .catch(function () {
                        msg.className = 'veriqa-msg err';
                        msg.textContent = sendError;
                        msg.style.display = 'block';
                        btn.disabled = false;
                      });
                  });
                })();
              </script>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Builds the HTML of the Push mode compose page (mailto button + fields for manual copying).
    /// </summary>
    /// <param name="localizer">Localizer of the page texts (Natural Keys).</param>
    /// <param name="servedLocale">The locale the page is served in (null — base language).</param>
    /// <param name="branding">Effective branding of the generated page (null — neutral canon).</param>
    /// <param name="inbound">Inbound processing settings.</param>
    /// <param name="prefill">
    /// The resolved deep-link prefill message of the Email channel — the SOURCE of the subject and the
    /// body of the mail the user is about to send. The wording that frames the token lives in this text,
    /// which is why the inbound side matches the form of the token instead of the framing (SPEC-016 §5.4).
    /// </param>
    /// <param name="correlationToken">The correlation token of the current transaction.</param>
    /// <param name="transactionIdString">The transaction identifier (session_id) for the pull link.</param>
    /// <param name="logger">Logger of the page's misconfiguration signals — the render degradations of
    /// the prefill and the "mail with no correlation token" one.</param>
    /// <param name="pathBase">Path base the application is hosted under (empty — the site root).</param>
    /// <returns>The HTML string of the page.</returns>
    private static string BuildPushComposePageHtml(
        IConfirmationPromptLocalizer localizer,
        string? servedLocale,
        CorePageBranding? branding,
        EmailInboundOptions inbound,
        ResolvedMessage prefill,
        string correlationToken,
        string transactionIdString,
        ILogger logger,
        string pathBase)
    {
        // The method assembles the email address/subject/body and renders the compose page,
        // localized into the request language (ICC-050)
        var htmlLang = ResolveServedHtmlLang(localizer, servedLocale);
        var recipient = BuildPushRecipientAddress(inbound, correlationToken);

        // If the recipient address is not set — a dev warning instead of a non-working compose page.
        // In Production this case is precluded by the validator (InboundAddress is required).
        if (string.IsNullOrEmpty(recipient))
        {
            return BuildPushMissingInboundAddressHtml(transactionIdString, branding, pathBase);
        }

        // Subject and body of the prefilled mail: one message, rendered into its two parts. The token is
        // its {token} slot, so the mail carries it in the text (EM-021 — duplicated with the address)
        // and the phrasing around it is the deployment's to reword.
        var rendered = MessageTextRenderer.RenderParts(
            prefill,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [SlotNames.Token] = correlationToken
            },
            servedLocale,
            // No zone reaches this page: it holds the IDENTIFIER of the transaction, not the
            // transaction itself, and reads none. On today's shipment nothing is lost by that — the
            // prefill states one slot, the correlation token, and no moment — but the set of slots is
            // the deployment's to change: one that declares a moment here would need the recipient
            // zone read off the transaction, the way every other render point of this contour reads it.
            recipientTimeZone: null,
            localizer,
            // The prefill of a mail is plain text — a mail client opens it in its compose window, not
            // in a rendered view. The render degradations go to the logger this method already holds:
            // a prefill silently rendered off its minimal variant is the same misconfiguration as the
            // "mail with no correlation token" signal below, and it is reported for the same reason.
            MessageRenderMode.PlainText,
            logger);

        // A ladder none of whose steps states the plain-text edition leaves the prefilled mail without
        // wording (SPEC-016 §4.3); the compose page still opens. Whether the token survives that
        // depends on the ADDRESS and is not unconditional: plus-addressing carries it in the address
        // (EM-021), a bare InboundAddress does not.
        var subject = rendered?.Subject ?? string.Empty;
        var body = rendered?.Body ?? string.Empty;

        // The token is the ONE thing the inbound side matches a reply on, and it travels in the address
        // or in the text of the mail (EM-021). With it in neither, the reply can never be matched — an
        // operational signal, not a case to render: the deployment is misconfigured and the page is the
        // wrong place to say so. The address half is precluded at startup by the validator; the text half
        // means a deployment reworded the step out of its token slot.
        if (!recipient.Contains(correlationToken, StringComparison.Ordinal)
            && !subject.Contains(correlationToken, StringComparison.Ordinal)
            && !body.Contains(correlationToken, StringComparison.Ordinal))
        {
            logger.LogError(
                "The prefilled mail of the Push compose page carries no correlation token: neither the "
                + "recipient address nor the text of message kind {MessageKind} holds it, so a reply to "
                + "it can never be matched to its transaction. Turn Inbound.UsePlusAddressing on, or "
                + "state a plain-text step whose wording references the token slot.",
                MessageKinds.DeeplinkPrefill);
        }

        var mailtoHref = $"mailto:{Uri.EscapeDataString(recipient)}"
            + $"?subject={Uri.EscapeDataString(subject)}"
            + $"&body={Uri.EscapeDataString(body)}";

        var encodedHref = HtmlEncode(mailtoHref);
        var encodedRecipient = HtmlEncode(recipient);
        var encodedSubject = HtmlEncode(subject);
        var encodedBody = HtmlEncode(body);

        // The link carries the path base: this application may be hosted under a sub-path, and a
        // root-relative href would lead past it.
        var pullFormUrl = HtmlEncode(
            $"{pathBase}{EmailAdapterConstants.PullStartPath}"
            + $"?{EmailAdapterConstants.SessionIdQueryParam}={Uri.EscapeDataString(transactionIdString)}");

        return $$"""
            <!DOCTYPE html>
            <html lang="{{htmlLang}}">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposeTitle, servedLocale))}}</title>
              {{CorePageHead.BuildServicePageHead(branding)}}
            </head>
            <body>
              <main class="veriqa-card">
                <h1 class="veriqa-title">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposeTitle, servedLocale))}}</h1>
                <p class="veriqa-instruction">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposeInstruction, servedLocale))}}</p>

                <a class="veriqa-btn" href="{{encodedHref}}">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposeOpenMailButton, servedLocale))}}</a>

                <div class="veriqa-fields">
                  <p class="veriqa-hint">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposeFallbackHint, servedLocale))}}</p>
                  <div class="veriqa-field">
                    <span class="veriqa-field-label">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposeAddressLabel, servedLocale))}}</span>
                    <div class="veriqa-field-value">{{encodedRecipient}}</div>
                  </div>
                  <div class="veriqa-field">
                    <span class="veriqa-field-label">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposeSubjectLabel, servedLocale))}}</span>
                    <div class="veriqa-field-value">{{encodedSubject}}</div>
                  </div>
                  <div class="veriqa-field">
                    <span class="veriqa-field-label">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PushComposeBodyLabel, servedLocale))}}</span>
                    <div class="veriqa-field-value">{{encodedBody}}</div>
                  </div>
                </div>

                <a href="{{pullFormUrl}}" class="veriqa-link">{{HtmlEncode(ResolveServedText(localizer, EmailAdapterMailStrings.PullFormLinkText, servedLocale))}}</a>
              </main>
              {{CorePageAttribution.BuildLine(htmlLang)}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Builds the HTML of the Push compose error page (invalid/expired token or disabled mode).
    /// </summary>
    /// <returns>The HTML string of the error page.</returns>
    private static string BuildPushInvalidPageHtml(
        IConfirmationPromptLocalizer localizer,
        string? servedLocale,
        CorePageBranding? branding)
    {
        // The method reuses the confirmation page style for consistency
        return BuildConfirmPageHtml(
            localizer,
            servedLocale,
            branding,
            isError: true,
            errorMessage: EmailAdapterMailStrings.PushComposeInvalidToken);
    }

    /// <summary>
    /// Builds the HTML of the page with a dev warning about an unset Inbound.InboundAddress.
    /// Shown only in the dev environment — in Production the validator blocks startup.
    /// No pull forms: only setup instructions.
    /// </summary>
    /// <param name="transactionIdString">The transaction identifier (for the pull link).</param>
    /// <param name="branding">Effective branding of the generated page (null — neutral canon).</param>
    /// <param name="pathBase">Path base the application is hosted under (empty — the site root).</param>
    /// <returns>The HTML string of the warning page.</returns>
    private static string BuildPushMissingInboundAddressHtml(
        string transactionIdString,
        CorePageBranding? branding,
        string pathBase)
    {
        // The method shows a dev instruction: which user-secret to set and in which project

        // The link carries the path base: this application may be hosted under a sub-path, and a
        // root-relative href would lead past it.
        var pullFormUrl = HtmlEncode(
            $"{pathBase}{EmailAdapterConstants.PullStartPath}"
            + $"?{EmailAdapterConstants.SessionIdQueryParam}={Uri.EscapeDataString(transactionIdString)}");

        var setupCommand = HtmlEncode(
            "dotnet user-secrets set \"Veriqa:Channels:Email:Inbound:InboundAddress\" \"auth@yourdomain.com\""
            + " --project src/hosts/Veriqa.Core.AuthServer.Host");

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>{{HtmlEncode(EmailAdapterMailStrings.PushSetupTitle)}}</title>
              {{CorePageHead.BuildServicePageHead(branding)}}
            </head>
            <body>
              <main class="veriqa-card veriqa-card-wide veriqa-card--start">
                <div class="veriqa-badge">Dev / Config</div>
                <h1 class="veriqa-title">{{HtmlEncode(EmailAdapterMailStrings.PushSetupHeading)}}</h1>
                <p class="veriqa-warning-note">
                  <code>Veriqa:Channels:Email:Inbound:InboundAddress</code> {{HtmlEncode(EmailAdapterMailStrings.PushSetupNotSetText)}}
                  {{HtmlEncode(EmailAdapterMailStrings.PushSetupSecretHint)}} <code>Veriqa.Core.AuthServer.Host</code>:
                </p>
                <div class="veriqa-code">{{setupCommand}}</div>
                <p class="veriqa-warning-note">
                  {{HtmlEncode(EmailAdapterMailStrings.PushSetupRestartHint)}} <code>Veriqa.AppHost</code>{{HtmlEncode(EmailAdapterMailStrings.PushSetupSecretsScopeHint)}}
                </p>
                <a href="{{pullFormUrl}}" class="veriqa-link">{{HtmlEncode(EmailAdapterMailStrings.PullFormLinkText)}}</a>
              </main>
              {{CorePageAttribution.BuildLine(EmailAdapterConstants.DefaultHtmlLang)}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Builds the Push mode recipient address, taking plus-addressing into account.
    /// </summary>
    /// <param name="inbound">Inbound processing settings.</param>
    /// <param name="correlationToken">The correlation token to insert into the plus segment.</param>
    /// <returns>The recipient address, or an empty string when InboundAddress is unset.</returns>
    private static string BuildPushRecipientAddress(EmailInboundOptions inbound, string correlationToken)
    {
        // The method inserts the correlation token into the address via plus-addressing, if it is enabled
        var address = inbound.InboundAddress.Trim();

        if (!inbound.UsePlusAddressing)
        {
            return address;
        }

        var atIndex = address.LastIndexOf('@');
        if (atIndex <= 0)
        {
            return address;
        }

        var localPart = address[..atIndex];
        var domain = address[(atIndex + 1)..];

        return $"{localPart}{EmailAdapterConstants.PlusAddressSeparator}{correlationToken}@{domain}";
    }

    /// <summary>
    /// Escapes a string for safe insertion into HTML.
    /// </summary>
    /// <param name="value">The source string.</param>
    /// <returns>The HTML-escaped string.</returns>
    private static string HtmlEncode(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }
}
