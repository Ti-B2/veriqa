// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Canonical slot names of the messages the product ships — declared as values of the <c>Core</c>
/// level of the contract key, not as a registry of their own (TPL-109, TPL-005). Names are the single
/// source for the substitution matcher: the token in a template is <c>"{" + name + "}"</c>. No magic
/// strings — every core slot name is a constant here.
/// </summary>
public static class SlotNames
{
    /// <summary>Client application name (confirmation prompt, initiator context).</summary>
    public const string App = "app";

    /// <summary>Initiator browser (confirmation prompt, best-effort).</summary>
    public const string Browser = "browser";

    /// <summary>Initiator OS/platform (confirmation prompt, best-effort).</summary>
    public const string Os = "os";

    /// <summary>Initiator region "City, Country" (confirmation prompt, best-effort).</summary>
    public const string Region = "region";

    /// <summary>Magic-link expiry moment shown in sign-in mails (datetime, system source).</summary>
    public const string ValidUntil = "valid_until";

    /// <summary>Magic-link URL of the sign-in mail (system source); the mail body uses it twice.</summary>
    public const string Link = "link";

    /// <summary>
    /// Language tag the text is ACTUALLY rendered in (system source) — what the sign-in mail declares
    /// as the language of its HTML document.
    /// </summary>
    public const string Lang = "lang";

    /// <summary>
    /// URL of the sign-in QR image (system source, not guaranteed). Which form of the QR reaches a mail
    /// is decided by the slots a variant references, never by a setting: this one is the form for a
    /// composer that hosts the image, and the core supply states no value for it.
    /// </summary>
    public const string QrUrl = "qr_url";

    /// <summary>
    /// Content-Id of the attached sign-in QR image, without the <c>"cid:"</c> prefix (system source, not
    /// guaranteed). A mail carries the attachment only when the variant that was rendered referenced
    /// this slot.
    /// </summary>
    public const string QrCid = "qr_cid";

    /// <summary>Masked recipient address shown on the served "link sent" page (system source).</summary>
    public const string EmailMasked = "email_masked";

    /// <summary>Sign-in code (<c>auth_{transaction_id}</c>) placed into the WhatsApp deep-link prefill text (system source).</summary>
    public const string Code = "code";

    /// <summary>
    /// Correlation token carried by the TEXT of a mail the user sends back (system source, not
    /// guaranteed): the Email form of the deep-link prefill. The wording that frames it belongs to the
    /// template variant, so the inbound side matches the FORM of the token rather than the framing.
    /// </summary>
    public const string Token = "token";

    /// <summary>
    /// Moment the transaction ended — the one an outcome receipt reports (datetime, system source).
    /// Stated by the render point that shows the receipt: the recording of the decision for a
    /// confirmed or a declined outcome, the TTL deadline for an expired one — never the moment the
    /// expiry was noticed, which differs by surface and says nothing about the transaction. A point
    /// that reports an outcome it has no moment of (a decision arriving for a transaction the store
    /// no longer has, a page that words its receipts before any of them happens) states none, and the
    /// slot then has no value like any other absent server slot.
    /// </summary>
    public const string OutcomeAt = "outcome_at";
}
