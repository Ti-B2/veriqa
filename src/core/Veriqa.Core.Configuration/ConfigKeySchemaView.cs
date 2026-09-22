// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Immutable;

namespace Veriqa.Core.Configuration;

/// <summary>
/// One key as the schema of declared keys knows it (SPEC-012 §10.6): its boundaries and nothing else.
/// What an INSTALLATION put into the setting is not part of it — a schema states what the owner of the
/// key declared, and a resolved value is never declared by anyone.
/// <para>
/// The answer the owner declared for the CORE level is therefore here
/// (<see cref="DeclaredDefault"/>): the owner did declare it, next to the levels, the addresses, the
/// domain and the secrecy, and a schema that dropped it would be describing a key its own owner
/// describes more fully. It is carried as TEXT for the same reason the domain is — a declaration is
/// read, not evaluated — and a key whose value is a secret carries the fact of the answer without its
/// text.
/// </para>
/// <para>
/// The predicate of the domain and the combining functions of the key are absent for the same
/// reason: a declaration is read, not evaluated. What is left of the domain is the text of its
/// boundary — the thing an operator is told a rejected value fell outside of.
/// </para>
/// </summary>
/// <param name="Name">Setting name.</param>
/// <param name="Kind">Level-combining semantics of the key.</param>
/// <param name="Levels">Levels that may populate the key, in resolution precedence order.</param>
/// <param name="GatedLevels">
/// Levels whose override requires a gate, in precedence order; empty — every declared level overrides
/// freely.
/// </param>
/// <param name="Dimensions">
/// Names of the key's dimensions in order of decreasing specificity; empty — the key is not composite
/// (a degenerate chain of one step).
/// </param>
/// <param name="CachePolicy">
/// Caching policy of the key; a key that asked for none carries <see cref="ConfigCachePolicy.None"/>
/// rather than an absence — "not cached" is an answer.
/// </param>
/// <param name="Domain">Boundary text the key admits its values within; null — the key declares no domain.</param>
/// <param name="OnRejected">What the key does about a value outside its domain.</param>
/// <param name="IsSecret">The value of the key is a secret.</param>
/// <param name="DeclaredDefault">
/// What the key declares its CORE level answers where the record of that level states nothing; null —
/// the owner declared no such answer. It is a type of its own rather than a text so that a declared
/// answer whose value IS null stays distinguishable from no declared answer at all
/// (<see cref="ConfigDeclaredDefault"/>).
/// </param>
public sealed record ConfigKeyView(
    string Name,
    ConfigKeyKind Kind,
    ImmutableArray<ConfigLevel> Levels,
    ImmutableArray<ConfigLevel> GatedLevels,
    ImmutableArray<string> Dimensions,
    ConfigCachePolicy CachePolicy,
    string? Domain,
    ConfigValueRejectionPolicy OnRejected,
    bool IsSecret,
    ConfigDeclaredDefault? DeclaredDefault);

/// <summary>
/// Read-only slice of the SCHEMA OF DECLARED KEYS (SPEC-012 §10.6): every key the deployment
/// declared, with the boundaries it declared it within.
/// <para>
/// This is a different slice from the contour map <see cref="ConfigSourceMapView"/>: that one answers
/// "which source serves which level", this one answers "what does a key declare". Neither repeats a
/// fact of the other, and they are deliberately not merged into one type.
/// </para>
/// <para>
/// The slice exists so that checks over the schema AS A WHOLE become possible outside the mechanism —
/// a contour asserting an invariant over every key of a family, an operator printing what the
/// deployment actually declared. It is data and carries no way back to the registry; the registry
/// itself stays internal.
/// </para>
/// <para>
/// The facts are captured when the slice is built, so a caller that asks before the keys are
/// registered gets the state as of that moment — no guarantee of "after full registration" is given
/// or implied.
/// </para>
/// </summary>
public sealed class ConfigKeySchemaView
{
    /// <summary>
    /// Creates the slice of the schema.
    /// </summary>
    /// <param name="keys">Declared keys, ordered by setting name.</param>
    internal ConfigKeySchemaView(ImmutableArray<ConfigKeyView> keys) => Keys = keys;

    /// <summary>
    /// Declared keys, ordered by setting name so that two prints of the same deployment read the same.
    /// </summary>
    public ImmutableArray<ConfigKeyView> Keys { get; }
}
