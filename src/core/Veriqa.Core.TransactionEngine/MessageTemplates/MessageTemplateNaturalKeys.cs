// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// The single source of the core message-template Natural Keys (English base text = localization key,
/// SPEC-036 §3.5, TASK-057 flip). Living in the engine, below the channel layer, this is the ONE place
/// the literals of a message are declared; the channel-layer and page-layer constants that still carry
/// such a text reference these instead of spelling it again (TPL-110/TPL-093). The prompt keys keep
/// their exact pre-migration values (byte-for-byte, TPL-051/TPL-092).
/// <para>
/// The mail keys live here rather than with the mail adapter because it is the CONTRACT of the sign-in
/// mail that names them: a localized-string slot of that contract stands for the translation of one of
/// these keys (SPEC-036 §4.1), so the phrases belong to the message, not to the channel that sends it.
/// The same holds for the wordings of the confirmation prompt.
/// </para>
/// <para>
/// The wordings of the terminal outcome RECEIPT are no longer among them: they became a message of
/// the mechanism (SPEC-036 TPL-123), so they live where every wording of a message lives — in the
/// declaration the product ships, addressed by the dimensions that tell the surfaces apart. What is
/// left here of that group is the ERROR of a channel, which is not an outcome of a transaction and
/// stays the channel's own text (SPEC-036 §1.3).
/// </para>
/// All keys are registered in every host locale file (i18n-locale-parity, TPL-040/TPL-044).
/// </summary>
public static class MessageTemplateNaturalKeys
{
    /// <summary>Full prompt: application + browser + OS + region (unchanged, TPL-051).</summary>
    public const string PromptFull = "Confirm sign-in to \"{app}\" — request from {browser} on {os}, {region}";

    /// <summary>Prompt without region: application + browser + OS (unchanged, TPL-051).</summary>
    public const string PromptWithoutRegion = "Confirm sign-in to \"{app}\" — request from {browser} on {os}";

    /// <summary>Minimal prompt: application only (unchanged, TPL-051).</summary>
    public const string PromptApplicationOnly = "Confirm sign-in to \"{app}\"";

    /// <summary>
    /// The prompt a channel shows when the transaction carries no initiator details at all — the floor
    /// below the ladder above, which every step of names at least the application (ICC-042).
    /// </summary>
    public const string PromptDefault = "Confirm sign-in";

    /// <summary>
    /// Generic anomaly note appended to the prompt when no specific reason is set (ICC-053). Specific
    /// reasons (unusual region/device) arrive as keys of the host's anomaly detector and are checked
    /// against the loaded locale files — keys are not created here without a producer (SPEC-017 §9).
    /// </summary>
    public const string PromptAnomalyGeneric = "⚠ This request looks unusual — confirm only if it was you";

    /// <summary>
    /// Terminal receipt of a failed sign-in on the prompt surface of a messenger. It names the next
    /// step: the failure leaves the user in front of a dead prompt, and the only move that gets him
    /// anywhere is the one the sign-in page offers.
    /// </summary>
    public const string OutcomeErrorInChannel = "Something went wrong ⚠️ Go back to the sign-in page and start again.";

    /// <summary>
    /// Terminal receipt of a failed sign-in for a channel that states no wording of its own — the same
    /// sentence without the emoji, which such a channel is not guaranteed to render.
    /// </summary>
    public const string OutcomeErrorNeutral = "Something went wrong. Go back to the sign-in page and start again.";

    /// <summary>
    /// Reply to a sign-in link the core cannot serve any more: the transaction it names is gone, its
    /// window has closed, or it no longer accepts the event. One sentence for all of those reasons on
    /// purpose — the sender is not told which one applies (SPEC-039 C15) — and it names the next
    /// step, since the link in hand will not work however many times it is followed.
    /// </summary>
    public const string StaleLinkReply =
        "This sign-in link has expired or is no longer valid. Return to the application and start the sign-in again.";

