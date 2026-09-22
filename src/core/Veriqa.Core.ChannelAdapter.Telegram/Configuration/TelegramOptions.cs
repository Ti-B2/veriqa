// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Telegram.Enums;

namespace Veriqa.Core.ChannelAdapter.Telegram.Configuration;

/// <summary>
/// Telegram adapter settings (SPEC-003 §17.1, SPEC-012 §6.9).
/// </summary>
public sealed class TelegramOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Veriqa:Channels:Telegram";

    /// <summary>
    /// Whether the Telegram adapter is active.
    /// Disabled by default so that a missing configuration section does not enable the channel implicitly.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Telegram bot token.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Update delivery mode.
    /// </summary>
    public TelegramUpdateMode UpdateMode { get; set; } = TelegramUpdateMode.Webhook;

    /// <summary>
    /// Base URL for the webhook (including scheme and host).
    /// </summary>
    public string WebhookBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Secret token for validating webhook requests.
    /// </summary>
    public string WebhookSecretToken { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the Telegram deep link.
    /// Default: "https://t.me/".
    /// </summary>
    public string DeepLinkBaseUrl { get; set; } = "https://t.me/";

    /// <summary>
    /// Public bot username (without @) — the bot identity (SPEC-003 §17.3, tenant-credential).
    /// When set, it is used directly for the deep link without a Bot API request; when empty — resolved via the API.
    /// </summary>
    public string BotUsername { get; set; } = string.Empty;

    /// <summary>
    /// Returns the tenant-credential group (tenant level, CFG-202) — derived from the flat properties.
    /// In self-hosted (N=1) these are the credentials of the single implicit tenant from the global IOptions.
    /// </summary>
    /// <returns>Tenant-credential settings group.</returns>
    public TelegramTenantCredentials GetTenantCredentials() =>
        new(BotToken, WebhookSecretToken, BotUsername, DeepLinkBaseUrl);
}
