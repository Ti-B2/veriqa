// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veriqa.Core.Configuration;

/// <summary>
/// Report on what a CONFIGURATION SNAPSHOT holds that the mechanism cannot use (SPEC-012 §4.1
/// CFG-240, §8.2 CFG-152/CFG-154): a value outside the domain its setting declares, a value that
/// cannot be read into the type of its setting at all, and a record that did not bind and is
/// therefore absent from the effective configuration as a whole. All of them are
/// properties of the configuration, not of a request — the resolution path drops such a value in
/// silence, because a rejection repeated on every sign-in says nothing new after the first — so they
/// are named here, once per snapshot: at start, and once per reload of a source that carries one.
/// <para>
/// The same walk carries the comparison of the snapshot with the SCHEMA of declared keys, in both
/// directions: a pair "setting + level" the walk covers that the schema does not declare
/// (<see cref="ReportSettingsOutsideTheSchema"/>), and a declared setting the walked level states no
/// value for (<see cref="ConfigSnapshotFact.SettingNotStated"/>). What is compared is the pair itself
/// and the fact that the level states a value at all, which is what catches a declaration and a walk
/// that drifted apart. The ADDRESS a key declares for a level enters NEITHER side — the walk reads the
/// records of the level and not the address it is declared at — so a typo inside that address is not
/// what this catches.
/// </para>
/// <para>
/// Which levels are walked is decided by the catalogs registered for them
/// (<see cref="IConfigSnapshotCatalog"/>), and a level whose source cannot enumerate its records —
/// because none is registered for it, or because the one registered is not the source this contour
/// serves the level from (<see cref="IConfigSnapshotCatalog.CanWalk"/>) — is simply not among them.
/// Such a level is NAMED at start rather than passed over quietly: silence about a level is
/// indistinguishable from a level that was walked and found clean.
/// </para>
/// <para>
/// By DEFAULT nothing found here fails the start (CFG-152, CFG-240): a broken entry of one client must
/// not stop the host, and the setting it states resolves from the level below it. The exception is
/// declared by the OWNER of a key rather than by this report — a setting whose domain states
/// <see cref="ConfigValueRejectionPolicy.FailStart"/> — and WHAT that exception does is decided by the
/// LEVEL the inadmissible value lies at (CFG-246): the core level stops the start
/// (<see cref="RefuseToStartOnRejectedValues"/>), and it does so only once the whole report has been
/// written, while every level above it has its RECORD thrown out of the effective configuration
/// (<see cref="DiscardedRecordsOf"/>) and the host comes up. The strict counterpart of the default is
/// the start-time validation of the GLOBAL section, which is critical and lives in the validators of
/// the options — this report walks the global section all the same, because a RELOAD that breaks it
/// produces no start failure for an operator to read.
/// </para>
/// </summary>
internal sealed class ConfigSnapshotDiagnosticsService : IHostedService, IDisposable
{
    /// <summary>
    /// Every catalog registered in this deployment, including one that is not the source of its level
    /// (<see cref="IConfigSnapshotCatalog.CanWalk"/>): the walk takes only <see cref="Walkable"/>.
    /// </summary>
    private readonly IReadOnlyList<IConfigSnapshotCatalog> _catalogs;

    /// <summary>
    /// Registry of the extraction bindings — the source of the answer "which key with a domain is
    /// bound at which level", and therefore of the levels that stay outside the walk.
    /// </summary>
    private readonly ConfigBindingRegistry _bindings;

    /// <summary>
    /// What this report leaves for the resolution path: how far the walk reaches, and which records it
    /// threw out of the effective configuration (CFG-246).
    /// </summary>
    private readonly ConfigSnapshotEffect _effect;

    /// <summary>
    /// Logger of the report.
    /// </summary>
    private readonly ILogger<ConfigSnapshotDiagnosticsService> _logger;

    /// <summary>
    /// Subscriptions to the snapshot boundaries of the sources; disposed together with the service.
    /// </summary>
    private readonly List<IDisposable> _subscriptions = [];

    /// <summary>
    /// Guards the whole report — the walk, the comparison with the previous snapshot and the writing.
    /// Anything left outside it reopens the same hole from another side: two overlapping reloads can
    /// walk the catalogs in one order and reach the log in the other, leaving the operator with a
    /// warning about a snapshot that no longer exists — which then becomes the baseline the next
    /// snapshot is compared against.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Findings the last snapshot held. Each FACT is published once and stays quiet while it is still
    /// there: the sources of a deployment share one configuration root, so a single edit wakes several
    /// subscriptions, and a value that stays broken is not news on the second walk. Only what this set
    /// does not already hold reaches the log — a fact of one level is never republished because the
    /// findings of another level changed, which would leave the operator unable to tell a repeated line
    /// from a new one. A fact that disappears and comes back is news again and is reported again.
    /// </summary>
    private IReadOnlyList<ConfigSnapshotFinding> _reported = [];

    /// <summary>
    /// Levels the last snapshot could not read; deduplicated exactly like the findings and for the same
    /// reason.
    /// </summary>
    private IReadOnlyList<UnreadableLevel> _reportedUnreadable = [];

