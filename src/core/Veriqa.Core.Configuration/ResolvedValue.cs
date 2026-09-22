// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

namespace Veriqa.Core.Configuration;

/// <summary>
/// The effective value of a setting together with the level it was determined at (SPEC-012 §10.6).
/// <para>
/// For composite semantics (<see cref="ConfigKeyKind.Set"/>, <see cref="ConfigKeyKind.ProtectiveCeiling"/>,
/// <see cref="ConfigKeyKind.GatedValue"/>) the level reported is the MOST SPECIFIC one that influenced
/// the outcome. <c>null</c> — no level defined the setting at all, and <see cref="Value"/> is
/// <c>default</c>.
/// </para>
/// <para>
/// A consumer that does not need the level reads <see cref="Value"/>: the contract does not split in
/// two because of it.
/// </para>
/// </summary>
/// <typeparam name="T">Type of the setting value.</typeparam>
/// <param name="Value">Effective value of the setting.</param>
/// <param name="SourceLevel">Level the value was determined at; null — no level defined it.</param>
public readonly record struct ResolvedValue<T>(T Value, ConfigLevel? SourceLevel)
{
    /// <summary>
    /// Dimensions the answering step addressed; null when nothing stamped them.
    /// </summary>
    private readonly IReadOnlySet<string>? _addressedDimensions;

    /// <summary>
    /// Names of the DIMENSIONS the answering step of the fallback chain addressed — the step's own
    /// subset of the key's dimensions, narrowed to the ones the caller actually asked about. An empty
    /// set means the step that answered addressed none of them: the value stands "for everything
    /// else" at its level.
    /// <para>
    /// It is a second FACT about the same answer, next to <see cref="SourceLevel"/>: the level says
    /// WHO stated the value, and this says HOW SPECIFICALLY it was addressed. A caller that has to
    /// know whether it got the value declared for exactly what it asked about — or a coarser one
    /// standing in for it — reads it off the result it already holds, without resolving a second
    /// time.
    /// </para>
    /// <para>
    /// The two facts never disagree: a resolution that no level answered reports no level and an
    /// empty set, and a cached answer carries the set the read that filled the entry produced.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> AddressedDimensions
    {
        get => _addressedDimensions ?? FrozenSet<string>.Empty;
        init => _addressedDimensions = value;
    }
}
