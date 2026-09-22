// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Email.Enums;

namespace Veriqa.Core.ChannelAdapter.Email.Configuration;

/// <summary>
/// Outbound email delivery settings for Pull mode (SPEC-016 §7.1, §8).
/// </summary>
public sealed class EmailOutboundOptions
{
    /// <summary>
    /// Outbound delivery provider. Defaults to SMTP.
    /// </summary>
    public EmailOutboundProvider Provider { get; set; } = EmailOutboundProvider.Smtp;

    /// <summary>
    /// Sender email address (the From field).
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// Sender display name (the From display name field).
    /// </summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// Reply email address (Reply-To). Optional.
    /// Not used as identity (EM-033).
    /// </summary>
    public string? ReplyToAddress { get; set; }

    /// <summary>
    /// SMTP settings. Used when <see cref="Provider"/> = <see cref="EmailOutboundProvider.Smtp"/>.
    /// </summary>
    public EmailSmtpOptions? Smtp { get; set; }
}
