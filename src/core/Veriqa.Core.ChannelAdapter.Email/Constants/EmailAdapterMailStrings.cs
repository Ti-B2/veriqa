// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.MessageTemplates;

namespace Veriqa.Core.ChannelAdapter.Email.Constants;

/// <summary>
/// Localization strings of the Email adapter's served pages and API answers.
/// Natural Keys — the English text is the key and the core default (base language after the
/// TASK-057 flip). The served pages (confirm page, Push compose page, standalone pull form) and the
/// Pull API error texts are resolved into the request/recipient language through
/// <c>IConfirmationPromptLocalizer</c> and are registered in the host locale files; a missing
/// locale/translation degrades to the key itself. Exceptions kept English-only by design: the Push
/// setup dev page (<c>PushSetup*</c>) and the email placeholder <c>PullFormEmailPlaceholder</c>.
/// </summary>
internal static class EmailAdapterMailStrings
{

    /// <summary>
    /// The question the served confirmation page asks. Shared Natural Key with the core web
    /// confirmation page (WebConfirmPage) — one wording for both confirmation surfaces.
    /// </summary>
    public const string ConfirmPageQuestion = "Do you confirm the sign-in?";

    /// <summary>
    /// The affirmative answer on the served confirmation page.
    /// </summary>
    public const string ConfirmPageAnswerYes = "Yes";

    /// <summary>
    /// The negative answer on the served confirmation page.
    /// </summary>
    public const string ConfirmPageAnswerNo = "No";

    /// <summary>
    /// The title of the confirmation page.
    /// </summary>
    public const string ConfirmPageTitle = "Approve sign-in";

    /// <summary>
    /// The confirmation error message (expired token).
    /// </summary>
    public const string ConfirmPageTokenExpired = "The link has expired. Request a new one.";

    /// <summary>
    /// The confirmation error message (used token).
    /// </summary>
    public const string ConfirmPageTokenUsed = "The link has already been used. Request a new one.";

    /// <summary>
    /// The confirmation error message (token not found).
    /// </summary>
    public const string ConfirmPageTokenNotFound = "The link is invalid or has expired. Request a new one.";

    /// <summary>
    /// The generic error message on the confirmation page.
    /// </summary>
    public const string ConfirmPageError = "Something went wrong. Try requesting a new link.";

    /// <summary>
    /// The title of the Push mode compose page.
    /// </summary>
    public const string PushComposeTitle = "Sign-in by email — Veriqa";

    /// <summary>
    /// The instruction on the Push mode compose page.
    /// </summary>
    public const string PushComposeInstruction =
        "Send the mail from this page to sign in. The subject and the body are already filled in — just press \"Send\" in your mail app.";

    /// <summary>
    /// The text of the button that opens the mail app (mailto).
    /// </summary>
    public const string PushComposeOpenMailButton = "Open mail app";

    /// <summary>
    /// A hint for manual sending (fallback block with copyable fields).
    /// </summary>
    public const string PushComposeFallbackHint =
        "If the button did not work, copy the data below and send the mail manually:";

    /// <summary>
    /// The label of the recipient address field.
    /// </summary>
    public const string PushComposeAddressLabel = "To";

    /// <summary>
    /// The label of the email subject field.
    /// </summary>
    public const string PushComposeSubjectLabel = "Subject";

    /// <summary>
    /// The label of the email body field.
    /// </summary>
    public const string PushComposeBodyLabel = "Body";

    /// <summary>
    /// The served "link sent" text — an ordinary localized string of the page, not a message of the
    /// template mechanism (migrated from a concatenation, TPL-052). The <c>{email_masked}</c> token is
    /// substituted client-side by token replacement into <c>textContent</c> (not innerHTML, not prefix
    /// concatenation) so the localized position of the address is preserved.
    /// </summary>
    public const string PushComposePullLinkSent = MessageTemplateNaturalKeys.MailLinkSent;

    /// <summary>
    /// The generic Pull-fallback error message.
    /// </summary>
    public const string PushComposePullError = "Failed to send the link. Check the address and try again.";

