// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Veriqa.Core.AuthServer.UiConfig;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration.Resolution;

/// <summary>
/// Reader of the <c>ui_config</c> record AS A NODE — the record of the
/// <see cref="ConfigLevel.UiConfig"/> level addressed by path instead of by a property of
/// <see cref="UiConfigRecord"/> (SPEC-012 CFG-203, §10.6). EVERY setting of this level is read here —
/// no extraction binding over the typed record is left — which is how a new setting is introduced
/// without touching the record class, itself a public serialization contract with already deployed
/// consumers.
/// <para>
/// An address is resolved against the record's TYPED properties first and against its unknown fields
/// (<see cref="UiConfigRecord.Extra"/>) afterwards. The order is the compatibility guarantee: a name
/// that a record carries in both places keeps reading what an installation was reading before.
/// </para>
/// <para>
/// The store is not reached from here — the record comes from <see cref="UiConfigRecordReader"/>,
/// which stays the single point that touches it (CFG-231, CFG-235). What this reader adds is the node
/// over the record, so the failure of a read, the logging of it and the liveness of the store are
/// answered in one place for both readers rather than in two that could drift apart.
/// </para>
/// <para>
/// The record is DERIVED — taken from the scope of the current resolution rather than read behind it
/// (<see cref="IDerivedConfigRecordReader{TRecord}"/>). Assembling one sign-in page reads the text
/// settings of this level off the typed record and its design settings off the node over that same
/// record: read outside the scope, one page would reach the store twice and open two container scopes
/// where the rule of the mechanism grants it one.
/// </para>
/// </summary>
internal sealed class UiConfigNodeReader : IDerivedConfigRecordReader<ConfigNode>
{
    /// <summary>
    /// How the record is turned into the tree the node is read from. The record's own
    /// <c>[JsonPropertyName]</c> attributes decide the names, so a path addresses a typed property by
    /// the SAME name a stored record spells it with — and an unknown field by the name it was stored
    /// under.
    /// </summary>
    /// <remarks>
    /// Serialization is what merges the two halves of the record into one tree: the typed properties
    /// are written first and the unknown fields of <c>[JsonExtensionData]</c> after them, so a name
    /// stated in both is found as the typed one.
    /// </remarks>
    private static readonly JsonSerializerOptions RecordSerializerOptions = new()
    {
        // A property the record leaves unset must not become a stated JSON null: the level would then
        // claim a value it does not have, instead of ceding to the level below (SPEC-012 §10.3).
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Reader of the <c>ui_config</c> record — the single point that reaches the record store.
    /// </summary>
    private readonly UiConfigRecordReader _recordReader;

    /// <summary>
    /// Creates the node reader of the <c>ui_config</c> record.
    /// </summary>
    /// <param name="recordReader">Reader of the <c>ui_config</c> record.</param>
    public UiConfigNodeReader(UiConfigRecordReader recordReader)
    {
        _recordReader = recordReader ?? throw new ArgumentNullException(nameof(recordReader));
    }

    /// <inheritdoc />
    public string Name => "UiConfigNode";

    /// <inheritdoc />
    /// <remarks>
    /// The answer is the store's, translated by the record reader: both readers stand over the same
    /// store, so a liveness stated separately here is exactly what would let them disagree.
    /// </remarks>
    public bool? IsLive => _recordReader.IsLive;

    /// <inheritdoc />
    /// <remarks>
    /// The read outside a scope — the record reader is asked directly. Inside a resolution the scope
    /// overload below is the one taken, and it is the resolution that this level is normally read in.
    /// </remarks>
    public async ValueTask<ConfigNode?> ReadAsync(
        ResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return NodeOver(await _recordReader.ReadAsync(context, cancellationToken), context);
    }

    /// <inheritdoc />
    public async ValueTask<ConfigNode?> ReadAsync(
        ConfigResolutionScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        // The record travels through the scope, so the typed reader of this level and this one share
        // the single read the scope grants their common source.
        return NodeOver(await scope.ReadAsync(_recordReader, cancellationToken), scope.Context);
    }

    /// <summary>
    /// Node over the record that was read, or null when there is no record of this level.
    /// </summary>
    /// <param name="record">Record read, or null.</param>
    /// <param name="context">Context of the read.</param>
    /// <returns>Node over the record, or null.</returns>
    /// <remarks>
    /// Null covers everything the record reader treats as "no record of this level": no selector in
    /// the context, no such record, or a failure of the store it has already logged. Whether the
    /// record was REJECTED (CFG-246) is not asked here either — the resolver asks the snapshot before
    /// the level is read at all.
    /// </remarks>
    private static ConfigNode? NodeOver(UiConfigRecord? record, ResolutionContext context) =>
        // The label of the record is its selector — what an operator names the record by, and what the
        // snapshot report calls it. A record read on the resolution path carries no configuration
        // address: which store it came from is the level's own business, and only the walk of the
        // self-hosted catalog knows the section its records are stated in.
        record is null ? null : NodeOver(record, context.UiConfigSelector, configurationKey: null);

    /// <summary>
    /// Node over one <c>ui_config</c> record. It is stated HERE and not per consumer, because turning
    /// the record into the tree a path is read from is the one place the record's JSON contract is
    /// spelled: the resolution path reads a record through it, and so does the walk of the level's
    /// snapshot (<see cref="UiConfigNodeWalker"/>) — a second spelling is exactly what would let a
    /// value be readable on one of the two paths and invisible on the other.
    /// </summary>
    /// <param name="record">Record to stand over.</param>
    /// <param name="recordLabel">Label of the record — its selector.</param>
    /// <param name="configurationKey">Address of the record in the configuration; null when it has none.</param>
    /// <returns>Node over the record.</returns>
    internal static ConfigNode NodeOver(UiConfigRecord record, string? recordLabel, string? configurationKey) =>
        ConfigNode.Over(
            JsonSerializer.SerializeToElement(record, RecordSerializerOptions),
            ConfigLevel.UiConfig,
            recordLabel,
            configurationKey is null ? null : address => ConfigurationKeyOf(configurationKey, address));

    /// <summary>
    /// Configuration key of the value the record states at an address inside it. A record of THIS level
    /// is addressed inside by the names of its JSON schema, while a deployment that keeps the records in
    /// its application configuration states them under the names the configuration BINDER reads — the
    /// names of the record's C# properties. Naming the JSON spelling to an operator would name a key
    /// their configuration does not hold: the binder ignores case but not the underscores of a
    /// snake_case name, so the value they were told about would be neither greppable nor editable.
    /// </summary>
    /// <param name="recordKey">Address of the record in the configuration.</param>
    /// <param name="address">Address of the value inside the record.</param>
    /// <returns>Configuration key of the value.</returns>
    private static string ConfigurationKeyOf(string recordKey, string address) =>
        $"{recordKey}{ConfigNode.PathSeparator}{BinderSpellingOf(address)}";

    /// <summary>
    /// An address inside the record, respelled the way the configuration binder reads it. The
    /// translation is taken from the record's OWN contract — the metadata of the very serializer that
    /// builds the tree the address is read from — so the two spellings cannot drift apart the way a
    /// second, hand-kept table of names would.
    /// </summary>
    /// <param name="address">Address of the value inside the record.</param>
    /// <returns>The same address in the binder's spelling.</returns>
    /// <remarks>
    /// A segment the record answers for with no member of its own is left AS IT STANDS: it is an entry
    /// of a map keyed by the deployment (the channel of a per-channel map) or a field the record does
    /// not know, and the binder reads such a name verbatim too.
    /// </remarks>
    private static string BinderSpellingOf(string address)
    {
        var spelled = new StringBuilder(address.Length);
        var typeInfo = RecordSerializerOptions.GetTypeInfo(typeof(UiConfigRecord));

        foreach (var segment in address.Split(ConfigNode.PathSeparator))
        {
            if (spelled.Length > 0)
            {
                spelled.Append(ConfigNode.PathSeparator);
            }

            var property = typeInfo?.Properties.FirstOrDefault(
                candidate => string.Equals(candidate.Name, segment, StringComparison.Ordinal));

            spelled.Append(MemberNameOf(property) ?? segment);

            // The next segment is addressed inside the member this one named, so the names it may be
            // matched against are that member's; past a segment nothing matched there are none.
            typeInfo = property is null ? null : RecordSerializerOptions.GetTypeInfo(property.PropertyType);
        }

        return spelled.ToString();
    }

    /// <summary>
    /// Name of the C# member behind one serialized property — the name the configuration binder reads
    /// it under. Null when there is no member to name: the property is not backed by one, and the
    /// caller then keeps the name the address already carries.
    /// </summary>
    /// <param name="property">Serialized property of the record, or null when none matched.</param>
    /// <returns>Name of the member, or null.</returns>
    private static string? MemberNameOf(JsonPropertyInfo? property) =>
        (property?.AttributeProvider as MemberInfo)?.Name;
}
