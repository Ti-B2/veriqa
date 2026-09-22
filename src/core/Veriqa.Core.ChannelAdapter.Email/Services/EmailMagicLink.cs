// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Email.Constants;
using Veriqa.Core.ChannelAdapter.Email.Domain;

namespace Veriqa.Core.ChannelAdapter.Email.Services;

/// <summary>
/// The single builder of the Pull-mode magic link URL (SPEC-016 §4.3).
/// </summary>
/// <remarks>
/// The link is needed in two places that sit on opposite sides of the body/transport boundary: the
/// composer puts it into the template context, and the delivery provider encodes it into the QR
/// payload. A second copy of the concatenation would let the mail body and the QR code point at
/// different URLs.
/// </remarks>
internal static class EmailMagicLink
{
    /// <summary>
    /// Builds the magic link URL from the message data.
    /// </summary>
    /// <param name="message">The sign-in mail data.</param>
    /// <returns>The magic link URL.</returns>
    public static string Build(EmailLoginMessage message)
    {
        // The method concatenates the base URL with the confirmation path and the token
        var baseUrl = message.PublicBaseUrl.TrimEnd('/');
        var encodedToken = Uri.EscapeDataString(message.ActionToken);
        return $"{baseUrl}{EmailAdapterConstants.PullConfirmPath}?{EmailAdapterConstants.TokenQueryParam}={encodedToken}";
    }
}