    /// <summary>
    /// The message for an invalid/expired Push mode compose page.
    /// </summary>
    public const string PushComposeInvalidToken =
        "The sign-in page is invalid or has expired. Go back and try again.";

    /// <summary>
    /// The message for a disabled Pull mode (standalone pull form).
    /// </summary>
    public const string PullFormModeDisabled = "The Email adapter Pull mode is disabled.";

    /// <summary>
    /// The title of the standalone pull form page.
    /// </summary>
    public const string PullFormTitle = "Sign in via email link";

    /// <summary>
    /// The heading (h1) of the standalone pull form page.
    /// </summary>
    public const string PullFormHeading = "Sign in with a link";

    /// <summary>
    /// The description of the standalone pull form page.
    /// </summary>
    public const string PullFormDescription = "Enter your email — we will send you a sign-in link.";

    /// <summary>
    /// A warning about a missing session on the pull form page.
    /// </summary>
    public const string PullFormNoSession = "Session not found. Return to the sign-in page.";

    /// <summary>
    /// The email field placeholder (the address format is identical across languages — not localized).
    /// </summary>
    public const string PullFormEmailPlaceholder = "your@email.com";

    /// <summary>
    /// The accessible name (aria-label) of the email field on the pull form page.
    /// </summary>
    public const string PullFormEmailAriaLabel = "Email address";

    /// <summary>
    /// The text of the magic-link submit button on the pull form page.
    /// </summary>
    public const string PullFormSubmitButton = "Get a link";

    /// <summary>
    /// The text of the link to the standalone pull form (from the compose and dev pages).
    /// </summary>
    public const string PullFormLinkText = "Sign in with an emailed link →";

    /// <summary>
    /// The title of the Push mode setup dev page.
    /// </summary>
    public const string PushSetupTitle = "Email Push setup";

    /// <summary>
    /// The heading (h1) of the Push mode setup dev page.
    /// </summary>
    public const string PushSetupHeading = "InboundAddress is not set";

    /// <summary>
    /// A dev-instruction fragment after the config key name.
    /// </summary>
    public const string PushSetupNotSetText = "is not set in the auth server configuration.";

    /// <summary>
    /// A dev-instruction fragment before the project name.
    /// </summary>
    public const string PushSetupSecretHint = "Set the secret in the project";

    /// <summary>
    /// A dev-instruction fragment about the restart (before the AppHost project name).
    /// </summary>
    public const string PushSetupRestartHint = "Then restart the server. Secrets set in";

    /// <summary>
    /// A dev-instruction fragment after the AppHost project name.
    /// </summary>
    public const string PushSetupSecretsScopeHint = ", do not propagate automatically to child projects.";

    /// <summary>
    /// API error: unsupported request content type.
    /// </summary>
    public const string ApiErrorUnsupportedContentType = "Unsupported content type.";

    /// <summary>
    /// API error: invalid email address.
    /// </summary>
    public const string ApiErrorInvalidEmail = "Invalid email address.";

    /// <summary>
    /// API error: missing session_id.
    /// </summary>
    public const string ApiErrorMissingSessionId = "Missing session_id.";

    /// <summary>
    /// API error: invalid session_id.
    /// </summary>
    public const string ApiErrorInvalidSessionId = "Invalid session_id.";

    /// <summary>
    /// API error (generic): the Email channel is unavailable.
    /// </summary>
    public const string ApiErrorChannelUnavailable = "The Email channel is unavailable.";

    /// <summary>
    /// API error (generic): the session is invalid or has expired.
    /// </summary>
    public const string ApiErrorSessionInvalid = "The session is invalid or has expired.";

    /// <summary>
    /// API error: the session has already been processed.
    /// </summary>
    public const string ApiErrorSessionAlreadyProcessed = "The session has already been processed.";

    /// <summary>
    /// API error (generic): the Email channel is unavailable for this session.
    /// </summary>
    public const string ApiErrorChannelNotAllowedForSession = "The Email channel is unavailable for this session.";

    /// <summary>
    /// API error: failed to send the email.
    /// </summary>
    public const string ApiErrorSendFailed = "Failed to send the mail. Try again later.";
}
