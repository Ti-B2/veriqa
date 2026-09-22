// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// The two settings a message is made of (SPEC-036 §4.6, TPL-116): the CONTRACT of the message —
/// which slots exist — and the TEMPLATE ladder that uses them. They are ordinary keys of the canonical
/// resolver, declared by address like any other (SPEC-012 §10.6, anti-fork CFG-202): there is no
/// second precedence mechanism and no registry of messages beside the deployment's configuration.
/// <para>
/// <b>Why two keys and not one value.</b> Overriding a text is then one address and one array of
/// variants — the slots are declared once and are not restated by whoever rewrites the wording. It is
/// also what closes <c>ui_config</c> structurally: the level is declared by the TEMPLATE alone, so a
/// record chosen by a request parameter can pick which of the deployment's wordings a user sees and
/// can neither widen the slot allowlist nor introduce an action type of its own (TPL-108).
/// </para>
/// <para>
/// <b>The ladders are handwritten</b> (<see cref="ConfigKeyBuilder{T}.Fallback"/>) rather than
/// generated from the dimensions: the generated ladder nests the attributes strictly one inside the
/// other, and a step naming the surface WITHOUT the transaction type is one a message needs. The
/// composition follows two rules — what the action is about outranks where it is shown
/// (<c>action_type</c> ⟩ <c>surface</c>), and the channel refines the surface, so it is the most
/// specific attribute and never appears without one. The kind is the IDENTITY dimension: "the text of
/// WHICH message" is the question itself, so it stands in every step.
/// </para>
/// <para>
/// The <c>channel</c> axis was declared before its first consumer, and that order paid off: the
/// prefilled deep-link message now states values along it, while every other kind still states none.
/// Declaring it early costs nothing — a dimension the caller does not name is not projected onto the
/// step, and the step that would have addressed it behaves as the coarser one — while the reverse,
/// asking a key about a dimension it does not declare, is an error (SPEC-012 §10.6). Declaring the
/// axis later would therefore have been the expensive order, not the cheap one.
/// </para>
/// <para>
/// The <c>Tenant</c> level is declared as SUPPORTED, not as shipped: the core contour registers no
/// node reader for it, and a level nobody binds simply states nothing and lets the resolution drop to
/// <c>Application</c>/<c>Core</c>. That is the mechanism working, not a gap.
/// </para>
/// </summary>
public static class MessageTemplateConfigKeys
{
    /// <summary>
    /// Identity dimension of both keys: WHICH message is being asked about. It stands in every step of
    /// both ladders and never drops out of them (SPEC-012 CFG-239).
    /// </summary>
    public const string KindDimensionName = "kind";

    /// <summary>Transaction type the message belongs to (sign-in, confirmation, …).</summary>
    public const string TransactionTypeDimensionName = "transaction_type";

    /// <summary>Action being confirmed or reported — the subject of the message.</summary>
    public const string ActionTypeDimensionName = "action_type";

    /// <summary>Surface the message is shown on (in-channel, a served page, a mail).</summary>
    public const string SurfaceDimensionName = "surface";

    /// <summary>Channel the message goes through — a refinement of the surface.</summary>
    public const string ChannelDimensionName = "channel";

    /// <summary>Canonical name of the message-contract key.</summary>
    public const string ContractKeyName = "MessageTemplates.Contract";

    /// <summary>Canonical name of the message-template key.</summary>
    public const string TemplateKeyName = "MessageTemplates.Template";

    /// <summary>
    /// Member of a declaration node the contract is stated at.
    /// </summary>
    private const string ContractMember = "Contract";

    /// <summary>
    /// Member of a declaration node the template ladder is stated at.
    /// </summary>
    private const string TemplatesMember = "Templates";

    /// <summary>
    /// Group of a declaration node narrowing by the transaction type.
    /// </summary>
    public const string ByTypeGroup = "ByType";

    /// <summary>
    /// Group of a declaration node narrowing by the action type.
    /// </summary>
    public const string ByActionGroup = "ByAction";

    /// <summary>
    /// Group of a declaration node narrowing by the surface.
    /// </summary>
    public const string BySurfaceGroup = "BySurface";

    /// <summary>
    /// Group of a declaration node narrowing by the channel.
    /// </summary>
    public const string ByChannelGroup = "ByChannel";

    /// <summary>
    /// Address of a declaration node inside the record of a level: the kind, then the axis groups a
    /// step keeps. A group the step does not address is dropped whole, which is what lets ONE address
    /// serve every step of the ladder (SPEC-012 CFG-238).
    /// </summary>
    private const string NodeAddress =
        MessageTemplatesOptions.GroupName + ":{" + KindDimensionName + "}"
        + ":[" + ByTypeGroup + ":{" + TransactionTypeDimensionName + "}]"
        + ":[" + ByActionGroup + ":{" + ActionTypeDimensionName + "}]";

