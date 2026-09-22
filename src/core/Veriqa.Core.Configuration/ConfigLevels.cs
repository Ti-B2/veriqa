// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Builders of the level set a key declares. The set is data on the key's declaration — the levels
/// that MAY populate it — and its order means nothing: the order of application is
/// <see cref="ConfigLevel"/> precedence.
/// </summary>
public static class ConfigLevels
{
    /// <summary>
    /// The core level alone — a global setting that no owner above the core populates.
    /// </summary>
    public static IReadOnlySet<ConfigLevel> CoreOnly { get; } = Of(ConfigLevel.Core);

    /// <summary>
    /// Builds a level set out of the listed levels.
    /// </summary>
    /// <param name="levels">Levels that may populate the key.</param>
    /// <returns>Immutable level set.</returns>
    /// <exception cref="ArgumentException">No level was listed.</exception>
    public static IReadOnlySet<ConfigLevel> Of(params ConfigLevel[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (levels.Length == 0)
        {
            throw new ArgumentException("A configuration key must declare at least one level.", nameof(levels));
        }

        return new HashSet<ConfigLevel>(levels);
    }
}
