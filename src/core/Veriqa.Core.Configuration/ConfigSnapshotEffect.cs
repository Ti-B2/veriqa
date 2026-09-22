// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// What the walk of the configuration SNAPSHOT leaves behind for the resolution path (SPEC-012 §4.1
/// CFG-246). The report is what enumerates the records of a level, so it is the only place that can
/// tell one record of a level from its neighbours — and throwing a record out of the effective
/// configuration is a decision about the SNAPSHOT, made once per walk, not a decision a single
/// resolution could make from the one record it happens to read.
/// <para>
/// It carries the two answers the resolution needs and nothing else:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="Covers"/> — whether the report reaches a pair "setting + level" at all. A level whose
/// source does not enumerate its records stays outside the walk, and CFG-246 does not cover it: a
/// value rejected there is dropped exactly as the default of CFG-240 drops it.
/// </description></item>
/// <item><description>
/// <see cref="IsRecordDiscarded"/> — whether the record a level would serve for a resolution context
/// was thrown out of the effective configuration by the last walk.
/// </description></item>
/// </list>
/// <para>
/// Both answers are published by the report and only read by the resolution. The coverage is fixed
/// when the container is built and is published once; the discarded records are republished by every
/// walk, as a whole set, so a snapshot that fixed a value takes its record back into the effective
/// configuration without anything having to remove an entry.
/// </para>
/// </summary>
internal sealed class ConfigSnapshotEffect
{
    /// <summary>
    /// Pairs "setting + level" the snapshot report walks. Empty until the report has started, which is
    /// the safe default: nothing is covered, and every rejection behaves as the default of CFG-240.
    /// </summary>
    private IReadOnlySet<(string SettingKey, ConfigLevel Level)> _covered =
        new HashSet<(string, ConfigLevel)>();

    /// <summary>
    /// Records the last walk threw out of the effective configuration.
    /// </summary>
    private IReadOnlySet<ConfigDiscardedRecord> _discarded = new HashSet<ConfigDiscardedRecord>();

    /// <summary>
    /// Publishes the reach of the report — the pairs "setting + level" a walkable catalog covers.
    /// Called once, before the first walk: which catalogs the deployment registered is fixed when the
    /// container is built.
    /// </summary>
    /// <param name="covered">Pairs the report walks.</param>
    public void PublishCoverage(IReadOnlySet<(string SettingKey, ConfigLevel Level)> covered) =>
        Volatile.Write(ref _covered, covered);

    /// <summary>
    /// Publishes the records the walk threw out of the effective configuration. The set REPLACES the
    /// previous one: what a walk did not find is no longer discarded, which is how a corrected value
    /// brings its record back. Whether a record the walk did not find is a record that was FIXED is
    /// decided by the report before it publishes — a level the walk could not read fixed nothing.
    /// </summary>
    /// <param name="discarded">Records thrown out by the walk.</param>
    public void PublishDiscardedRecords(IReadOnlySet<ConfigDiscardedRecord> discarded) =>
        Volatile.Write(ref _discarded, discarded);

    /// <summary>
    /// Whether the snapshot report walks this pair "setting + level".
    /// </summary>
    /// <param name="settingKey">Setting name.</param>
    /// <param name="level">Level.</param>
    /// <returns><c>true</c> when the pair is inside the walk.</returns>
    public bool Covers(string settingKey, ConfigLevel level) =>
        Volatile.Read(ref _covered).Contains((settingKey, level));

    /// <summary>
    /// Whether the record this level would serve for the given resolution context was thrown out of
    /// the effective configuration. Every setting of a discarded record answers as an unstated one,
    /// which is what "absent from the effective configuration" means on the resolution path — the same
    /// outcome a record that did not bind already has.
    /// </summary>
    /// <param name="level">Level being read.</param>
    /// <param name="context">Resolution context.</param>
    /// <returns><c>true</c> when the record is out of the effective configuration.</returns>
    public bool IsRecordDiscarded(ConfigLevel level, ResolutionContext context)
    {
        var discarded = Volatile.Read(ref _discarded);

        // The normal state of a deployment: nothing was thrown out, and no record has to be addressed
        // to find that out.
        if (discarded.Count == 0)
        {
            return false;
        }

        return RecordIdOf(level, context) is { } recordId
            && discarded.Contains(new ConfigDiscardedRecord(level, recordId));
    }

    /// <summary>
    /// Identity of the record a level serves for a resolution context — the part of the context that
    /// ADDRESSES the record at that level, which is the same identity the walk of the level names its
    /// records by (the client id of an entry of the clients array, the code of a <c>ui_config</c>
    /// record).
    /// <para>
    /// The core level and the per-request level answer null: neither has a record to throw out — the
    /// core level is a closure over the global options, and CFG-246 answers a rejection there with the
    /// last valid value rather than with a discard.
    /// </para>
    /// </summary>
    /// <param name="level">Level being read.</param>
    /// <param name="context">Resolution context.</param>
    /// <returns>Identity of the record, or null when the level has none.</returns>
    public static string? RecordIdOf(ConfigLevel level, ResolutionContext context) => level switch
    {
        ConfigLevel.Application => Named(context.ApplicationId),
        ConfigLevel.UiConfig => Named(context.UiConfigSelector),
        ConfigLevel.Tenant => Named(context.TenantId),
        ConfigLevel.UserOverride => Named(context.UserOverride?.UserId),
        _ => null
    };

    /// <summary>
    /// Keeps an identity only while it names something: a blank identifier addresses no record.
    /// </summary>
    /// <param name="identifier">Identifier taken from the context.</param>
    /// <returns>The identifier, or null when it names nothing.</returns>
    private static string? Named(string? identifier) =>
        string.IsNullOrWhiteSpace(identifier) ? null : identifier;
}

/// <summary>
/// One record thrown out of the effective configuration by a walk of the snapshot (CFG-246).
/// </summary>
/// <param name="Level">Level the record belongs to.</param>
/// <param name="RecordId">
/// Identity of the record inside its level, as the resolution addresses it
/// (<see cref="ConfigSnapshotEffect.RecordIdOf"/>).
/// </param>
internal readonly record struct ConfigDiscardedRecord(ConfigLevel Level, string RecordId);
