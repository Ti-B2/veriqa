// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// The normalized, frozen set of validated caller values for a transaction (SPEC-036 §4.4(c)). Immutable
/// after creation — caller values are append-only and never change once the transaction is created.
/// </summary>
public sealed class ValidatedCallerValues
{
    /// <summary>
    /// An empty validated set (no caller values).
    /// </summary>
    public static ValidatedCallerValues Empty { get; } =
        new(FrozenDictionary<string, string>.Empty);

    private readonly FrozenDictionary<string, string> _values;

    /// <summary>
    /// Creates the validated set from normalized values.
    /// </summary>
    /// <param name="values">Slot name → normalized value.</param>
    public ValidatedCallerValues(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Validated slot values (normalized), keyed by slot name.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;
}
