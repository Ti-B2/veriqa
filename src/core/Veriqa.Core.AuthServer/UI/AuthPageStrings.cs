// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Constants;

namespace Veriqa.Core.AuthServer.UI;

/// <summary>
/// Natural Keys of the authentication page (SPEC-007 §12, UI-079). The key IS the English text —
/// the base language (TASK-057), i.e. source text, not a translation. No translations live here:
/// every non-base language is resolved data-driven from the host locale files
/// (<c>wwwroot/locales/&lt;code&gt;.json</c>) through
/// <see cref="ChannelAdapter.Pipeline.IConfirmationPromptLocalizer"/> — see
/// <see cref="AuthPageLocalization"/> for the resolution rule and
/// <see cref="AuthPageLanguageRegistry"/> for the language registry.
/// </summary>
internal static class AuthPageStrings
{
    /// <summary>
    /// Page title.
    /// </summary>
    public const string Title = "Sign in via trusted channel";

    /// <summary>
    /// Instruction for the user.
    /// </summary>
    public const string Instruction = "Scan the QR code or tap the channel button";

    /// <summary>
    /// Instruction for a page that hides the QR code (SPEC-007 UI-020) — the counterpart of
    /// <see cref="Instruction"/>. Both wordings are rendered; the page shows the one that matches what
    /// it actually displays, because only the browser knows the device.
    /// </summary>
    public const string InstructionWithoutQr = "Tap the channel button to sign in";

    /// <summary>
    /// Instruction when multiple channels are available.
    /// </summary>
    public const string MultiChannelInstruction = "Choose a channel and scan the QR code or tap the button";

    /// <summary>
    /// Instruction for a page that hides the QR code and offers several channels — the counterpart of
    /// <see cref="MultiChannelInstruction"/> (SPEC-007 UI-020).
    /// </summary>
    public const string MultiChannelInstructionWithoutQr = "Choose a channel and tap the button";

    /// <summary>
    /// QR code alt text.
    /// </summary>
    public const string QrCodeAlt = "Sign-in QR code";

    /// <summary>
    /// "Open in Telegram" button.
    /// </summary>
    public const string OpenInTelegram = "Open in Telegram";

    /// <summary>
    /// "Open in WhatsApp" button.
    /// </summary>
    public const string OpenInWhatsApp = "Open in WhatsApp";

    /// <summary>
    /// "Open in MAX" button.
    /// </summary>
    public const string OpenInMax = "Open in MAX";

    /// <summary>
    /// "Sign in via Email" button.
    /// </summary>
    public const string LoginViaEmail = "Sign in via Email";

    /// <summary>
    /// "Open Email Client" button for the Email Push mode (compose-helper flow).
    /// </summary>
    public const string OpenEmailClient = "Open Email Client";

    /// <summary>
    /// Toggle link to manual email entry (from the Push panel).
    /// </summary>
    public const string EmailLoginByLink = "Sign in via email link";

    /// <summary>
    /// "OR" divider between the email client button and the manual email entry link.
    /// </summary>
    public const string OrDivider = "OR";

    /// <summary>
    /// Toggle link back to the QR code (from the Pull panel).
    /// </summary>
    public const string EmailBackToQr = "← Back to QR code";

    /// <summary>
    /// The same toggle link on a page that hides the QR code — the counterpart of
    /// <see cref="EmailBackToQr"/> (SPEC-007 UI-020). It names the direction only: the section it
    /// returns to is there either way, the QR in it is not.
    /// </summary>
    public const string EmailBackWithoutQr = "← Back";

    /// <summary>
    /// Email input field label (used for aria-label and the label element).
    /// </summary>
    public const string EmailInputLabel = "Email address";

    /// <summary>
    /// Email input field placeholder (not localized — the format is the same for all languages).
    /// </summary>
    public const string EmailInputPlaceholder = "your@email.com";

    /// <summary>
    /// "Get a link" button for the email form.
    /// </summary>
    public const string EmailSendLink = "Get a link";

    /// <summary>
    /// Message shown after the email was sent successfully.
    /// </summary>
    public const string EmailSentMessage = "Link sent to {0}. Check your inbox.";

    /// <summary>
    /// Email send error message.
    /// </summary>
    public const string EmailSendError = "Failed to send the link. Please try again.";

    /// <summary>
    /// Email Pull status: the user opened the confirmation page from the email (SPEC-016 §10.1).
    /// </summary>
    public const string EmailLinkOpened = "Email link opened. Confirm the sign-in.";

