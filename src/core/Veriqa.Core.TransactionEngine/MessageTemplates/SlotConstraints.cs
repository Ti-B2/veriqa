// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Immutable;
using System.Globalization;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Type-specific constraints of a slot declaration (SPEC-036 §4.1). Grouped as a value object because
/// the fields share one purpose — bounding a slot value — and travel together (core-rules §5). Only the
/// fields relevant to the slot's <see cref="SlotType"/> are populated; the rest are null.
/// <para>
/// The collection constraints defensively copy their assigned values into an <see cref="ImmutableArray{T}"/>
/// on init, so mutability cannot leak through the exposed <c>IReadOnly*</c> surface regardless of how the
/// caller built the collection (csharp-rules §2).
/// </para>
/// </summary>
public sealed record SlotConstraints
{
    /// <summary>
    /// Maximum value length for <see cref="SlotType.String"/>/<see cref="SlotType.EntityRef"/>
    /// (mandatory, ≤ <see cref="MessageTemplateLimits.MaxSlotValueLength"/>).
    /// </summary>
    public int? MaxLength { get; init; }

    private readonly IReadOnlyList<UnicodeCategory>? _allowedCategories;

    /// <summary>
    /// Optional allowlist of Unicode categories for a string value (TPL-012, OWASP free-form text).
    /// Null — categories are not restricted (only the control-character ban applies).
    /// </summary>
    public IReadOnlyList<UnicodeCategory>? AllowedCategories
    {
        get => _allowedCategories;
        init => _allowedCategories = value is null ? null : ImmutableArray.CreateRange(value);
    }

    private readonly IReadOnlyList<string>? _enumValues;

    /// <summary>
    /// The allowed discrete set for <see cref="SlotType.Enum"/> (mandatory, non-empty). Compared
    /// with <see cref="StringComparison.Ordinal"/> (TPL-013).
    /// </summary>
    public IReadOnlyList<string>? EnumValues
    {
        get => _enumValues;
        init => _enumValues = value is null ? null : ImmutableArray.CreateRange(value);
    }

    /// <summary>
    /// Optional lower bound for <see cref="SlotType.Number"/> (TPL-014).
    /// </summary>
    public decimal? NumberMin { get; init; }

    /// <summary>
    /// Optional upper bound for <see cref="SlotType.Number"/> (TPL-014).
    /// </summary>
    public decimal? NumberMax { get; init; }

    private readonly IReadOnlyList<string>? _moneyCurrencies;

    /// <summary>
    /// The ISO 4217 currency allowlist for <see cref="SlotType.Money"/> (mandatory). Reserved with the
    /// forward-declared money type (TPL-015); a money slot is rejected in configuration v1 (D3).
    /// </summary>
    public IReadOnlyList<string>? MoneyCurrencies
    {
        get => _moneyCurrencies;
        init => _moneyCurrencies = value is null ? null : ImmutableArray.CreateRange(value);
    }

    /// <summary>
    /// Empty constraints (used by <see cref="SlotType.Datetime"/>, which has none).
    /// </summary>
    public static SlotConstraints None { get; } = new();
}
