// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Max.Configuration;

/// <summary>
/// MAX tenant-credential settings group (SPEC-003 §17.3, CFG-202): the tenant level
/// (in self-hosted ≡ core). Credentials, bot identity and branding — per tenant (CA-160/CA-162).
/// </summary>
/// <param name="BotToken">MAX bot token.</param>
/// <param name="WebhookSecretToken">Secret token for webhook request validation.</param>
/// <param name="DeepLinkBaseUrl">Deep link base URL (bot identity).</param>
/// <param name="BotPublicName">Public bot nickname (identity); empty — resolve via the Bot API.</param>
public sealed record MaxTenantCredentials(
    string BotToken,
    string WebhookSecretToken,
    string DeepLinkBaseUrl,
    string BotPublicName);
