// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Assembly of a domain group's result: the owner of the group states, in code, which keys the
/// aggregate is made of and where each resolved value goes (SPEC-012 §10.6, CFG-231).
/// <para>
/// The delegate is the whole filling mechanism — no reflection, no compiled expressions, nothing
/// generated: the types are checked by the compiler at the point of declaration.
/// </para>
/// </summary>
/// <typeparam name="TResult">Type of the aggregate the group fills.</typeparam>
/// <param name="resolution">Reader of the group's keys inside the shared scope.</param>
/// <returns>Filled aggregate.</returns>
public delegate ValueTask<TResult> ConfigGroupAssembler<TResult>(ConfigGroupResolution resolution);

/// <summary>
/// Consistency rule of a domain group: it is applied to the ALREADY FILLED aggregate and answers
/// with the reason the combination of values is inconsistent, or <c>null</c> when it is not.
/// <para>
/// The rule sees the aggregate and nothing else, so it can only state what a single key's domain
/// cannot: a relation BETWEEN values of different keys. What one value alone admits is the domain of
/// its key (<see cref="ConfigValueDomain{T}"/>) and is applied a level at a time, long before a group
/// is assembled.
/// </para>
/// </summary>
/// <typeparam name="TResult">Type of the aggregate the group fills.</typeparam>
/// <param name="value">Filled aggregate.</param>
/// <returns>Reason of the inconsistency, or null.</returns>
public delegate string? ConfigGroupConsistencyRule<in TResult>(TResult value);

/// <summary>
/// A DOMAIN GROUP of setting keys — the second unit of resolution next to the single key (SPEC-012
/// §10.6, CFG-231/CFG-232). It states three things: the keys the aggregate is made of (named by the
/// assembler below, where the compiler checks them), the type of the result and the rule of
/// consistency between the resolved values.
/// <para>
/// One call of the resolver returns the filled aggregate. The group is built OVER the single-key
/// entry rather than beside it: every value still goes through
/// <see cref="IConfigurationResolver.ResolveAsync{T}(ConfigKey{T}, ConfigResolutionScope, ConfigDimensionValues, CancellationToken)"/>,
/// with the precedence, the narrowing, the gates and the domains applied exactly as they are for a
/// key resolved on its own — and the form of the configuration sections does not change at all.
/// </para>
/// <para>
/// The whole group is resolved inside ONE <see cref="ConfigResolutionScope"/>, so a record serving
/// several of its keys — the global section, the client entry, the <c>ui_config</c> record — is read
/// once for the group rather than once per key. A key belonging to two groups is allowed and costs
/// nothing extra for that reason.
/// </para>
/// <para>
/// The mechanism knows no key of the group by name and no field of the aggregate: it holds the two
/// delegates and the name, which is what keeps the group on the mechanism's side of the ownership
/// criterion (CFG-231).
/// </para>
/// <para>
/// <b>A declarative map of the aggregate is a form already considered and DEFERRED</b> — a builder
/// naming the key behind each member instead of a hand-written body, in the shape
/// <c>ConfigAggregate.For(...).Map(...).MapByDimension(...).Check(...).Build()</c>. It would be an
/// ADDITION to the assembler above, never a replacement of it, and the reason it does not exist is
/// measured rather than assumed: on the only group in the product such a map saves about three
/// lines, against a type of its own.
/// </para>
/// <para>
/// The FORM of that map is left open on purpose, because the aggregates being filled today are
/// immutable records: a setter delegate <c>(o, v) => o.Prop = v</c>, which the compiler checks, does
/// not compile against an init-only member, whereas a compiled expression can set one. Which of the
/// two a map would use is the first consumer's decision, made by how mutable ITS aggregate actually
/// is, and it is not fixed here. The map earns its keep when an aggregate it fills WHOLE appears, or
/// when there is more than one such aggregate (SPEC-012 §10.6, CFG-243).
/// </para>
/// </summary>
/// <typeparam name="TResult">Type of the aggregate the group fills.</typeparam>
public sealed class ConfigKeyGroup<TResult>
    where TResult : class
{
    /// <summary>
    /// Creates a domain group.
    /// </summary>
    private ConfigKeyGroup(
        string name,
        ConfigGroupAssembler<TResult> assemble,
        ConfigGroupConsistencyRule<TResult>? consistency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(assemble);

        Name = name;
        Assemble = assemble;
        Consistency = consistency;
    }

    /// <summary>
    /// Name of the group — for diagnostics and for the message of a failed consistency rule; it is
    /// not used for branching logic.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Filling of the aggregate, declared by the owner of the group.
    /// </summary>
    internal ConfigGroupAssembler<TResult> Assemble { get; }

    /// <summary>
    /// Consistency rule of the filled aggregate; <c>null</c> — the group declares none.
    /// </summary>
    internal ConfigGroupConsistencyRule<TResult>? Consistency { get; }

    /// <summary>
    /// Declares a domain group.
    /// </summary>
    /// <param name="name">Name of the group.</param>
    /// <param name="assemble">Filling of the aggregate — the keys of the group are named here.</param>
    /// <param name="consistency">
    /// Consistency rule applied after the filling; null — the values of the group constrain each other
    /// in no way.
    /// </param>
    /// <returns>Domain group.</returns>
    public static ConfigKeyGroup<TResult> Of(
        string name,
        ConfigGroupAssembler<TResult> assemble,
        ConfigGroupConsistencyRule<TResult>? consistency = null) =>
        new(name, assemble, consistency);
}
