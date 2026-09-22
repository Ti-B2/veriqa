// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// A point of the fallback chain: the dimension values a composite key is asked with on the current
/// step. A dimension that the step does not address is ABSENT from the set (rather than present with
/// a null or empty value).
/// <para>
/// The same type is the INPUT of a resolution for a composite key: the caller states what is being
/// asked, while <see cref="ResolutionContext"/> carries ownership — who wins. The two axes are
/// orthogonal, which is why dimensions are not folded into the context.
/// </para>
/// </summary>
/// <param name="Values">Dimension values by dimension name.</param>
public readonly record struct ConfigDimensionValues(IReadOnlyDictionary<string, string> Values)
{
    /// <summary>
    /// Empty set of dimension values — a key without dimensions (a degenerate chain of one step).
    /// </summary>
    public static ConfigDimensionValues None { get; } =
        new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Dimension values, never null: a default-constructed value behaves as <see cref="None"/>.
    /// </summary>
    private IReadOnlyDictionary<string, string> Declared => Values ?? None.Values;

    /// <summary>
    /// Number of declared dimension values.
    /// </summary>
    public int Count => Declared.Count;

    /// <summary>
    /// Builds a point of the chain out of pairs "dimension name → value" — the single place a consumer
    /// states WHAT it is asking about. It lives on the type of the point rather than at each consumer:
    /// three private copies of it (two in the sign-in window, one in the generated service pages) is
    /// exactly the multiplication of reading paths the single resolution contract exists to prevent
    /// (SPEC-012 CFG-231/CFG-235).
    /// <para>
    /// A value that is null is kept as an empty one rather than dropped: the caller asked about a
    /// dimension whose value it does not know (an unnamed channel), and the map lookup of that step
    /// simply never matches — the chain then answers from the step that addresses nothing, exactly as
    /// an unknown value does.
    /// </para>
    /// </summary>
    /// <param name="values">Pairs "dimension name → value".</param>
    /// <returns>Point of the chain.</returns>
    /// <exception cref="ArgumentException">A dimension name is blank, or is given twice.</exception>
    public static ConfigDimensionValues Of(params ReadOnlySpan<(string Dimension, string? Value)> values)
    {
        if (values.Length == 0)
        {
            return None;
        }

        var declared = new Dictionary<string, string>(values.Length, StringComparer.Ordinal);

        foreach (var (dimension, value) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dimension);

            if (!declared.TryAdd(dimension, value ?? string.Empty))
            {
                throw new ArgumentException(
                    $"Dimension '{dimension}' is given twice in one point of the fallback chain: which of the "
                    + "two values is being asked about would depend on the order of the arguments.",
                    nameof(values));
            }
        }

        return new ConfigDimensionValues(declared);
    }

    /// <summary>
    /// Tells whether the point carries a value of the dimension — that is, whether the current step
    /// addresses it at all.
    /// </summary>
    /// <param name="dimensionName">Name of the dimension.</param>
    /// <returns><c>true</c> when the point addresses the dimension.</returns>
    internal bool Addresses(string dimensionName) => Declared.ContainsKey(dimensionName);

    /// <summary>
    /// Value of the dimension at this point; an empty string when the point does not address it.
    /// </summary>
    /// <param name="dimensionName">Name of the dimension.</param>
    /// <returns>Value of the dimension, or an empty string.</returns>
    internal string ValueOf(string dimensionName) =>
        Declared.TryGetValue(dimensionName, out var value) ? value : string.Empty;

    /// <summary>
    /// Builds the point of the chain for one fallback step: the declared values narrowed down to the
    /// dimensions the step addresses.
    /// </summary>
    /// <param name="step">Dimensions addressed by the step.</param>
    /// <returns>Values of the addressed dimensions alone.</returns>
    internal ConfigDimensionValues Project(IReadOnlySet<string> step)
    {
        if (step.Count == 0)
        {
            return None;
        }

        var projected = new Dictionary<string, string>(step.Count, StringComparer.Ordinal);
        foreach (var dimension in step)
        {
            if (Declared.TryGetValue(dimension, out var value))
            {
                projected[dimension] = value;
            }
        }

        return new ConfigDimensionValues(projected);
    }

    /// <summary>
    /// Checks the call against the key's declaration: a value of a dimension the key does not declare
    /// (or any value at all for a key without dimensions) is a caller error, not something to ignore
    /// silently — otherwise the caller believes it asked a question that was never asked.
    /// </summary>
    /// <param name="keyName">Setting name (for the message).</param>
    /// <param name="dimensions">Dimensions declared by the key; null — the key is not composite.</param>
    /// <exception cref="ArgumentException">A dimension value was passed that the key does not declare.</exception>
    internal void ValidateAgainst(string keyName, ConfigKeyDimensions? dimensions)
    {
        if (Count == 0)
        {
            return;
        }

        if (dimensions is null)
        {
            throw new ArgumentException(
                $"Configuration key '{keyName}' declares no dimensions, but dimension values were passed to its resolution.",
                nameof(dimensions));
        }

        var declared = new HashSet<string>(dimensions.Names, StringComparer.Ordinal);
        foreach (var dimension in Declared.Keys)
        {
            if (!declared.Contains(dimension))
            {
                throw new ArgumentException(
                    $"Configuration key '{keyName}' was resolved with a value of dimension '{dimension}', which it does not declare.",
                    nameof(dimensions));
            }
        }
    }
}
