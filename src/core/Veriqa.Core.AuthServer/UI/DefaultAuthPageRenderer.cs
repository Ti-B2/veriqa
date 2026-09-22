// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text;
using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Configuration.Enums;
using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.UI.Services;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.UI;

/// <summary>
/// Authentication page renderer interface (SPEC-007 §7, UI-050).
/// Allows embedded-mode clients to fully replace the rendering logic.
/// </summary>
public interface IAuthPageRenderer
{
    /// <summary>
    /// Generates the authentication HTML page.
    /// </summary>
    /// <param name="context">Page rendering context.</param>
    /// <returns>HTML content of the page.</returns>
    string RenderAuthPage(AuthPageRenderContext context);
}

/// <summary>
/// Authentication page rendering context.
/// Contains all data required to build the HTML.
/// </summary>
public sealed class AuthPageRenderContext
{
    /// <summary>
    /// Session identifier (external session_id).
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Optional callback URL to redirect to after successful authentication.
    /// May be used by custom renderer implementations; the default renderer
    /// obtains the redirect URL from the SignalR message TransactionStatusMessage.RedirectUrl.
    /// </summary>
    public string? CallbackUrl { get; init; }

    /// <summary>
    /// Transaction expiration moment (UTC).
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Seconds LEFT of the transaction's lifetime at the moment the page was built, measured by the
    /// server's own clock. Never negative: a transaction already past its moment has none left.
    /// <para>
    /// The countdown of the page is driven by this remainder and not by <see cref="ExpiresAt"/>,
    /// because the two answer different questions. A moment has to be compared against a clock, and
    /// the only clock the page has is the browser's: a device running minutes ahead then declared a
    /// live transaction expired and stopped listening for its outcome. A remainder needs no shared
    /// clock at all — the browser measures the interval with its own, and only the DRIFT over that
    /// interval can be wrong.
    /// </para>
    /// </summary>
    public double RemainingSeconds { get; init; }

    /// <summary>
    /// Where the page sends the user to start over once its transaction has expired; null or empty —
    /// the page offers no way out and shows the expiry as text alone.
    /// <para>
    /// It is stated by the caller because only the caller knows whether starting over is a thing this
    /// surface may offer: the sign-in window is reached by an authorize request that can simply be
    /// made again, while a confirmation transaction created server-to-server has no such request to
    /// repeat, and a page inventing one would send the user somewhere the relying party never
    /// created (SPEC-039 C15).
    /// </para>
    /// </summary>
    public string? RestartUrl { get; init; }

    /// <summary>
    /// Channel data for display (deep link, QR code).
    /// </summary>
    public required IReadOnlyList<ChannelDisplayInfo> Channels { get; init; }

    /// <summary>
    /// Language code (ru, en, zh).
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// CSP nonce for inline scripts (SPEC-007 §6.3).
    /// If set, it is added to all &lt;script&gt; tags as the nonce="..." attribute.
    /// </summary>
    public string? CspNonce { get; init; }

    /// <summary>
    /// Effective design of the page (SPEC-012 §4.2–4.3), already resolved by the caller: every field
    /// carries the value of the level that owns it (CFG-210). The renderer merges nothing — a level
    /// added to a design field reaches the page without a line of change here, and an integrator's own
    /// renderer cannot silently lose one.
    /// </summary>
    public AuthPageDesignOptions Design { get; init; } = new();

    /// <summary>
    /// Page title stated by an owning level (SPEC-012 CFG-203); null — no level stated one and the
    /// renderer uses its own text. The stated value is a Natural Key, not finished output: the
    /// renderer resolves it through the host locale files exactly as it resolves its own key, and a
    /// value no locale file carries stays the text it already is.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// On-page instruction stated by an owning level; null — the renderer's own text. A stated value
    /// is a Natural Key, resolved the same way as <see cref="Title"/>.
    /// </summary>
    public string? Instruction { get; init; }

    /// <summary>
    /// Effective QR visibility of the window, resolved by the caller (SPEC-012 CFG-210). It arrives
    /// already resolved because the resolution context of the sign-in page is built ONCE, at the entry
    /// of the request, and travels explicitly: a renderer — including an integrator's own — neither
    /// builds a context nor reaches for one ambiently.
    /// </summary>
    public QrCodeVisibility QrVisibility { get; init; } = QrCodeVisibility.Always;

    /// <summary>
    /// Whether the page is allowed to take the browser off itself when a status arrives that has a
    /// navigation target. <see langword="true"/> (the default) — the behaviour of the sign-in window:
    /// a completed transaction continues on the OIDC callback.
    /// <para>
    /// <see langword="false"/> — the page addresses a transaction that has NO browser callback of its
    /// own (a confirmation created server-to-server, SPEC-039 C15): the terminal outcome is shown in
    /// place, the browser stays on this URL and the transaction stays readable through the status
    /// surface. In this mode the callback target is not built and a target arriving in a status
    /// message is ignored — the page has one destination of its own instead, and only that one
    /// (<see cref="ConfirmationQuestionUrl"/>).
    /// </para>
    /// </summary>
    public bool NavigatesAway { get; init; } = true;

    /// <summary>
    /// Root-relative URL of the page where the core ASKS what this transaction is about
    /// (SPEC-039 R37, E29) — the single destination of a page rendered with
    /// <see cref="NavigatesAway"/> = <see langword="false"/>. Null or empty — the page has no
    /// destination at all and stays where it is, which is what a page WITH navigation gets: on that
    /// path the question belongs to the OIDC callback, not here.
    /// <para>
    /// It is stated by the caller rather than composed by the page script, so that both delivery
    /// paths of a status — the SignalR message and the polling fallback — take the browser to the
    /// same place: the target is a fact of the transaction, not a field one of the two carries. The
    /// path base is applied by the page itself, as it is to every other root-relative URL here.
    /// </para>
    /// </summary>
    public string? ConfirmationQuestionUrl { get; init; }

    /// <summary>
    /// Terminal line for a CONFIRMED outcome, ready and localized; null or empty — the renderer uses
    /// its own sign-in wording ("Sign-in successful!").
    /// <para>
    /// A page that does not navigate away shows its status line as the LAST screen the user sees
    /// (SPEC-039 R51): on the sign-in path that line lives for the moment before the callback takes
    /// over, while here it stays, and a sign-in wording would assert a sign-in that never happened.
    /// The wording therefore comes from the caller — from the one port that words terminal outcomes —
    /// and the renderer neither resolves nor invents one, which is what keeps this page free of a
    /// second set of terminal texts.
    /// </para>
    /// </summary>
    public string? TerminalConfirmedText { get; init; }

    /// <summary>
    /// Terminal line for an EXPIRED outcome; null or empty — the renderer's own sign-in wording. See
    /// <see cref="TerminalConfirmedText"/> for why it is stated by the caller.
    /// </summary>
    public string? TerminalExpiredText { get; init; }

    /// <summary>
    /// Terminal line for a transaction the USER declined; null or empty — the text of
    /// <see cref="TerminalErrorText"/> is shown instead, which is the behaviour of the sign-in path:
    /// it words every failure alike and tells no refusal from a fault.
    /// </summary>
    public string? TerminalDeclinedText { get; init; }

    /// <summary>
    /// Terminal line for a failure that is NOT the user's refusal; null or empty — the renderer's own
    /// sign-in wording ("Authentication error"). A failure with no reason stated lands here as well:
    /// asserting that the user refused, without the reason saying so, would be the page inventing an
    /// outcome.
    /// </summary>
    public string? TerminalErrorText { get; init; }

