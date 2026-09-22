// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.WhatsApp.Abstractions;

/// <summary>
/// Abstraction of a WhatsApp message delivery provider.
/// Each provider implements outgoing message sending and health checking,
/// while the channel logic (incoming event processing, identity) stays unified in the adapter.
/// </summary>
public interface IWhatsAppProvider
{
    /// <summary>
    /// Code of this provider; matched against the configured <c>Veriqa:Channels:WhatsApp:Provider</c>
    /// at startup. The shipped codes live in <c>WhatsAppProviderNames</c>; a provider supplied by the
    /// host names itself with its own code.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Sends a text message to the user.
    /// </summary>
    /// <param name="recipientPhone">Recipient number (digits only, without <c>+</c>).</param>
    /// <param name="messageText">Message text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the send succeeded.</returns>
    Task<bool> SendTextMessageAsync(
        string recipientPhone,
        string messageText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an interactive message with confirm/decline buttons.
    /// </summary>
    /// <param name="recipientPhone">Recipient number (digits only, without <c>+</c>).</param>
    /// <param name="bodyText">Message body text.</param>
    /// <param name="confirmButtonId">Confirm button identifier.</param>
    /// <param name="confirmButtonTitle">Confirm button text.</param>
    /// <param name="declineButtonId">Decline button identifier.</param>
    /// <param name="declineButtonTitle">Decline button text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether the send succeeded.</returns>
    Task<bool> SendInteractiveButtonsMessageAsync(
        string recipientPhone,
        string bodyText,
        string confirmButtonId,
        string confirmButtonTitle,
        string declineButtonId,
        string declineButtonTitle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks provider availability and credential validity.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the provider is available; error details when unavailable.</returns>
    Task<(bool IsHealthy, string? Details)> CheckHealthAsync(CancellationToken cancellationToken = default);
}
