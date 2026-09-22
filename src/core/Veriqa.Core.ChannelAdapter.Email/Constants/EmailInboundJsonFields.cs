// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Constants;

/// <summary>
/// Field names of the provider-agnostic JSON of an inbound Push-mode email message (SPEC-016 §7.2).
/// Used when parsing the webhook request body into <see cref="Veriqa.Core.ChannelAdapter.Email.Domain.InboundEmailMessage"/>.
/// </summary>
internal static class EmailInboundJsonFields
{
    /// <summary>
    /// Email identifier (Message-ID or provider event id).
    /// </summary>
    public const string MessageId = "messageId";

    /// <summary>
    /// Sender email address from the From header.
    /// </summary>
    public const string From = "from";

    /// <summary>
    /// Envelope sender / return-path.
    /// </summary>
    public const string EnvelopeSender = "envelopeSender";

    /// <summary>
    /// Recipient email address.
    /// </summary>
    public const string To = "to";

    /// <summary>
    /// Email subject.
    /// </summary>
    public const string Subject = "subject";

    /// <summary>
    /// Email body (plaintext).
    /// </summary>
    public const string Body = "body";

    /// <summary>
    /// Time the email was received (ISO-8601).
    /// </summary>
    public const string ReceivedAt = "receivedAt";

    /// <summary>
    /// SPF check result.
    /// </summary>
    public const string Spf = "spf";

    /// <summary>
    /// DKIM check result.
    /// </summary>
    public const string Dkim = "dkim";

    /// <summary>
    /// DMARC check result.
    /// </summary>
    public const string Dmarc = "dmarc";

    /// <summary>
    /// Email address verified by the inbound provider.
    /// </summary>
    public const string ProviderVerifiedSender = "providerVerifiedSender";
}
