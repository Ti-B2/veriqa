// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Identifiers of the message kinds (message kinds) governed by the typed placeholder schema
/// (SPEC-036 §4.3). Kinds are constants, never magic strings; a caller-slot configuration and the
/// resolver key both address a kind by these identifiers.
/// </summary>
public static class MessageKinds
{
    /// <summary>The sign-in confirmation prompt (messenger channels, SPEC-017 §7.2).</summary>
    public const string ConfirmationPrompt = "confirmation-prompt";

    /// <summary>
    /// The sign-in mail (Pull mode, SPEC-016 §4.3). ONE kind for the whole mail: its subject and its
    /// body are fields of the template variant, and the render mode (<c>html</c>/<c>plain</c>) is
    /// another — neither a part of a message nor a way of rendering it is a kind of its own.
    /// </summary>
    public const string SignInMail = "sign-in-mail";

    /// <summary>
    /// The deep-link prefill message that carries the sign-in code or the correlation token
    /// (SPEC-003 §10.4, SPEC-016 §5.3). ONE kind for every channel that prefills a deep link: the
    /// channel is a value of the <c>channel</c> dimension of the template key, never a kind of its own.
    /// </summary>
    public const string DeeplinkPrefill = "deeplink-prefill";

    /// <summary>
    /// Terminal receipt of a CONFIRMED decision (SPEC-036 TPL-123). ONE kind for every wording of it:
    /// what the transaction was about, what is shown where and through which channel are values of the
    /// declared dimensions of the template key — never kinds of their own.
    /// </summary>
    public const string OutcomeReceiptConfirmed = "outcome-receipt-confirmed";

    /// <summary>Terminal receipt of a DECLINED decision (SPEC-036 TPL-123).</summary>
    public const string OutcomeReceiptDeclined = "outcome-receipt-declined";

    /// <summary>
    /// Terminal receipt of a transaction whose time is over — the TTL handler and a decision arriving
    /// for a transaction the store no longer has both end here (SPEC-036 TPL-123, SPEC-003 CA-192).
    /// </summary>
    public const string OutcomeReceiptExpired = "outcome-receipt-expired";

    /// <summary>
    /// Whether the kind is one of the three outcome receipts — the group whose caller slots are
    /// filled from values accepted against ANOTHER contract, and are therefore never guaranteed
    /// (SPEC-036 TPL-123).
    /// </summary>
    /// <param name="kind">Message kind identifier.</param>
    /// <returns><c>true</c> — the kind is an outcome receipt.</returns>
    public static bool IsOutcomeReceipt(string kind) =>
        string.Equals(kind, OutcomeReceiptConfirmed, StringComparison.Ordinal)
        || string.Equals(kind, OutcomeReceiptDeclined, StringComparison.Ordinal)
        || string.Equals(kind, OutcomeReceiptExpired, StringComparison.Ordinal);
}
