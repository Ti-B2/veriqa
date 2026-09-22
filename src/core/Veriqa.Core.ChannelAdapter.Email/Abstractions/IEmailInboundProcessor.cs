// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.Email.Domain;

namespace Veriqa.Core.ChannelAdapter.Email.Abstractions;

/// <summary>
/// Contract of the inbound email processing provider for the Push mode (SPEC-016 §7.2).
/// The implementation is selected via configuration and DI.
/// </summary>
public interface IEmailInboundProcessor
{
    /// <summary>
    /// Processes an inbound email message and builds the result for the channel pipeline.
    /// Applies sender verification according to the configured verification policy.
    /// </summary>
    /// <param name="message">Inbound email message from the inbound provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Processing result for the channel pipeline.
    /// On successful verification — a <see cref="ChannelAuthConfirmResult"/>.
    /// On a verification failure — a <see cref="ChannelUnrelatedResult"/>.
    /// </returns>
    Task<ChannelInboundResult> ProcessInboundEmailAsync(
        InboundEmailMessage message,
        CancellationToken cancellationToken);
}
