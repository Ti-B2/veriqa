// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;

namespace Veriqa.Core.Configuration;

/// <summary>
/// A subtree of the record of one ownership level, read BY PATH rather than through a property of a
/// statically typed record. It is what lets a setting exist at a level without a field carrying it:
/// the address of the value is data (a relative path), so the record type of the level does not grow
/// a member per key (SPEC-012 CFG-231).
/// <para>
/// The node carries the IDENTITY of the record it stands over — the level and the record's label, the
/// same label the snapshot report names a record by
/// (<see cref="ConfigWalkedValue{T}.RecordLabel"/>, CFG-246). Above the core the unit a deployment
/// accepts or rejects is the RECORD, so a subtree without an identity could not state which record a
/// value came from.
/// </para>
/// <para>
/// Two sources stand behind one contract of reading: a configuration section (the levels served by the
/// application configuration — files, environment variables, secrets) and a JSON element (the record
/// of an external store). Whichever it is, the members below behave the same, so a consumer of a node
/// never learns which level's record it was handed: a path leads to a stated value or to nothing, a
/// stated scalar is read FROM ITS TEXT by the same converter the configuration binder converts a
/// configuration value with, and a value the requested type cannot take fails on both sources instead
/// of on one. Only the INSIDE of a composite value is left to each source's own binder.
/// </para>
/// <para>
/// The node decides NOTHING about a value it could not read into the requested type: a parser failure
/// leaves this type as an exception. What such a value does to the level — and to the record that
/// stated it — is decided once, in the mechanism (CFG-240/CFG-246), and a second decision here is
/// exactly what would let the two disagree. The mechanism can ASK for the same record STATING NOTHING
/// (<see cref="WhereNothingIsStated"/>), which is the same rule seen from the other side: the decision
/// is still the mechanism's, made after it has reported the value, and the node only carries it out.
/// </para>
/// </summary>
public sealed class ConfigNode
{
    /// <summary>
    /// Separator of path segments — a colon, as in <c>IConfiguration</c>. The path carries no reserved
    /// characters beyond it: an address a deployment cannot spell in an environment variable would be
    /// an address half of the configuration sources could not serve.
    /// </summary>
    public const char PathSeparator = ':';

    /// <summary>
    /// The subtree behind the node — a configuration section or a JSON element.
    /// </summary>
    private readonly Subtree _subtree;

    /// <summary>
    /// How an address inside the record becomes the configuration key of the value stated there; null
    /// when the record has no configuration address at all — see <see cref="ConfigurationKeyOf"/>.
    /// </summary>
    private readonly Func<string, string>? _configurationKeyOf;

    /// <summary>
    /// Creates a node over an already built subtree.
    /// </summary>
    /// <param name="subtree">Subtree behind the node.</param>
    /// <param name="level">Level whose record the subtree belongs to.</param>
    /// <param name="recordLabel">Label of the record, or null when the record has none.</param>
    /// <param name="configurationKeyOf">
    /// Configuration key of a value at an address inside the record, or null when the record has no
    /// configuration address — see <see cref="ConfigurationKeyOf"/>.
    /// </param>
    private ConfigNode(
        Subtree subtree,
        ConfigLevel level,
        string? recordLabel,
        Func<string, string>? configurationKeyOf)
    {
        _subtree = subtree;
        _configurationKeyOf = configurationKeyOf;
        Level = level;
        RecordLabel = recordLabel;
    }

    /// <summary>
    /// Level whose record this node is a subtree of.
    /// </summary>
    public ConfigLevel Level { get; }

    /// <summary>
    /// Label of the record — what an operator recognizes it by in the snapshot report (the client
    /// identifier of an application entry, the selector of a <c>ui_config</c> record). Null when the
    /// record of the level has no label of its own.
    /// </summary>
    public string? RecordLabel { get; }

