// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Catalog of one SETTING at one level: the owner of the level's records enumerates what they state
/// for the key, and the domain of the key decides what of that is rejected. The decision is made here
/// and not by the owner — the boundary is declared once, on the key
/// (<see cref="ConfigKey{T}.Domain"/>), and both readings of it, the one on the resolution path and
/// the one in this report, come from that single declaration (SPEC-012 §10.6 — one mechanism, no fork).
/// <para>
/// Beside a value the setting does not admit, the walk states the setting the level states NO value
/// for — the schema-to-snapshot direction of the comparison the report makes. It is not a defect and
/// is stated at information level: it says that the walk covered this setting at this level and found
/// nothing written, and nothing beyond that. WHY nothing is written it does not tell — a setting
/// nobody overrode and an address nobody writes to are the same fact to the walk, which reads the
/// records of the level and not the address the key is declared at.
/// </para>
/// <para>
/// The third thing the walk states is an address the level states a value at that CANNOT BE READ into
/// the type of the setting (<see cref="ConfigSnapshotFact.ValueUnreadable"/>). It is not a shade of
/// the rejection — the domain never saw it — and it is emphatically not the silence above: a level
/// holding a broken value is not a level holding nothing, and the two are repaired differently.
/// </para>
/// </summary>
/// <typeparam name="T">Type of the setting value.</typeparam>
public abstract class ConfigValueCatalog<T> : IConfigSnapshotCatalog
{
    /// <summary>
    /// Key whose values are walked.
    /// </summary>
    private readonly ConfigKey<T> _key;

    /// <summary>
    /// Domain declared by the key — what a stated value is checked against.
    /// </summary>
    private readonly ConfigValueDomain<T> _domain;

    /// <summary>
    /// Creates the catalog of one setting at one level.
    /// </summary>
    /// <param name="key">Key whose values the level states.</param>
    /// <param name="level">Level being walked.</param>
    /// <param name="sectionKey">Configuration section whose values the catalog contributes.</param>
    /// <param name="readThroughSectionKey">
    /// Wider section the level is read as part of; null — the level's own section is what is read.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The key declares no domain (there would be nothing to check the values against) or declares the
    /// value a secret (the report names the value it rejects, and a secret never reaches diagnostics).
    /// </exception>
    protected ConfigValueCatalog(
        ConfigKey<T> key,
        ConfigLevel level,
        string sectionKey,
        string? readThroughSectionKey = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

        if (key.Domain is not { } domain)
        {
            throw new InvalidOperationException(
                $"Configuration key '{key.Name}' declares no value domain: a snapshot catalog over it "
                + "would have nothing to check the stated values against.");
        }

        if (key.IsSecret)
        {
            throw new InvalidOperationException(
                $"Configuration key '{key.Name}' is declared secret and cannot be walked by the snapshot "
                + "report: the report names the value it rejects, and the value of a secret key never "
                + "reaches diagnostics.");
        }

        _key = key;
        _domain = domain;
        Level = level;
        SectionKey = sectionKey;
        ReadThroughSectionKey = readThroughSectionKey;
        SettingKeys = [key.Name];
    }

    /// <inheritdoc />
    public ConfigLevel Level { get; }

    /// <inheritdoc />
    /// <remarks>
    /// A catalog over a level served from the application configuration is always the source of it —
    /// that source is not substitutable. A catalog whose level is served by a replaceable store
    /// overrides this and answers about the store the container actually holds.
    /// </remarks>
    public virtual bool CanWalk => true;

    /// <inheritdoc />
    public string SectionKey { get; }

    /// <inheritdoc />
    public string? ReadThroughSectionKey { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<string> SettingKeys { get; }

    /// <inheritdoc />
    public abstract IDisposable? Subscribe(Action onSnapshot);

    /// <inheritdoc />
    /// <remarks>
    /// A level that states NOTHING for the key yields a fact of its own
    /// (<see cref="ConfigSnapshotFact.SettingNotStated"/>) — the schema-to-snapshot direction of the
    /// comparison the walk makes. It is stated here rather than derived by the report, because "the
    /// level states no value" is exactly what <see cref="Enumerate"/> answers and nothing downstream
    /// can: the report sees the findings of a walk, and an empty walk is indistinguishable from a walk
    /// that found everything in order.
    /// <para>
    /// An address whose value could not be READ counts as an answer of the level just as a readable
    /// one does, so a level that stated one broken value and nothing else is never called silent: the
    /// two assertions are different, and only one of them is true there.
    /// </para>
    /// </remarks>
    public IEnumerable<ConfigSnapshotFinding> Walk()
    {
        var answered = false;

        foreach (var walked in Enumerate())
        {
            answered = true;

            // A value no read could take never reaches the domain: a predicate over the type of the
            // setting has nothing to be asked about. The address is named with the reason the read
            // ended and never with the value, which does not exist in that type and may be a secret.
            if (walked.UnreadableReason is { } unreadable)
            {
                yield return new ConfigSnapshotFinding(
                    ConfigSnapshotFact.ValueUnreadable,
                    Level,
                    walked.ConfigurationKey,
                    walked.RecordLabel,
                    _key.Name,
                    Value: null,
                    unreadable,
                    _domain.CorrectAt);

                continue;
            }

            if (_domain.Admits(walked.Value!, out var violatedRule))
            {
                continue;
            }

            yield return new ConfigSnapshotFinding(
                ConfigSnapshotFact.ValueRejected,
                Level,
                walked.ConfigurationKey,
                walked.RecordLabel,
                _key.Name,
                ConfigValueText.Of(walked.Value!),
                violatedRule,
                _domain.CorrectAt);
        }

        if (answered)
        {
            yield break;
        }

        // The section is the address of the fact: there is no address of a value to name, because
        // there is no value. The record label is absent for the same reason — no record stated it.
        yield return new ConfigSnapshotFinding(
            ConfigSnapshotFact.SettingNotStated,
            Level,
            SectionKey,
            RecordLabel: null,
            _key.Name,
            Value: null,
            "the level states no value for the setting");
    }

    /// <summary>
    /// Enumerates what every address the level states the key at answered, address by address. An
    /// address the level states nothing at yields nothing; an address whose value could not be read
    /// yields the failure of that read (<see cref="ConfigWalkedValue{T}.Unreadable"/>) rather than
    /// dropping out of the walk. A level that cannot be read AT ALL throws, and the report names it.
    /// </summary>
    /// <returns>Answers of the addresses the level was walked at.</returns>
    protected abstract IEnumerable<ConfigWalkedValue<T>> Enumerate();
}