    /// <summary>
    /// Address of the contract inside the record of a level.
    /// </summary>
    private const string ContractAddress = NodeAddress + ":" + ContractMember;

    /// <summary>
    /// Address of the template ladder inside the record of a level — the contract's address with the
    /// two presentation axes the template has and the contract has not.
    /// </summary>
    private const string TemplateAddress =
        NodeAddress
        + ":[" + BySurfaceGroup + ":{" + SurfaceDimensionName + "}]"
        + ":[" + ByChannelGroup + ":{" + ChannelDimensionName + "}]"
        + ":" + TemplatesMember;

    /// <summary>
    /// The core level has no record: its address names the section of the host configuration and the
    /// path inside it (SPEC-012 §10.1).
    /// </summary>
    private const string CoreContractAddress = MessageTemplatesOptions.RootSectionName + ":" + ContractAddress;

    /// <summary>
    /// Address of the template ladder in the host configuration.
    /// </summary>
    private const string CoreTemplateAddress = MessageTemplatesOptions.RootSectionName + ":" + TemplateAddress;

    /// <summary>
    /// Catalog of the declarations of this owner. It is initialized before the declarations that fill
    /// it, which is the order the initializers of this class run in.
    /// </summary>
    private static readonly ConfigKeyCatalog Declared = new();

    /// <summary>
    /// Ladder of the CONTRACT: the subject of the message outranks nothing else — a contract knows
    /// only what the message is about, never where it is shown.
    /// </summary>
    private static readonly string[][] ContractLadder =
    [
        [KindDimensionName, TransactionTypeDimensionName, ActionTypeDimensionName],
        [KindDimensionName, TransactionTypeDimensionName],
        [KindDimensionName]
    ];

    /// <summary>
    /// Ladder of the TEMPLATE (SPEC-036 §4.6): the subject first, the surface after it, the channel as
    /// a refinement of the surface. The steps naming a surface without a transaction type are what the
    /// generated ladder cannot express and the reason this one is written out.
    /// </summary>
    private static readonly string[][] TemplateLadder =
    [
        [KindDimensionName, TransactionTypeDimensionName, ActionTypeDimensionName, SurfaceDimensionName, ChannelDimensionName],
        [KindDimensionName, TransactionTypeDimensionName, ActionTypeDimensionName, SurfaceDimensionName],
        [KindDimensionName, TransactionTypeDimensionName, ActionTypeDimensionName],
        [KindDimensionName, TransactionTypeDimensionName, SurfaceDimensionName, ChannelDimensionName],
        [KindDimensionName, TransactionTypeDimensionName, SurfaceDimensionName],
        [KindDimensionName, TransactionTypeDimensionName],
        [KindDimensionName, SurfaceDimensionName, ChannelDimensionName],
        [KindDimensionName, SurfaceDimensionName],
        [KindDimensionName]
    ];

    /// <summary>
    /// Contract of a message: the slots it declares. Levels — <c>Tenant</c>, <c>Application</c>,
    /// <c>Core</c>; <c>ui_config</c> is deliberately NOT among them (TPL-108), which is what makes the
    /// slot allowlist unreachable from a record a request parameter selects.
    /// <para>
    /// The WALK of a configuration snapshot does not read this pair, and says so itself: the address of
    /// every level substitutes the IDENTITY dimension of the key outside its groups, so there is no
    /// address of a step narrowing nothing for a walk to read the records at, and the report names the
    /// pair as standing outside it (<see cref="IDimensionBoundCatalog"/>). What such a walk would run
    /// into if that ever changed is stated here rather than left to be discovered: the type of the key
    /// is the CONTRACT, while a record states the OPTIONS a contract is read from, so a reading by the
    /// address alone would fail — <see cref="MessageContract"/> has only a private constructor. It
    /// would need the reading DECLARED, the way a key declares one today
    /// (<c>ConfigKeyBuilder.EnumTokens</c>), and not the parse hook, which reads a step of the chain
    /// and is applied by the path of reading that calls it.
    /// </para>
    /// </summary>
    public static ConfigKey<MessageContract?> Contract { get; } = Declared
        .Of<MessageContract?>(ContractKeyName)
        .Identity(KindDimensionName)
        .Narrowing(TransactionTypeDimensionName, ActionTypeDimensionName)
        .Fallback(ContractLadder)
        .At(ConfigLevel.Tenant, ContractAddress)
        .At(ConfigLevel.Application, ContractAddress)
        .At(ConfigLevel.Core, CoreContractAddress)
        .Parse(static (node, path, _) => Read(node, path, ReadContract))
        // A contract of NO slots is admitted: "this message substitutes nothing" is a statement a
        // message makes, and it is the one the shipped outcome receipt makes (SPEC-036 TPL-123 lets
        // the group carry server slots and the caller slots its contract declares, and what a level
        // declares is configuration, not a property of the kind). What the
        // domain refuses is the absence of a contract where a level claimed to state one — a stated
        // step that read as nothing at all leaves the message without its allowlist.
        .Domain(new ConfigValueDomain<MessageContract?>(
            static contract => contract is not null,
            "a message contract is a well-formed declaration of the slots the message has, the empty "
                + "set of them included",
            MessageTemplatesOptions.SectionName + ":<kind>:" + ContractMember))
        .Declare();