    /// <summary>
    /// Records the last walk left out of the effective configuration — what was published to
    /// <see cref="ConfigSnapshotEffect"/>, kept here so that a level this walk could not read carries
    /// its own records over (see <see cref="KeepDiscardedRecordsOfUnreadableLevels"/>) instead of
    /// having them dropped by a set rebuilt out of findings that level could not produce.
    /// </summary>
    private IReadOnlySet<ConfigDiscardedRecord> _discardedRecords = new HashSet<ConfigDiscardedRecord>();

    /// <summary>
    /// Creates the snapshot report service.
    /// </summary>
    /// <param name="catalogs">Catalogs of the levels that can be walked.</param>
    /// <param name="bindings">Registry of the extraction bindings.</param>
    /// <param name="effect">Effect of this report on the resolution path.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="InvalidOperationException">
    /// Two catalogs cover the same pair "setting + level" — see <see cref="RefuseDuplicates"/>.
    /// </exception>
    public ConfigSnapshotDiagnosticsService(
        IEnumerable<IConfigSnapshotCatalog> catalogs,
        ConfigBindingRegistry bindings,
        ConfigSnapshotEffect effect,
        ILogger<ConfigSnapshotDiagnosticsService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalogs);

        _catalogs = [.. catalogs];
        _bindings = bindings;
        _effect = effect;
        _logger = logger;

        RefuseDuplicates(_catalogs);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Published BEFORE anything can walk: the reach of this report is what tells the resolution
        // whether CFG-246 covers a pair "setting + level" at all, and a walk landing before it would
        // publish discarded records into a resolution that still believes nothing is covered.
        var walked = WalkedPairs();
        _effect.PublishCoverage(walked);

        // Subscribed BEFORE the first walk, not after it: a reload landing between the two would fire a
        // token nobody listens to yet, and its snapshot would be lost until the next reload. The order
        // costs nothing — a reload racing the first walk finds the same snapshot, and the deduplication
        // below drops the duplicate.
        foreach (var catalog in Walkable())
        {
            Subscribe(catalog.Subscribe(ReportReload));
        }

        var snapshot = Report();
        ReportLevelsOutsideTheWalk(walked);
        ReportSettingsOutsideTheSchema();

        // The refusal comes LAST, after the whole report is written: it throws, and anything left
        // behind it would never reach the operator — least of all the levels outside the walk, which
        // are exactly the ones the refusal could not look at. It decides on the findings THIS walk
        // returned rather than on the field the walks share: the subscriptions above are already live,
        // so a reload landing in the window between the two replaces that field with the findings of
        // ITS snapshot — and a level that reload could not read carries the FailStart rejection out of
        // them, leaving the host to start on the very value the policy exists to stop.
        RefuseToStartOnRejectedValues(snapshot);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    /// <summary>
    /// Catalogs that are the source of their level in THIS deployment
    /// (<see cref="IConfigSnapshotCatalog.CanWalk"/>). One that is not is treated exactly as a level
    /// with no catalog at all: it is neither walked nor subscribed to, and it does not count as
    /// covering its setting — so the level is NAMED as being outside the report rather than passing for
    /// walked and clean.
    /// </summary>
    /// <returns>Catalogs to walk.</returns>
    private IEnumerable<IConfigSnapshotCatalog> Walkable() => _catalogs.Where(catalog => catalog.CanWalk);

    /// <summary>
    /// Pairs "setting + level" this report actually walks — the REACH of the report. It answers two
    /// questions from one place: which pairs have to be named as standing outside the walk
    /// (<see cref="ReportLevelsOutsideTheWalk"/>), and which pairs the alternative of CFG-240 covers at
    /// all (<see cref="ConfigSnapshotEffect.Covers"/>). A catalog whose unit is the RECORD covers no
    /// setting and contributes nothing here.
    /// </summary>
    /// <returns>Pairs inside the walk.</returns>
    private IReadOnlySet<(string SettingKey, ConfigLevel Level)> WalkedPairs() =>
        Walkable()
            .SelectMany(catalog => catalog.SettingKeys.Select(setting => (SettingKey: setting, catalog.Level)))
            .ToHashSet();

    /// <summary>
    /// Refuses a second catalog on the same pair "setting + level" — the same answer
    /// <see cref="ConfigBindingRegistry"/> gives a second BINDING of a pair, and for the same reason:
    /// the two would walk one source twice over, and which of them a finding came from would depend on
    /// the order of the DI registrations. The container deduplicates the catalogs by implementation
    /// type, which says nothing about the pair they cover, so this is where the pair is checked.
    /// <para>
    /// A catalog covering no setting at all walks records as a whole
    /// (<see cref="IConfigSnapshotCatalog.SettingKeys"/>) and states no pair to collide on: its unit is
    /// the record, and two such catalogs on one level report different facts about it rather than the
    /// same fact twice.
    /// </para>
    /// </summary>
    /// <param name="catalogs">Catalogs of the deployment.</param>
    /// <exception cref="InvalidOperationException">Two catalogs cover the same pair.</exception>
    private static void RefuseDuplicates(IReadOnlyList<IConfigSnapshotCatalog> catalogs)
    {
        var covered = new HashSet<(string SettingKey, ConfigLevel Level)>();

        foreach (var catalog in catalogs)
        {
            foreach (var settingKey in catalog.SettingKeys)
            {
                if (!covered.Add((settingKey, catalog.Level)))
                {
                    throw new InvalidOperationException(
                        $"Setting '{settingKey}' already has a snapshot catalog at level {catalog.Level}: a second "
                        + "one would report the values of that level twice and make the report depend on the order "
                        + "of the registrations.");
                }
            }
        }
    }

