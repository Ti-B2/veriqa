// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Json;

using Veriqa.Core.Configuration;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// The message declarations the product SHIPS — a configuration file inside the assembly, the same way
/// the locale files are shipped, read as the bottom layer of the core level (SPEC-036 §4.6).
/// <para>
/// It is a file and not a handwritten C# table on purpose: a table in code is exactly the second home
/// of the core values the two settings exist to remove. Everything a deployment may state, the product
/// states in the same shape at the same addresses — so "what the core declares" is read, not compiled.
/// </para>
/// <para>
/// <b>Why a source of its own rather than a configuration layer under the host's.</b> The
/// configuration library merges a LIST by index: a deployment stating one variant where the product
/// ships three would keep the product's variants 2 and 3 behind its own, and a rendered message would
/// carry a text the deployment never wrote. Read as a separate record, consulted only where the host
/// configuration states nothing AT THE SAME ADDRESS, the shipped declaration is REPLACED whole by the
/// one a deployment writes — which is what "the ladder of a step is one value" means (SPEC-036 §9).
/// The observable invariant is the one the decision names: an installation without a single line of
/// <c>Veriqa:MessageTemplates</c> renders exactly the texts the product shipped.
/// </para>
/// </summary>
internal static class ShippedMessageTemplates
{
    /// <summary>
    /// Name of the embedded resource holding the shipped declarations.
    /// </summary>
    private const string ResourceName =
        "Veriqa.Core.TransactionEngine.MessageTemplates.ShippedMessageTemplates.json";

    /// <summary>
    /// Label the record carries in diagnostics — what an operator recognizes the source by when a
    /// value of it could not be read.
    /// </summary>
    private const string RecordLabel = "shipped message templates";

    /// <summary>
    /// The parsed file. Held for the lifetime of the process: it is read-only, tiny, and the node
    /// below stands over an element of it.
    /// </summary>
    private static readonly JsonDocument Document = Load();

    /// <summary>
    /// The shipped record, addressed exactly as the core level is: the node stands over the root
    /// section of the product configuration, so one address serves the host configuration and this
    /// file alike.
    /// </summary>
    public static ConfigNode Node { get; } = ConfigNode.Over(
        Document.RootElement.GetProperty(MessageTemplatesOptions.RootSectionName),
        ConfigLevel.Core,
        RecordLabel);

    /// <summary>
    /// The shipped declarations in the shape the section has — what the startup validation checks the
    /// product's own file against, and the fallback the conformance check reads a contract from when a
    /// deployment states only a template.
    /// </summary>
    public static MessageTemplatesOptions Options { get; } = ReadOptions();

    /// <summary>
    /// Reads the embedded file.
    /// </summary>
    /// <returns>The parsed document.</returns>
    /// <exception cref="InvalidOperationException">The assembly carries no such resource.</exception>
    private static JsonDocument Load()
    {
        using var stream = typeof(ShippedMessageTemplates).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The shipped message declarations are missing from the assembly: no resource '{ResourceName}'.");

        return JsonDocument.Parse(stream);
    }

    /// <summary>
    /// Reads the shipped declarations into the shape of the section.
    /// </summary>
    /// <returns>The shipped declarations.</returns>
    private static MessageTemplatesOptions ReadOptions()
    {
        var options = new MessageTemplatesOptions();
        var group = Document.RootElement
            .GetProperty(MessageTemplatesOptions.RootSectionName)
            .GetProperty(MessageTemplatesOptions.GroupName);

        foreach (var kind in group.EnumerateObject())
        {
            var declaration = kind.Value.Deserialize<MessageTemplateKindOptions>(MessageTemplateJson.KindOptions);

            if (declaration is not null)
            {
                options.Kinds[kind.Name] = declaration;
            }
        }

        return options;
    }
}

/// <summary>
/// How the shipped file is read into the shape of the section: the same case-insensitive member
/// matching and the same enum-by-name spelling the configuration binder gives the very same members on
/// the host's side, so one shape is written once and read alike from both sources.
/// </summary>
internal static class MessageTemplateJson
{
    /// <summary>
    /// Reading options of the shipped file.
    /// </summary>
    public static readonly JsonSerializerOptions KindOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
