// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Veriqa.Core.Configuration;

/// <summary>
/// Read-only slice of the contour map "level → source" (SPEC-012 §10.6). It states three facts and
/// nothing else: which levels this deployment serves, which it does not have, and the name of the
/// source behind each served level.
/// <para>
/// The slice exists so that the CONTOUR can assert its own expectation about levels — the cloud knows
/// it is unusable without the tenant level, the mechanism cannot know that and must not pretend to:
/// which levels exist is a property of the deployment (SPEC-012 §10.1), not a defect. The mechanism
/// therefore prints nothing beyond the aggregated startup line of absent levels, and the check
/// belongs to the contour — one line in its own composition of services.
/// </para>
/// <para>
/// The slice is data, not behaviour: it carries no way back to the registry, and the registry itself
/// stays internal. The facts are captured when the slice is built, so a contour that asks before its
/// sources are registered gets the state as of that moment — no guarantee of "after full
/// registration" is given or implied.
/// </para>
/// </summary>
public sealed class ConfigSourceMapView
{
    /// <summary>
    /// Creates the slice.
    /// </summary>
    /// <param name="servedLevels">Levels served in this contour, in resolution precedence order.</param>
    /// <param name="absentLevels">Levels absent in this contour, in resolution precedence order.</param>
    /// <param name="sourceNames">Name of the source serving each served level.</param>
    internal ConfigSourceMapView(
        ImmutableArray<ConfigLevel> servedLevels,
        ImmutableArray<ConfigLevel> absentLevels,
        FrozenDictionary<ConfigLevel, string> sourceNames)
    {
        ServedLevels = servedLevels;
        AbsentLevels = absentLevels;
        SourceNames = sourceNames;
    }

    /// <summary>
    /// Levels served in this contour, in the order of resolution precedence (§10.3).
    /// </summary>
    public ImmutableArray<ConfigLevel> ServedLevels { get; }

    /// <summary>
    /// Levels absent in this contour, in the order of resolution precedence. A level declared by a key
    /// but served by no source lands here and throws nothing: an absent level is a line of the map,
    /// not an error (SPEC-012 §10.1).
    /// </summary>
    public ImmutableArray<ConfigLevel> AbsentLevels { get; }

    /// <summary>
    /// Name of the source serving each level of <see cref="ServedLevels"/>. A level outside that set
    /// is absent from the map.
    /// </summary>
    public FrozenDictionary<ConfigLevel, string> SourceNames { get; }
}
