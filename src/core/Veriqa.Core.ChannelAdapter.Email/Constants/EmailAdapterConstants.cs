// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.Contracts;

namespace Veriqa.Core.ChannelAdapter.Email.Constants;

/// <summary>
/// Email adapter constants (SPEC-016).
/// Contains the string and numeric literals of the adapter itself. The wordings of a message it
/// shows the user are not among them: those belong to the message and are declared with it
/// (<see cref="Veriqa.Core.TransactionEngine.MessageTemplates.MessageTemplateNaturalKeys"/>).
/// </summary>
internal static class EmailAdapterConstants
{
    /// <summary>
    /// Adapter version in semver format.
    /// </summary>
    public const string AdapterVersion = "1.0.0";

    /// <summary>
    /// The <c>&lt;html lang&gt;</c> fallback for served pages when the request has no supported locale
    /// (base language, English — TASK-057).
    /// </summary>
    public const string DefaultHtmlLang = "en";

    /// <summary>
    /// Webhook path for inbound Push-mode email events.
    /// </summary>
    public const string InboundWebhookPath = "/api/channels/email/inbound";

    /// <summary>
    /// Endpoint path for starting the Pull flow (email input and action token creation).
    /// </summary>
    public const string PullStartPath = "/auth/email/start";

    /// <summary>
    /// Endpoint path for the magic link confirmation page.
    /// </summary>
    public const string PullConfirmPath = "/auth/email/confirm";

    /// <summary>
    /// Path of the compose-helper page for the Push flow.
    /// </summary>
    public const string PushComposePath = "/auth/email/push/compose";

    /// <summary>
    /// Path of the CORE page that asks what a confirmation transaction is about (SPEC-039 R37) — where
    /// this adapter hands the user over instead of asking a question of its own.
    /// </summary>
    /// <remarks>
    /// The value duplicates the route constant of the auth server, and duplicating it is the same
    /// trade its <c>EmailEndpointPaths</c> makes in the opposite direction: the adapter is a satellite
    /// and does not reference the assembly that owns the route, so the alternative would be a
    /// reference back into it. Both spellings are covered by the same acceptance check of the flow —
    /// a link that lands anywhere else lands on nothing.
    /// </remarks>
    public const string CoreConfirmPagePath = "/auth/transaction/confirm";

    /// <summary>
    /// Name of the action/correlation token parameter in requests.
    /// </summary>
    public const string TokenQueryParam = "token";

    /// <summary>
    /// Name of the answer field of the served confirmation page. Both answers post to the same path;
    /// this field is what distinguishes them.
    /// </summary>
    public const string AnswerFormField = "answer";

    /// <summary>
    /// The answer value that declines the sign-in. Any other value (including an absent field) is a
    /// confirmation, so the fast link — which posts no answer at all — keeps working unchanged.
    /// </summary>
    public const string AnswerDecline = "no";

    /// <summary>
    /// HTTP header of the webhook secret token.
    /// </summary>
    public const string WebhookSecretHeader = "X-Email-Webhook-Secret";

    /// <summary>
    /// Retry-After header value (in seconds) for the 503 response on a transient failure completing
    /// an inbound Push transaction — instructs the provider to retry email delivery.
    /// </summary>
    public const string InboundRetryAfterSeconds = "30";

    /// <summary>
    /// Maximum raw metadata size in bytes (CA-005). Alias of the contract-wide
    /// <see cref="ChannelAdapterLimits.MaxRawMetadataSize"/> — the limit is identical for every channel,
    /// so this class does not carry its own literal.
    /// </summary>
    public const int MaxRawMetadataSize = ChannelAdapterLimits.MaxRawMetadataSize;

    /// <summary>
    /// Maximum inbound webhook body size in bytes (protection against unbounded buffering / DoS).
    /// The provider-agnostic JSON envelope of an inbound email fits within hundreds of KB.
    /// </summary>
    public const long MaxInboundBodyBytes = 512L * 1024L;

    /// <summary>
    /// Maximum length of the sender display name carried into the <c>preferred_username</c> claim
    /// (SPEC-016 §6.3). The value is an alias the sender types himself, so it needs a ceiling of its own:
    /// deliberately below the message-slot ceiling
    /// <see cref="TransactionEngine.MessageTemplates.MessageTemplateLimits.MaxSlotValueLength"/>
    /// (that one caps a rendered message line, not a token claim) and above the sign-in button label
    /// <see cref="CustomChannelConstants.MaxDisplayNameLength"/> (a brand name is shorter than a personal
    /// name). Anything longer is not a name — it is padding, and it is truncated rather than dropped.
    /// </summary>
    public const int MaxSenderDisplayNameLength = 64;

    /// <summary>
    /// Name of the RawMetadata field for the mode (pull/push).
    /// </summary>
    public const string MetadataKeyMode = "mode";

    /// <summary>
    /// Name of the RawMetadata field for the delivery provider.
    /// </summary>
    public const string MetadataKeyDeliveryProvider = "delivery_provider";

    /// <summary>
    /// Name of the RawMetadata field for the email message id.
    /// </summary>
    public const string MetadataKeyMessageId = "message_id";