    /// <summary>
    /// Subject of the confirmation, ready and localized as PLAIN text, shown when the deployment put
    /// this surface into the set of subject-display surfaces (SPEC-012 §4.11, CFG-104); null or empty —
    /// the page shows no subject and its markup is unchanged.
    /// <para>
    /// The text arrives worded: the substitution, the sanitizing and the degradation between wording
    /// variants happen in the one engine of the contour, and this page's whole share of them is the
    /// HTML encoding on output (SPEC-039 R8). The question itself is NOT asked here — this surface
    /// only shows what is being confirmed (R36).
    /// </para>
    /// </summary>
    public string? SubjectText { get; init; }

    /// <summary>
    /// Application path base (<c>HttpContext.Request.PathBase</c>) when hosting the issuer under
    /// a sub-path of the origin (for example, <c>/demo</c> for https://veriqa.app/demo). Empty string —
    /// application at the root. The renderer prepends the prefix to all root-relative page URLs
    /// (SignalR hub, fallback polling, email endpoints, bundled assets) — otherwise, behind a prefixed
    /// route, the browser would request them outside the application.
    /// </summary>
    public string PathBase { get; init; } = string.Empty;
}

/// <summary>
/// Built-in authentication page renderer (SPEC-007 §7).
/// Generates production-ready HTML with QR codes, channel buttons,
/// SignalR connection, countdown, and animations.
/// </summary>
internal sealed class DefaultAuthPageRenderer : IAuthPageRenderer
{
    /// <summary>
    /// Transaction status polling interval in fallback mode (ms). 3000 ms = ~20 requests/min —
    /// with a large margin fits within the polling rate limit (60/min by default,
    /// <see cref="Configuration.RateLimitOptions.PollingPermitLimit"/>). SPEC-007 §5.4.
    /// </summary>
    private const int PollingIntervalMs = 3000;

    /// <summary>
    /// Channel-count threshold at which tabs are laid out as a 2×2 grid (modifier
    /// <c>veriqa-tabs--grid</c>) instead of a single row — otherwise, on a narrow screen the last tab
    /// (Email) would be clipped past the right edge of the container.
    /// </summary>
    private const int FourChannelGridThreshold = 4;

    /// <summary>
    /// Name of the root attribute that carries the QR visibility setting into the page. The value is
    /// handed over as it is resolved — the server does not branch the markup on it, the page decides
    /// (SPEC-007 UI-020). The stylesheet of the window keys its hiding rules on this attribute.
    /// </summary>
    private const string QrVisibilityAttributeName = "data-qr-visibility";

    /// <summary>
    /// Name of the root attribute the page script sets once it has established the device class. It is
    /// absent by default, and every rule that hides something requires it to be present — so a device
    /// that could not be established keeps showing everything.
    /// </summary>
    private const string DeviceAttributeName = "data-device";

    /// <summary>
    /// Name of the root attribute carrying the state of the page. It is the third attribute of the
    /// same mechanism as <see cref="QrVisibilityAttributeName"/> and <see cref="DeviceAttributeName"/>:
    /// the state is written on the root element and the stylesheet decides what each state shows, so
    /// the page needs no handler of its own on any of the elements it puts out.
    /// </summary>
    private const string PageStateAttributeName = "data-page-state";

    /// <summary>
    /// State of a page whose transaction is still running — the value the markup carries.
    /// </summary>
    private const string WaitingPageStateValue = "waiting";

    /// <summary>
    /// State of a page whose transaction has run out of time, set by the script. Everything the page
    /// offered as a way IN is dropped in this state: a QR that is no longer scannable, a channel
    /// button whose deep link no longer resolves and an email form whose request the server would
    /// refuse are three ways to spend the user's attention on an action that cannot succeed.
    /// </summary>
    private const string ExpiredPageStateValue = "expired";

    /// <summary>
    /// Value of <see cref="DeviceAttributeName"/> for a phone — the only device class the window
    /// establishes today.
    /// </summary>
    private const string MobileDeviceValue = "mobile";

    /// <summary>
    /// Class of the wording that only holds while the QR code is on the page. The stylesheet drops it
    /// on the very attributes that drop the QR itself, so the text cannot outlive what it describes.
    /// </summary>
    private const string QrShownTextClass = "veriqa-if-qr";

    /// <summary>
    /// Class of the wording for a page without the QR code — the counterpart of
    /// <see cref="QrShownTextClass"/>, hidden by default and shown exactly where the QR is not.
    /// </summary>
    private const string QrHiddenTextClass = "veriqa-if-no-qr";

    /// <summary>
    /// Attribute form of the setting values: the enum name in lower case, the same convention the
    /// preset attribute uses. Building the values here rather than spelling them out keeps the markup,
    /// the page script and the stylesheet on one source — the enum itself.
    /// </summary>
    private static readonly string DesktopOnlyVisibilityValue =
        QrCodeVisibility.DesktopOnly.ToString().ToLowerInvariant();

    /// <summary>
    /// Data-driven localizer over the host locale files — the single source of translations for the
    /// page (TASK-062). The same infrastructure the channel contour uses (SPEC-017 §7.2), so window
    /// texts and channel messages share one translation source.
    /// </summary>
    private readonly IConfirmationPromptLocalizer _promptLocalizer;

    /// <summary>
    /// Logger of the page — tells the operator about a footer block the page refused to emit.
    /// </summary>
    private readonly ILogger<DefaultAuthPageRenderer> _logger;

    /// <summary>
    /// Creates the authentication page renderer.
    /// </summary>
    /// <param name="promptLocalizer">Locale-file localizer — the source of all page translations.</param>
    /// <param name="logger">Logger of the page.</param>
    public DefaultAuthPageRenderer(
        IConfirmationPromptLocalizer promptLocalizer,
        ILogger<DefaultAuthPageRenderer> logger)
    {
        _promptLocalizer = promptLocalizer;
        _logger = logger;
    }

    /// <summary>
    /// Resolves a Natural Key into the target language via the shared auth-contour rule
    /// (<see cref="AuthPageLocalization.Localize"/>): every language, English included, comes from
    /// the host locale files, with the key's own base text as the fallback.
    /// </summary>
    /// <param name="naturalKey">Natural Key (English base text).</param>
    /// <param name="language">Content language code (the result of ResolveContentLanguage).</param>
    /// <returns>Localized text.</returns>
    private string Localize(string naturalKey, string language)
    {
        return AuthPageLocalization.Localize(_promptLocalizer, naturalKey, language);
    }

    /// <summary>
    /// Returns the text the CALLER stated, falling back to this renderer's own Natural Key when it
    /// stated none.
    /// </summary>
    /// <remarks>
    /// A stated text arrives ready and localized and is used as it is — it comes from the mechanism
    /// that words terminal outcomes, and passing it through the key localizer would ask that
    /// localizer to translate an already translated sentence. The fallback is what keeps the sign-in
    /// path unchanged: it states nothing, so it gets the wording it always had.
    /// </remarks>
    /// <param name="stated">Text stated by the caller; null or empty — none was stated.</param>
    /// <param name="ownNaturalKey">This renderer's own Natural Key for the same line.</param>
    /// <param name="language">Content language code (the result of ResolveContentLanguage).</param>
    /// <returns>Text to render.</returns>
    private string Stated(string? stated, string ownNaturalKey, string language)
    {
        return string.IsNullOrEmpty(stated) ? Localize(ownNaturalKey, language) : stated;
    }

