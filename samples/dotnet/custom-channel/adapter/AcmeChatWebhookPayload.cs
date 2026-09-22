// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Veriqa.Sample.CustomChannel.Adapter;

/// <summary>
/// Webhook body of the sample platform. The shape belongs to the adapter's author — the core hands
/// over the raw request body and never interprets its format.
/// </summary>
/// <param name="TransactionId">Veriqa transaction the user started from the deep link.</param>
/// <param name="UserId">User identifier on the platform.</param>
/// <param name="DisplayName">User display name on the platform.</param>
public sealed record AcmeChatWebhookPayload(
    [property: JsonPropertyName("transaction_id")] string? TransactionId,
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("display_name")] string? DisplayName);
