// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// OPTIONAL catalog capability of a configuration level: enumerating everything the level holds, which
/// <see cref="IConfigRecordReader{TRecord}"/> deliberately cannot do — a reader answers a resolution
/// context and returns ONE record, while a snapshot report has to walk them all (SPEC-012 §10.6).
/// <para>
/// It is implemented by the owner of the level's record type, next to the reader of that type. The
/// capability is optional on purpose: a source that cannot enumerate its records, or does not want to
/// (an expensive query against a database on every configuration reload), simply does not implement it
/// and stays out of the walk — and the report NAMES what it did not walk rather than staying silent
/// about it.
/// </para>
/// <para>
/// The snapshot BOUNDARY belongs to the source as well: only the owner of a level knows what a reload
/// of its source looks like, so the catalog is asked to subscribe to it rather than being told what to
/// listen to. Every subscription drives the same walk of every catalog: the sources of a deployment
/// usually share one configuration root, so one edit wakes several of them and the report deduplicates
/// what that produces.
/// </para>
/// </summary>
public interface IConfigSnapshotCatalog
{
    /// <summary>
    /// Configuration level this catalog walks.
    /// </summary>
    ConfigLevel Level { get; }

    /// <summary>
    /// The catalog is the source of its level in THIS deployment. A catalog is registered by the owner
    /// of the level's record type, and that registration is made BEFORE a contour may substitute the
    /// store behind the level — so a catalog reading the application configuration can end up standing
    /// next to a level served from a database, walking a section nobody reads.
    /// <para>
    /// Answering <c>false</c> says exactly what registering no catalog at all says FOR THE WALK AND THE
    /// REPORT: the level stays out of the walk and the report NAMES it as such, instead of counting it
    /// as walked and found clean. The registration itself stands — the pair "setting + level" the
    /// catalog states is claimed all the same, and a second catalog on that pair is refused whichever
    /// way either of them answers here.
    /// The answer is a property of the composed deployment and does not change over its lifetime.
    /// </para>
    /// </summary>
    bool CanWalk { get; }

    /// <summary>
    /// Configuration section whose contents the catalog contributes — the section named in the report
    /// when the catalog cannot be walked. It is never a section WIDER than what is walked: a wider
    /// label would read as a claim about the levels nested in it.
    /// </summary>
    string SectionKey { get; }

    /// <summary>
    /// Wider section the level is read as part of, and therefore the section a failure of the read
    /// belongs to; null when the level's own section is what is read and the two coincide.
    /// </summary>
    string? ReadThroughSectionKey { get; }

    /// <summary>
    /// Names of the settings this catalog covers; empty when it walks records as a whole rather than
    /// one setting. It is what lets the report tell a level nobody walks from a level walked and found
    /// clean.
    /// </summary>
    IReadOnlyCollection<string> SettingKeys { get; }

    /// <summary>
    /// Subscribes to the snapshot boundary of the level's source: the action is invoked once per
    /// reload of that source.
    /// </summary>
    /// <param name="onSnapshot">Action to invoke on a new snapshot.</param>
    /// <returns>Subscription to dispose; null when the source announces no reload.</returns>
    IDisposable? Subscribe(Action onSnapshot);

    /// <summary>
    /// Walks the level and yields what the snapshot holds. An unreadable level throws — the report
    /// names it and goes on walking the others.
    /// </summary>
    /// <returns>Findings of this level.</returns>
    IEnumerable<ConfigSnapshotFinding> Walk();
}

/// <summary>
/// What ONE address of one record answered the walk: the value the level states there, or the failure
/// that kept that value from being read into the type of the setting.
/// <para>
/// The two are one type because they are one answer of one address, and the walk has to be able to
/// give the second: an address whose value cannot be read is neither a value the domain can judge nor
/// an address the level said nothing at, and dropping it would leave the report asserting the second
/// (<see cref="ConfigSnapshotFact.ValueUnreadable"/>). An address the level states NOTHING at is not
/// an answer at all and is simply not yielded.
/// </para>
/// </summary>
/// <typeparam name="T">Type of the setting value.</typeparam>
/// <param name="ConfigurationKey">Address of the value in the configuration.</param>
/// <param name="RecordLabel">
/// Identity of the RECORD that states the value, inside its level — the client id of an entry of the
/// clients array, the code of a <c>ui_config</c> record. It travels on into the finding of the report
/// (<see cref="ConfigSnapshotFinding.RecordLabel"/>), where it is both what an operator recognizes the
/// record by and what the resolution addresses it by; null only when the level has no records to tell
/// apart.
/// </param>
/// <param name="Value">Value stated there; the default of the type when the read failed.</param>
/// <param name="UnreadableReason">
/// Why the value could not be read; null when it was read. It is what the report prints as the reason
/// of the fact, so it never carries the text of the value — for a key whose value is a secret it is
/// reduced to the TYPE of the failure.
/// </param>
public readonly record struct ConfigWalkedValue<T>(
    string ConfigurationKey,
    string? RecordLabel,
    T? Value,
    string? UnreadableReason)
{
    /// <summary>
    /// The answer of an address the level states a readable value at.
    /// </summary>
    /// <param name="configurationKey">Address of the value in the configuration.</param>
    /// <param name="recordLabel">Identity of the record that states it.</param>
    /// <param name="value">Value stated there.</param>
    /// <returns>Answer of the address.</returns>
    public static ConfigWalkedValue<T> Stated(string configurationKey, string? recordLabel, T value) =>
        new(configurationKey, recordLabel, value, UnreadableReason: null);

    /// <summary>
    /// The answer of an address the level states a value at that cannot be read into the type of the
    /// setting.
    /// </summary>
    /// <param name="configurationKey">Address of the value in the configuration.</param>
    /// <param name="recordLabel">Identity of the record that states it.</param>
    /// <param name="reason">Why the read ended — never the value itself.</param>
    /// <returns>Answer of the address.</returns>
    public static ConfigWalkedValue<T> Unreadable(string configurationKey, string? recordLabel, string reason) =>
        new(configurationKey, recordLabel, default, reason);
}
