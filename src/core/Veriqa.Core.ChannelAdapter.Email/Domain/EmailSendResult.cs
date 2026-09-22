// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Email.Domain;

/// <summary>
/// Result of sending an email (SPEC-016 §7.1).
/// </summary>
/// <param name="Success">Whether the send succeeded.</param>
/// <param name="MessageId">Message identifier from the email provider (optional).</param>
/// <param name="ErrorCode">Error code on a failed send (optional).</param>
/// <param name="ErrorMessage">Error message for logging (do not expose to the user).</param>
public sealed record EmailSendResult(
    bool Success,
    string? MessageId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Creates a successful send result.
    /// </summary>
    /// <param name="messageId">Message identifier from the provider.</param>
    /// <returns>A successful <see cref="EmailSendResult"/>.</returns>
    public static EmailSendResult Ok(string? messageId = null) =>
        new(Success: true, MessageId: messageId);

    /// <summary>
    /// Creates a failed send result.
    /// </summary>
    /// <param name="errorCode">Error code.</param>
    /// <param name="errorMessage">Error message for logging.</param>
    /// <returns>A failed <see cref="EmailSendResult"/>.</returns>
    public static EmailSendResult Fail(string errorCode, string? errorMessage = null) =>
        new(Success: false, ErrorCode: errorCode, ErrorMessage: errorMessage);
}