    /// <summary>
    /// Configuration key an operator edits to change the value the record states at an address inside
    /// it — the address of the RECORD in the configuration of the deployment and the address inside it,
    /// joined. Null when the record comes from a source that has no configuration address at all (a
    /// store keeping records as rows): there is then no key to name, and a consumer names the value by
    /// the address inside the record alone.
    /// <para>
    /// It is asked PER ADDRESS and not composed from a prefix by the caller, because the two spellings
    /// need not be the same one: a record is addressed inside by the names of its own contract, while
    /// the configuration holds it under the names its BINDER reads, and only the record knows whether
    /// the two differ. Naming a key the deployment does not hold would leave the operator with nothing
    /// to grep for and nothing to edit.
    /// </para>
    /// <para>
    /// It is the address and not a second copy of the reading: nothing is read through it. The node
    /// answers it for the same reason it carries <see cref="RecordLabel"/> — the walk of a snapshot has
    /// to NAME what it found, and the record is the only thing that knows where it lives.
    /// </para>
    /// </summary>
    /// <param name="address">Relative path of the value inside the record.</param>
    /// <returns>Configuration key of the value, or null when the record has no configuration address.</returns>
    /// <exception cref="ArgumentException">The address is null, empty or whitespace.</exception>
    public string? ConfigurationKeyOf(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        return _configurationKeyOf?.Invoke(address);
    }

    /// <summary>
    /// Builds a node over a section of the application configuration.
    /// </summary>
    /// <param name="section">Section the paths are relative to.</param>
    /// <param name="level">Level whose record the section is.</param>
    /// <param name="recordLabel">Label of the record, or null when it has none.</param>
    /// <returns>Node over the section.</returns>
    /// <exception cref="ArgumentNullException">The section is null.</exception>
    public static ConfigNode Over(IConfigurationSection section, ConfigLevel level, string? recordLabel)
    {
        ArgumentNullException.ThrowIfNull(section);

        // A section is addressed inside by the very names the configuration holds it under, so the key
        // of a value is the path of the section and the address inside it, joined.
        return new ConfigNode(
            new SectionSubtree(section),
            level,
            recordLabel,
            address => $"{section.Path}{PathSeparator}{address}");
    }

    /// <summary>
    /// Builds a node over the JSON element of a store record.
    /// </summary>
    /// <param name="element">Element the paths are relative to.</param>
    /// <param name="level">Level whose record the element is.</param>
    /// <param name="recordLabel">Label of the record, or null when it has none.</param>
    /// <param name="configurationKeyOf">
    /// Configuration key of a value at an address inside the record; null when the record's source has
    /// no configuration address at all — see <see cref="ConfigurationKeyOf"/>. It is the record's own
    /// contract that answers it: a record whose members the configuration binder reads under names
    /// other than the ones the address is spelled in is the reason the answer is not a prefix.
    /// </param>
    /// <returns>Node over the element.</returns>
    public static ConfigNode Over(
        JsonElement element,
        ConfigLevel level,
        string? recordLabel,
        Func<string, string>? configurationKeyOf = null) =>
        new(new JsonSubtree(element), level, recordLabel, configurationKeyOf);

    /// <summary>
    /// Whether the node has anything at the relative path. A path that leads to an empty section or to
    /// a JSON <c>null</c> counts as absent — the level then states nothing and cedes to the level below
    /// it, which is what an unset value means (SPEC-012 §10.3).
    /// </summary>
    /// <param name="path">Relative path, segments separated by <see cref="PathSeparator"/>.</param>
    /// <returns>true when the path leads to a stated value.</returns>
    /// <exception cref="ArgumentException">The path is null, empty or whitespace.</exception>
    public bool Has(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _subtree.Has(path);
    }

