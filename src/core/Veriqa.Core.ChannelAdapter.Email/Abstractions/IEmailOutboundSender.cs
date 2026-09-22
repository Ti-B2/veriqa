// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Email.Domain;

namespace Veriqa.Core.ChannelAdapter.Email.Abstractions;

/// <summary>
/// Contract of the outbound email delivery provider for Pull mode (SPEC-016 §7.1).
/// The implementation is selected via configuration and DI.
/// </summary>
public interface IEmailOutboundSender
{
    /// <summary>
    /// Sends a magic link email for Pull-mode authentication.
    /// </summary>
    /// <param name="message">Data for building and sending the email.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The email send result.</returns>
    Task<EmailSendResult> SendLoginEmailAsync(
        EmailLoginMessage message,
        CancellationToken cancellationToken);
}
