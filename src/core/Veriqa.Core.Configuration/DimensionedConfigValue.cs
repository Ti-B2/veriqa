// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Veriqa.Core.Configuration;

/// <summary>
/// Reading of a setting that declares DIMENSIONS (SPEC-012 §10.6, CFG-236): the value a level states
/// FOR a value of a dimension lives in a paired map, and — for a chain whose last step addresses no
/// dimension — the plain field next to it is the "for everything else" value of the same level.
/// <para>
/// One place for the two operations any dimension needs — the map lookup and the layer value of a
/// chain step — so the levels of such a key (the global section, the client entry, the
/// <c>ui_config</c> record) cannot answer "what is the value for this theme / for this channel"
/// differently, and no consumer grows a precedence chain of its own (SPEC-012 CFG-231/CFG-235). It
/// lives in the MECHANISM rather than at a consumer because it names no setting key, no options type
/// and no record type: it is told the names of the dimensions and handed the level's maps, and that
/// is all it knows (SPEC-012 §10.6 — the criterion of what belongs to the module).
/// </para>
/// <para>
/// <b>The helper carries no step order of its own.</b> The order of the fallback steps is declared by
/// the key alone (<see cref="ConfigKeyDimensions.Fallback"/>), and the resolver calls a binding once
/// per step with an ALREADY PROJECTED point. The dimension names the members below are given are the
/// names the level's MAP is cut by — a property of the level's record shape — and never the order of
/// the steps: a second carrier of the step order would be a second authority with no rule for
/// resolving a disagreement between them. Which step is being served is answered by the point alone.
/// </para>
/// <para>
/// A step over SEVERAL dimensions reads nested maps: the owner of the key narrows the level's values
/// one dimension at a time with <see cref="TryValueFor{TValue}"/> — whose value type is the inner map
/// or the inner record — and closes the walk with <c>Layer</c> over the innermost map. That is why
/// neither member knows the value type of the setting: the same two operations serve a chain over one
/// dimension and a chain over four, instead of a copy of the helper per key.
/// </para>
/// <para>
/// Whether a stated value is one the setting ADMITS is not asked here at all: that is the domain of
/// the key (<see cref="ConfigValueDomain{T}"/>), applied by the resolution mechanism to every binding.
/// </para>
/// </summary>
public static class DimensionedConfigValue
{
    /// <summary>
    /// Text value a level states for one value of the dimension, or null when it states none. The key
    /// is matched case-insensitively: the map comes from configuration written by hand, where the
    /// casing of a theme or a channel name is not the deployment's to get exactly right; a blank value
    /// counts as unstated.
    /// </summary>
    /// <param name="valuesByDimension">Values by dimension value stated by the level; null — none at all.</param>
    /// <param name="dimensionValue">Value of the dimension being asked about (a theme, a channel type).</param>
    /// <returns>Value stated for it, or null.</returns>
    public static string? TextFor(
        IEnumerable<KeyValuePair<string, string>>? valuesByDimension,
        string dimensionValue) =>
        TryLookup(valuesByDimension, dimensionValue, IsStatedText, out var found) ? found : null;

    /// <summary>
    /// Value a level states at the point of the chain for ONE dimension — the leaf value of a chain
    /// over a single dimension, or the NESTED map (or record) of a chain over several, which the owner
    /// of the key narrows further by the next dimension.
    /// <para>
    /// The point is the one handed to the binding by the resolver, so a dimension the current step does
    /// not address is simply absent from it and the answer is <c>false</c> — the step then behaves as
    /// the one that addresses nothing rather than failing.
    /// </para>
    /// </summary>
    /// <typeparam name="TValue">Type of what the level states per dimension value: a leaf value or an inner map.</typeparam>
    /// <param name="point">Point of the chain handed in by the resolver.</param>
    /// <param name="dimensionName">Name of the dimension to narrow by.</param>
    /// <param name="valuesByDimension">Values by dimension value stated by the level; null — none at all.</param>
    /// <param name="value">What the level states for the dimension value carried by the point.</param>
    /// <returns><c>true</c> when the level states something for it.</returns>
    public static bool TryValueFor<TValue>(
        ConfigDimensionValues point,
        string dimensionName,
        IEnumerable<KeyValuePair<string, TValue>>? valuesByDimension,
        [MaybeNullWhen(false)] out TValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimensionName);

