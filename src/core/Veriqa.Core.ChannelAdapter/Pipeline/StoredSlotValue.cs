// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;

using Veriqa.Core.TransactionEngine.MessageTemplates;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Reader of a caller slot value as the confirmation snapshot stores it. The snapshot keeps the
/// canonical textual form of the declared type (SPEC-039 C16), while a render takes typed values and
/// states the type-specific form itself — so every message rendered out of those values reads them
/// here: the subject of a confirmation and its outcome receipt alike. Two readings would part company
/// the moment the canonical form of one type changes.
/// </summary>
internal static class StoredSlotValue
{
    /// <summary>
    /// Reads a stored slot value into the value the render takes: the moment behind a round-trip
    /// datetime, the number behind an invariant decimal, the text itself for every other type.
    /// </summary>
    /// <remarks>
    /// The exact inverse of the normalization the value passed on the way in, and nothing else: no
    /// form is chosen here, so this is not a second normalizer of the values (SPEC-039 C16). A value
    /// that does not read back — a snapshot written before the declaration of its slot changed type,
    /// or a receipt contract declaring the slot with another type than the contract the value was
    /// accepted by — stays the text it is, and the render shows it as text instead of failing over a
    /// formatting question.
    /// </remarks>
    /// <param name="slot">Declaration of the slot the value is rendered by.</param>
    /// <param name="value">Value as the snapshot stores it.</param>
    /// <returns>The typed value, or the original text.</returns>
    public static object Read(SlotDeclaration slot, string value) => slot.Type switch
    {
        SlotType.Datetime => DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var moment)
            ? moment
            : value,
        SlotType.Number => decimal.TryParse(
            value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var number)
            ? number
            : value,
        _ => value,
    };
}