    /// <summary>
    /// Returns the localized channel CTA button text (brand names are not translated).
    /// A third-party channel has no localized CTA phrasing: it shows the label declared through
    /// <c>IChannelDisplayMetadata</c> (a brand name — deliberately not localized), and without
    /// metadata it degrades to the raw channel type.
    /// </summary>
    /// <param name="channel">Channel display data.</param>
    /// <param name="language">Language code.</param>
    /// <returns>Button text.</returns>
    private string LocalizeChannelButton(ChannelDisplayInfo channel, string language)
    {
        return channel.ChannelType switch
        {
            ChannelTypes.Telegram => Localize(AuthPageStrings.OpenInTelegram, language),
            ChannelTypes.WhatsApp => Localize(AuthPageStrings.OpenInWhatsApp, language),
            ChannelTypes.Max => Localize(AuthPageStrings.OpenInMax, language),
            ChannelTypes.Email => Localize(AuthPageStrings.LoginViaEmail, language),
            _ => string.IsNullOrWhiteSpace(channel.DisplayName) ? channel.ChannelType : channel.DisplayName
        };
    }

    /// <inheritdoc />
    public string RenderAuthPage(AuthPageRenderContext context)
    {
        // The method builds the complete authentication HTML page

        // The effective design arrives resolved: the levels were combined by the canonical resolver
        // before the page was handed over (SPEC-012 CFG-231).
        var design = context.Design;

        // WCAG 3.1.1: html lang must match the language of the rendered text. The requested language
        // is honoured only when the locale files actually resolve the window's keys; otherwise the
        // page degrades to the English base text as a whole. Resolved ONCE and used both for
        // html lang and for every Localize call below — localizing with context.Language instead
        // would produce a mixed page (some strings translated, the rest English base).
        var lang = AuthPageLocalization.ResolveContentLanguage(
            _promptLocalizer,
            AuthPageStrings.Title,
            context.Language);

        // Preset selection (SPEC-012 §4.2). CFG-023: with a custom design (§4.3) the preset
        // is ignored — we render the base Default, and the styling is set by the custom CSS.
        var preset = AuthPageBranding.ResolvePreset(design);

        // Color scheme: an explicit Theme (Light/Dark) takes priority; Auto follows the OS system
        // setting (prefers-color-scheme), but the Dark preset (SPEC-012 §4.2) also yields dark.
        var htmlTheme = design.Theme switch
        {
            AuthPageTheme.Light => CorePageThemes.Light,
            AuthPageTheme.Dark => CorePageThemes.Dark,
            _ => preset is DesignPreset.Dark ? CorePageThemes.Dark : CorePageThemes.Auto
        };

        var hasMultipleChannels = context.Channels.Count > 1;

        // Title/instruction: the level that owns the text supplies the KEY, the renderer supplies
        // its own when no level states one — and localization is the shared tail of both, not a
        // branch of the default. A configured value is a Natural Key like any other: a locale file
        // that carries an entry for it translates it, and a value absent from every file resolves to
        // its own base text, so a deployment that states plain wording keeps that wording.
        var title = Localize(context.Title ?? AuthPageStrings.Title, lang);

        // The default instruction names the QR code, so it is rendered as a pair with the wording for
        // a page that hides it — the page picks the one that matches what it shows. A deployment that
        // supplied its own instruction through ui_config states a single wording: which QR the page
        // shows is not something that text can be re-phrased for, and its author knows the value of
        // the setting.
        var instructionMarkup = context.Instruction is { } instruction
            ? HtmlEncode(Localize(instruction, lang))
            : hasMultipleChannels
                ? BuildQrDependentText(
                    Localize(AuthPageStrings.MultiChannelInstruction, lang),
                    Localize(AuthPageStrings.MultiChannelInstructionWithoutQr, lang))
                : BuildQrDependentText(
                    Localize(AuthPageStrings.Instruction, lang),
                    Localize(AuthPageStrings.InstructionWithoutQr, lang));

        // QR visibility: resolved on the server, applied by the page. The value travels into the
        // document as an attribute and never as a rendering branch — which device is in front of the
        // window is knowable in the browser only (SPEC-007 UI-020).
        var qrVisibility = context.QrVisibility.ToString().ToLowerInvariant();

        // Branding of the window, built once: the head takes the stylesheet and the colors from it.
        var branding = AuthPageBranding.ToCoreBranding(design, preset);

        var sb = new StringBuilder(8192);

        // HTML head
        AppendHtmlHead(sb, lang, title, htmlTheme, preset, branding, qrVisibility, BuildNonceAttribute(context));

        // Page body
        AppendBodyOpen(sb);
        AppendContainer(sb, title, instructionMarkup, context, lang, hasMultipleChannels, preset, design, htmlTheme);

        // The attribution line stands under the card, and its guard right after it: the guard takes the
        // element before it as the line to keep, and it has to be watching before the integrator's
        // script, which AppendScript links last (SPEC-007 UI-055).
        sb.AppendLine(CorePageAttribution.BuildLine(lang));
        sb.AppendLine(CorePageAttribution.BuildGuard(context.CspNonce));
        AppendScript(sb, context, lang, design);
        AppendBodyClose(sb);

        return sb.ToString();
    }

