// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.TransactionEngine.Configuration;

/// <summary>
/// Resolver key registry for the initiator context (SPEC-017 ICC-081).
/// DisplayFields/Enabled are resolved per-application over the core default — the two levels the keys
/// declare, each with the address it is read at (SPEC-012 §10.6): the global section for the core
/// level, the member of the OIDC client entry for the application one.
/// </summary>
public static class InitiatorContextConfigKeys
{
    /// <summary>
    /// Catalog of the declarations of this registry — what the one registrar of the deployment declares
    /// and binds. It is initialized before the declarations that fill it, which is the order the
    /// initializers of this class run in.
    /// </summary>
    private static readonly ConfigKeyCatalog Declared = new();

    /// <summary>
    /// The initiator-context settings AS THE PRODUCT SHIPS THEM — a default-constructed options object,
    /// read for one thing only: the value a core level yields when the section states nothing. For the
    /// displayed fields that default is a non-empty set (ICC-015), so it is what the core level has
    /// always contributed to the intersection; taking it off the options class rather than restating it
    /// keeps the two from drifting.
    /// </summary>
    private static readonly InitiatorContextOptions Shipped = new();

    /// <summary>
    /// Members of the OIDC client entry the per-application initiator context is stated at. They are
    /// written as text rather than taken from the entry's type: the entry belongs to the auth server,
    /// which references this assembly and not the other way round.
    /// </summary>
    private const string ClientDisplayFields = "InitiatorContextDisplayFields";

    /// <summary>
    /// Member of the OIDC client entry carrying the per-application display flag.
    /// </summary>
    private const string ClientEnabled = "InitiatorContextEnabled";

    /// <summary>
    /// Set of displayed initiator context fields. A set (CFG-211, narrowing):
    /// the application may only narrow the core set (intersection). The effective set =
    /// the intersection of the declared levels that are specified.
    /// </summary>
    public static ConfigKey<IReadOnlyList<string>> DisplayFields { get; } = Declared
        .Of<IReadOnlyList<string>>("InitiatorContext.DisplayFields")
        .At(
            ConfigLevel.Core,
            InitiatorContextOptions.SectionName + ":" + nameof(InitiatorContextOptions.DisplayFields))
        .At(ConfigLevel.Application, ClientDisplayFields)
        .Set(static (upper, lower) =>
        {
            // Intersection: keep only fields present in both the upper and the lower set
            var allowed = new HashSet<string>(upper, StringComparer.OrdinalIgnoreCase);
            return lower.Where(allowed.Contains).ToList();
        })
        .Default(Shipped.DisplayFields)
        .Declare();

    /// <summary>
    /// Flag controlling whether the initiator context is displayed in the confirmation. A plain value:
    /// the application overrides the core default (it controls DISPLAY specifically, not collection).
    /// </summary>
    public static ConfigKey<bool> Enabled { get; } = Declared
        .Of<bool>("InitiatorContext.Enabled")
        .At(ConfigLevel.Core, InitiatorContextOptions.SectionName + ":" + nameof(InitiatorContextOptions.Enabled))
        .At(ConfigLevel.Application, ClientEnabled)
        .Default(Shipped.Enabled)
        .Declare();

    /// <summary>
    /// Declarations of this registry — what the one registrar of the deployment declares and binds.
    /// </summary>
    public static ConfigKeyCatalog Catalog => Declared;
}