    /// <summary>
    /// Email Push status: the user opened the compose page (SPEC-016 §10.1).
    /// </summary>
    public const string EmailComposeOpened = "Email draft opened. Send the email to sign in.";

    /// <summary>
    /// Email Push status: the incoming email was received (SPEC-016 §10.1).
    /// </summary>
    public const string EmailMailReceived = "Email received. Verifying the sender…";

    /// <summary>
    /// Email Push status: the sender was verified (SPEC-016 §10.1).
    /// </summary>
    public const string EmailSenderVerified = "Sender verified. Completing sign-in…";

    /// <summary>
    /// Status: waiting for scan.
    /// </summary>
    public const string WaitingForScan = "Waiting for confirmation…";

    /// <summary>
    /// Status: confirmed, processing.
    /// </summary>
    public const string ConfirmedProcessing = "Confirmed. Processing…";

    /// <summary>
    /// Status: successful sign-in.
    /// </summary>
    public const string LoginSuccess = "Sign-in successful!";

    /// <summary>
    /// Status: session expired.
    /// </summary>
    public const string SessionExpired = "Session expired";

    /// <summary>
    /// The one action an expired sign-in window offers: go back and start over. It is worded as the
    /// user's intent and not as a mechanism ("retry", "reload"), because what the link does is begin a
    /// NEW sign-in — the expired one cannot be resumed.
    /// </summary>
    public const string RestartSignIn = "Start the sign-in again";

    /// <summary>
    /// Status: error.
    /// </summary>
    public const string LoginError = "Authentication error";

    /// <summary>
    /// Base language code. The only language code that belongs in code: it is the language OF the
    /// Natural Keys (the key is the text), not a supported-language entry. Every other language
    /// comes from the locale files — see <see cref="AuthPageLanguageRegistry"/>.
    /// </summary>
    public const string LanguageEn = "en";

    /// <summary>
    /// Detects the language from the Accept-Language header. If the language is not recognized
    /// or the header is missing — the default language from configuration is returned (SPEC-007 §12).
    /// Detection follows the order of header segments (the client orders them by preference);
    /// q-weights are NOT taken into account — the first segment whose primary subtag
    /// (the part before "-", e.g. "en" from "en-US") is in the supplied registry is used.
    /// Comparison via Contains is robust to the set's enumeration order (unlike StartsWith) and to
    /// language codes that are prefixes of each other. The registry is data-driven and built by
    /// <see cref="AuthPageLanguageRegistry"/>: dropping a locale file into the host's localization
    /// directory automatically includes the language in detection.
    /// </summary>
    /// <param name="acceptLanguage">Accept-Language header value.</param>
    /// <param name="defaultLanguage">Default language from configuration (Veriqa:Localization:DefaultLanguage).</param>
    /// <param name="supportedLanguages">Data-driven registry of supported languages.</param>
    /// <returns>Supported language code.</returns>
    public static string DetectLanguage(
        string? acceptLanguage,
        string defaultLanguage,
        IReadOnlySet<string> supportedLanguages)
    {
        // An invalid default language is normalized to English (the base language, TASK-057).
        var fallback = supportedLanguages.Contains(defaultLanguage) ? defaultLanguage : LanguageEn;

        if (string.IsNullOrEmpty(acceptLanguage))
        {
            return fallback;
        }

        // Limit the length to protect against malformed headers
        var headerValue = acceptLanguage.Length > 256
            ? acceptLanguage[..256]
            : acceptLanguage;

        // Iterate the segments in preference order (e.g. "en-US,ru;q=0.9" → ["en-US", "ru"]),
        // take the segment's primary subtag ("en-US" → "en") and check it against the registry via Contains.
        foreach (var segment in headerValue.Split(','))
        {
            var lang = segment.Trim().Split(';')[0].Trim();
            if (lang.Length == 0)
            {
                continue;
            }

            // The primary subtag, normalized to lowercase (registry codes are lowercase).
            var primarySubtag = lang.Split('-')[0].ToLowerInvariant();

            if (supportedLanguages.Contains(primarySubtag))
            {
                return primarySubtag;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Returns the channel display name for a tab.
    /// </summary>
    /// <param name="channelType">Channel type.</param>
    /// <returns>Channel display name.</returns>
    public static string GetChannelTabName(string channelType)
    {
        // The method returns the channel name displayed in the tab (not localized — brand names)
        return channelType switch
        {
            ChannelTypes.Telegram => "Telegram",
            ChannelTypes.WhatsApp => "WhatsApp",
            ChannelTypes.Max => "MAX",
            ChannelTypes.Email => "Email",
            _ => channelType
        };
    }
}