    /// <summary>
    /// Names of the members the record states DIRECTLY under the relative path — the one question a
    /// walk of a snapshot asks that a resolution never does: a resolution knows the address it is
    /// after, while a walk has to find out what a record actually holds under a map whose entries are
    /// named by the deployment (the channel types of a per-channel map, SPEC-012 CFG-237).
    /// <para>
    /// A path that leads to nothing, or to a single value rather than to a subtree, has no members and
    /// yields nothing. The names are those of the DIRECT members alone: the walk descends by asking
    /// again, so a nested map costs nothing here.
    /// </para>
    /// </summary>
    /// <param name="path">Relative path, segments separated by <see cref="PathSeparator"/>.</param>
    /// <returns>Names of the members under the path.</returns>
    /// <exception cref="ArgumentException">The path is null, empty or whitespace.</exception>
    public IEnumerable<string> Children(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _subtree.Children(path);
    }

    /// <summary>
    /// Reads the value at the relative path.
    /// </summary>
    /// <typeparam name="T">Type the value is read into.</typeparam>
    /// <param name="path">Relative path, segments separated by <see cref="PathSeparator"/>.</param>
    /// <param name="value">Value read; the default of the type when there is nothing at the path.</param>
    /// <returns>true when the path leads to a stated value that was read.</returns>
    /// <exception cref="ArgumentException">The path is null, empty or whitespace.</exception>
    /// <remarks>
    /// false means the path leads to NOTHING. A path that leads to a value that cannot be read into
    /// <typeparamref name="T"/> — a text no converter takes, a shape the type is not read from —
    /// raises instead, on either source alike (<see cref="InvalidOperationException"/>, carrying the
    /// parser's own exception where there is one). The two are raised as different exception types, so
    /// that the mechanism deciding what a level does next can tell a value the deployment stated and
    /// got wrong from a SHAPE the type is not read from — a value no parser has yet looked at. See the
    /// type's summary for why the two outcomes are kept apart.
    /// </remarks>
    public bool TryRead<T>(string path, [MaybeNullWhen(false)] out T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return _subtree.TryRead(path, out value);
    }

    /// <summary>
    /// The SAME record — its level and its label — stating nothing anywhere: every address leads to
    /// nothing and every member list is empty.
    /// <para>
    /// It exists for the one caller that has already reported a value it could not read and now has to
    /// ask the KEY what its level states where the record says nothing: a key whose owner declared that
    /// a level always states a value (the core level of a shipped default) answers with that value, and
    /// a key that lets the level cede answers with nothing. Asking the key over an empty record is what
    /// keeps the two answers in the key's own hook rather than in a second copy of the shipped defaults
    /// in the mechanism.
    /// </para>
    /// <para>
    /// The record is emptied WHOLE, and that is the load-bearing part rather than a shortcut. A hook is
    /// free to read several members of its level — a path together with its SRI hash, a themed map
    /// beside the plain field, a value beside the gate that governs it — and a record that kept the
    /// readable members while silencing the rejected one would hand such a hook a value assembled half
    /// out of what the deployment stated and half out of what it never did: a script path whose
    /// integrity pin has quietly gone missing. Over a record stating nothing there is no half to
    /// assemble, so the level either speaks with what the product ships or cedes as a whole.
    /// </para>
    /// <para>
    /// The identity of the record travels with it because a hook reads by it: which spelling of a
    /// neighbouring member an address takes is decided by the level (<c>ui_config</c> spells its
    /// members in the snake_case of its JSON contract), and a level that lost its identity here would
    /// be answered by a different branch of the hook than the one that failed.
    /// </para>
    /// </summary>
    /// <returns>Node over the same record, stating nothing.</returns>
    internal ConfigNode WhereNothingIsStated() =>
        new(EmptySubtree.Instance, Level, RecordLabel, _configurationKeyOf);

    /// <summary>
    /// Whether a value of the type is read FROM TEXT — the question both sources have to ask, because
    /// the application configuration states every value as text and a record of a store may spell a
    /// scalar the same way. The question goes to the type converter of the platform — the one the
    /// configuration binder converts a configuration value with — so asking it here keeps ONE dialect
    /// over the two sources instead of one dialect per source. It is that converter, not every answer
    /// the binder has: the two types the binder serves without one — <see cref="object"/>, handed the
    /// text itself, and a byte array, decoded from base64 — are NOT read from text here, so a text
    /// stated for either fails the read loudly and alike on both sources instead of parting them.
    /// </summary>
    /// <typeparam name="T">Type the value would be read into.</typeparam>
    /// <returns>true when a text is what a value of the type is read from.</returns>
    private static bool IsReadFromText<T>() =>
        TypeDescriptor.GetConverter(typeof(T)).CanConvertFrom(typeof(string));

