// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

namespace Veriqa.Core.Configuration;

/// <summary>
/// Setting value specified at a single configuration level (see <see cref="ConfigLevel"/>).
/// A layer provider returns <see cref="None"/> when the setting is not specified at its level —
/// such a level is skipped in precedence (SPEC-012 §10.3, "first specified").
/// </summary>
/// <typeparam name="T">Type of the setting value.</typeparam>
public readonly struct LayerValue<T>
{
    /// <summary>
    /// Creates a specified level value.
    /// </summary>
    /// <param name="value">Setting value at this level.</param>
    /// <param name="allowLowerOverride">
    /// Gate (SPEC-012 §10.3): the level allows lower levels to relax a protective setting.
    /// Ignored for non-protective settings.
    /// </param>
    /// <param name="declaresGate">
    /// The level stated the gate itself; false — it merely carries the default.
    /// </param>
    /// <param name="addressedDimensions">
    /// Dimensions the step of the fallback chain that stated the value addressed; null — none are
    /// known yet, and the value behaves as one stated by a step addressing nothing.
    /// </param>
    private LayerValue(
        T value,
        bool allowLowerOverride,
        bool declaresGate,
        IReadOnlySet<string>? addressedDimensions)
    {
        Value = value;
        HasValue = true;
        AllowLowerOverride = allowLowerOverride;
        DeclaresGate = declaresGate;
        _addressedDimensions = addressedDimensions;
    }

    /// <summary>
    /// Dimensions the answering step addressed; null until the chain stamps them.
    /// </summary>
    private readonly IReadOnlySet<string>? _addressedDimensions;

    /// <summary>
    /// Names of the DIMENSIONS the step of the fallback chain that stated this value addressed — the
    /// step's own subset of the key's dimensions, narrowed to the ones the caller actually asked
    /// about (SPEC-012 §10.6). Empty for a step that addresses none and for a level that states
    /// nothing.
    /// <para>
    /// It travels on the layer value rather than being recomputed by the caller because the answer is
    /// only known INSIDE the chain — and because the level's answer is cached whole, so a cached hit
    /// has to carry the same fact as the read that filled it.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> AddressedDimensions => _addressedDimensions ?? FrozenSet<string>.Empty;

    /// <summary>
    /// Setting value (meaningful only when <see cref="HasValue"/> = true).
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Indicates that the setting is specified at this level.
    /// </summary>
    public bool HasValue { get; }

    /// <summary>
    /// Gate flag of SPEC-012 §10.3: the level explicitly allows lower levels to relax
    /// a protective value. Default false — downwards only stricter.
    /// </summary>
    public bool AllowLowerOverride { get; }

    /// <summary>
    /// The level stated a gate of its own, rather than merely carrying the default `false` of
    /// <see cref="Set(T)"/>. Distinguishes "this level closes the gate" from "this level has no
    /// opinion about it": without the distinction any level that supplies a value silently revokes
    /// a gate opened below it — the gate belongs to the level that declared it (SPEC-012 §10.3).
    /// </summary>
    public bool DeclaresGate { get; }

    /// <summary>
    /// The level does not specify a value for the setting (skipped in precedence).
    /// </summary>
    public static LayerValue<T> None => default;

    /// <summary>
    /// Creates a specified level value without a gate of its own (default).
    /// </summary>
    /// <param name="value">Setting value.</param>
    /// <returns>Specified level value.</returns>
    public static LayerValue<T> Set(T value) =>
        new(value, allowLowerOverride: false, declaresGate: false, addressedDimensions: null);

    /// <summary>
    /// Creates a specified level value with an explicit gate flag (SPEC-012 §10.3).
    /// </summary>
    /// <param name="value">Setting value.</param>
    /// <param name="allowLowerOverride">Allow lower levels to relax the protective value.</param>
    /// <returns>Specified level value.</returns>
    public static LayerValue<T> Set(T value, bool allowLowerOverride) =>
        new(value, allowLowerOverride, declaresGate: true, addressedDimensions: null);

    /// <summary>
    /// The same layer value stamped with the dimensions the answering step of the fallback chain
    /// addressed. A level stating nothing is returned unchanged: there was no answering step to
    /// name, and stamping one would make the absence of a value carry an address.
    /// </summary>
    /// <param name="addressedDimensions">Dimensions the answering step addressed.</param>
    /// <returns>The layer value carrying the dimensions of its step.</returns>
    internal LayerValue<T> AtStep(IReadOnlySet<string> addressedDimensions) =>
        HasValue
            ? new LayerValue<T>(Value!, AllowLowerOverride, DeclaresGate, addressedDimensions)
            : this;
}