    /// <summary>
    /// Builds the HTML head: the inline styles of the window and the integrator's stylesheet link.
    /// </summary>
    private static void AppendHtmlHead(
        StringBuilder sb,
        string htmlLang,
        string title,
        string htmlTheme,
        DesignPreset preset,
        CorePageBranding branding,
        string qrVisibility,
        string nonceAttr)
    {
        // The method builds the DOCTYPE, head, and the styling head of the window.
        // Static rules live in the contour stylesheets (token layer + window rules); C# generates
        // only what depends on configuration: the QR box size and the branded primary.
        // The preset and the branding come already resolved from the caller — both the data-preset
        // attribute and the branding derive from that one resolution.
        var presetName = preset.ToString().ToLowerInvariant();
        var generatedTokens = $$"""

            :root { --veriqa-qr-size: {{AuthPageConstants.QrCodeImgAttributeSizePx}}px; }
            """;

        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="{{htmlLang}}" data-theme="{{htmlTheme}}" data-preset="{{presetName}}" {{QrVisibilityAttributeName}}="{{qrVisibility}}" {{PageStateAttributeName}}="{{WaitingPageStateValue}}">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>{{HtmlEncode(title)}}</title>
                {{CorePageHead.BuildPageHead(branding, AuthPageStyles.Css, generatedTokens)}}
            """);

        AppendDeviceDetectionScript(sb, nonceAttr);

        sb.AppendLine("\n</head>");
    }

    /// <summary>
    /// Builds the device-detection script of the window — the client half of the QR visibility setting
    /// (SPEC-007 UI-020). It lives in the head, ahead of the QR markup, so the verdict precedes the
    /// first paint and a QR that is about to be hidden never flashes.
    /// <para>
    /// The verdict needs TWO signals that CSS cannot combine on its own: a media query (a coarse
    /// pointer / no hover) and the User-Agent, which no stylesheet can see. Both are therefore read
    /// here and ANDed — a rule keyed on the media query alone would hide the QR on a touch-screen
    /// desktop, and a page whose only way in is a deep-link button the user cannot reach is worse than
    /// a QR shown where it is useless. The script narrows the page's behavior and never widens it: it
    /// is emitted for every value of the setting and returns immediately unless the value asks for a
    /// device check, and a browser that runs no script at all leaves the marker unset and the QR shown.
    /// </para>
    /// </summary>
    /// <param name="sb">StringBuilder for HTML output.</param>
    /// <param name="nonceAttr">Ready CSP nonce attribute of an inline script (may be empty).</param>
    private static void AppendDeviceDetectionScript(StringBuilder sb, string nonceAttr)
    {
        sb.Append($$"""

                <script{{nonceAttr}}>
                    (function() {
                        "use strict";
                        var root = document.documentElement;
                        if (root.getAttribute("{{QrVisibilityAttributeName}}") !== "{{DesktopOnlyVisibilityValue}}") return;

                        // First signal — the media query, evaluated here instead of in a stylesheet
                        // precisely so that it decides nothing by itself.
                        var query = window.matchMedia;
                        if (!query) return;
                        if (!query("(pointer: coarse)").matches && !query("(hover: none)").matches) return;

                        // Second signal — the User-Agent. A tablet is deliberately NOT a phone here:
                        // userAgentData.mobile is false on tablets, and the fallback pattern matches
                        // phone User-Agents only ("Mobile"/iPhone/iPod). The QR is scanned by another
                        // device's camera, so an uncertain device keeps it.
                        var uaData = navigator.userAgentData;
                        var isPhone = uaData && typeof uaData.mobile === "boolean"
                            ? uaData.mobile
                            : /Mobi|iPhone|iPod/i.test(navigator.userAgent || "");
                        if (isPhone) root.setAttribute("{{DeviceAttributeName}}", "{{MobileDeviceValue}}");
                    })();
                </script>
            """);
    }

    /// <summary>
    /// Builds the opening body tag.
    /// </summary>
    private static void AppendBodyOpen(StringBuilder sb)
    {
        sb.AppendLine("<body>");
    }

    /// <summary>
    /// Appends the brand row of the window — one path for every preset that shows the row
    /// (SPEC-007 UI-100). The shipped default is a PAIR: when no level states either the logo or the
    /// brand name, the row carries the product's diamond and wordmark; a single value stated by the
    /// integrator replaces that default as a whole, so an integrator's logo never stands beside the
    /// product's name. The two keys themselves still resolve on their own, so all four combinations of
    /// the integrator's values are legitimate: with a name the row is a <c>veriqa-brand-row</c> holding
    /// what there is, and without one the logo stands alone. The name is plain text and is encoded
    /// here; the logo markup is built by <see cref="LogoMarkup"/>.
    /// <para>
    /// A logo counts as stated when a level states it for either theme, even if its URL is not
    /// displayable: the value is the integrator's, and replacing a broken logo with the product's mark
    /// would put the product's name on the integrator's page.
    /// </para>
    /// </summary>
    /// <param name="sb">Buffer of the page.</param>
    /// <param name="design">Effective design settings.</param>
    /// <param name="htmlTheme">Theme of the page, as it was put into the data-theme attribute.</param>
    /// <param name="lang">Content language of the page.</param>
    private void AppendBrandRow(
        StringBuilder sb,
        AuthPageDesignOptions design,
        string htmlTheme,
        string lang)
    {
        var statesLogo = design.GetLogoUrl(CorePageThemes.Light) is not null
            || design.GetLogoUrl(CorePageThemes.Dark) is not null;

        if (!statesLogo && string.IsNullOrWhiteSpace(design.BrandName))
        {
            sb.AppendLine(
                $"        <div class=\"veriqa-brand-row\">{CorePageBrandMark.VeriqaDiamondSvg}<span>{HtmlEncode(CorePageBrandMark.VeriqaBrandName)}</span></div>");
            return;
        }

        var logoMarkup = LogoMarkup(design, htmlTheme);

        if (string.IsNullOrWhiteSpace(design.BrandName))
        {
            if (logoMarkup is not null)
            {
                sb.AppendLine($"        {logoMarkup}");
            }

            return;
        }

        var brandName = HtmlEncode(Localize(design.BrandName, lang));

        sb.AppendLine(
            $"        <div class=\"veriqa-brand-row\">{logoMarkup}<span>{brandName}</span></div>");
    }

    /// <summary>
    /// Builds the markup of the client's logo in the brand row, or null when the page displays no logo.
    /// The logo is cut by theme, and how many values the markup carries depends on what the
    /// server knows about the theme:
    /// <list type="bullet">
    /// <item><description>
    /// the theme is known (the page mode is Light or Dark) — one <c>&lt;img&gt;</c> with the value
    /// resolved for THAT theme, the chain of the key having already fallen back to the level's common
    /// value when the theme states none of its own;
    /// </description></item>
    /// <item><description>
    /// the theme is the browser's to pick (mode auto) and the two values differ — a
    /// <c>&lt;picture&gt;</c> handing both to the browser, the canonical HTML way to switch an image
    /// by <c>prefers-color-scheme</c> without a line of script;
    /// </description></item>
    /// <item><description>
    /// otherwise — one <c>&lt;img&gt;</c> with the single value there is (the light one when the dark
    /// URL has no form a <c>srcset</c> list can carry), exactly the markup the page carried before
    /// the theme dimension existed.
    /// </description></item>
    /// </list>
    /// A value that is unset or whose URL scheme is not allowed leaves the page without a logo — the
    /// client brands the page itself.
    /// </summary>
    /// <param name="design">Effective design settings.</param>
    /// <param name="htmlTheme">Theme of the page, as it was put into the data-theme attribute.</param>
    /// <returns>Markup of the logo, or null when the page displays none.</returns>
    private static string? LogoMarkup(AuthPageDesignOptions design, string htmlTheme)
    {
        if (!string.Equals(htmlTheme, CorePageThemes.Auto, StringComparison.Ordinal))
        {
            return LogoImageMarkup(DisplayableLogo(design, htmlTheme));
        }

        var light = DisplayableLogo(design, CorePageThemes.Light);
        var dark = DisplayableLogo(design, CorePageThemes.Dark);

        if (light is not null
            && dark is not null
            && !string.Equals(light, dark, StringComparison.Ordinal)
            && SrcsetCandidate(dark) is { } darkCandidate)
        {
            return $"<picture><source srcset=\"{HtmlEncode(darkCandidate)}\" media=\"(prefers-color-scheme: dark)\" />"
                + $"<img class=\"veriqa-logo\" src=\"{HtmlEncode(light)}\" alt=\"\" /></picture>";
        }

        return LogoImageMarkup(light ?? dark);
    }

    /// <summary>
    /// The URL as one candidate of a <c>srcset</c> list, or null when it cannot be written as one.
    /// The grammar of the attribute is not that of <c>src</c>: a candidate ends at whitespace (a
    /// descriptor may follow) and at a comma (the next candidate follows), so HTML escaping alone
    /// leaves such a URL silently dropped by the browser together with the whole dark variant.
    /// <para>
    /// A space is encoded rather than rejected: it is not a legal URI character at all, and the
    /// browser encodes it the very same way for <c>src</c>, so both themes still request one file.
    /// Anything else structural stays as it is and the page falls back to a single image — rewriting
    /// a legal URL would ask the deployment's server for a different address than the one configured.
    /// </para>
    /// </summary>
    /// <param name="logoUrl">Displayable logo URL.</param>
    /// <returns>Candidate for the attribute, or null when the URL has no safe form.</returns>
    private static string? SrcsetCandidate(string logoUrl)
    {
        var candidate = logoUrl.Replace(" ", "%20", StringComparison.Ordinal);

        return candidate.Any(char.IsWhiteSpace) || candidate.Contains(',', StringComparison.Ordinal)
            ? null
            : candidate;
    }

    /// <summary>
    /// Logo URL the page may display for a theme: unset, blank or a URL whose scheme is outside the
    /// allowlist all mean "no logo" (protection against injection through configuration).
    /// </summary>
    /// <param name="design">Effective design settings.</param>
    /// <param name="theme">Theme the logo is asked for.</param>
    /// <returns>Displayable URL, or null.</returns>
    private static string? DisplayableLogo(AuthPageDesignOptions design, string theme) =>
        design.GetLogoUrl(theme) is { } logoUrl && CorePageHead.IsAllowedUrlScheme(logoUrl)
            ? logoUrl
            : null;

    /// <summary>
    /// Builds the logo image; a null URL yields no markup at all.
    /// </summary>
    /// <param name="logoUrl">Displayable logo URL.</param>
    /// <returns>Markup of the image, or null.</returns>
    private static string? LogoImageMarkup(string? logoUrl) =>
        logoUrl is null
            ? null
            : $"<img class=\"veriqa-logo\" src=\"{HtmlEncode(logoUrl)}\" alt=\"\" />";

    /// <summary>
    /// Builds the page container with QR codes, buttons, and status. The instruction arrives as ready
    /// markup (already encoded): it is either one text or the pair of wordings built by
    /// <see cref="BuildQrDependentText"/>, which is why it is not encoded again here.
    /// </summary>
    private void AppendContainer(
        StringBuilder sb,
        string title,
        string instructionMarkup,
        AuthPageRenderContext context,
        string lang,
        bool hasMultipleChannels,
        DesignPreset preset,
        AuthPageDesignOptions design,
        string htmlTheme)
    {
        // The method builds the main container with the authentication card

        sb.AppendLine("    <div class=\"veriqa-container\" role=\"main\">");

        // Brand row (SPEC-007 UI-100): the server does not emit it in the effective Minimal preset —
        // leaving hidden markup with the integrator's name for a stylesheet rule to hide would make the
        // preset a second home of the row. Every other preset takes the one path of the row.
        if (preset is not DesignPreset.Minimal)
        {
            AppendBrandRow(sb, design, htmlTheme, lang);
        }

        // Title and instruction (eyebrow badge removed — docs/design-rules.md §9)
        sb.AppendLine($"        <h1 class=\"veriqa-title\">{HtmlEncode(title)}</h1>");
        sb.AppendLine($"        <p class=\"veriqa-instruction\">{instructionMarkup}</p>");

        // What is being confirmed, when the deployment asked this surface to show it (SPEC-039 R38).
        // The same text primitive of the card the instruction above uses — the subject is a sentence
        // addressed to the user, not a control of its own — and the same encoding on output: the
        // wording arrived plain from the engine that produced it.
        if (!string.IsNullOrEmpty(context.SubjectText))
        {
            sb.AppendLine($"        <p class=\"veriqa-instruction\">{HtmlEncode(context.SubjectText)}</p>");
        }

        // Channel tabs (shown only when there are multiple channels)
        if (hasMultipleChannels)
        {
            // 4+ channels are laid out as a 2×2 grid (modifier --grid), 2–3 — in a single row.
            var tabsGridClass = context.Channels.Count >= FourChannelGridThreshold
                ? " veriqa-tabs--grid"
                : string.Empty;
            // A clipped third-party label gets the contour's tooltip instead of a native title.
            // The strip clips its own content, so the bubble lives in a host around the strip and
            // is shared by its tabs — only third-party ones become triggers.
            var tabsNeedTooltip = HasThirdPartyLabel(context.Channels);
            if (tabsNeedTooltip)
            {
                sb.AppendLine("        <div class=\"veriqa-tooltip-host\">");
            }

            sb.AppendLine($"        <div class=\"veriqa-tabs{tabsGridClass}\" role=\"tablist\">");
            for (var i = 0; i < context.Channels.Count; i++)
            {
                var channel = context.Channels[i];
                // The first channel is the active tab on initial render; a single predicate drives
                // its active class, aria-selected and roving tabindex so they can never diverge.
                var isActive = i is 0;
                var activeClass = isActive ? " active" : string.Empty;
                // Tab name: the brand name of a built-in channel, or the label a third-party
                // adapter declared via IChannelDisplayMetadata (fallback — the raw channel type).
                var tabName = string.IsNullOrWhiteSpace(channel.DisplayName)
                    ? AuthPageStrings.GetChannelTabName(channel.ChannelType)
                    : channel.DisplayName;
                // Clipping and the tooltip apply to a third-party label only: a built-in tab name is
                // a short brand name that always fits, and a bubble repeating it would only cover
                // the page with a duplicate of what is already fully readable.
                var isThirdPartyTabLabel = !IsBuiltInChannel(channel.ChannelType);
                var tabTooltip = isThirdPartyTabLabel ? $" data-veriqa-tooltip=\"{HtmlEncode(tabName)}\"" : string.Empty;
                var tabLabelClass = isThirdPartyTabLabel ? " class=\"veriqa-tab-label\"" : string.Empty;
                var ariaSelected = isActive ? "true" : "false";
                // Roving tabindex (WAI-ARIA Tabs pattern): only the active tab is in the tab order
                // (0), the rest are -1. The active tab is index 0 on first render, so the state is
                // correct from the markup alone — no init script call is needed.
                var tabIndex = isActive ? "0" : "-1";
                var tabId = $"veriqa-tab-{i}";
                var panelId = $"veriqa-panel-{HtmlEncode(channel.ChannelType)}";
                // Channel brand icon in the tab (decorative; empty for an unknown channel)
                var tabIcon = ChannelIconSvg.Render(
                    channel.ChannelType,
                    $"veriqa-tab-icon veriqa-tab-icon--{HtmlEncode(channel.ChannelType)}",
                    channel.IconSvgPath);
                sb.AppendLine($"            <button class=\"veriqa-tab{activeClass}\" id=\"{tabId}\" data-channel=\"{HtmlEncode(channel.ChannelType)}\" role=\"tab\" aria-selected=\"{ariaSelected}\" aria-controls=\"{panelId}\" tabindex=\"{tabIndex}\"{tabTooltip}>{tabIcon}<span{tabLabelClass}>{HtmlEncode(tabName)}</span></button>");
            }
            sb.AppendLine("        </div>");

            if (tabsNeedTooltip)
            {
                sb.AppendLine("            <span class=\"veriqa-tooltip\" aria-hidden=\"true\" hidden></span>");
                sb.AppendLine("        </div>");
            }
        }

        // Channel panels
        for (var i = 0; i < context.Channels.Count; i++)
        {
            var channel = context.Channels[i];
            var displayStyle = i is 0 ? string.Empty : " style=\"display:none;\"";
            var tabId = $"veriqa-tab-{i}";
            var panelId = $"veriqa-panel-{HtmlEncode(channel.ChannelType)}";

            // ARIA attributes role/aria-labelledby only when tabs are present
            var panelAriaAttributes = hasMultipleChannels
                ? $" role=\"tabpanel\" aria-labelledby=\"{tabId}\" tabindex=\"0\""
                : string.Empty;

            sb.AppendLine($"        <div class=\"veriqa-panel\" id=\"{panelId}\"{panelAriaAttributes}{displayStyle}>");

            if (channel.ChannelType is ChannelTypes.Email)
            {
                // Push mode is determined by the DeepLinkUrl scheme: an absolute http(s) compose-helper
                // URL, or a direct mailto: (SPEC-016 §5.3 direct-mailto, Inbound.TokenInLocalPart). Both are
                // rendered as a QR + "open mail client" button; Pull (relative /auth/email/start path) is the
                // inline email entry form. Without the mailto: case a direct-mailto link falls through to Pull
                // and no QR is shown.
                var isPushMode = channel.DeepLinkUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || channel.DeepLinkUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || channel.DeepLinkUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

                if (isPushMode)
                {
                    AppendEmailPushPanel(sb, channel, context.SessionId, lang);
                }
                else
                {
                    AppendEmailPanel(sb, context.SessionId, lang);
                }
            }
            else
            {
                var qrAlt = Localize(AuthPageStrings.QrCodeAlt, lang);
                var btnText = LocalizeChannelButton(channel, lang);
                var btnClass = GetChannelButtonClass(channel.ChannelType);
                // Channel brand icon in the CTA (color — button text via currentColor).
                // A third-party channel may supply its own glyph via IChannelDisplayMetadata;
                // without one the button renders without an icon.
                var btnIcon = ChannelIconSvg.Render(channel.ChannelType, "veriqa-btn-icon", channel.IconSvgPath);
                // Same rule as for the tab: only a third-party label is clipped and gets a tooltip;
                // the localized CTA of a built-in channel keeps wrapping (its length is our own,
                // translated and pseudo-locale-checked text, not an arbitrary SPI string).
                var isThirdPartyBtnLabel = !IsBuiltInChannel(channel.ChannelType);
                var btnTooltip = isThirdPartyBtnLabel ? $" data-veriqa-tooltip=\"{HtmlEncode(btnText)}\"" : string.Empty;
                var btnLabelMarkup = isThirdPartyBtnLabel
                    ? $"<span class=\"veriqa-btn-label\">{HtmlEncode(btnText)}</span>"
                    : HtmlEncode(btnText);

                sb.AppendLine("            <div class=\"veriqa-qr\">");
                sb.AppendLine($"                <img src=\"{AuthPageConstants.PngDataUriPrefix}{HtmlEncode(channel.QrCodeBase64)}\" alt=\"{HtmlEncode(qrAlt)}\" width=\"{AuthPageConstants.QrCodeImgAttributeSizePx}\" height=\"{AuthPageConstants.QrCodeImgAttributeSizePx}\" />");
                sb.AppendLine("            </div>");
                if (isThirdPartyBtnLabel)
                {
                    sb.AppendLine("            <span class=\"veriqa-tooltip-host\">");
                }

                sb.AppendLine($"            <a href=\"{HtmlEncode(channel.DeepLinkUrl)}\" class=\"veriqa-btn veriqa-btn-channel {btnClass}\" target=\"_blank\" rel=\"noopener noreferrer\"{btnTooltip}>");
                sb.AppendLine($"                {btnIcon}{btnLabelMarkup}");
                sb.AppendLine("            </a>");
                if (isThirdPartyBtnLabel)
                {
                    sb.AppendLine("                <span class=\"veriqa-tooltip\" aria-hidden=\"true\" hidden></span>");
                    sb.AppendLine("            </span>");
                }
            }

            // The channel hint closes its own panel — one insertion point for every channel type,
            // including both Email sections. Living inside the panel is what makes "the active tab
            // only" fall out of the composition: the hidden panel hides its hint with it, so the
            // multi-channel screen needs neither a script nor a state of its own. Absent hint —
            // absent element, so a deployment that sets none renders exactly today's markup.
            if (!string.IsNullOrEmpty(channel.Hint))
            {
                sb.AppendLine($"            <p class=\"veriqa-hint\">{HtmlEncode(channel.Hint)}</p>");
            }

            sb.AppendLine("        </div>");
        }

        // Spinner (SPEC-007 UI-045)
        sb.AppendLine("        <div class=\"veriqa-spinner\" id=\"veriqa-spinner\" aria-hidden=\"true\"></div>");

        // Status (SPEC-007 UI-048)
        var waitingText = Localize(AuthPageStrings.WaitingForScan, lang);
        sb.AppendLine($"        <div class=\"veriqa-status\" id=\"veriqa-status\" role=\"status\" aria-live=\"polite\">{HtmlEncode(waitingText)}</div>");

        // Countdown (SPEC-007 UI-094)
        sb.AppendLine("        <div class=\"veriqa-countdown\" id=\"veriqa-countdown\" aria-live=\"off\"></div>");

        // The one way out of an expired page, rendered ready and hidden: the state attribute shows it,
        // no script builds it. It stands NEXT TO the status line rather than inside it — the line is a
        // live region whose text is reassigned on every status, which would take a child element with
        // it, and a link announced as part of a status message is not what the region is for.
        if (!string.IsNullOrEmpty(context.RestartUrl))
        {
            var restartText = Localize(AuthPageStrings.RestartSignIn, lang);
            sb.AppendLine(
                $"        <a class=\"veriqa-btn veriqa-btn-restart\" id=\"veriqa-restart\" href=\"{HtmlEncode(context.RestartUrl)}\">{HtmlEncode(restartText)}</a>");
        }

        // The integrator's footer block closes the card, in every preset.
        AppendFooter(sb, design, lang);

        sb.AppendLine("    </div>");
    }

    /// <summary>
    /// Builds the integrator's footer block — the last element of the card — when a level states one.
    /// The text reaching the page is the TRANSLATION of the stated value, which the domain of the key
    /// never saw, so its shape is checked again here: a translation outside it leaves the page without
    /// the block and is reported, never emitted.
    /// </summary>
    /// <param name="sb">StringBuilder for HTML output.</param>
    /// <param name="design">Effective design of the page.</param>
    /// <param name="lang">Content language of the page.</param>
    private void AppendFooter(StringBuilder sb, AuthPageDesignOptions design, string lang)
    {
        if (string.IsNullOrWhiteSpace(design.FooterHtml))
        {
            return;
        }

        var markup = Localize(design.FooterHtml, lang);

        if (!AuthPageFooterMarkup.IsValid(markup))
        {
            _logger.LogWarning(
                "The text of {ConfigKey} for language {Language} is not admissible footer markup; the "
                + "sign-in page is rendered without the footer block",
                AuthServerConfigKeys.PageFooterHtml.Name,
                lang);

            return;
        }

        // The ONE place the page emits markup it did not build: the value is the integrator's, it is
        // not encoded, and AuthPageFooterMarkup is what stands between it and the page.
        sb.AppendLine($"        <footer class=\"veriqa-footer\">{markup}</footer>");
    }

    /// <summary>
    /// Builds the Email Push-mode HTML panel: QR code, "Open mail client" button,
    /// and an embedded Pull form as a fallback (toggled on click, without a separate page).
    /// The Push section (#veriqa-push-section) and Pull section (#veriqa-pull-section) are toggled by JS.
    /// </summary>
    /// <param name="sb">StringBuilder for HTML output.</param>
    /// <param name="channel">Channel data with the QR code and a deep link to the compose-helper.</param>
    /// <param name="sessionId">Transaction identifier for the Pull form (data-session-id).</param>
    /// <param name="lang">Language code.</param>
    private void AppendEmailPushPanel(StringBuilder sb, ChannelDisplayInfo channel, string sessionId, string lang)
    {
        // The method renders the Push section (QR + button) and the hidden Pull section (email entry).
        // INVARIANT: exactly one email panel is rendered on the page (Push OR Pull —
        // see the branching by isPushMode in AppendBody). Both panels use identical
        // ids (veriqa-email-input/submit/msg) — a joint render would produce duplicate ids
        // and break getElementById (review feedback).
        var qrAlt = Localize(AuthPageStrings.QrCodeAlt, lang);
        var btnText = Localize(AuthPageStrings.OpenEmailClient, lang);
        var orText = Localize(AuthPageStrings.OrDivider, lang);
        var fallbackText = Localize(AuthPageStrings.EmailLoginByLink, lang);
        // The back link names the QR code, which this very page may be hiding — so it travels as the
        // same pair of wordings as the instruction. One button (the ids are an invariant here), two
        // spans inside it: the hidden one leaves the accessible name with it.
        var backMarkup = BuildQrDependentText(
            Localize(AuthPageStrings.EmailBackToQr, lang),
            Localize(AuthPageStrings.EmailBackWithoutQr, lang));
        var inputLabel = Localize(AuthPageStrings.EmailInputLabel, lang);
        var submitText = Localize(AuthPageStrings.EmailSendLink, lang);
        var placeholder = AuthPageStrings.EmailInputPlaceholder;
        // Envelope icon in the mail-client CTA (color — button text via currentColor)
        var emailBtnIcon = ChannelIconSvg.Render(ChannelTypes.Email, "veriqa-btn-icon");

        sb.AppendLine($$"""
                    <div id="veriqa-push-section">
                        <div class="veriqa-qr">
                            <img src="{{AuthPageConstants.PngDataUriPrefix}}{{HtmlEncode(channel.QrCodeBase64)}}" alt="{{HtmlEncode(qrAlt)}}" width="{{AuthPageConstants.QrCodeImgAttributeSizePx}}" height="{{AuthPageConstants.QrCodeImgAttributeSizePx}}" />
                        </div>
                        <a href="{{HtmlEncode(channel.DeepLinkUrl)}}" class="veriqa-btn veriqa-btn-channel veriqa-btn-email" target="_blank" rel="noopener noreferrer">
                            {{emailBtnIcon}}{{HtmlEncode(btnText)}}
                        </a>
                        <div class="veriqa-divider">{{HtmlEncode(orText)}}</div>
                        <button class="veriqa-text-link" id="veriqa-show-pull" type="button">{{HtmlEncode(fallbackText)}}</button>
                    </div>
                    <div id="veriqa-pull-section" style="display:none;">
                        <button class="veriqa-text-link" id="veriqa-show-push" type="button">{{backMarkup}}</button>
                        <div class="veriqa-email-form">
                            <input
                                class="veriqa-email-input"
                                id="veriqa-email-input"
                                type="email"
                                autocomplete="email"
                                placeholder="{{HtmlEncode(placeholder)}}"
                                aria-label="{{HtmlEncode(inputLabel)}}"
                                required
                            />
                            <button
                                class="veriqa-btn"
                                id="veriqa-email-submit"
                                type="button"
                                data-session-id="{{HtmlEncode(sessionId)}}"
                            >
                                {{HtmlEncode(submitText)}}
                            </button>
                            <div class="veriqa-email-msg" id="veriqa-email-msg" role="status" aria-live="polite"></div>
                        </div>
                    </div>
            """);
    }

    /// <summary>
    /// Builds the Email-channel HTML panel with an email-address entry form.
    /// </summary>
    /// <param name="sb">StringBuilder for HTML output.</param>
    /// <param name="sessionId">Session identifier to send together with the email.</param>
    /// <param name="lang">Language code.</param>
    private void AppendEmailPanel(StringBuilder sb, string sessionId, string lang)
    {
        // The method renders the email-address entry form with a "Get link" button.
        // INVARIANT: exactly one email panel per page — the ids match AppendEmailPushPanel
        // (see the comment there, review feedback).

        var inputLabel = Localize(AuthPageStrings.EmailInputLabel, lang);
        var btnText = Localize(AuthPageStrings.EmailSendLink, lang);
        // The placeholder is not localized — the email format is the same for all languages
        var placeholder = AuthPageStrings.EmailInputPlaceholder;

        sb.AppendLine($$"""
                    <div class="veriqa-email-form">
                        <input
                            class="veriqa-email-input"
                            id="veriqa-email-input"
                            type="email"
                            autocomplete="email"
                            placeholder="{{HtmlEncode(placeholder)}}"
                            aria-label="{{HtmlEncode(inputLabel)}}"
                            required
                        />
                        <button
                            class="veriqa-btn"
                            id="veriqa-email-submit"
                            type="button"
                            data-session-id="{{HtmlEncode(sessionId)}}"
                        >
                            {{HtmlEncode(btnText)}}
                        </button>
                        <div class="veriqa-email-msg" id="veriqa-email-msg" role="status" aria-live="polite"></div>
                    </div>
            """);
    }

    /// <summary>
    /// Builds the script part of the window: the configuration of THIS request, the SignalR client
    /// and the window's own script (the connection, the countdown, the channel tabs, the email form).
    /// <para>
    /// The script itself is not written here — it lives in its own file and arrives as an embedded
    /// resource (<see cref="AuthPageScript"/>). What this method composes is everything the request
    /// decides: the values are collected into one <see cref="AuthPageScriptConfig"/> and handed over
    /// in a JSON data block, so the script stays free of server substitutions and the page keeps the
    /// shape it had — inline scripts under the page's nonce, no extra request (SPEC-015 §1).
    /// </para>
    /// </summary>
    private void AppendScript(
        StringBuilder sb,
        AuthPageRenderContext context,
        string lang,
        AuthPageDesignOptions design)
    {
        var msgConfirmed = Localize(AuthPageStrings.ConfirmedProcessing, lang);

        // The three terminal lines: the caller's wording where it stated one, this renderer's
        // sign-in wording where it did not (SPEC-039 R51). The sign-in path states none, so its
        // texts — and its behaviour — are exactly what they were.
        var msgSuccess = Stated(context.TerminalConfirmedText, AuthPageStrings.LoginSuccess, lang);
        var msgExpired = Stated(context.TerminalExpiredText, AuthPageStrings.SessionExpired, lang);
        var msgError = Stated(context.TerminalErrorText, AuthPageStrings.LoginError, lang);

        // A refusal by the user differs from a fault only by the reason code, and only where a
        // wording for it was stated: the sign-in path words every failure alike, and falling back to
        // the error text is what keeps it doing so.
        var msgDeclined = string.IsNullOrEmpty(context.TerminalDeclinedText)
            ? msgError
            : context.TerminalDeclinedText;
        var msgEmailSent = Localize(AuthPageStrings.EmailSentMessage, lang);
        var msgEmailError = Localize(AuthPageStrings.EmailSendError, lang);
        var msgEmailOpened = Localize(AuthPageStrings.EmailLinkOpened, lang);
        var msgEmailCompose = Localize(AuthPageStrings.EmailComposeOpened, lang);
        var msgEmailReceived = Localize(AuthPageStrings.EmailMailReceived, lang);
        var msgEmailVerified = Localize(AuthPageStrings.EmailSenderVerified, lang);

        // The remainder in milliseconds, as an invariant integer: the page adds it to its own clock
        // reading, so the number crossing into the script must not depend on the culture that formats
        // it. Rounded down — a countdown may not promise a second the transaction does not have.
        var remainingMs = (long)Math.Floor(Math.Max(0, context.RemainingSeconds) * 1000);

        // CSP nonce attribute for inline scripts (SPEC-007 §6.3)
        var nonceAttr = BuildNonceAttribute(context);

        // Path to the SignalR client: a configurable override or the bundled resource by default.
        // An empty/unset value → the package's bundled asset (default behavior is preserved).
        // The bundled asset is root-relative, so when hosted under a sub-path (PathBase)
        // it gets the prefix; a configured override is not touched (the value belongs to the operator).
        var signalRClientPath = !string.IsNullOrWhiteSpace(design.SignalRClientPath)
            ? design.SignalRClientPath
            : $"{context.PathBase}{SignalRConstants.SignalRClientPath}";

        // Status fallback-polling URL and the deterministic callback (the same one SignalR sends
        // in RedirectUrl on Completed — built from session_id, carries no sensitive data).
        // The values are root-relative: the PathBase prefix is added by the client helper withBase()
        // (a single normalization point, also for URLs from SignalR messages).
        var statusPollUrl = $"{OidcEndpoints.TransactionStatusBase}/{Uri.EscapeDataString(context.SessionId)}/status";

        // A page rendered without a navigation target does not get one built: its transaction has no
        // browser callback, and the callback of the sign-in path is not a place it may be sent to.
        var callbackUrl = context.NavigatesAway
            ? $"{OidcEndpoints.AuthorizeCallback}?{OidcConstants.SessionIdParameterName}={Uri.EscapeDataString(context.SessionId)}"
            : string.Empty;

        // The one destination such a page does have: the core page that asks the confirming question
        // (SPEC-039 E29). It is taken as the caller stated it and only in the mode without callback
        // navigation — on the sign-in path the question is asked past the callback, and a second
        // destination there would be a second way out of the window.
        var confirmationQuestionUrl = context.NavigatesAway
            ? string.Empty
            : context.ConfirmationQuestionUrl ?? string.Empty;

        var config = new AuthPageScriptConfig
        {
            SessionId = context.SessionId,
            BasePath = context.PathBase,
            RemainingMs = remainingMs,
            PollIntervalMs = PollingIntervalMs,
            NavigatesAway = context.NavigatesAway,
            StatusUrl = statusPollUrl,
            CallbackUrl = callbackUrl,
            ConfirmUrl = confirmationQuestionUrl,
            EmailStartUrl = EmailEndpointPaths.Start,
            EmailChannelType = ChannelTypes.Email,
            PageStateAttribute = PageStateAttributeName,
            ExpiredPageState = ExpiredPageStateValue,
            DeclinedReasonCode = TransactionErrorCodes.DeclinedByUser,
            // Named arguments: these carry same-typed values whose ORDER is the only thing telling
            // them apart, and a silent swap of two texts is exactly the defect this page cannot show
            // in a test — it renders, it just says the wrong thing.
            Statuses = new AuthPageScriptStatuses(
                Confirmed: TransactionStatusNames.Confirmed,
                AwaitingWebConfirmation: TransactionStatusNames.AwaitingWebConfirmation,
                Completed: TransactionStatusNames.Completed,
                Expired: TransactionStatusNames.Expired,
                Failed: TransactionStatusNames.Failed),
            Texts = new AuthPageScriptTexts(
                Confirmed: msgConfirmed,
                Success: msgSuccess,
                Expired: msgExpired,
                Error: msgError,
                Declined: msgDeclined,
                EmailSent: msgEmailSent,
                EmailError: msgEmailError),
            Hub = new AuthPageScriptHub(
                Path: SignalRConstants.AuthHubPath,
                StatusMethod: SignalRConstants.OnStatusChangedMethod,
                ChannelStatusMethod: SignalRConstants.OnChannelStatusChangedMethod,
                JoinMethod: SignalRConstants.JoinTransactionMethod),
            EmailStatusTexts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [EmailChannelStatusKeys.PullOpened] = msgEmailOpened,
                [EmailChannelStatusKeys.PushComposeOpened] = msgEmailCompose,
                [EmailChannelStatusKeys.PushMailReceived] = msgEmailReceived,
                [EmailChannelStatusKeys.PushVerified] = msgEmailVerified
            }
        };

        // Everything of the request reaches the script through ONE data block, placed ahead of it.
        // A block of type application/json is not executed, so no value of the transaction is ever
        // parsed as code, and each value is encoded for exactly one context instead of being spliced
        // into a JS string literal of its own.
        sb.Append($$"""
                <script type="application/json" id="{{AuthPageScript.ConfigElementId}}"{{nonceAttr}}>{{AuthPageScript.SerializeConfig(config)}}</script>
                <script src="{{HtmlEncode(signalRClientPath)}}"{{nonceAttr}}></script>
                <script{{nonceAttr}}>

            """);

        // The script itself: inlined from its own file, never served over a URL (SPEC-015 §1), and
        // carrying the page's nonce like every other inline script of the window (SPEC-007 §6.3).
        sb.Append(AuthPageScript.Js);
        sb.Append("        </script>");

        // Inclusion of custom JavaScript (SPEC-007 UI-055)
        if (!string.IsNullOrEmpty(design.CustomJsPath) && CorePageHead.IsAllowedUrlScheme(design.CustomJsPath))
        {
            var sriAttr = !string.IsNullOrEmpty(design.JsSriHash)
                ? $" integrity=\"{HtmlEncode(design.JsSriHash)}\" crossorigin=\"anonymous\""
                : string.Empty;
            sb.Append($"\n        <script src=\"{HtmlEncode(design.CustomJsPath)}\"{sriAttr}{nonceAttr}></script>");
        }
    }

    /// <summary>
    /// Builds the closing body and html tags.
    /// </summary>
    private static void AppendBodyClose(StringBuilder sb)
    {
        sb.AppendLine("\n</body>");
        sb.AppendLine("</html>");
    }

    /// <summary>
    /// Returns the CSS class of the channel button.
    /// </summary>
    private static string GetChannelButtonClass(string channelType)
    {
        return channelType switch
        {
            ChannelTypes.Telegram => "veriqa-btn-telegram",
            ChannelTypes.WhatsApp => "veriqa-btn-whatsapp",
            ChannelTypes.Max => "veriqa-btn-max",
            ChannelTypes.Email => "veriqa-btn-email",
            _ => "veriqa-btn-default"
        };
    }

    /// <summary>
    /// Tells whether at least one channel of the window carries a third-party label — the only kind
    /// that gets clipped and therefore needs the tooltip of the contour (SPEC-015 §4.16). Without
    /// one the tooltip host and its bubble are not rendered at all.
    /// </summary>
    /// <param name="channels">Channels displayed in the window.</param>
    /// <returns>true when at least one label comes from a third-party adapter.</returns>
    private static bool HasThirdPartyLabel(IReadOnlyList<ChannelDisplayInfo> channels)
    {
        foreach (var channel in channels)
        {
            if (!IsBuiltInChannel(channel.ChannelType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tells whether the channel is one of the built-in ones, whose displayed texts are our own
    /// (a short brand name in the tab, a localized phrase in the CTA). Everything else comes from a
    /// third-party adapter through the SPI, so its label length is not under our control.
    /// </summary>
    private static bool IsBuiltInChannel(string channelType)
    {
        return channelType is ChannelTypes.Telegram
            or ChannelTypes.WhatsApp
            or ChannelTypes.Max
            or ChannelTypes.Email;
    }

    /// <summary>
    /// Builds the CSP nonce attribute of an inline script (SPEC-007 §6.3): every inline script of the
    /// window gets it from one place, so a newly added one cannot be left out of the policy and
    /// silently blocked. No nonce from the host — an empty attribute.
    /// </summary>
    /// <param name="context">Page rendering context.</param>
    /// <returns>Attribute with a leading space, or an empty string.</returns>
    private static string BuildNonceAttribute(AuthPageRenderContext context)
    {
        return !string.IsNullOrEmpty(context.CspNonce)
            ? $" nonce=\"{HtmlEncode(context.CspNonce)}\""
            : string.Empty;
    }

    /// <summary>
    /// Builds a piece of page text in its two wordings: the one that holds while the QR code is shown
    /// and the one for a page that hides it (SPEC-007 UI-020). Both go into the markup, and the page
    /// shows the wording that matches what it actually displays — the server cannot choose, it does not
    /// know the device, and the same two attributes that hide the QR hide the sentence about it. The
    /// wording that does not hold is removed with <c>display: none</c>, so it leaves the accessibility
    /// tree as well and no reader is promised an element that is not there.
    /// </summary>
    /// <param name="withQr">Wording for a page that shows the QR code.</param>
    /// <param name="withoutQr">Wording for a page that hides it.</param>
    /// <returns>Ready HTML of the two wordings.</returns>
    private static string BuildQrDependentText(string withQr, string withoutQr)
    {
        return $"<span class=\"{QrShownTextClass}\">{HtmlEncode(withQr)}</span>"
            + $"<span class=\"{QrHiddenTextClass}\">{HtmlEncode(withoutQr)}</span>";
    }

    /// <summary>
    /// HTML-encodes a string for safe embedding in HTML.
    /// </summary>
    private static string HtmlEncode(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }
}
