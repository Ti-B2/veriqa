// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// HttpContext.Items keys shared between layers (ChannelAdapter ↔ AuthServer).
/// The constants are defined in the lower layer (ChannelAdapter) so that channel
/// adapter pages can read values set by the upper-layer (AuthServer) middleware
/// without a reverse dependency (review feedback — CSP nonce for inline scripts).
/// </summary>
public static class HttpContextItemKeys
{
    /// <summary>
    /// Key of the CSP nonce set by SecurityHeadersMiddleware (SPEC-007 §6.3).
    /// Pages with inline scripts must insert this value into the nonce attribute
    /// of the script tag — otherwise CSP will block the script.
    /// </summary>
    public const string CspNonce = "csp-nonce";
}
