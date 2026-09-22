// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Enums;

/// <summary>
/// Outbound email delivery provider (SPEC-016 §7.1).
/// </summary>
public enum EmailOutboundProvider
{
    /// <summary>
    /// SMTP — basic on-premise integration. The default.
    /// </summary>
    Smtp,

    /// <summary>
    /// SendGrid — SaaS outbound delivery.
    /// </summary>
    SendGrid,

    /// <summary>
    /// Postmark — transactional email delivery.
    /// </summary>
    Postmark,

    /// <summary>
    /// Mailgun — transactional email + inbound routes.
    /// </summary>
    Mailgun,

    /// <summary>
    /// Custom — client-provided implementation via DI.
    /// </summary>
    Custom
}
