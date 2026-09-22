// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Max.Enums;

namespace Veriqa.Core.ChannelAdapter.Max.Configuration;

/// <summary>
/// MAX adapter settings (SPEC-003 §11, SPEC-012 §6).
/// </summary>
public sealed class MaxOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Veriqa:Channels:Max";

    /// <summary>
    /// Default deep link base URL.
    /// Used in <see cref="DeepLinkBaseUrl"/> as the default value.
    /// </summary>
    public const string DefaultDeepLinkBaseUrl = "https://max.ru/";

    /// <summary>
    /// Whether the MAX adapter is active.
    /// Disabled by default so that a missing configuration section does not enable the channel implicitly.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// MAX bot token.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Update delivery mode.
    /// </summary>
    public MaxUpdateMode UpdateMode { get; set; } = MaxUpdateMode.Webhook;

    /// <summary>
    /// Base URL for the webhook (including the scheme and host).
    /// </summary>
    public string WebhookBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Secret token for webhook request validation.
    /// Passed in the X-Max-Bot-Api-Secret header.
    /// </summary>
    public string WebhookSecretToken { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the MAX bot deep link.
    /// Default: <see cref="DefaultDeepLinkBaseUrl"/>.
    /// Format: {DeepLinkBaseUrl}{botUsername}?start={payload}.
    /// A trailing slash is appended automatically if missing.
    /// </summary>
    public string DeepLinkBaseUrl
    {
        get => _deepLinkBaseUrl;
        set => _deepLinkBaseUrl = string.IsNullOrEmpty(value)
            ? value
            : value.TrimEnd('/') + '/';
    }

    // Backing field for DeepLinkBaseUrl with the normalized default value
    private string _deepLinkBaseUrl = DefaultDeepLinkBaseUrl;

    /// <summary>
    /// Public bot name (nickname) — the exact nick from the public MAX link.
    /// For example, for the link https://max.ru/mybot the value is "mybot".
    /// If specified, it is used directly without a Bot API call.
    /// </summary>
    public string BotPublicName { get; set; } = string.Empty;

    /// <summary>
    /// Returns the tenant-credential group (tenant level, CFG-202) — derived from the flat properties.
    /// In self-hosted (N=1) — the credentials of the single implicit tenant from the global IOptions.
    /// </summary>
    /// <returns>Tenant-credential settings group.</returns>
    public MaxTenantCredentials GetTenantCredentials() =>
        new(BotToken, WebhookSecretToken, DeepLinkBaseUrl, BotPublicName);
}