    /// <summary>
    /// The failure of a read that no parser even gets to attempt: a level states a value in one of two
    /// shapes — a text or a subtree — and the type is read from the other one. Both sources raise it,
    /// with one message, because a consumer catching it must not have to know which source the record
    /// came from.
    /// </summary>
    /// <typeparam name="T">Type the value was to be read into.</typeparam>
    /// <param name="path">Path of the value, as precise as its source can spell it.</param>
    /// <param name="statesText">Whether the level states a text rather than a subtree.</param>
    /// <returns>Exception to raise.</returns>
    private static ConfigValueShapeException NotTheStatedShape<T>(string path, bool statesText) =>
        new(
            $"Failed to read the value at '{path}' into '{typeof(T)}': " +
            $"the level states {(statesText ? "a value" : "a section")}.",
            path);

    /// <summary>
    /// The subtree behind a node: one contract of reading over the two sources a record can come from.
    /// </summary>
    private abstract class Subtree
    {
        /// <summary>
        /// Whether the subtree states anything at the relative path.
        /// </summary>
        /// <param name="path">Relative path.</param>
        /// <returns>true when the path leads to a stated value.</returns>
        public abstract bool Has(string path);

        /// <summary>
        /// Names of the members stated directly under the relative path.
        /// </summary>
        /// <param name="path">Relative path.</param>
        /// <returns>Names of the members under the path.</returns>
        public abstract IEnumerable<string> Children(string path);

        /// <summary>
        /// Reads the value at the relative path.
        /// </summary>
        /// <typeparam name="T">Type the value is read into.</typeparam>
        /// <param name="path">Relative path.</param>
        /// <param name="value">Value read, or the default of the type.</param>
        /// <returns>true when the path leads to a stated value that was read.</returns>
        public abstract bool TryRead<T>(string path, [MaybeNullWhen(false)] out T value);
    }

    /// <summary>
    /// Subtree of a record that states NOTHING — the record the mechanism asks a key again over
    /// (<see cref="WhereNothingIsStated"/>). It is a subtree of its own rather than a flag on the two
    /// real ones, because "the record says nothing" has to hold for every address a hook may reach and
    /// not only for the one address that was rejected.
    /// </summary>
    private sealed class EmptySubtree : Subtree
    {
        /// <summary>
        /// The one instance: the subtree carries no state, and the identity of the record it stands for
        /// is the node's (<see cref="ConfigNode.Level"/>, <see cref="ConfigNode.RecordLabel"/>).
        /// </summary>
        public static readonly EmptySubtree Instance = new();

        /// <inheritdoc />
        public override bool Has(string path) => false;

        /// <inheritdoc />
        public override IEnumerable<string> Children(string path) => [];

        /// <inheritdoc />
        public override bool TryRead<T>(string path, [MaybeNullWhen(false)] out T value)
        {
            value = default;

            return false;
        }
    }

    /// <summary>
    /// Subtree over a section of the application configuration. The parsing of a value is the
    /// configuration binder's, so a path served by a file, an environment variable or a secret behaves
    /// here exactly as the same path bound into a typed options class would.
    /// </summary>
    /// <param name="section">Section the paths are relative to.</param>
    private sealed class SectionSubtree(IConfigurationSection section) : Subtree
    {
        /// <inheritdoc />
        /// <remarks>
        /// A section that has neither a value nor children does not exist for the configuration
        /// library, which is the same "states nothing" the node reports.
        /// </remarks>
        public override bool Has(string path) => section.GetSection(path).Exists();

        /// <inheritdoc />
        public override IEnumerable<string> Children(string path) =>
            section.GetSection(path).GetChildren().Select(child => child.Key);

