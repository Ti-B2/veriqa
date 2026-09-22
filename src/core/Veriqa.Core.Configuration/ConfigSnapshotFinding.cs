// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Kind of fact a walk of the configuration snapshot can find. Every kind is a property of the
/// CONFIGURATION rather than of a request, which is why they are reported once per snapshot and not
/// once per resolution.
/// </summary>
public enum ConfigSnapshotFact
{
    /// <summary>
    /// A level states a value the setting does not admit: the value is outside the domain declared by
    /// the key (<see cref="ConfigValueDomain{T}"/>).
    /// </summary>
    ValueRejected = 0,

    /// <summary>
    /// A record of the level did not bind and is therefore absent from the effective configuration —
    /// everything it states is missing, not just one setting.
    /// </summary>
    RecordNotBound = 1,

    /// <summary>
    /// A setting the walk covers at a level, for which that level's snapshot states NO value at all.
    /// It is the schema-to-snapshot direction of the comparison: the key is declared, the level is
    /// walked, and nothing is written under it.
    /// <para>
    /// It is a NORMAL state of a deployment rather than a defect — a setting nobody overrode resolves
    /// from the level below it or from its default — so it is stated at information level and never
    /// touches a record or a start. It is worth stating all the same, because it makes the COVERAGE of
    /// the walk visible: without it a setting the walk covers and finds nothing for is silent, and
    /// that silence is indistinguishable from a setting the walk does not cover at all. It says
    /// nothing about WHY the level states nothing — a setting nobody overrode and an address nobody
    /// writes to produce this same line.
    /// </para>
    /// </summary>
    SettingNotStated = 2,

    /// <summary>
    /// A level STATES a value at the address, and it cannot be read into the type of the setting: a
    /// text no converter takes, a section where a scalar belongs, a shape the type is not read from.
    /// <para>
    /// It is a fact of its own and not a shade of <see cref="ValueRejected"/>: a value the domain
    /// rejects was read and judged, while this one never reached the domain — no predicate can be
    /// asked about a value that does not exist in the type it would be asked in. It is not
    /// <see cref="SettingNotStated"/> either: that assertion would be false here — the level stated a
    /// value, and it is broken.
    /// </para>
    /// <para>
    /// The finding carries no VALUE: the read failed, so there is nothing of the setting's type to
    /// name, and the raw text the level holds could be a secret. What it carries instead is the
    /// reason the read ended, reduced to the TYPE of the failure for a key whose value is a secret.
    /// </para>
    /// </summary>
    ValueUnreadable = 3
}

/// <summary>
/// One fact of a configuration snapshot, in the form the report prints it (SPEC-012 §4.1).
/// It names WHAT was found, the setting and the level it belongs to, the address in the configuration,
/// and the boundary or reason behind it.
/// <para>
/// A finding deliberately carries no CONSEQUENCE of the fact for any consumer — neither the fate of a
/// client, nor what a user will see on a page. The walk knows the configuration and nothing else, and
/// a consequence computed from a state it cannot observe is an assertion about a fact it does not
/// have. That is a rule of the format, not a decision of one report.
/// </para>
/// </summary>
/// <param name="Fact">Kind of the fact.</param>
/// <param name="Level">Configuration level the fact belongs to.</param>
/// <param name="ConfigurationKey">Address of the value or of the record in the configuration.</param>
/// <param name="RecordLabel">
/// Identity of the RECORD the fact belongs to, inside its level — the client id of an entry of the
/// clients array, the code of a <c>ui_config</c> record. It is the sign an operator recognizes the
/// record by AND the identity the resolution addresses that record by, which is what lets a record be
/// left out of the effective configuration as a whole (SPEC-012 §4.1 CFG-246). A catalog states it
/// even where the address of the value already contains it: an address is a path in a file, and
/// nothing on the resolution path parses paths. Null when the level has no records to tell apart —
/// the global section of the core level.
/// </param>
/// <param name="SettingKey">Name of the setting; null for a fact about a record as a whole.</param>
/// <param name="Value">
/// Value stated there; null for a fact about a record as a whole, for one about a value that was
/// never stated (<see cref="ConfigSnapshotFact.SettingNotStated"/>) and for one about a value that
/// could not be read (<see cref="ConfigSnapshotFact.ValueUnreadable"/>) — there is no value of the
/// setting's type to name, and the text the level holds may be a secret.
/// </param>
/// <param name="Detail">
/// Rule the value broke (<see cref="ConfigSnapshotFact.ValueRejected"/>), the reason a record did
/// not bind (<see cref="ConfigSnapshotFact.RecordNotBound"/>), the reason a stated value could not
/// be read (<see cref="ConfigSnapshotFact.ValueUnreadable"/>) or what the level was looked at for
/// (<see cref="ConfigSnapshotFact.SettingNotStated"/>).
/// </param>
/// <param name="CorrectAt">
/// Where a correct value is stated, as the owner of the setting named it; null when the owner named
/// no address, or for a fact about a record as a whole. It is the second half of what an operator
/// needs: the rule says what was wrong, this says where to write the right thing.
/// </param>
public readonly record struct ConfigSnapshotFinding(
    ConfigSnapshotFact Fact,
    ConfigLevel Level,
    string ConfigurationKey,
    string? RecordLabel,
    string? SettingKey,
    string? Value,
    string Detail,
    string? CorrectAt = null);
