// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Machine names of the slot-value validation rules (SPEC-036 §4.2). These are the final identifiers
/// surfaced in the details of a <c>slot_value_invalid</c> error as "slot_name: rule".
/// Constants, never magic strings.
/// </summary>
public static class SlotValidationRuleNames
{
    /// <summary>Value is empty/whitespace — indistinguishable from an absent slot after sanitize.</summary>
    public const string EmptyValue = "empty_value";

    /// <summary>Value exceeds the declared max length (checked after NFC normalization).</summary>
    public const string MaxLength = "max_length";

    /// <summary>Value contains control characters.</summary>
    public const string ControlChars = "control_chars";

    /// <summary>Value is not a member of the declared enum set.</summary>
    public const string NotInEnum = "not_in_enum";

    /// <summary>Value is not a valid number.</summary>
    public const string NotANumber = "not_a_number";

    /// <summary>Numeric value is out of the declared min/max range.</summary>
    public const string OutOfRange = "out_of_range";

    /// <summary>Value is not a valid ISO 8601 datetime with an offset.</summary>
    public const string NotIso8601 = "not_iso8601";

    /// <summary>Value contains characters outside the declared Unicode category allowlist.</summary>
    public const string CharCategory = "char_category";
}
