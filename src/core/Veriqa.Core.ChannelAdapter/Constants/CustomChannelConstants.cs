// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.MessageTemplates;

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Public SPI constants for third-party channels registered through
/// <c>ChannelAdapterBuilder.AddChannel</c>. Holds the channel-type contract, the generic
/// webhook path convention and the neutral default ERROR text of the pipeline.
/// <para>
/// There are no confirmed/declined defaults beside it: the terminal outcome of a transaction is a
/// message of the mechanism (SPEC-036 TPL-123), resolved by address from the
/// levelled configuration, so a channel neither states it nor takes a default for it. The error a
/// channel reports is not an outcome of the transaction and stays its own (SPEC-036 §1.3).
/// </para>
/// </summary>
public static class CustomChannelConstants
{
    /// <summary>
    /// Allowed <c>ChannelType</c> format of a third-party channel. Alias of the canonical
    /// <see cref="CustomChannelContract.ChannelTypePattern"/> in the MIT contracts assembly (which
    /// documents the contract and the <c>\A</c>/<c>\z</c> anchor choice); kept so the existing core
    /// references stay valid without duplicating the value.
    /// </summary>
    public const string ChannelTypePattern = CustomChannelContract.ChannelTypePattern;

    /// <summary>
    /// Maximum length of the display label a third-party adapter may declare. Alias of the canonical
    /// <see cref="CustomChannelContract.MaxDisplayNameLength"/> in the MIT contracts assembly.
    /// </summary>
    public const int MaxDisplayNameLength = CustomChannelContract.MaxDisplayNameLength;

    /// <summary>
    /// Maximum length of the glyph markup a third-party adapter may declare. Alias of the canonical
    /// <see cref="CustomChannelContract.MaxIconSvgPathLength"/> in the MIT contracts assembly.
    /// </summary>
    public const int MaxIconSvgPathLength = CustomChannelContract.MaxIconSvgPathLength;

    /// <summary>
    /// Prefix of the generic webhook path (the convention of the built-in channels).
    /// </summary>
    public const string WebhookPathPrefix = "/api/channels/";

    /// <summary>
    /// Suffix of the generic webhook path (the convention of the built-in channels).
    /// </summary>
    public const string WebhookPathSuffix = "/webhook";

    /// <summary>
    /// Default status text of a processing error for a third-party channel (neutral wording).
    /// </summary>
    public const string ErrorMessageText = MessageTemplateNaturalKeys.OutcomeErrorNeutral;

    /// <summary>
    /// Builds the generic webhook path of a channel by the SPI convention:
    /// <c>/api/channels/{channelType}/webhook</c>. The channel type is expected to be already
    /// validated against <see cref="ChannelTypePattern"/> (the builder validates it at startup).
    /// </summary>
    /// <param name="channelType">Channel type.</param>
    /// <returns>Webhook path relative to the base URL.</returns>
    public static string BuildWebhookPath(string channelType)
        => WebhookPathPrefix + channelType + WebhookPathSuffix;
}