    /// <summary>
    /// Keeps a subscription alive for the lifetime of the service. A source that announces no reload
    /// returns null — the report then stays a start-time one for that level.
    /// </summary>
    /// <param name="subscription">Subscription returned by the catalog; null — none was made.</param>
    private void Subscribe(IDisposable? subscription)
    {
        if (subscription is not null)
        {
            _subscriptions.Add(subscription);
        }
    }

    /// <summary>
    /// Walks every catalog that is the source of its level and reports the facts the previous snapshot
    /// did not hold.
    /// </summary>
    /// <returns>
    /// Everything THIS walk found, not only what it wrote: the deduplication decides what an operator
    /// is told, while a caller deciding on the snapshot — <see cref="RefuseToStartOnRejectedValues"/> —
    /// asks what the snapshot holds. Empty when the walk itself could not be made (see the guard
    /// below), which is what the report says about it too.
    /// </returns>
    private IReadOnlyList<ConfigSnapshotFinding> Report()
    {
        try
        {
            lock (_gate)
            {
                var findings = new List<ConfigSnapshotFinding>();
                var unreadable = new List<UnreadableLevel>();

                foreach (var catalog in Walkable())
                {
                    Walk(catalog, findings, unreadable);
                }

                // Only the facts the previous snapshot did not hold are written: a level whose findings
                // did not change says nothing new just because a neighbouring level's did.
                var reported = _reported.ToHashSet();
                var reportedUnreadable = _reportedUnreadable.ToHashSet();

                // Published only after they are written, so a logger that fails leaves the next snapshot
                // to report these facts again instead of swallowing them for good.
                foreach (var finding in findings.Where(finding => !reported.Contains(finding)))
                {
                    ReportFinding(finding);
                }

                foreach (var level in unreadable.Where(level => !reportedUnreadable.Contains(level)))
                {
                    ReportUnreadableLevel(level);
                }

                // Republished as a WHOLE set on every walk: a snapshot that no longer holds the
                // inadmissible value takes the record back into the effective configuration, and a
                // record that is still broken stays out without anything having to remember it. What
                // a level could not be READ for is not the same as what it no longer holds, so those
                // records are carried over rather than rebuilt out of findings that do not exist.
                var discarded = KeepDiscardedRecordsOfUnreadableLevels(
                    DiscardedRecordsOf(findings, reported),
                    unreadable);

                _effect.PublishDiscardedRecords(discarded);

                _discardedRecords = discarded;
                _reported = findings;
                _reportedUnreadable = unreadable;

                return findings;
            }
        }
        // This runs INSIDE the reload callback of a source, so letting anything out of here would break
        // the reload over a diagnostic. A level that cannot be read is caught per level (see Walk) and
        // named in the report instead of cancelling it; what is left for this guard is the work around
        // those walks and the writing of the lines. Cancellation is not a configuration failure and
        // stays unhandled.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "The configuration snapshot could not be walked; the report is skipped.");

            return [];
        }
    }

    /// <summary>
    /// Reload callback of a source. The same report: a reload refuses no start
    /// (<see cref="RefuseToStartOnRejectedValues"/>), so what its walk found decides nothing about the
    /// host coming up. It does decide what the EFFECTIVE configuration holds from here on — the records
    /// the new snapshot throws out are republished by the walk itself (CFG-246), which is the point of
    /// a snapshot boundary rather than a start-time one.
    /// </summary>
    private void ReportReload() => Report();

    /// <summary>
    /// Walks one catalog, isolating its failure from the rest of the report: the levels are independent
    /// configuration sources and fail independently, so a reload that breaks one of them must not
    /// cancel the part of the report the others owe (CFG-240). The findings of a catalog are ordered by
    /// their configuration key, so two snapshots holding the same facts compare equal whatever order
    /// the source enumerated them in.
    /// </summary>
    /// <param name="catalog">Catalog to walk.</param>
    /// <param name="findings">Findings to append to.</param>
    /// <param name="unreadable">Unreadable levels to append to.</param>
    private static void Walk(
        IConfigSnapshotCatalog catalog,
        List<ConfigSnapshotFinding> findings,
        List<UnreadableLevel> unreadable)
    {
        try
        {
            // Materialized here, inside the guard: the walk is lazy, and its reads happen on enumeration.
            findings.AddRange(catalog.Walk().OrderBy(finding => finding.ConfigurationKey, StringComparer.Ordinal));
        }
        // A read of a level is a whole bind-and-validate pass, and both halves of it fail with
        // exceptions of their own — a broken rule as OptionsValidationException, a value that does not
        // convert to the declared type as an InvalidOperationException out of the binder. The guard must
        // not depend on which exception a third-party binder chooses to raise. Cancellation is not a
        // configuration failure and stays unhandled.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            unreadable.Add(
                new UnreadableLevel(catalog.Level, catalog.SectionKey, catalog.ReadThroughSectionKey, ex.Message));
        }
    }

    /// <summary>
    /// Reports one fact of the snapshot. The line names the fact, the setting and its level, the
    /// address in the configuration and the boundary or reason — and stops there: what the fact means
    /// for a consumer is not something this walk can observe (<see cref="ConfigSnapshotFinding"/>).
    /// </summary>
    /// <param name="finding">Finding to report.</param>
    private void ReportFinding(ConfigSnapshotFinding finding)
    {
        if (finding.Fact == ConfigSnapshotFact.RecordNotBound)
        {
            ReportRecordNotBound(finding);

            return;
        }

        if (finding.Fact == ConfigSnapshotFact.SettingNotStated)
        {
            ReportSettingNotStated(finding);

            return;
        }

        if (finding.Fact == ConfigSnapshotFact.ValueUnreadable)
        {
            ReportValueUnreadable(finding);

            return;
        }

        // A record of an ARRAY is addressed in the configuration by its position, so the sign an
        // operator recognizes it by is named next to the key rather than pretended to be a segment of it.
        if (finding.RecordLabel is { } labelled)
        {
            _logger.LogWarning(
                "Configuration key {ConfigKey} — in the record of {ConfigRecord} — states {ConfigValue} for "
                + "setting {ConfigSetting} at level {ConfigLevel}: the setting admits {ConfigBoundary}, so "
                + "the value is rejected.{ConfigCorrectAt}",
                finding.ConfigurationKey,
                labelled,
                finding.Value,
                finding.SettingKey,
                finding.Level,
                finding.Detail,
                CorrectAtOf(finding));

            return;
        }

        _logger.LogWarning(
            "Configuration key {ConfigKey} states {ConfigValue} for setting {ConfigSetting} at level "
            + "{ConfigLevel}: the setting admits {ConfigBoundary}, so the value is rejected.{ConfigCorrectAt}",
            finding.ConfigurationKey,
            finding.Value,
            finding.SettingKey,
            finding.Level,
            finding.Detail,
            CorrectAtOf(finding));
    }

    /// <summary>
    /// Reports one address a level states a value at that cannot be read into the type of the setting.
    /// The line names the address, the record, the setting and the reason the read ended — and never
    /// the value: the read failed, so there is no value of the setting's type to print, and the text
    /// the record holds may be a secret. Nobody else tells an operator this: the resolution path logs
    /// its own read of the value once, per key and per step, and a level whose value nothing resolved
    /// today would go unmentioned until the first request that needs it.
    /// </summary>
    /// <param name="finding">Finding to report.</param>
    private void ReportValueUnreadable(ConfigSnapshotFinding finding)
    {
        if (finding.RecordLabel is { } labelled)
        {
            _logger.LogWarning(
                "Configuration key {ConfigKey} — in the record of {ConfigRecord} — states a value for "
                + "setting {ConfigSetting} at level {ConfigLevel} that cannot be read into the type of the "
                + "setting: {Reason}.{ConfigCorrectAt}",
                finding.ConfigurationKey,
                labelled,
                finding.SettingKey,
                finding.Level,
                finding.Detail,
                CorrectAtOf(finding));

            return;
        }

        _logger.LogWarning(
            "Configuration key {ConfigKey} states a value for setting {ConfigSetting} at level "
            + "{ConfigLevel} that cannot be read into the type of the setting: {Reason}.{ConfigCorrectAt}",
            finding.ConfigurationKey,
            finding.SettingKey,
            finding.Level,
            finding.Detail,
            CorrectAtOf(finding));
    }

    /// <summary>
    /// The sentence naming where a correct value is stated, or nothing when the owner of the setting
    /// named no address. It is appended to the line rather than made a line of its own: the two halves
    /// — what was wrong and where to fix it — are read together or not at all.
    /// </summary>
    /// <param name="finding">Finding being reported.</param>
    /// <returns>Sentence to append, possibly empty.</returns>
    private static string CorrectAtOf(ConfigSnapshotFinding finding) =>
        finding.CorrectAt is { } address ? $" State a correct value at {address}." : string.Empty;

    /// <summary>
    /// Reports one record missing from the effective configuration. A record the binder cannot build is
    /// dropped silently — the validation of the collection validates what it was given, and nothing
    /// downstream sees the loss — so this line is the only place the fact can come from.
    /// </summary>
    /// <param name="finding">Finding to report.</param>
    private void ReportRecordNotBound(ConfigSnapshotFinding finding)
    {
        if (finding.RecordLabel is { } labelled)
        {
            _logger.LogWarning(
                "Configuration key {ConfigKey} — the record of {ConfigRecord} — did not bind and is ABSENT "
                + "from the effective configuration of level {ConfigLevel}: every setting it states is "
                + "resolved without it. Reason: {Reason}",
                finding.ConfigurationKey,
                labelled,
                finding.Level,
                finding.Detail);

            return;
        }

        _logger.LogWarning(
            "Configuration key {ConfigKey} did not bind and is ABSENT from the effective configuration of "
            + "level {ConfigLevel}: every setting it states is resolved without it. Reason: {Reason}",
            finding.ConfigurationKey,
            finding.Level,
            finding.Detail);
    }

    /// <summary>
    /// Reports a setting the walk covers and the snapshot states no value for — the schema-to-snapshot
    /// direction of the comparison (SPEC-012 §10.6 CFG-244). It is stated at INFORMATION level: a
    /// setting nobody overrode is the normal state of a deployment, and the line exists so that the
    /// coverage of the walk is visible rather than inferred from silence. It states the fact and not
    /// its cause: a setting nobody overrode and an address nobody writes to reach this same line. The
    /// line names the setting, the level and the section — never a value, because there is none.
    /// </summary>
    /// <param name="finding">Finding to report.</param>
    private void ReportSettingNotStated(ConfigSnapshotFinding finding) =>
        _logger.LogInformation(
            "Setting {ConfigSetting} is declared at level {ConfigLevel}, whose snapshot is walked, and that level "
            + "states no value for it anywhere under configuration section {ConfigSection}: the setting is "
            + "resolved from the level below it, or from its default.",
            finding.SettingKey,
            finding.Level,
            finding.ConfigurationKey);

    /// <summary>
    /// Reports one level missing from this snapshot report. Nobody else tells the operator this on a
    /// RELOAD: the start-time validation has already run, and the walk of the level is exactly what did
    /// not happen. The gap is named as ONE level, never as the report: the levels are walked
    /// independently, so whatever is not mentioned here was read.
    /// </summary>
    /// <param name="level">Level that could not be read.</param>
    private void ReportUnreadableLevel(UnreadableLevel level)
    {
        if (level.ReadThroughSectionKey is { } rootSectionKey)
        {
            _logger.LogWarning(
                "The contents of configuration section {ConfigSection} are missing from this snapshot "
                + "report: the section is read as part of configuration section {ConfigRoot}, which did not "
                + "bind or validate as a whole — so the setting that failed is the one named by the reason "
                + "and need not belong to the section itself. The other levels of this report were read. "
                + "Reason: {Reason}",
                level.SectionKey,
                rootSectionKey,
                level.Reason);

            return;
        }

        _logger.LogWarning(
            "The contents of configuration section {ConfigSection} are missing from this snapshot report: "
            + "the section could not be read. The other levels of this report were read. Reason: {Reason}",
            level.SectionKey,
            level.Reason);
    }

    /// <summary>
    /// Policies of the declared keys by setting name — what each owner said should happen to a value
    /// their setting does not admit.
    /// </summary>
    /// <returns>Rejection policy by setting name.</returns>
    private Dictionary<string, ConfigValueRejectionPolicy> PoliciesByKey() =>
        _bindings.Keys.ToDictionary(key => key.Name, key => key.OnRejected, StringComparer.Ordinal);

    /// <summary>
    /// The records this walk throws out of the effective configuration (CFG-246). A value the setting
    /// refuses to be served the level below in place of (<see cref="RefusedValue"/>) takes its whole
    /// RECORD with it when two things hold: the owner of the setting
    /// declared <see cref="ConfigValueRejectionPolicy.FailStart"/>, and the value lies ABOVE the core
    /// level — the core level has no record to throw out and is answered by the start refusal below
    /// and, after the start, by the last valid value of the level.
    /// <para>
    /// The outcome is the one a record that did not bind already has: the record is absent from the
    /// effective configuration as a whole, so the settings STATED BESIDE the rejected one are resolved
    /// from the level below as well. Records of the same level that hold nothing inadmissible are not
    /// touched — which is the whole reason the unit is the record and not the level.
    /// </para>
    /// </summary>
    /// <param name="findings">Findings of this walk.</param>
    /// <param name="alreadyReported">
    /// Findings the previous snapshot already held — the deduplication of the report, reused so that a
    /// record that cannot be addressed is named once rather than on every reload.
    /// </param>
    /// <returns>Records to take out of the effective configuration.</returns>
    private HashSet<ConfigDiscardedRecord> DiscardedRecordsOf(
        IReadOnlyList<ConfigSnapshotFinding> findings,
        HashSet<ConfigSnapshotFinding> alreadyReported)
    {
        var discarded = new HashSet<ConfigDiscardedRecord>();
        var policies = PoliciesByKey();

        foreach (var finding in findings)
        {
            if (finding.Level == ConfigLevel.Core || !RefusedValue(finding, policies))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(finding.RecordLabel))
            {
                if (!alreadyReported.Contains(finding))
                {
                    ReportUnaddressableRecord(finding);
                }

                continue;
            }

            discarded.Add(new ConfigDiscardedRecord(finding.Level, finding.RecordLabel));
        }

        return discarded;
    }

    /// <summary>
    /// Keeps the records a level was already holding out of the effective configuration when THIS walk
    /// could not read that level. A record leaves the set on the evidence that the snapshot no longer
    /// states the inadmissible value; a level that could not be read states nothing either way, and
    /// treating its silence as a correction would put the record back on the first failed read — the
    /// piecemeal substitution the policy of the owner exists to prevent. It is the same choice the
    /// guard around the whole report makes when a walk cannot be made at all: what is not known to be
    /// fixed stays out until a walk says otherwise, and the operator is told the level is missing from
    /// the report (<see cref="ReportUnreadableLevel"/>).
    /// </summary>
    /// <param name="discarded">Records this walk threw out.</param>
    /// <param name="unreadable">Levels this walk could not read.</param>
    /// <returns>Records to take out of the effective configuration.</returns>
    private IReadOnlySet<ConfigDiscardedRecord> KeepDiscardedRecordsOfUnreadableLevels(
        HashSet<ConfigDiscardedRecord> discarded,
        IReadOnlyList<UnreadableLevel> unreadable)
    {
        if (unreadable.Count == 0)
        {
            return discarded;
        }

        var levels = unreadable.Select(level => level.Level).ToHashSet();

        foreach (var record in _discardedRecords.Where(record => levels.Contains(record.Level)))
        {
            discarded.Add(record);
        }

        return discarded;
    }

    /// <summary>
    /// Reports a rejected value whose record the walk cannot name. The setting declared that it would
    /// rather lose the whole record than be served the value of the level below, and the record is
    /// exactly what the catalog left unidentified — so the value is dropped as the DEFAULT of CFG-240
    /// drops it, and the operator is told that the stricter outcome did not apply. Staying silent here
    /// would leave the owner of the setting believing a guarantee that nothing enforces.
    /// </summary>
    /// <param name="finding">Refused value whose record has no identity.</param>
    private void ReportUnaddressableRecord(ConfigSnapshotFinding finding) =>
        _logger.LogWarning(
            "Setting {ConfigSetting} is stated with {ConfigWrong} under configuration key "
            + "{ConfigKey} at level {ConfigLevel}, and the setting asks for the whole record to be left out "
            + "of the effective configuration — but the walk of that level names no record for this value, "
            + "so the value is dropped on its own and the neighbouring settings of the record still apply.",
            finding.SettingKey,
            WhatIsWrongWith(finding),
            finding.ConfigurationKey,
            finding.Level);

    /// <summary>
    /// Whether a finding is a value the OWNER of its setting refused to be served the level below in
    /// place of (<see cref="ConfigValueRejectionPolicy.FailStart"/>). TWO facts of the walk are such a
    /// value, and they are treated alike here by the declaration of the owner rather than by their own
    /// shape: a value outside the domain, and a value that cannot be read into the type of the setting
    /// at all. The owner who said that a quiet substitution of their setting is worse than its loss
    /// meant both — which of the two ways a stated value turned out to be unusable changes nothing
    /// about what the deployment would be served instead (CFG-246).
    /// </summary>
    /// <param name="finding">Finding of the walk.</param>
    /// <param name="policies">Rejection policy by setting name.</param>
    /// <returns>true when the fact is a refused value of a setting declaring FailStart.</returns>
    private static bool RefusedValue(
        ConfigSnapshotFinding finding,
        Dictionary<string, ConfigValueRejectionPolicy> policies) =>
        finding.Fact is ConfigSnapshotFact.ValueRejected or ConfigSnapshotFact.ValueUnreadable
        && finding.SettingKey is { } settingKey
        && policies.TryGetValue(settingKey, out var policy)
        && policy is ConfigValueRejectionPolicy.FailStart;

    /// <summary>
    /// What is wrong with the stated value, as a CLAUSE of a sentence — the one thing the two facts of
    /// <see cref="RefusedValue"/> say differently. It is data of the message rather than a second
    /// message, so the two facts are reported and refused by one code and cannot drift into saying
    /// different things about the same outcome.
    /// </summary>
    /// <param name="finding">Finding of the walk.</param>
    /// <returns>Clause naming what is wrong with the value.</returns>
    private static string WhatIsWrongWith(ConfigSnapshotFinding finding) =>
        finding.Fact is ConfigSnapshotFact.ValueUnreadable
            ? "a value that cannot be read into the type of the setting"
            : "a value it does not admit";

    /// <summary>
    /// Stops the start while the snapshot holds, at the CORE level, a value the setting refuses to be
    /// served the level below in place of — one outside its domain, or one that cannot be read into its
    /// type at all (<see cref="RefusedValue"/>) — AND
    /// the owner of that setting declared that it would rather not start than be served the value of
    /// the level below (<see cref="ConfigValueRejectionPolicy.FailStart"/>). Every other rejection stays
    /// what it has always been — a warning of this report and a skipped step of the chain (CFG-240) —
    /// or, above the core level, the loss of the record that held it
    /// (<see cref="DiscardedRecordsOf"/>).
    /// <para>
    /// The core level is the one where there is nothing to throw out and nothing below to cede to
    /// (CFG-246): the setting would fall through to a default nobody declared. A level ABOVE the core
    /// one never stops the start — a broken entry of one owner taking the host down is exactly what
    /// CFG-240 forbids.
    /// </para>
    /// <para>
    /// It runs after the whole report — the walk of the snapshot and the levels left outside it — so the
    /// operator reads the full report BEFORE the refusal rather than one line of it. On a later reload
    /// nothing is refused: the host is already serving, and taking it down over an edit would turn one
    /// broken value into an outage — the level answers with the last valid value it was read with, and
    /// where there is none the step is dropped exactly as SkipStep drops it.
    /// </para>
    /// <para>
    /// The message names the setting, the level, the rule and the address to fix it at — never the
    /// value: a catalog refuses a secret key outright (<see cref="ConfigValueCatalog{T}"/>), and the
    /// message keeps the same shape whatever the catalog was built over.
    /// </para>
    /// </summary>
    /// <param name="findings">
    /// Findings of the walk this refusal belongs to. They are PASSED rather than read from the field the
    /// walks share, so that the decision is made on the snapshot that was actually walked at start: the
    /// reload subscriptions are live by then, and a reload landing in between would leave the field
    /// holding the findings of a different snapshot — one where a level that had gone unreadable states
    /// nothing at all, and the rejection that should stop the start has quietly ceased to exist.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A setting that declares FailStart states, at the core level, a value it does not admit or a value
    /// that cannot be read into its type.
    /// </exception>
    private void RefuseToStartOnRejectedValues(IReadOnlyList<ConfigSnapshotFinding> findings)
    {
        var policies = PoliciesByKey();

        foreach (var finding in findings)
        {
            if (finding.Level != ConfigLevel.Core || !RefusedValue(finding, policies))
            {
                continue;
            }

            var correctAt = finding.CorrectAt is { } address
                ? $" State a correct value at {address}."
                : string.Empty;

            throw new InvalidOperationException(
                $"Setting '{finding.SettingKey}' is stated at level {finding.Level}, under configuration key "
                + $"'{finding.ConfigurationKey}', with {WhatIsWrongWith(finding)}: {finding.Detail}. The setting "
                + $"declares that the host must not start on such a value rather than be served the value of the "
                + $"level below.{correctAt}");
        }
    }

    /// <summary>
    /// Names, once at start, every pair "setting with a domain + level it is bound at" that the walk
    /// does not cover. It is a property of the CONTOUR rather than an anomaly of the deployment — a
    /// source behind an expensive query is not obliged to be walked — which is why it is stated once
    /// and at information level, exactly like the map of the contour.
    /// <para>
    /// FOUR reasons put a pair outside the walk, and they are told apart because they are fixed in
    /// different places and by different people. The first belongs to the KEY as a whole and is named
    /// ahead of the pass below (<see cref="ReportKeysNoWalkReproduces"/>): the value of the key is
    /// assembled by its parse hook, which no walk can apply. The other three belong to a pair: the
    /// SOURCE of the level does not enumerate its records (a
    /// property of the deployed composition — the operator's contour, or a store substituted behind
    /// the level); the ADDRESS of the level requires a value for a dimension it substitutes outside its
    /// groups (a property of the declaration of the key — there is no address the records could be read
    /// at at all, <see cref="IDimensionBoundCatalog"/>); and the KEY is declared SECRET, which keeps it
    /// out whatever serves its level, because a catalog over it is refused outright and therefore never
    /// registered (<see cref="ConfigValueCatalog{T}"/>). One wording for the three would send an
    /// operator looking at a source that enumerates its records perfectly well.
    /// </para>
    /// </summary>
    /// <param name="walked">Pairs "setting + level" this report walks.</param>
    private void ReportLevelsOutsideTheWalk(IReadOnlySet<(string SettingKey, ConfigLevel Level)> walked)
    {
        ReportKeysNoWalkReproduces();

        foreach (var key in _bindings.Keys.Where(key => key.Domain is not null))
        {
            foreach (var level in _bindings.BoundLevelsOf(key.Name))
            {
                if (walked.Contains((key.Name, level)))
                {
                    continue;
                }

                // Settled by the DECLARATION, before any catalog is asked: a secret key is refused a
                // snapshot catalog outright, so it never has one to answer with an address — and the
                // source-shaped wording below would send an operator to a deployment that has nothing
                // to fix, the setting being kept out by how it is declared.
                if (key.IsSecret)
                {
                    _logger.LogInformation(
                        "Setting {ConfigSetting} admits {ConfigBoundary} and is bound at level {ConfigLevel}, "
                        + "and is declared secret: the report never names the value of such a setting, so the "
                        + "values stated there are outside the configuration snapshot report.",
                        key.Name,
                        key.Domain,
                        level);

                    continue;
                }

                if (AddressKeepingTheWalkOut(key.Name, level) is { } address)
                {
                    _logger.LogInformation(
                        "Setting {ConfigSetting} admits {ConfigBoundary} and is bound at level {ConfigLevel}, "
                        + "whose address '{ConfigAddress}' requires a value for a dimension it substitutes "
                        + "outside its groups: the level states nothing at an address a walk could read, so "
                        + "the values stated there are outside the configuration snapshot report.",
                        key.Name,
                        key.Domain,
                        level,
                        address);

                    continue;
                }

                _logger.LogInformation(
                    "Setting {ConfigSetting} admits {ConfigBoundary} and is bound at level {ConfigLevel}, "
                    + "whose source does not enumerate its records: the values stated there are outside the "
                    + "configuration snapshot report.",
                    key.Name,
                    key.Domain,
                    level);
            }
        }
    }

    /// <summary>
    /// Names, once at start, every key whose DECLARATION says that no walk reproduces its value
    /// (<see cref="ConfigKeyBuilder{T}.NotWalked"/>) — the fourth reason a pair stands outside the
    /// report, and the only one that belongs to the KEY rather than to one of its levels: the value is
    /// assembled by the parse hook of the key, which the path of reading calls and a walk cannot. The
    /// levels are therefore named together, in one line per key, instead of repeating one fact once per
    /// level.
    /// <para>
    /// Without this the report would be SILENT about such a key: no snapshot catalog is registered for
    /// it (a catalog is registered for the pairs of a key that declares a domain), so the pass below
    /// never reaches it, and an operator reading the report as an account of the declared catalog could
    /// not tell "the walk found nothing stated here" from "the walk was never here".
    /// </para>
    /// </summary>
    private void ReportKeysNoWalkReproduces()
    {
        foreach (var key in _bindings.Keys.Where(key => key.NotWalked).OrderBy(key => key.Name, StringComparer.Ordinal))
        {
            var levels = _bindings.BoundLevelsOf(key.Name).OrderBy(level => level).ToList();

            if (levels.Count == 0)
            {
                continue;
            }

            _logger.LogInformation(
                "Setting {ConfigSetting} is bound at {ConfigLevels}, and its value is assembled by the parse hook "
                + "of the key: a walk reads one address and cannot reproduce it, so the values stated there are "
                + "outside the configuration snapshot report.",
                key.Name,
                string.Join(", ", levels));
        }
    }

    /// <summary>
    /// Address that keeps the walk of a pair out on its own; null when nothing but the source of the
    /// level does. The catalog of the pair is asked rather than the declaration of the key: the address
    /// is parsed once, by the catalog that would have walked it, and a second reading of it here would
    /// be a second place deciding what a walkable address is.
    /// <para>
    /// A pair no catalog claims at all — a level whose owner registered no catalog — has no address to
    /// name and answers null, which is exactly the source-shaped reason; the one other pair without a
    /// catalog, a secret key, is answered by the caller before this is asked. At most one catalog
    /// claims a pair (<see cref="RefuseDuplicates"/>), so the first match is the only one.
    /// </para>
    /// </summary>
    /// <param name="settingKey">Setting of the pair.</param>
    /// <param name="level">Level of the pair.</param>
    /// <returns>Address of the level; null when the address is not what keeps the walk out.</returns>
    private string? AddressKeepingTheWalkOut(string settingKey, ConfigLevel level) =>
        _catalogs.FirstOrDefault(catalog => catalog.Level == level && catalog.SettingKeys.Contains(settingKey))
                is IDimensionBoundCatalog bound
            ? bound.AddressRequiringDimensionValue
            : null;

    /// <summary>
    /// Names, once at start, every pair "setting + level" the walk covers that the SCHEMA of declared
    /// keys does not — the snapshot-to-schema direction of the comparison (SPEC-012 §10.6 CFG-244).
    /// Two shapes of it are told apart, because they are fixed in different places: the schema knows no
    /// such setting at all (its owner registered the walk of it and not its declaration), and the
    /// schema knows the setting but not at this level (the level was widened on one side only).
    /// <para>
    /// It is a WARNING and never a refusal: what is walked here is the CONFIGURATION, and the default
    /// of CFG-240 is that a defect of the configuration does not take the host down. The line names the
    /// section the values are stated in, the setting and the level — a value never enters it, so a
    /// secret cannot. It names no RECORD, because there is none to name: the fact belongs to the level
    /// as a whole and holds whatever record of it states anything.
    /// </para>
    /// <para>
    /// A level nobody walks contributes nothing here and is silent: it is named by
    /// <see cref="ReportLevelsOutsideTheWalk"/> instead, which is the only honest thing to say about a
    /// level whose records the source does not enumerate.
    /// </para>
    /// </summary>
    private void ReportSettingsOutsideTheSchema()
    {
        var declared = _bindings.Keys.ToDictionary(key => key.Name, key => key.Levels, StringComparer.Ordinal);

        foreach (var (settingKey, level, sectionKey) in Walkable()
                     .SelectMany(catalog => catalog.SettingKeys.Select(
                         setting => (Setting: setting, catalog.Level, catalog.SectionKey)))
                     .OrderBy(pair => pair.Setting, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Level))
        {
            if (!declared.TryGetValue(settingKey, out var levels))
            {
                _logger.LogWarning(
                    "Configuration section {ConfigSection} is walked for setting {ConfigSetting} at level "
                    + "{ConfigLevel}, which the schema of declared keys does not declare at all: whatever is "
                    + "stated for it there reaches no resolution.",
                    sectionKey,
                    settingKey,
                    level);

                continue;
            }

            if (levels.Contains(level))
            {
                continue;
            }

            _logger.LogWarning(
                "Configuration section {ConfigSection} is walked for setting {ConfigSetting} at level "
                + "{ConfigLevel}, which the key does not declare among its levels: whatever is stated for it "
                + "there reaches no resolution.",
                sectionKey,
                settingKey,
                level);
        }
    }

    /// <summary>
    /// One level whose configuration could not be read for the snapshot.
    /// </summary>
    /// <param name="Level">
    /// Level the unreadable catalog serves — what the records already thrown out of the effective
    /// configuration are kept by while the level cannot be read
    /// (<see cref="KeepDiscardedRecordsOfUnreadableLevels"/>).
    /// </param>
    /// <param name="SectionKey">Configuration section whose contents are missing from the report.</param>
    /// <param name="ReadThroughSectionKey">
    /// Wider section the level is read as part of, and therefore the section the failure belongs to;
    /// null when the level's own section is what is read.
    /// </param>
    /// <param name="Reason">Why the section could not be read.</param>
    private readonly record struct UnreadableLevel(
        ConfigLevel Level,
        string SectionKey,
        string? ReadThroughSectionKey,
        string Reason);
}
