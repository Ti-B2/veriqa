// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Telegram.Configuration;

/// <summary>
/// Telegram tenant-credential settings group (SPEC-003 §17.3, CFG-202): tenant level
/// (in self-hosted ≡ core). Credentials, bot identity, and branding are resolved per tenant
/// of the transaction/request via the seam (CA-160/CA-162). Resolved by <c>(tenant, ChannelType)</c>.
/// </summary>
/// <param name="BotToken">Telegram bot token.</param>
/// <param name="WebhookSecretToken">Secret token for validating webhook requests.</param>
/// <param name="BotUsername">Public bot username (identity, §17.3); when empty — resolved via the Bot API.</param>
/// <param name="DeepLinkBaseUrl">Deep link base URL (bot identity).</param>
public sealed record TelegramTenantCredentials(
    string BotToken,
    string WebhookSecretToken,
    string BotUsername,
    string DeepLinkBaseUrl);
