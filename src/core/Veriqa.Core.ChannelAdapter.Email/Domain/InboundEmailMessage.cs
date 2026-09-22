// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Domain;

/// <summary>
/// Inbound email message for processing in Push mode (SPEC-016 §7.2).
/// Represents the data of an email received via webhook/IMAP/SMTP relay.
/// </summary>
/// <param name="MessageId">Email identifier (Message-ID header or provider event id).</param>
/// <param name="FromAddress">Sender email address from the From header.</param>
/// <param name="EnvelopeSender">Envelope sender / return-path (may differ from From).</param>
/// <param name="ToAddress">Recipient email address.</param>
/// <param name="Subject">Email subject.</param>
/// <param name="Body">Email body (plaintext).</param>
/// <param name="ReceivedAt">Moment the email was received (UTC).</param>
/// <param name="SpfResult">SPF check result from the inbound provider (optional).</param>
/// <param name="DkimResult">DKIM check result from the inbound provider (optional).</param>
/// <param name="DmarcResult">DMARC check result from the inbound provider (optional).</param>
/// <param name="ProviderVerifiedSender">
/// Email address verified by the inbound provider (if the provider offers such a guarantee).
/// </param>
/// <param name="RawHeaders">Raw email headers for additional verification (optional).</param>
public sealed record InboundEmailMessage(
    string MessageId,
    string FromAddress,
    string? EnvelopeSender,
    string ToAddress,
    string Subject,
    string? Body,
    DateTimeOffset ReceivedAt,
    string? SpfResult = null,
    string? DkimResult = null,
    string? DmarcResult = null,
    string? ProviderVerifiedSender = null,
    IReadOnlyDictionary<string, string>? RawHeaders = null);
