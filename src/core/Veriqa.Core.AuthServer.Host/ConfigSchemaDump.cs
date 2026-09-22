// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Host;

/// <summary>
/// The answer this host gives to "what does this installation declare": the SCHEMA of the declared
/// keys it composed, written to the standard output, after which the host exits without serving a
/// request (SPEC-012 §10.6, CFG-244).
/// <para>
/// It answers from the CONTAINER — the very catalogs <see cref="PathConfigKeyRegistrar"/> walks when
/// this host starts for real — so the answer is what this composition resolves by and not a list
/// written a second time somewhere. A key that no composition of this host registers is absent from
/// the answer for the same reason it is absent from a resolution: nobody declared it.
/// </para>
/// <para>
/// Readers: an operator who has to see what the installation in front of them declares before setting
/// a single value, and the generator of the configuration tables of the client documentation, which
/// prints exactly this answer.
/// </para>
/// </summary>
internal static class ConfigSchemaDump
{
    /// <summary>
    /// Command-line switch asking the host for its schema instead of for service.
    /// </summary>
    public const string Switch = "--dump-config-schema";

    /// <summary>
    /// Line written before the document. The document shares the standard output with whatever the
    /// startup of a .NET host writes to it, so a reader needs the boundaries of the answer stated
    /// rather than guessed from the first brace.
    /// </summary>
    private const string BeginMarker = "veriqa-config-schema:begin";

    /// <summary>
    /// Line written after the document. Its ABSENCE is what tells a reader that the host stopped
    /// halfway, which no partial document could say on its own.
    /// </summary>
    private const string EndMarker = "veriqa-config-schema:end";

