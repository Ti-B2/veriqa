// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Sample.CustomChannel.Adapter;

/// <summary>
/// Settings of the sample third-party channel. The options belong to the adapter's author:
/// the Veriqa core knows nothing about this section and does not bind it — the host does.
/// </summary>
public sealed class AcmeChatOptions
{
    /// <summary>
    /// Configuration section of the sample channel.
    /// </summary>
    public const string SectionName = "AcmeChat";

    /// <summary>
    /// Shared secret the platform sends in the webhook signature header.
    /// A webhook without the exact value is rejected with 403 by the pipeline.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the deep link opened from the sign-in window.
    /// </summary>
    public string DeepLinkBaseUrl { get; set; } = "https://chat.acme.example/start";
}
