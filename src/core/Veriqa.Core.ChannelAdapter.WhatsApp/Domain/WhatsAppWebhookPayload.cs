// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json.Serialization;

namespace Veriqa.Core.ChannelAdapter.WhatsApp.Domain;

/// <summary>
/// Root model of the incoming WhatsApp Cloud API webhook.
/// </summary>
internal sealed class WhatsAppWebhookPayload
{
    /// <summary>
    /// Webhook object type.
    /// </summary>
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = string.Empty;

    /// <summary>
    /// List of webhook entries.
    /// </summary>
    [JsonPropertyName("entry")]
    public IReadOnlyList<WhatsAppWebhookEntry> Entries { get; init; } = [];
}

/// <summary>
/// Webhook entry.
/// </summary>
internal sealed class WhatsAppWebhookEntry
{
    /// <summary>
    /// WhatsApp Business Account identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// List of changes in the entry.
    /// </summary>
    [JsonPropertyName("changes")]
    public IReadOnlyList<WhatsAppWebhookChange> Changes { get; init; } = [];
}

/// <summary>
/// Webhook entry change.
/// </summary>
internal sealed class WhatsAppWebhookChange
{
    /// <summary>
    /// Change type.
    /// </summary>
    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    /// <summary>
    /// Change value.
    /// </summary>
    [JsonPropertyName("value")]
    public WhatsAppWebhookChangeValue? Value { get; init; }
}

/// <summary>
/// Webhook change value.
/// </summary>
internal sealed class WhatsAppWebhookChangeValue
{
    /// <summary>
    /// Meta messaging product identifier.
    /// </summary>
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; init; }

    /// <summary>
    /// Business number metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public WhatsAppWebhookMetadata? Metadata { get; init; }

    /// <summary>
    /// List of sender contacts.
    /// </summary>
    [JsonPropertyName("contacts")]
    public IReadOnlyList<WhatsAppWebhookContact>? Contacts { get; init; }

    /// <summary>
    /// List of incoming messages.
    /// </summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<WhatsAppWebhookMessage>? Messages { get; init; }
}

/// <summary>
/// WhatsApp business number metadata.
/// </summary>
internal sealed class WhatsAppWebhookMetadata
{
    /// <summary>
    /// Displayed phone number.
    /// </summary>
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; init; }

    /// <summary>
    /// Phone number identifier.
    /// </summary>
    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; init; }
}

/// <summary>
/// Sender contact.
/// </summary>
internal sealed class WhatsAppWebhookContact
{
    /// <summary>
    /// Sender's WhatsApp phone number.
    /// </summary>
    [JsonPropertyName("wa_id")]
    public string? WaId { get; init; }

    /// <summary>
    /// Sender profile.
    /// </summary>
    [JsonPropertyName("profile")]
    public WhatsAppWebhookContactProfile? Profile { get; init; }
}

/// <summary>
/// Contact profile.
/// </summary>
internal sealed class WhatsAppWebhookContactProfile
{
    /// <summary>
    /// Sender name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>
/// Incoming webhook message.
/// </summary>
internal sealed class WhatsAppWebhookMessage
{
    /// <summary>
    /// Sender phone number.
    /// </summary>
    [JsonPropertyName("from")]
    public string? From { get; init; }

    /// <summary>
    /// Message identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// Message type.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Message timestamp (unix timestamp as a string).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    /// <summary>
    /// Text content.
    /// </summary>
    [JsonPropertyName("text")]
    public WhatsAppWebhookTextContent? Text { get; init; }

    /// <summary>
    /// Interactive content.
    /// </summary>
    [JsonPropertyName("interactive")]
    public WhatsAppWebhookInteractiveContent? Interactive { get; init; }

    /// <summary>
    /// Button content.
    /// </summary>
    [JsonPropertyName("button")]
    public WhatsAppWebhookButtonContent? Button { get; init; }
}

/// <summary>
/// Message text content.
/// </summary>
internal sealed class WhatsAppWebhookTextContent
{
    /// <summary>
    /// Message text.
    /// </summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
}

/// <summary>
/// Message interactive content.
/// </summary>
internal sealed class WhatsAppWebhookInteractiveContent
{
    /// <summary>
    /// Interactive content type.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>
    /// Button reply.
    /// </summary>
    [JsonPropertyName("button_reply")]
    public WhatsAppWebhookButtonReply? ButtonReply { get; init; }
}

/// <summary>
/// Button reply data.
/// </summary>
internal sealed class WhatsAppWebhookButtonReply
{
    /// <summary>
    /// Button identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// Button text.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

/// <summary>
/// Button content of a template/button message.
/// </summary>
internal sealed class WhatsAppWebhookButtonContent
{
    /// <summary>
    /// Button payload.
    /// </summary>
    [JsonPropertyName("payload")]
    public string? Payload { get; init; }

    /// <summary>
    /// Button text.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