        /// <inheritdoc />
        public override bool TryRead<T>(string path, [MaybeNullWhen(false)] out T value)
        {
            var node = section.GetSection(path);

            if (!node.Exists())
            {
                value = default;

                return false;
            }

            // A level states a value in one of two shapes — a text or a subtree — and a type is read
            // from one of them. When the two do not meet, the binder answers with the default of the
            // type or with null, and either one handed back as a value that WAS read would make the
            // level state something it never stated: neither the "nothing" that cedes to the level
            // below (SPEC-012 §10.3) nor a failure the mechanism can see. The other source raises on
            // such a pair, and so does this one.
            var statesText = node.Value is not null;

            if (statesText != IsReadFromText<T>())
            {
                throw NotTheStatedShape<T>(node.Path, statesText);
            }

            var read = node.Get<T>(StrictBinding);

            if (read is null)
            {
                value = default;

                return false;
            }

            value = read;

            return true;
        }

        /// <summary>
        /// Binding that says out loud what the standard one swallows. Without it the binder DROPS an
        /// element of a collection it cannot convert and hands back the rest, so a misspelt member of a
        /// set arrives as a smaller set that is perfectly valid — the level states a narrowing it never
        /// wrote, and no fact of the mechanism arises to say so (SPEC-012 CFG-240/CFG-246, CFG-160).
        /// With the flag the read FAILS, and the boundary that called it decides by the policy of the
        /// key: above the core level the record is discarded, at the core level a key declaring
        /// FailStart stops the host.
        /// <para>
        /// The same flag also refuses a member of the subtree the type does not carry — a leftover of a
        /// previous version, a stray key, a "comment" written as a field. That is the accepted price of
        /// the guarantee and not a side effect to be trimmed away for some shapes and kept for others:
        /// the flag stands on the whole read of a subtree, and a read that is strict about one member
        /// and lenient about the next would be exactly the silence this removes.
        /// </para>
        /// </summary>
        private static void StrictBinding(BinderOptions options) =>
            options.ErrorOnUnknownConfiguration = true;
    }

    /// <summary>
    /// Subtree over the JSON element of a store record. Segments are matched case-insensitively and
    /// the FIRST match wins — the same addressing the configuration library gives a section, so one
    /// path spells one address in both sources.
    /// </summary>
    /// <param name="root">Element the paths are relative to.</param>
    private sealed class JsonSubtree(JsonElement root) : Subtree
    {
        /// <summary>
        /// How a value NESTED inside a composite value is read into the requested type. Numbers are
        /// accepted from a string and enums by name, because that is what the configuration binder does
        /// with the same value on the other source. A scalar at the path ITSELF never goes through
        /// these settings: it is read from its text by the binder's own converter
        /// (<see cref="ReadFromText{T}"/>), which is what makes the two sources one contract rather
        /// than two dialects tuned to look alike.
        /// </summary>
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <inheritdoc />
        public override bool Has(string path) => TryFind(path, out _);

        /// <inheritdoc />
        /// <remarks>
        /// A name the record states TWICE — a typed property and an unknown field spelled alike — is
        /// listed once, exactly as it is READ once (see <see cref="TryGetMember"/>): the walk would
        /// otherwise report one value of the record under one address twice over.
        /// </remarks>
        public override IEnumerable<string> Children(string path)
        {
            if (!TryFind(path, out var element) || element.ValueKind is not JsonValueKind.Object)
            {
                return [];
            }

            var names = new List<string>();
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in element.EnumerateObject())
            {
                if (listed.Add(property.Name))
                {
                    names.Add(property.Name);
                }
            }

            return names;
        }

