// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// JSON converter for TransactionId.
/// Serializes and deserializes TransactionId as a string.
/// </summary>
public sealed class TransactionIdJsonConverter : JsonConverter<TransactionId>
{
    /// <inheritdoc />
    public override TransactionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Check the JSON token type before reading the value
        if (reader.TokenType is JsonTokenType.Null)
        {
            throw new JsonException("TransactionId cannot be null.");
        }

        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a string for TransactionId but got a token of type '{reader.TokenType}'.");
        }

        // Read the string value after checking the token type
        var value = reader.GetString();

        if (TransactionId.TryParse(value, out var result))
        {
            return result;
        }

        throw new JsonException($"Invalid TransactionId format: '{value}'");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TransactionId value, JsonSerializerOptions options)
    {
        // Write as a string
        writer.WriteStringValue(value.ToString());
    }
}
