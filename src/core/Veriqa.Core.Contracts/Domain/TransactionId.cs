// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Veriqa.Core.TransactionEngine.Domain;

/// <summary>
/// Strongly-typed transaction identifier.
/// Generated from 256 bits of cryptographically strong randomness (CSPRNG) → Base62 encoding → 43 characters of fixed length.
/// Base62 alphabet: 0-9, a-z, A-Z.
/// </summary>
[JsonConverter(typeof(TransactionIdJsonConverter))]
public readonly struct TransactionId : IEquatable<TransactionId>, IParsable<TransactionId>
{
    /// <summary>
    /// Fixed length of the string representation.
    /// </summary>
    public const int StringLength = 43;

    /// <summary>
    /// Message of the exception thrown by <see cref="Parse"/> on an unparseable value.
    /// Deliberately free of the value itself: arbitrary request input reaches this path.
    /// </summary>
    private const string InvalidFormatMessage = "The value is not a valid transaction identifier.";

    /// <summary>
    /// Base62 alphabet for encoding.
    /// </summary>
    private const string Base62Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// String value of the identifier.
    /// </summary>
    private readonly string _value;

    /// <summary>
    /// Creates a TransactionId from a string value.
    /// </summary>
    /// <param name="value">String value (43 characters, Base62).</param>
    private TransactionId(string value)
    {
        _value = value;
    }

    /// <summary>
    /// Generates a new unique TransactionId from 256 bits of cryptographically strong randomness (CSPRNG).
    /// </summary>
    /// <returns>New TransactionId (43 Base62 characters).</returns>
    public static TransactionId NewId()
    {
        // Fill 32 bytes (256 bits) with the cryptographically strong system RNG (CSPRNG).
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);

        // Encode into Base62
        var encoded = EncodeBase62(bytes);

        return new TransactionId(encoded);
    }

    /// <summary>
    /// Tries to parse a string value into a TransactionId.
    /// Checks the length (43 characters) and the validity of each character (Base62).
    /// </summary>
    /// <param name="value">String value to parse.</param>
    /// <param name="result">Parse result; default(TransactionId) when parsing fails.</param>
    /// <returns>true if parsing succeeded.</returns>
    public static bool TryParse(string? value, out TransactionId result)
    {
        result = default;

        if (value is null || value.Length != StringLength)
        {
            return false;
        }

        // Check that all characters are from the Base62 alphabet
        foreach (var ch in value)
        {
            if (!IsBase62Char(ch))
            {
                return false;
            }
        }

        result = new TransactionId(value);
        return true;
    }

    /// <summary>
    /// Tries to parse a string value into a TransactionId (<see cref="IParsable{TSelf}"/> form).
    /// </summary>
    /// <param name="s">String value to parse.</param>
    /// <param name="provider">Ignored: the identifier format is culture-invariant.</param>
    /// <param name="result">Parse result; default(TransactionId) when parsing fails.</param>
    /// <returns>true if parsing succeeded.</returns>
    public static bool TryParse(string? s, IFormatProvider? provider, out TransactionId result) =>
        TryParse(s, out result);

    /// <summary>
    /// Parses a string value into a TransactionId (<see cref="IParsable{TSelf}"/> form).
    /// </summary>
    /// <param name="s">String value to parse.</param>
    /// <param name="provider">Ignored: the identifier format is culture-invariant.</param>
    /// <returns>Parsed transaction identifier.</returns>
    /// <exception cref="FormatException">The value is not a valid transaction identifier.</exception>
    public static TransactionId Parse(string s, IFormatProvider? provider) =>
        TryParse(s, out var result) ? result : throw new FormatException(InvalidFormatMessage);

    /// <summary>
    /// Returns the string representation of the identifier.
    /// For default(TransactionId) returns an empty string.
    /// </summary>
    /// <returns>A string of 43 Base62 characters, or an empty string for a default instance.</returns>
    public override string ToString() => _value ?? string.Empty;

    /// <inheritdoc />
    public bool Equals(TransactionId other) =>
        string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is TransactionId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        _value?.GetHashCode(StringComparison.Ordinal) ?? 0;

    /// <summary>
    /// Explicit conversion to string. For default(TransactionId) returns an empty string.
    /// Explicit on purpose: an implicit one let a typed identifier decay into a bare string
    /// wherever a string was expected, which is exactly what the type exists to prevent.
    /// </summary>
    /// <param name="id">Transaction identifier.</param>
    public static explicit operator string(TransactionId id) => id._value ?? string.Empty;

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(TransactionId left, TransactionId right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(TransactionId left, TransactionId right) => !left.Equals(right);

    /// <summary>
    /// Checks whether the character is a valid Base62 character.
    /// </summary>
    /// <param name="ch">Character to check.</param>
    /// <returns>true if the character belongs to the Base62 alphabet.</returns>
    private static bool IsBase62Char(char ch) =>
        ch is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    /// <summary>
    /// Encodes a byte array into a Base62 string of fixed length 43 characters.
    /// Uses leading '0' characters to pad to the fixed length.
    /// </summary>
    /// <param name="bytes">Source bytes (32 bytes).</param>
    /// <returns>Base62 string 43 characters long.</returns>
    private static string EncodeBase62(ReadOnlySpan<byte> bytes)
    {
        // Treat the bytes as a big number and divide by 62
        // For 32 bytes (256 bits) we need ceil(256 * log(2) / log(62)) ≈ 43 characters
        const int base62 = 62;
        var result = new char[StringLength];

        // Copy the bytes via stackalloc for in-place division (no heap allocation)
        Span<byte> data = stackalloc byte[bytes.Length];
        bytes.CopyTo(data);

        // Fill the result from right to left
        for (var i = StringLength - 1; i >= 0; i--)
        {
            var remainder = 0;
            for (var j = 0; j < data.Length; j++)
            {
                var accumulator = (remainder << 8) + data[j];
                data[j] = (byte)(accumulator / base62);
                remainder = accumulator % base62;
            }

            result[i] = Base62Alphabet[remainder];
        }

        return new string(result);
    }
}