        /// <inheritdoc />
        public override bool TryRead<T>(string path, [MaybeNullWhen(false)] out T value)
        {
            if (!TryFind(path, out var element))
            {
                value = default;

                return false;
            }

            // The same pairing of shapes the other source checks: a scalar of the record answers a type
            // read from text, a subtree answers a type bound from members, and a mismatch is a failed
            // read rather than a value fabricated out of nothing.
            var statesScalar = IsScalar(element);

            if (statesScalar != IsReadFromText<T>())
            {
                throw NotTheStatedShape<T>(path, statesScalar);
            }

            // A record may spell a scalar in quotes ("true", "8"): on the levels served by the
            // application configuration every value is text to begin with, so a value readable at one
            // level must not be unreadable at another merely because a store keeps its own JSON types.
            var read = statesScalar
                ? ReadFromText<T>(element, path)
                : element.Deserialize<T>(SerializerOptions);

            if (read is null)
            {
                value = default;

                return false;
            }

            value = read;

            return true;
        }

        /// <summary>
        /// Whether the element states a single value rather than a subtree.
        /// </summary>
        /// <param name="element">Element at the path.</param>
        /// <returns>true when the element is a scalar.</returns>
        private static bool IsScalar(JsonElement element) =>
            element.ValueKind is JsonValueKind.String
                or JsonValueKind.Number
                or JsonValueKind.True
                or JsonValueKind.False;

        /// <summary>
        /// Reads a scalar of the record from its text, through the type converter the configuration
        /// binder converts a configuration value with — the same conversion the other source performs
        /// on the same text, rather than a second dialect of the same configuration.
        /// </summary>
        /// <typeparam name="T">Type the value is read into.</typeparam>
        /// <param name="element">Scalar element at the path.</param>
        /// <param name="path">Relative path, for the message of a failed read.</param>
        /// <returns>Value read, or the default of the type when the text states nothing.</returns>
        /// <exception cref="InvalidOperationException">The text is not a value of the type.</exception>
        private static T? ReadFromText<T>(JsonElement element, string path)
        {
            var text = element.ValueKind is JsonValueKind.String
                ? element.GetString()
                : element.GetRawText();

            if (text is null)
            {
                return default;
            }

            object? converted;

            try
            {
                converted = TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(text);
            }
            catch (Exception exception)
            {
                // The failure of a read is ONE thing on both sources: the binder wraps the converter's
                // own exception the same way. A converter left unwrapped would also raise the
                // ArgumentException this type reserves for a caller's bad path, and the mechanism
                // would have no way to tell a broken record from a broken call.
                throw new InvalidOperationException(
                    $"Failed to read the value at '{path}' into '{typeof(T)}'.", exception);
            }

            return converted is null ? default : (T)converted;
        }

        /// <summary>
        /// Walks the path segment by segment from the root of the record.
        /// </summary>
        /// <param name="path">Relative path.</param>
        /// <param name="found">Element at the path, when the path leads to a stated value.</param>
        /// <returns>true when the path leads to a stated value.</returns>
        private bool TryFind(string path, out JsonElement found)
        {
            var current = root;

            foreach (var segment in path.Split(PathSeparator))
            {
                if (current.ValueKind is not JsonValueKind.Object || !TryGetMember(current, segment, out current))
                {
                    found = default;

                    return false;
                }
            }

            // A member stated as JSON null states nothing: the level is skipped and the value comes
            // from the level below, exactly as for a path that is not in the record at all.
            if (current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                found = default;

                return false;
            }

            found = current;

            return true;
        }

        /// <summary>
        /// Finds a member of a JSON object by name, case-insensitively, taking the first match.
        /// </summary>
        /// <param name="element">Object being addressed.</param>
        /// <param name="name">Member name.</param>
        /// <param name="member">Member found.</param>
        /// <returns>true when the object has such a member.</returns>
        /// <remarks>
        /// The first match is what resolves a record that states one name twice — a typed property of
        /// the record and an unknown field of the same name, both serialized into this element. The
        /// typed property is written first and therefore wins, which keeps an already deployed
        /// installation reading the value it read before.
        /// </remarks>
        private static bool TryGetMember(JsonElement element, string name, out JsonElement member)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    member = property.Value;

                    return true;
                }
            }

            member = default;

            return false;
        }
    }
}
