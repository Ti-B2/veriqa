// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Outcome of resolving a domain group (SPEC-012 §10.6): the filled aggregate together with the
/// verdict of the group's consistency rule.
/// <para>
/// The aggregate is present either way — the filling itself succeeded, and every value in it went
/// through the ordinary resolution. What the rule decides is whether the COMBINATION of those values
/// makes sense, and the answer belongs to the caller: a presentational group renders anyway and
/// reports the reason, while a group whose inconsistency makes the work impossible refuses it. The
/// mechanism does not choose for either of them.
/// </para>
/// <para>
/// A failing rule stays inside its group: the resolution of keys outside it — including a key this
/// group happens to share — is untouched.
/// </para>
/// </summary>
/// <typeparam name="TResult">Type of the aggregate the group fills.</typeparam>
/// <param name="Value">Filled aggregate.</param>
/// <param name="Violation">
/// Reason the combination of resolved values is inconsistent; <c>null</c> — the group is consistent
/// (or declared no rule).
/// </param>
public readonly record struct ResolvedGroup<TResult>(TResult Value, string? Violation)
    where TResult : class
{
    /// <summary>
    /// The combination of the resolved values passed the group's consistency rule.
    /// </summary>
    public bool IsConsistent => Violation is null;
}