    /// <summary>
    /// How the document is written: named the way the members of the records below are named, and
    /// indented, because a human reads this output too.
    /// </summary>
    private static readonly JsonSerializerOptions DocumentFormat = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true
    };

    /// <summary>
    /// Whether the command line asks for the schema rather than for service.
    /// </summary>
    /// <param name="args">Command-line arguments of the host.</param>
    /// <returns><see langword="true"/> when the host is to state its schema and exit.</returns>
    public static bool Requested(string[] args) =>
        args is not null && Array.Exists(args, argument => string.Equals(argument, Switch, StringComparison.Ordinal));

    /// <summary>
    /// Writes the schema of the declared keys of a composition, between the markers.
    /// </summary>
    /// <param name="output">Where the document is written.</param>
    /// <param name="catalogs">Catalogs of declared keys the composition registered.</param>
    public static void Write(TextWriter output, IEnumerable<ConfigKeyCatalog> catalogs)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(catalogs);

        var reader = new DeclarationReader();

        foreach (var declared in catalogs.SelectMany(catalog => catalog.Keys))
        {
            declared.Accept(reader);
        }

        // The markers go on lines of their own, and the closing one only after the document is
        // complete: a reader that finds an opening marker without a closing one knows it is looking at
        // a run that died mid-answer rather than at a short schema.
        output.WriteLine(BeginMarker);
        output.WriteLine(JsonSerializer.Serialize(new SchemaDocument(reader.Keys), DocumentFormat));
        output.WriteLine(EndMarker);
        output.Flush();
    }

    /// <summary>
    /// Turns the declarations of the catalogs into the entries of the document. It is a visitor
    /// because half of what an entry states is on the ERASED half of a declaration (name, value type,
    /// levels with their addresses, dimensions) and half on the typed one (secrecy, the gated levels):
    /// the visitor is the seam the mechanism offers for exactly that.
    /// </summary>
    private sealed class DeclarationReader : IConfigKeyDeclarationVisitor
    {
        /// <summary>
        /// Entries collected so far, in the order the declarations were visited.
        /// </summary>
        public List<DeclaredKeyEntry> Keys { get; } = [];

        /// <inheritdoc />
        public void Visit<T>(DeclaredConfigKey<T> declared)
        {
            ArgumentNullException.ThrowIfNull(declared);

            var gated = declared.Key.GatedLevels;
            var nullable = Nullable.GetUnderlyingType(declared.ValueType);

            var valueType = nullable ?? declared.ValueType;

            Keys.Add(new DeclaredKeyEntry(
                declared.Name,
                // The type is stated the way the runtime spells it — its own name, its namespace, and
                // a Nullable<T> unwrapped into the type plus the flag. How a reader wants to SEE a
                // type is that reader's decision, and the namespace is what lets it be made: without
                // it "String" and a type of ours that happens to be called the same are one word.
                valueType.Name,
                valueType.Namespace,
                nullable is not null,
                declared.Key.IsSecret,
                // The declaration answers this itself, already reduced to what a schema may carry: the
                // text of the answer, or the bare fact of one for a key whose value is a secret.
                declared.DeclaredDefault is { } declaredDefault
                    ? new DeclaredDefaultEntry(declaredDefault.Text)
                    : null,
                [.. declared.Dimensions?.Names ?? []],
                [.. declared.Levels.Select(level => new DeclaredLevelEntry(
                    level.Level.ToString(),
                    level.Kind.ToString(),
                    level.Address,
                    gated?.Contains(level.Level) ?? false))]));
        }
    }

    /// <summary>
    /// The document: everything the composition declares. It is an object with one member rather than
    /// a bare array, so that a later answer can state something ABOUT the schema without breaking a
    /// reader of this one.
    /// </summary>
    /// <param name="Keys">Declared keys of the composition, in the order the catalogs state them.</param>
    private sealed record SchemaDocument(IReadOnlyList<DeclaredKeyEntry> Keys);

    /// <summary>
    /// One declared key: everything its DECLARATION states, and nothing an INSTALLATION put in it —
    /// the answer a key declares for its core level is the owner's own statement and belongs here,
    /// while a resolved value is nobody's declaration and never does.
    /// </summary>
    /// <param name="Name">Setting name.</param>
    /// <param name="ValueType">Name of the type of the value, with a nullable value type unwrapped.</param>
    /// <param name="ValueTypeNamespace">Namespace of that type; null for a type declared in none.</param>
    /// <param name="ValueNullable">The declared type is a nullable value type.</param>
    /// <param name="Secret">The value of the key is a secret.</param>
    /// <param name="DeclaredDefault">
    /// What the key declares its CORE level answers where the record of that level states nothing;
    /// null — the owner declared no such answer. It is an OBJECT and not a text so that the two
    /// answers stay apart in the document: a key that declares nothing carries a null here, while a
    /// key whose declared answer IS null carries an object with a null text.
    /// </param>
    /// <param name="Dimensions">Names of the key's dimensions, in order of decreasing specificity.</param>
    /// <param name="Levels">Levels the key is declared at, in the order the declaration states them.</param>
    private sealed record DeclaredKeyEntry(
        string Name,
        string ValueType,
        string? ValueTypeNamespace,
        bool ValueNullable,
        bool Secret,
        DeclaredDefaultEntry? DeclaredDefault,
        IReadOnlyList<string> Dimensions,
        IReadOnlyList<DeclaredLevelEntry> Levels);

    /// <summary>
    /// The answer a key declares for its core level, as the document states it: the text of the value,
    /// and nothing but the fact of the answer where there is no text to state — a declared answer whose
    /// value is null, and a key whose value is a secret.
    /// </summary>
    /// <param name="Text">Value of the answer as text; null when there is none to state.</param>
    private sealed record DeclaredDefaultEntry(string? Text);

    /// <summary>
    /// One level of a declared key: the level, the FORM of its address, the address itself where the
    /// form has one, and whether an override of this level requires an open gate.
    /// </summary>
    /// <param name="Level">Level of the key.</param>
    /// <param name="Kind">Form of the address the declaration gives this level.</param>
    /// <param name="Address">Address as it is written; null when the level has no path.</param>
    /// <param name="Gated">The override of this level requires an open gate.</param>
    private sealed record DeclaredLevelEntry(string Level, string Kind, string? Address, bool Gated);
}