    /// <summary>
    /// Answer to a decision refused because the per-user attempt budget is spent. It deliberately does
    /// NOT advise starting over: a new attempt on a spent budget is refused as well, so the next step
    /// here is waiting out the window (one minute, SPEC-007 §6.1) and only then signing in again.
    /// </summary>
    public const string OutcomeTooManyAttempts = "Too many attempts. Wait a minute and start the sign-in again.";

    /// <summary>Subject of the sign-in mail — the one text of the mail that carries a slot and stays a key.</summary>
    public const string MailTitle = "Sign in to {app}";

    /// <summary>Label of the confirmation button of the sign-in mail.</summary>
    public const string MailButton = "Approve sign-in";

    /// <summary>Instruction above the QR image of the sign-in mail.</summary>
    public const string MailQrInstruction = "Or scan the QR code to finish signing in on another device:";

    /// <summary>Alternative text of the QR image of the sign-in mail.</summary>
    public const string MailQrAlt = "Sign-in QR code";

    /// <summary>Intro line of the plain-text part of the sign-in mail.</summary>
    public const string MailTextIntro = "You requested sign-in via Veriqa.";

    /// <summary>Link hint of the plain-text part of the sign-in mail.</summary>
    public const string MailOpenLinkHint = "To confirm, open the following link:";

    /// <summary>Forwarding warning of the sign-in mail (SPEC-016 §9.3).</summary>
    public const string MailForwardWarning = "Do not forward this mail: the link grants sign-in access to your account.";

    /// <summary>Notice for a recipient who did not request the sign-in.</summary>
    public const string MailIgnoreNotice = "If you did not request sign-in, simply ignore this mail.";

    /// <summary>Label of the direct link in the footer of the sign-in mail.</summary>
    public const string MailDirectLinkLabel = "Direct link:";

    /// <summary>
    /// Served "link sent" text of the compose page. An ordinary localized string, not a message of the
    /// template mechanism: the <c>{email_masked}</c> token is a named position for the masked address in
    /// the sentence (was <c>"... sent to"</c> + concatenation, TPL-052), substituted by the page itself.
    /// </summary>
    public const string MailLinkSent = "The sign-in link has been sent to {email_masked}";

    /// <summary>
    /// WhatsApp deep-link prefill, full variant with the application name (SPEC-003 §10.4). Multi-line
    /// plain text: a title, a one-line instruction, then the sign-in code (<c>auth_{transaction_id}</c>)
    /// on its own last line so no line wrap can split it. Only "cheap" URL-safe punctuation is used —
    /// the whole text is percent-encoded into the <c>wa.me ?text=</c> parameter.
    /// </summary>
    public const string WhatsappPrefillWithApp =
        "Sign in to {app}\nSend this message to sign in.\n\nAccess code\n{code}";

    /// <summary>
    /// WhatsApp deep-link prefill, minimal variant without the application name (degradation floor when
    /// the application name is unavailable, SPEC-003 §10.4). The sign-in code stays on its own last line.
    /// </summary>
    public const string WhatsappPrefillWithoutApp =
        "Send this message to sign in.\n\nAccess code\n{code}";

    /// <summary>
    /// Deep-link prefill carrying no payload in its text at all — the degradation floor of the ladder,
    /// and the live text of a prefill whose payload rides elsewhere (the Email direct-mailto, where the
    /// correlation token is the recipient local part, SPEC-016 §5.3).
    /// </summary>
    public const string PrefillWithoutPayload = "Send this message to sign in.";

    /// <summary>Subject of the payload-free mail prefill — deliberately short, so the QR stays small.</summary>
    public const string PrefillMailSubject = "Sign in";

    /// <summary>
    /// Opening line of the mail prefill whose body carries the correlation token: it asks the user not to
    /// edit what he is about to send, since the token travels in the text.
    /// </summary>
    public const string PrefillMailIntro =
        "This mail confirms sign-in to Veriqa. Do not change the subject or the body of the mail.";
}
