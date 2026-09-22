// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Canonical multi-level resolver of the effective setting value (SPEC-012 §10).
/// The ONLY precedence resolver in the core (anti-fork, SPEC-012 §10.6): the channel track consumes it too.
/// There must be no second precedence resolver.
/// </summary>
public interface IConfigurationResolver
{
    /// <summary>
    /// Computes the effective setting value per the precedence of SPEC-012 §10.3
    /// (user → per-request → ui_config → application → tenant → core), applying
    /// the narrowing rule and the protective logic of the same order, and reports the level the
    /// value was determined at (SPEC-012 §10.6).
    /// <para>
    /// This is the ONLY entry of resolution (SPEC-012 §10.6): a synchronous overload does not exist, because
    /// a level served by an external store is read asynchronously and a synchronous wrapper over it
    /// would be sync-over-async.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Setting value type.</typeparam>
    /// <param name="key">Declarative setting key.</param>
    /// <param name="context">Resolution context (level identifiers — who owns the value).</param>
    /// <param name="dimensions">
    /// Dimension values of a composite key — what is being asked;
    /// <see cref="ConfigDimensionValues.None"/> for a key without dimensions.
    /// </param>
    /// <param name="cancellationToken">Cancellation token; it is propagated, never swallowed.</param>
    /// <returns>Effective value together with the level it was determined at.</returns>
    ValueTask<ResolvedValue<T>> ResolveAsync<T>(
        ConfigKey<T> key,
        ResolutionContext context,
        ConfigDimensionValues dimensions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same resolution inside a scope the CALLER owns — which is what makes "a source is read once"
    /// hold over everything resolved in that scope instead of over a single resolution
    /// (<see cref="ConfigResolutionScope"/>). Assembling one sign-in page resolves fifteen keys served
    /// by the same <c>ui_config</c> record; in one scope that record is read once.
    /// <para>
    /// The ownership context is taken from the scope: it is already there, and a second one passed
    /// alongside would be a second source of truth about who owns the values.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Setting value type.</typeparam>
    /// <param name="key">Declarative setting key.</param>
    /// <param name="scope">Scope of resolution, carrying the context and the records already read.</param>
    /// <param name="dimensions">
    /// Dimension values of a composite key — what is being asked;
    /// <see cref="ConfigDimensionValues.None"/> for a key without dimensions.
    /// </param>
    /// <param name="cancellationToken">Cancellation token; it is propagated, never swallowed.</param>
    /// <returns>Effective value together with the level it was determined at.</returns>
    ValueTask<ResolvedValue<T>> ResolveAsync<T>(
        ConfigKey<T> key,
        ConfigResolutionScope scope,
        ConfigDimensionValues dimensions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a DOMAIN GROUP of keys (<see cref="ConfigKeyGroup{TResult}"/>) and returns the filled
    /// aggregate together with the verdict of the group's consistency rule (SPEC-012 §10.6,
    /// CFG-231/CFG-232). This is the second unit of resolution next to the single key: the caller
    /// states what it needs once, and the level-owned values of a whole domain object arrive filled in.
    /// <para>
    /// The group is resolved inside ONE scope created here, so a record serving several of its keys is
    /// read once for the aggregate rather than once per key.
    /// </para>
    /// </summary>
    /// <typeparam name="TResult">Type of the aggregate the group fills.</typeparam>
    /// <param name="group">Domain group.</param>
    /// <param name="context">Resolution context (level identifiers — who owns the values).</param>
    /// <param name="cancellationToken">Cancellation token; it is propagated, never swallowed.</param>
    /// <returns>Filled aggregate together with the verdict of the consistency rule.</returns>
    ValueTask<ResolvedGroup<TResult>> ResolveGroupAsync<TResult>(
        ConfigKeyGroup<TResult> group,
        ResolutionContext context,
        CancellationToken cancellationToken = default)
        where TResult : class;

    /// <summary>
    /// The same resolution of a domain group inside a scope the CALLER owns — for a caller that
    /// assembles several groups, or a group and single keys, over the same records.
    /// </summary>
    /// <typeparam name="TResult">Type of the aggregate the group fills.</typeparam>
    /// <param name="group">Domain group.</param>
    /// <param name="scope">Scope of resolution, carrying the context and the records already read.</param>
    /// <param name="cancellationToken">Cancellation token; it is propagated, never swallowed.</param>
    /// <returns>Filled aggregate together with the verdict of the consistency rule.</returns>
    ValueTask<ResolvedGroup<TResult>> ResolveGroupAsync<TResult>(
        ConfigKeyGroup<TResult> group,
        ConfigResolutionScope scope,
        CancellationToken cancellationToken = default)
        where TResult : class;
}
