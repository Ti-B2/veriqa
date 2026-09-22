// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Constants;

namespace Veriqa.Core.ChannelAdapter.WhatsApp.Constants;

/// <summary>
/// Common WhatsApp adapter constants (SPEC-003 §10, §21).
/// Provider-specific constants are extracted into <see cref="MetaCloudApiConstants"/>.
/// </summary>
internal static class WhatsAppAdapterConstants
{
    /// <summary>
    /// Adapter version in semver format.
    /// </summary>
    public const string AdapterVersion = "2.0.0";

    /// <summary>
    /// Webhook path relative to the base URL.
    /// </summary>
    public const string WebhookPath = "/api/channels/whatsapp/webhook";

    /// <summary>
    /// Webhook signature header from Meta.
    /// </summary>
    public const string SignatureHeader = "X-Hub-Signature-256";

    /// <summary>
    /// Webhook signature prefix in the header.
    /// </summary>
    public const string SignaturePrefix = "sha256=";

    /// <summary>
    /// Query parameter key of the Hub Challenge mode.
    /// </summary>
    public const string HubModeQueryKey = "hub.mode";

    /// <summary>
    /// Query parameter key of the verify token in the Hub Challenge.
    /// </summary>
    public const string HubVerifyTokenQueryKey = "hub.verify_token";

    /// <summary>
    /// Query parameter key of the challenge in the Hub Challenge.
    /// </summary>
    public const string HubChallengeQueryKey = "hub.challenge";

    /// <summary>
    /// Expected value of the Hub Challenge mode.
    /// </summary>
    public const string HubSubscribeMode = "subscribe";

    /// <summary>
    /// WhatsApp Business webhook object type.
    /// </summary>
    public const string WebhookObjectType = "whatsapp_business_account";

    /// <summary>
    /// Value of the change field carrying messages.
    /// </summary>
    public const string WebhookMessagesField = "messages";

    /// <summary>
    /// Inbound text message type.
    /// </summary>
    public const string InboundTypeText = "text";

    /// <summary>
    /// Inbound interactive message type.
    /// </summary>
    public const string InboundTypeInteractive = "interactive";

    /// <summary>
    /// Inbound button message type.
    /// </summary>
    public const string InboundTypeButton = "button";

    /// <summary>
    /// Confirm button text.
    /// </summary>
    public const string ConfirmButtonText = "Confirm ✅";

    /// <summary>
    /// Decline button text.
    /// </summary>
    public const string DeclineButtonText = "Decline ❌";

    /// <summary>
    /// WhatsApp deep link base URL.
    /// </summary>
    public const string DeepLinkBaseUrl = "https://wa.me/";

    /// <summary>
    /// Maximum raw metadata size in bytes (CA-005). Alias of the contract-wide
    /// <see cref="ChannelAdapterLimits.MaxRawMetadataSize"/> — the limit is identical for every channel,
    /// so this class does not carry its own literal.
    /// </summary>
    public const int MaxRawMetadataSize = ChannelAdapterLimits.MaxRawMetadataSize;

    /// <summary>
    /// Deep link prefix for authentication.
    /// </summary>
    public const string DeepLinkPrefix = CallbackDataPrefixes.Auth;
}
