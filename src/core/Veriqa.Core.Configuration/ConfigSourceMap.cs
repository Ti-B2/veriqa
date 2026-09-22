// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

namespace Veriqa.Core.Configuration;

/// <summary>
/// Map "level → source" of the contour (SPEC-012 §10.6). It is built once from the sources
/// registered by the contour and validated on the spot: everything that would otherwise make a level
/// silently stop working is a startup error here, not a value that turns out wrong in production.
/// <para>
/// The mechanism knows no contours: which levels exist and who serves them is data supplied by the
/// registration of the deployment, and the map only prints and checks it.
/// </para>
/// </summary>
internal sealed class ConfigSourceMap
{
    /// <summary>
    /// Source serving each level of the contour.
    /// </summary>
    private readonly IReadOnlyDictionary<ConfigLevel, IConfigSource> _sourcesByLevel;

    /// <summary>
    /// Builds and validates the map of the contour.
    /// </summary>
    /// <param name="sources">Sources registered by the contour.</param>
    /// <param name="bindings">Registry of the extraction bindings.</param>
    /// <exception cref="InvalidOperationException">
    /// Two sources declare the same level, or a binding addresses a level no source serves.
    /// </exception>
    public ConfigSourceMap(IEnumerable<IConfigSource> sources, ConfigBindingRegistry bindings)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(bindings);

        var map = new Dictionary<ConfigLevel, IConfigSource>();

        foreach (var source in sources)
        {
            foreach (var level in source.Levels)
            {
                if (map.TryGetValue(level, out var registered) && !ReferenceEquals(registered, source))
                {
                    // Two stores on one level would mean one of them never answers, silently — which is
                    // exactly the defect this rule exists to make impossible.
                    throw new InvalidOperationException(
                        $"Level {level} is served by two configuration sources at once: " +
                        $"'{registered.GetType().Name}' and '{source.GetType().Name}'. A second physical store " +
                        "belongs INSIDE a source (a second record reader under it), not next to it in DI.");
                }

                map[level] = source;
            }
        }

        _sourcesByLevel = map;

        ValidateBindings(bindings);
    }

    /// <summary>
    /// Levels served in this contour, in the order of resolution precedence.
    /// </summary>
    public IEnumerable<ConfigLevel> ServedLevels =>
        ConfigLevelPrecedence.Order.Where(_sourcesByLevel.ContainsKey);

    /// <summary>
    /// Levels absent from this contour — a line of the startup map, not a warning: which levels exist
    /// is a property of the deployment (CFG-202 — in self-hosted the tenant IS the core).
    /// </summary>
    public IEnumerable<ConfigLevel> AbsentLevels =>
        ConfigLevelPrecedence.Order.Where(level => !_sourcesByLevel.ContainsKey(level));

    /// <summary>
    /// Returns the source serving the level, or null when the contour has no such level.
    /// </summary>
    /// <param name="level">Level.</param>
    /// <returns>Source, or null.</returns>
    public IConfigSource? SourceOf(ConfigLevel level) =>
        _sourcesByLevel.GetValueOrDefault(level);

    /// <summary>
    /// Builds the read-only slice of this map for the contour (<see cref="ConfigSourceMapView"/>).
    /// The registry itself stays internal: what leaves the assembly is three facts, not the reachable
    /// sources behind them.
    /// </summary>
    /// <returns>Slice of the map as of this moment.</returns>
    public ConfigSourceMapView CreateView() =>
        new(
            [.. ServedLevels],
            [.. AbsentLevels],
            _sourcesByLevel.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Name));

    /// <summary>
    /// Checks that every registered binding addresses a level the contour actually serves: otherwise
    /// the binding would never be applied and the level would look empty without a single message.
    /// </summary>
    private void ValidateBindings(ConfigBindingRegistry bindings)
    {
        foreach (var (keyName, level, readerName) in bindings.RecordBindings())
        {
            if (!_sourcesByLevel.ContainsKey(level))
            {
                var registered = string.Join(
                    ", ",
                    _sourcesByLevel.Values.Distinct()
                        .Select(source => $"{source.GetType().Name} serves {{{string.Join(", ", source.Levels)}}}"));

                throw new InvalidOperationException(
                    $"Configuration key '{keyName}' is bound at level {level} through reader '{readerName}', " +
                    $"but no source of this contour serves that level ({registered}): the binding would never be applied.");
            }
        }
    }
}
