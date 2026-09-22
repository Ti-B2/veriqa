// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Validates a single slot value against its type and constraints (SPEC-036 §4.2). The input is the
/// string representation of the value (wire-agnostic); the output is the normalized value or a violation
/// naming the broken rule. <c>money</c> is not validated here — a money slot cannot reach this path
/// because it is rejected in configuration v1 (TPL-015, D3).
/// </summary>
public static partial class SlotValueValidator
{
    /// <summary>
    /// ISO 8601 offset suffix: a trailing <c>Z</c> or <c>±HH:MM</c>/<c>±HHMM</c>. Required so a naked
    /// local datetime (no offset) is rejected instead of silently assuming the server's zone (TPL-016).
    /// </summary>
    [GeneratedRegex("(Z|[+-]\\d{2}:?\\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex Iso8601OffsetSuffix();

    /// <summary>
    /// Number styles for the <c>number</c> type: integer or decimal with an optional sign, but NO
    /// exponent — <c>1e3</c> is rejected as <c>not_a_number</c> (edge case).
    /// </summary>
    private const NumberStyles NumberParseStyles =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>
    /// Validates <paramref name="value"/> against <paramref name="slot"/>. On an enum mismatch a
    /// security WARNING is logged with the slot and message kind but WITHOUT the raw value (core-rules §10).
    /// </summary>
    /// <param name="slot">Slot declaration (type + constraints).</param>
    /// <param name="value">Raw string value.</param>
    /// <param name="messageKind">Message kind (for the security log context only).</param>
    /// <param name="logger">Logger for the enum security event (null — no logging).</param>
    /// <returns>Normalized value on success; a failure whose <c>Error.Code</c> is the broken rule name.</returns>
    public static Result<string> Validate(
        SlotDeclaration slot,
        string value,
        string messageKind,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(messageKind);

        // Empty/whitespace is a violation for every type: after the render-time Trim, an empty value is
        // indistinguishable from an absent slot, so "sent empty" must not equal "not sent".
        if (string.IsNullOrWhiteSpace(value))
        {
            return Rule(SlotValidationRuleNames.EmptyValue, slot);
        }

        return slot.Type switch
        {
            SlotType.String or SlotType.EntityRef => ValidateText(slot, value),
            SlotType.Enum => ValidateEnum(slot, value, messageKind, logger),
            SlotType.Number => ValidateNumber(slot, value),
            SlotType.Datetime => ValidateDatetime(value, slot),
            SlotType.Money => throw new InvalidOperationException(
                "money is forward-declared in v1 and cannot reach value validation (rejected in configuration, D3)."),
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot.Type, "Unknown slot type."),
        };
    }

    /// <summary>
    /// Validates a string/entity_ref value: NFC normalization → max length → control-character ban →
    /// optional Unicode-category allowlist (order fixed by SPEC-036 §4.2).
    /// </summary>
    private static Result<string> ValidateText(SlotDeclaration slot, string value)
    {
        // NFC first: normalization may lengthen the string, so the length check must run AFTER it.
        var normalized = value.Normalize(NormalizationForm.FormC);

        var maxLength = slot.Constraints.MaxLength ?? MessageTemplateLimits.MaxSlotValueLength;
        if (normalized.Length > maxLength)
        {
            return Rule(SlotValidationRuleNames.MaxLength, slot);
        }

        var allowedCategories = slot.Constraints.AllowedCategories is { Count: > 0 } categories
            ? categories
            : null;

        foreach (var symbol in normalized)
        {
            if (char.IsControl(symbol))
            {
                return Rule(SlotValidationRuleNames.ControlChars, slot);
            }

            if (allowedCategories is not null && !allowedCategories.Contains(CharUnicodeInfo.GetUnicodeCategory(symbol)))
            {
                return Rule(SlotValidationRuleNames.CharCategory, slot);
            }
        }

        return Result<string>.Success(normalized);
    }

    /// <summary>
    /// Validates an enum value: exact Ordinal membership in the declared set; a mismatch is logged as a
    /// security event (TPL-013).
    /// </summary>
    private static Result<string> ValidateEnum(
        SlotDeclaration slot,
        string value,
        string messageKind,
        ILogger? logger)
    {
        var values = slot.Constraints.EnumValues ?? [];
        foreach (var allowed in values)
        {
            if (string.Equals(allowed, value, StringComparison.Ordinal))
            {
                return Result<string>.Success(value);
            }
        }

        // Security event: OWASP flags a discrete-set mismatch as a high security event. Log the slot and
        // kind only — never the raw value (core-rules §10).
        logger?.LogWarning(
            "Slot value validation rejected an enum value outside the declared set. Slot: {Slot}, kind: {Kind}.",
            slot.Name,
            messageKind);

        return Rule(SlotValidationRuleNames.NotInEnum, slot);
    }

    /// <summary>
    /// Validates a number: invariant parse without exponent, then optional range bounds.
    /// </summary>
    private static Result<string> ValidateNumber(SlotDeclaration slot, string value)
    {
        if (!decimal.TryParse(value, NumberParseStyles, CultureInfo.InvariantCulture, out var number))
        {
            return Rule(SlotValidationRuleNames.NotANumber, slot);
        }

        if ((slot.Constraints.NumberMin is decimal min && number < min)
            || (slot.Constraints.NumberMax is decimal max && number > max))
        {
            return Rule(SlotValidationRuleNames.OutOfRange, slot);
        }

        return Result<string>.Success(number.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Validates an ISO 8601 datetime with a mandatory offset; normalizes to the round-trip form.
    /// </summary>
    private static Result<string> ValidateDatetime(string value, SlotDeclaration slot)
    {
        if (!Iso8601OffsetSuffix().IsMatch(value.Trim())
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var moment))
        {
            return Rule(SlotValidationRuleNames.NotIso8601, slot);
        }

        return Result<string>.Success(moment.ToString("O", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Builds a violation result whose code is the broken rule and whose message names the slot.
    /// </summary>
    private static Result<string> Rule(string rule, SlotDeclaration slot) =>
        Result<string>.Failure(rule, $"Slot '{slot.Name}' failed rule '{rule}'.");
}
