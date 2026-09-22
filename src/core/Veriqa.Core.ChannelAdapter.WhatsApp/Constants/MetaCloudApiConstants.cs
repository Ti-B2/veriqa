// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.WhatsApp.Constants;

/// <summary>
/// Meta Cloud API provider constants.
/// </summary>
internal static class MetaCloudApiConstants
{
    /// <summary>
    /// Graph API message-sending resource segment.
    /// </summary>
    public const string GraphMessagesResourceSegment = "messages";

    /// <summary>
    /// Meta Cloud API HTTP client name.
    /// </summary>
    public const string HttpClientName = "whatsapp-meta-cloud-api";

    /// <summary>
    /// Bearer authentication scheme.
    /// </summary>
    public const string BearerScheme = "Bearer";

    /// <summary>
    /// Content-type value for JSON.
    /// </summary>
    public const string ApplicationJsonContentType = "application/json";

    /// <summary>
    /// Graph API HTTP client timeout in seconds.
    /// </summary>
    public const int GraphApiTimeoutSeconds = 30;

    /// <summary>
    /// WhatsApp messaging product (used in the JSON payload).
    /// </summary>
    public const string MessagingProduct = "whatsapp";

    /// <summary>
    /// Outbound text message type.
    /// </summary>
    public const string OutboundTypeText = "text";

    /// <summary>
    /// Outbound interactive message type.
    /// </summary>
    public const string OutboundTypeInteractive = "interactive";

    /// <summary>
    /// Interactive message type with buttons.
    /// </summary>
    public const string InteractiveTypeButton = "button";

    /// <summary>
    /// Reply button type.
    /// </summary>
    public const string ReplyButtonType = "reply";
}
