// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;

namespace Veriqa.Core.Configuration;

/// <summary>
/// Domain of a setting's value — which values the setting admits AT ALL, whichever level states one
/// (SPEC-012 §10.6, §4.1). It is a boundary of the KEY, of the same kind as the levels that may
/// populate it and the dimensions it may be asked about, and it is applied in the single seam every
/// binding of the key passes through — so neither a level, nor a reader, nor a consumer carries a
/// check of its own (SPEC-012 §10.6 — one mechanism, no fork).
/// <para>
/// What a rejection COSTS is declared here as well (<see cref="OnRejected"/>). By default a value
/// outside the domain leaves its step of the fallback chain unset, exactly as an unstated value does:
/// the setting is answered by the next step of the same level, and only after the whole chain of the
/// level is exhausted does the resolution drop to the level below (SPEC-012 §10.6). That is the right
/// answer for a setting whose substitution is harmless — and the wrong one for a setting where being
/// silently served somebody else's value is worse than not starting, which is why the choice belongs
/// to the owner of the key rather than to the mechanism.
/// </para>
/// <para>
/// The predicate travels together with a human-readable description of the boundary and — where the
/// owner states one — with the ADDRESS a correct value is written at, because the fact of a rejection
/// is reported to the operator once per configuration SNAPSHOT and that report has to say both what
/// the value fell outside of and where to fix it. A predicate alone cannot be printed, and a boundary
/// alone leaves the operator looking for the field.
/// </para>
/// </summary>
/// <typeparam name="T">Type of the setting value.</typeparam>
public sealed class ConfigValueDomain<T>
{
    /// <summary>
    /// Predicate of the domain: whether a value belongs to it.
    /// </summary>
    private readonly Func<T, bool> _admits;

    /// <summary>
    /// Creates a domain whose rejection is a degradation: the step that stated a value outside it is
    /// left unset and the resolution goes on (<see cref="ConfigValueRejectionPolicy.SkipStep"/>), and
    /// the report of a rejection names the boundary without an address to fix it at.
    /// </summary>
    /// <param name="admits">Predicate: whether the value belongs to the domain.</param>
    /// <param name="boundary">
    /// Human-readable description of the boundary, as an operator reads it in a report (for a range —
    /// the range itself with its unit).
    /// </param>
    public ConfigValueDomain(Func<T, bool> admits, string boundary)
        : this(admits, boundary, ConfigValueRejectionPolicy.SkipStep, correctAt: null)
    {
    }

    /// <summary>
    /// Creates a domain stating where a correct value is written and what a rejection costs.
    /// </summary>
    /// <param name="admits">Predicate: whether the value belongs to the domain.</param>
    /// <param name="boundary">Human-readable description of the boundary (see the other constructor).</param>
    /// <param name="correctAt">
    /// Where a correct value is stated, as an operator reads it — the section and the field, not a
    /// path to one record of one owner: the same setting is stated at several levels, and the address
    /// of the record that broke it is named by the report itself.
    /// </param>
    /// <param name="onRejected">What the setting does about a value outside the domain.</param>
    public ConfigValueDomain(
        Func<T, bool> admits,
        string boundary,
        string correctAt,
        ConfigValueRejectionPolicy onRejected = ConfigValueRejectionPolicy.SkipStep)
        : this(admits, boundary, onRejected, correctAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correctAt);
    }

    /// <summary>
    /// Creates a domain of a setting's value.
    /// </summary>
    /// <param name="admits">Predicate: whether the value belongs to the domain.</param>
    /// <param name="boundary">Human-readable description of the boundary.</param>
    /// <param name="onRejected">What the setting does about a value outside the domain.</param>
    /// <param name="correctAt">Where a correct value is stated; null — the owner names no address.</param>
    private ConfigValueDomain(
        Func<T, bool> admits,
        string boundary,
        ConfigValueRejectionPolicy onRejected,
        string? correctAt)
    {
        ArgumentNullException.ThrowIfNull(admits);

        // A blank boundary is a defect of the DECLARATION and is refused at start, in the same row as
        // the checks of the key itself: the boundary is the reason a verdict carries, and a verdict
        // with no reason tells the operator that something was rejected and nothing else.
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);

        _admits = admits;
        Boundary = boundary;
        CorrectAt = correctAt;
        OnRejected = onRejected;
    }

    /// <summary>
    /// Description of the boundary for diagnostics — what the operator is told a rejected value fell
    /// outside of, and the RULE a verdict of rejection names. It never quotes a value of the setting,
    /// only its boundary.
    /// </summary>
    public string Boundary { get; }

    /// <summary>
    /// Where a correct value is stated (the section and the field); null — the owner of the key names
    /// no address, and the report says what was rejected without saying where to fix it.
    /// </summary>
    public string? CorrectAt { get; }

    /// <summary>
    /// What the setting does about a value outside the domain.
    /// </summary>
    public ConfigValueRejectionPolicy OnRejected { get; }

    /// <summary>
    /// Verdict on a value stated by a level: whether the setting admits it and, when it does not, the
    /// rule it broke. The reason travels WITH the answer rather than being looked up afterwards — a
    /// caller that has to fetch the reason from elsewhere is free to report a rejection without one,
    /// and a rejection without a reason is indistinguishable from a value nobody stated.
    /// </summary>
    /// <param name="value">Value stated by a level.</param>
    /// <param name="violatedRule">Rule the value broke; null when the setting admits it.</param>
    /// <returns><c>true</c> when the setting admits the value.</returns>
    public bool Admits(T value, [NotNullWhen(false)] out string? violatedRule)
    {
        if (_admits(value))
        {
            violatedRule = null;

            return true;
        }

        violatedRule = Boundary;

        return false;
    }
}