        return TryLookup(
            valuesByDimension,
            point.ValueOf(dimensionName),
            isStated: null,
            out value);
    }

    /// <summary>
    /// Layer value of ONE step of the fallback chain for a TEXT setting: the step that addresses a
    /// dimension answers from the map, the step that addresses none answers from the plain field. A
    /// step that states nothing yields <see cref="LayerValue{T}.None"/>, so the chain moves on — and
    /// when no step of the level states anything, the level is skipped and the resolution drops to the
    /// level below.
    /// <para>
    /// A key whose chain has no "for everything else" step (a per-channel hint — there is no hint for
    /// "every channel") passes null as the plain value: the step simply never yields one.
    /// </para>
    /// </summary>
    /// <param name="point">Point of the chain handed in by the resolver.</param>
    /// <param name="dimensionNames">
    /// Names of the dimensions <paramref name="valuesByDimension"/> is cut by, innermost last (for a
    /// key with one dimension — the names the key declares). It carries no step order (see the type
    /// summary): the innermost of these names the POINT addresses is the one the map is looked up by,
    /// and a point addressing none of them is the step that answers from the plain field.
    /// </param>
    /// <param name="valuesByDimension">Values by dimension value stated by the level.</param>
    /// <param name="commonValue">Plain value stated by the level for every value of the dimension.</param>
    /// <returns>Layer value of the step.</returns>
    public static LayerValue<string?> Layer(
        ConfigDimensionValues point,
        IReadOnlyList<string> dimensionNames,
        IEnumerable<KeyValuePair<string, string>>? valuesByDimension,
        string? commonValue)
    {
        var value = InnermostAddressed(point, dimensionNames) is { } dimensionName
            ? TextFor(valuesByDimension, point.ValueOf(dimensionName))
            : commonValue;

        return IsStatedText(value) ? LayerValue<string?>.Set(value) : LayerValue<string?>.None;
    }

    /// <summary>
    /// Layer value of ONE step of the fallback chain for a setting whose value is not text (the QR
    /// pixel scale). A value that is present is STATED — for a value type there is no "blank" shape a
    /// half-finished edit leaves behind, so the only question the step answers is whether the level
    /// wrote something at this point of the chain. Whether the setting ADMITS what was written is a
    /// different question and belongs to the domain of the key
    /// (<see cref="ConfigValueDomain{T}"/>), which the resolution mechanism applies to every binding.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="point">Point of the chain handed in by the resolver.</param>
    /// <param name="dimensionNames">
    /// Names of the dimensions <paramref name="valuesByDimension"/> is cut by, innermost last; see the
    /// text overload for why this is not a step order.
    /// </param>
    /// <param name="valuesByDimension">Values by dimension value stated by the level.</param>
    /// <param name="commonValue">Plain value stated by the level; null — the level states none.</param>
    /// <returns>Layer value of the step.</returns>
    public static LayerValue<T> Layer<T>(
        ConfigDimensionValues point,
        IReadOnlyList<string> dimensionNames,
        IEnumerable<KeyValuePair<string, T>>? valuesByDimension,
        T? commonValue)
        where T : struct
    {
        if (InnermostAddressed(point, dimensionNames) is not { } dimensionName)
        {
            return commonValue is { } common
                ? LayerValue<T>.Set(common)
                : LayerValue<T>.None;
        }

        return TryValueFor(point, dimensionName, valuesByDimension, out var found)
            ? LayerValue<T>.Set(found)
            : LayerValue<T>.None;
    }

    /// <summary>
    /// Innermost dimension the current step addresses among the ones the map is cut by: the LAST of the
    /// given names the point carries a value for. That is the dimension the map handed to the step is
    /// keyed by — the outer ones have already been narrowed away by the owner of the key. A point
    /// carrying none of them is the step that addresses nothing, and the level answers it from its
    /// plain field.
    /// </summary>
    /// <param name="point">Point of the chain.</param>
    /// <param name="dimensionNames">Names the level's map is cut by, innermost last.</param>
    /// <returns>Name of the innermost addressed dimension, or null when the step addresses none.</returns>
    private static string? InnermostAddressed(ConfigDimensionValues point, IReadOnlyList<string> dimensionNames)
    {
        ArgumentNullException.ThrowIfNull(dimensionNames);

        for (var i = dimensionNames.Count - 1; i >= 0; i--)
        {
            if (point.Addresses(dimensionNames[i]))
            {
                return dimensionNames[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Looks the dimension value up in the level's map, accepting only an entry the caller counts as
    /// stated. A key of the map that is never asked for simply never matches.
    /// </summary>
    /// <typeparam name="T">Type of what the map holds.</typeparam>
    /// <param name="valuesByDimension">Values by dimension value stated by the level.</param>
    /// <param name="dimensionValue">Value of the dimension being asked about.</param>
    /// <param name="isStated">
    /// Tells whether a value read from the level counts as stated; null — every value present in the
    /// map counts, which is the case for a value type (there is no blank shape of one) and for an
    /// inner map (its own emptiness is answered by the step it serves).
    /// </param>
    /// <param name="found">Value found for the dimension value.</param>
    /// <returns><c>true</c> when the level states a usable value for it.</returns>
    private static bool TryLookup<T>(
        IEnumerable<KeyValuePair<string, T>>? valuesByDimension,
        string dimensionValue,
        Func<T, bool>? isStated,
        [MaybeNullWhen(false)] out T found)
    {
        found = default;

        if (valuesByDimension is null || string.IsNullOrWhiteSpace(dimensionValue))
        {
            return false;
        }

        foreach (var (statedFor, value) in valuesByDimension)
        {
            if (string.Equals(statedFor, dimensionValue, StringComparison.OrdinalIgnoreCase)
                && (isStated is null || isStated(value)))
            {
                found = value;

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tells whether a text value counts as stated: a blank text is the same as no value at all, so an
    /// emptied configuration field does not win the key over the level below it.
    /// </summary>
    /// <param name="value">Text value read from a level.</param>
    /// <returns><c>true</c> when the text carries something.</returns>
    private static bool IsStatedText(string? value) => !string.IsNullOrWhiteSpace(value);
}
