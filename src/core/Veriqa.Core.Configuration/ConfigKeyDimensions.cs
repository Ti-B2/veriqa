// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Dimensions of a composite setting key (SPEC-012 §10.6). Both the set of dimensions and the order
/// of the fallback steps are declared by the OWNER of the key; the mechanism knows neither and
/// hardcodes neither — otherwise every multi-dimensional setting grows a handwritten hierarchy of
/// its own.
/// <para>
/// The chain is LINEAR and finite: the steps are tried top to bottom against the record read ONCE
/// per resolution, and enumerating subsets is forbidden. Dimensions are orthogonal to levels: a
/// level answers "whose value wins", a dimension answers "what is being asked", and the level
/// dominates — specificity is resolved INSIDE a level.
/// </para>
/// </summary>
public sealed record ConfigKeyDimensions
{
    /// <summary>
    /// Dimension names in order of decreasing specificity (for example: channel, surface).
    /// </summary>
    public required IReadOnlyList<string> Names { get; init; }

    /// <summary>
    /// Fallback steps: each is a subset of <see cref="Names"/> applied as a whole. The last step
    /// (usually empty) is the "for everything else" value.
    /// </summary>
    public required IReadOnlyList<IReadOnlySet<string>> Fallback { get; init; }

    /// <summary>
    /// Checks the declaration: the dimensions are named once each, and every step of the chain
    /// addresses declared dimensions only. A step naming a dimension outside <see cref="Names"/> would
    /// never be reachable, and a name given twice makes the nesting of the level's maps ambiguous — the
    /// reading helper is told which dimension a map is cut by BY NAME. Both are declaration errors and
    /// stop startup rather than silently never matching.
    /// </summary>
    /// <param name="keyName">Setting name (for the message).</param>
    /// <exception cref="InvalidOperationException">A dimension is named twice, or a step names an undeclared dimension.</exception>
    internal void Validate(string keyName)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dimension in Names)
        {
            if (!declared.Add(dimension))
            {
                throw new InvalidOperationException(
                    $"Configuration key '{keyName}' declares dimension '{dimension}' twice: which of the two " +
                    "the level's map is cut by would be undecidable.");
            }
        }

        foreach (var step in Fallback)
        {
            foreach (var dimension in step)
            {
                if (!declared.Contains(dimension))
                {
                    throw new InvalidOperationException(
                        $"Configuration key '{keyName}' declares a fallback step over dimension '{dimension}', " +
                        "which is not among its declared dimensions.");
                }
            }
        }
    }
}
