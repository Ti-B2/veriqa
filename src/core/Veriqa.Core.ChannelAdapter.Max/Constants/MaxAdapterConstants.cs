// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Constants;

namespace Veriqa.Core.ChannelAdapter.Max.Constants;

/// <summary>
/// MAX adapter constants (SPEC-003 §11).
/// Contains the string and numeric literals of the adapter itself. The wordings of a message it
/// shows the user are not among them: those belong to the message and are declared with it
/// (<see cref="Veriqa.Core.TransactionEngine.MessageTemplates.MessageTemplateNaturalKeys"/>).
/// </summary>
internal static class MaxAdapterConstants
{
    /// <summary>
    /// Adapter version in semver format.
    /// </summary>
    public const string AdapterVersion = "1.0.0";

    /// <summary>
    /// HTTP header of the MAX Bot API webhook secret token.
    /// </summary>
    public const string WebhookSecretHeader = "X-Max-Bot-Api-Secret";

    /// <summary>
    /// Webhook path relative to the base URL.
    /// </summary>
    public const string WebhookPath = "/api/channels/max/webhook";

    /// <summary>
    /// Deep link parameter separator.
    /// </summary>
    public const string DeepLinkStartParam = "?start=";

    /// <summary>
    /// Deep link prefix for authentication (matches <see cref="CallbackDataPrefixes.Auth"/>).
    /// </summary>
    public const string DeepLinkPrefix = CallbackDataPrefixes.Auth;

    /// <summary>
    /// Confirm button text.
    /// </summary>
    public const string ConfirmButtonText = "Confirm ✅";

    /// <summary>
    /// Decline button text.
    /// </summary>
    public const string DeclineButtonText = "Decline ❌";

    /// <summary>
    /// Maximum raw metadata size in bytes (CA-005). Alias of the contract-wide
    /// <see cref="ChannelAdapterLimits.MaxRawMetadataSize"/> — the limit is identical for every channel,
    /// so this class does not carry its own literal.
    /// </summary>
    public const int MaxRawMetadataSize = ChannelAdapterLimits.MaxRawMetadataSize;

    /// <summary>
    /// Base delay between retried API calls on errors (in milliseconds).
    /// </summary>
    public const int RetryBaseDelayMs = 1000;

    /// <summary>
    /// Timeout of the getUpdates long-poll request (in seconds, TASK-051 — extracted magic constant).
    /// </summary>
    public const int LongPollTimeoutSeconds = 30;

    /// <summary>
    /// Maximum number of updates per one getUpdates long-poll request (TASK-051 — extracted magic constant).
    /// </summary>
    public const int LongPollUpdatesLimit = 100;

    /// <summary>
    /// MAX Bot API base URL (matches the Max.BotClient SDK default). Used for the direct
    /// keyboard-clearing edit (attachments: []) that the SDK message builder cannot express.
    /// </summary>
    public const string ApiBaseUrl = "https://platform-api.max.ru";

    /// <summary>
    /// Message edit/send API path (PUT /messages).
    /// </summary>
    public const string EditMessagePath = "/messages";

    /// <summary>
    /// Query parameter carrying the id of the message being edited.
    /// </summary>
    public const string MessageIdQueryParam = "message_id";

    /// <summary>
    /// Authorization header carrying the raw bot token (MAX auth is a header, not a query parameter).
    /// </summary>
    public const string AuthorizationHeaderName = "Authorization";

    /// <summary>
    /// Media type of the MAX Bot API request body.
    /// </summary>
    public const string JsonMediaType = "application/json";

    /// <summary>
    /// Named <see cref="System.Net.Http.IHttpClientFactory"/> client for the direct MAX Bot API edit call.
    /// </summary>
    public const string HttpClientName = "max-bot-api";

    /// <summary>
    /// Timeout (seconds) of the direct MAX Bot API edit call — an explicit bound instead of the default
    /// 100s, so a stuck request on the confirm/decline/expiry path fails fast rather than hanging.
    /// </summary>
    public const int EditMessageTimeoutSeconds = 30;

    /// <summary>
    /// Separator of the parts packed into the opaque inbound token: the callback identifier and the
    /// identifier of the message the button belongs to. Neither part can contain it — MAX
    /// callback identifiers and message ids never carry it.
    /// </summary>
    /// <remarks>
    /// The layout is the adapter's own business: the core carries the token back untouched and never
    /// parses it. The message id is packed here on purpose — the callback identifier exists only
    /// inside the inbound event, and the message id must survive a process restart or a
    /// different instance, where an in-process prompt store would have nothing to offer (CA-142).
    /// The chat is deliberately not packed: a MAX edit is addressed by the message id alone, so a
    /// chat slot would hold a value nothing ever reads.
    /// </remarks>
    public const string InboundTokenSeparator = "|";

    /// <summary>
    /// Number of parts in a well-formed inbound token. Only the first one (the callback identifier)
    /// is always filled; the message-id slot is empty when the press carried no source message.
    /// </summary>
    public const int InboundTokenPartCount = 2;

    /// <summary>
    /// Notification sent when answering a callback (<c>POST /answers</c>). The MAX Bot API rejects an
    /// answer that carries neither <c>message</c> nor <c>notification</c> (<c>proto.payload</c>); an empty
    /// notification satisfies it and clears the button's loading indicator without showing any text.
    /// </summary>
    public const string SilentCallbackNotification = "";
}