    /// <summary>
    /// Name of the RawMetadata field for the inbound processing provider.
    /// </summary>
    public const string MetadataKeyInboundProvider = "inbound_provider";

    /// <summary>
    /// Name of the RawMetadata field for the SPF check result.
    /// </summary>
    public const string MetadataKeySpfResult = "spf_result";

    /// <summary>
    /// Name of the RawMetadata field for the DKIM check result.
    /// </summary>
    public const string MetadataKeyDkimResult = "dkim_result";

    /// <summary>
    /// Name of the RawMetadata field for the DMARC check result.
    /// </summary>
    public const string MetadataKeyDmarcResult = "dmarc_result";

    /// <summary>
    /// Pull mode value for RawMetadata.
    /// </summary>
    public const string ModePull = "pull";

    /// <summary>
    /// Push mode value for RawMetadata.
    /// </summary>
    public const string ModePush = "push";

    /// <summary>
    /// Error code: failed to send the email.
    /// </summary>
    /// <remarks>
    /// The other SPEC-016 §11 error codes (email_invalid, email_token_expired, etc.)
    /// are added to the constants as they come into actual use in the code —
    /// keeping an unused dictionary is forbidden by the "no dead code" rule.
    /// </remarks>
    public const string ErrorCodeDeliveryFailed = "email_delivery_failed";

    /// <summary>
    /// Error code: sender verification failed.
    /// </summary>
    public const string ErrorCodeSenderVerificationFailed = "email_sender_verification_failed";

    /// <summary>
    /// Error code: the requested mode is disabled.
    /// </summary>
    public const string ErrorCodeModeDisabled = "email_mode_disabled";

    /// <summary>
    /// Name of the session_id parameter in requests.
    /// </summary>
    public const string SessionIdQueryParam = "session_id";

    /// <summary>
    /// Action token length in bytes (before Base64 encoding).
    /// </summary>
    public const int TokenByteLength = 32;

    /// <summary>
    /// Length of a token in CHARACTERS: <see cref="TokenByteLength"/> bytes in Base64Url without
    /// padding, i.e. one character per 6 bits, the last one rounded up. It is the machine contract of the
    /// inbound side — a run of exactly this many token characters is what an incoming mail is searched
    /// for, since the wording that frames the token belongs to the prefill message and a deployment may
    /// reword it.
    /// </summary>
    public const int TokenCharLength = ((TokenByteLength * 8) + 5) / 6;

    /// <summary>
    /// How many token-shaped runs of one incoming mail are checked against the correlation store. A mail
    /// arrives from outside and a long quoted reply may hold any number of Base64Url-looking runs, so the
    /// number of store reads one mail can cost is capped; a mail whose budget runs out is answered as one
    /// carrying no token.
    /// </summary>
    public const int MaxCorrelationTokenProbes = 8;

    /// <summary>
    /// Plus-addressing separator in the Push-mode recipient address.
    /// For example: login{PlusAddressSeparator}{token}@example.com.
    /// </summary>
    public const char PlusAddressSeparator = '+';

    /// <summary>
    /// The "pass" value of the SPF/DKIM/DMARC check result from the inbound provider.
    /// </summary>
    public const string VerificationResultPass = "pass";

    /// <summary>
    /// Name of the Veriqa channel OIDC claim (SPEC-016 §6.3).
    /// </summary>
    public const string ClaimVeriqaChannel = VeriqaClaimTypes.VeriqaChannel;

    /// <summary>
    /// Name of the Email channel mode OIDC claim: pull or push (SPEC-016 §6.3).
    /// </summary>
    public const string ClaimVeriqaEmailMode = VeriqaClaimTypes.VeriqaEmailMode;

    /// <summary>
    /// Content-Id of the embedded QR image in the Pull-mode HTML email (CID attachment).
    /// </summary>
    public const string PullQrContentId = "veriqa-login-qr";

    /// <summary>
    /// Pixels per QR-code module in the Pull-mode email.
    /// </summary>
    public const int PullQrPixelsPerModule = 6;

    /// <summary>
    /// Deadline of the SMTP connection liveness probe, in seconds: shorter than MailKit's own network
    /// timeout, because the probe is one command on a connection believed to be open and it waits while
    /// holding the send semaphore — but comfortably longer than any healthy round-trip, including a
    /// loaded relay.
    /// </summary>
    /// <remarks>
    /// The margin is deliberate in that direction. Timing out declares a healthy connection stale and
    /// buys a full TCP+TLS+AUTH reconnect on every send — more expensive than simply waiting for the
    /// NOOP — so the cost of being too tight is worse than the cost of being too patient.
    /// </remarks>
    public const int SmtpLivenessProbeTimeoutSeconds = 15;

    /// <summary>
    /// How long, in seconds, application shutdown waits for the SMTP send semaphore before skipping the
    /// graceful disconnect — so a stuck TCP SYN or a slow SMTP server does not block shutdown.
    /// </summary>
    public const int SmtpShutdownLockTimeoutSeconds = 5;
}