    /// <summary>
    /// Template ladder of a message: its variants, fullest → minimal, as ONE value — the step that
    /// states a ladder replaces it whole, and variants of different steps are never merged. The
    /// <c>ui_config</c> level is declared here and only here: a record selected by a request parameter
    /// picks which of the deployment's wordings a user sees (TPL-108).
    /// </summary>
    public static ConfigKey<MessageTemplateVariant[]?> Template { get; } = Declared
        .Of<MessageTemplateVariant[]?>(TemplateKeyName)
        .Identity(KindDimensionName)
        .Narrowing(
            TransactionTypeDimensionName,
            ActionTypeDimensionName,
            SurfaceDimensionName,
            ChannelDimensionName)
        .Fallback(TemplateLadder)
        .At(ConfigLevel.Tenant, TemplateAddress)
        .At(ConfigLevel.Application, TemplateAddress)
        .At(ConfigLevel.UiConfig, TemplateAddress)
        .At(ConfigLevel.Core, CoreTemplateAddress)
        .Parse(static (node, path, _) => Read(node, path, ReadTemplates))
        // A STATED edition of nothing but whitespace is refused as firmly as a variant stating none: an
        // operator believes that wording is in effect, and a recipient would receive it empty. An
        // edition the variant does not state at all is the legitimate "this step does not serve that
        // sink" and is not blank — the same reading the startup validation holds over the declarations
        // it can enumerate, asked here of the levels it cannot reach.
        .Domain(new ConfigValueDomain<MessageTemplateVariant[]?>(
            static templates => templates is { Length: > 0 }
                && Array.TrueForAll(
                    templates,
                    static variant => variant is not null
                        && (variant.Html is not null || variant.Plain is not null)
                        && (variant.Html is null || !string.IsNullOrWhiteSpace(variant.Html))
                        && (variant.Plain is null || !string.IsNullOrWhiteSpace(variant.Plain))),
            "a template ladder holds at least one variant, every variant states an edition, and no "
                + "edition a variant states is blank",
            MessageTemplatesOptions.SectionName + ":<kind>:" + TemplatesMember))
        .Declare();

    /// <summary>
    /// Declarations of this owner — what the one registrar of the deployment declares and binds, and
    /// what the generator of the configuration tables of the documentation prints.
    /// </summary>
    public static ConfigKeyCatalog Catalog => Declared;

    /// <summary>
    /// Reads one step of the chain, falling back to the SHIPPED declarations at the core level.
    /// <para>
    /// The fallback belongs to the core level alone: above it a level that states nothing cedes to the
    /// level below, and the product's own declarations are what the bottom of that descent holds. It
    /// is asked at the SAME address, so a deployment that states the step overrides it whole — the
    /// list of variants of one step never arrives half from the deployment and half from the product.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="node">Subtree of the level's record.</param>
    /// <param name="path">Address the step resolved to.</param>
    /// <param name="read">Reading of the value out of a node.</param>
    /// <returns>Layer value of the step.</returns>
    private static LayerValue<T> Read<T>(
        ConfigNode node,
        string path,
        Func<ConfigNode, string, LayerValue<T>> read)
    {
        var stated = read(node, path);

        if (stated.HasValue || node.Level is not ConfigLevel.Core)
        {
            return stated;
        }

        return read(ShippedMessageTemplates.Node, path);
    }

    /// <summary>
    /// Reads a contract out of a record.
    /// </summary>
    /// <param name="node">Subtree of the record.</param>
    /// <param name="path">Address the step resolved to.</param>
    /// <returns>Layer value of the step.</returns>
    private static LayerValue<MessageContract?> ReadContract(ConfigNode node, string path) =>
        node.TryRead<MessageContractOptions>(path, out var stated)
            ? LayerValue<MessageContract?>.Set(MessageContract.Read(stated))
            : LayerValue<MessageContract?>.None;

    /// <summary>
    /// Reads a template ladder out of a record.
    /// </summary>
    /// <param name="node">Subtree of the record.</param>
    /// <param name="path">Address the step resolved to.</param>
    /// <returns>Layer value of the step.</returns>
    private static LayerValue<MessageTemplateVariant[]?> ReadTemplates(ConfigNode node, string path) =>
        node.TryRead<MessageTemplateVariant[]>(path, out var stated)
            ? LayerValue<MessageTemplateVariant[]?>.Set(stated)
            : LayerValue<MessageTemplateVariant[]?>.None;
}
