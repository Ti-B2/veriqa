// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Email.Enums;

namespace Veriqa.Core.ChannelAdapter.Email.Configuration;

/// <summary>
/// Inbound email processing settings for Push mode (SPEC-016 §7.2, §8).
/// </summary>
public sealed class EmailInboundOptions
{
    /// <summary>
    /// Inbound processing provider. Default — Webhook (production default).
    /// </summary>
    public EmailInboundProvider Provider { get; set; } = EmailInboundProvider.Webhook;

    /// <summary>
    /// Inbound mailbox email address for Push mode.
    /// </summary>
    public string InboundAddress { get; set; } = string.Empty;

    /// <summary>
    /// Use plus addressing to embed the correlation token into the recipient address.
    /// For example: login+{token}@example.com.
    /// </summary>
    public bool UsePlusAddressing { get; set; } = true;

    /// <summary>
    /// Direct-mailto Push mode (SPEC-016 §5.3): when true, the Push QR/button carries a direct
    /// <c>mailto:{token}@{domain}</c> (the correlation token IS the recipient local part, requiring a
    /// catch-all mailbox such as a Cloudflare Email Worker) instead of the compose-helper page URL, and the
    /// inbound processor extracts the token from the full recipient local part. The token is lower-case
    /// Base32 and the local part is folded to lower case before the lookup, so a mail client or provider
    /// that changes the case of the address does not break the correlation (SPEC-016 §5.4). This keeps the QR
    /// self-contained (no hosted compose page) with a short subject/body. Default — false (compose-helper,
    /// backward compatible). Mutually exclusive in intent with <see cref="UsePlusAddressing"/>: with a
    /// catch-all local-part token there is no plus segment.
    /// </summary>
    public bool TokenInLocalPart { get; set; } = false;

    /// <summary>
    /// Sender verification policy. Default — DmarcAlignedPass (production default).
    /// Using None is forbidden in production (EM-030).
    /// </summary>
    public EmailVerificationPolicy VerificationPolicy { get; set; } = EmailVerificationPolicy.DmarcAlignedPass;

    /// <summary>
    /// List of allowed sender domains/addresses.
    /// Used when <see cref="VerificationPolicy"/> = <see cref="EmailVerificationPolicy.AllowListOnly"/>.
    /// </summary>
    public IReadOnlyList<string> AllowedDomains { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Secret token for validating webhook requests from the inbound provider.
    /// Must be stored via the standard secrets mechanism from SPEC-012 (EM-052).
    /// </summary>
    public string WebhookSecretToken { get; set; } = string.Empty;
}
