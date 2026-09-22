// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Registry of the canonical resolver key of login confirmation (SPEC-012 §4.4, §10.6): the
/// confirmation mode (<see cref="LoginConfirmation"/>). It is resolved as a plain value over the core,
/// tenant and application levels, and each level overrides the one below it freely.
/// There is no second precedence mechanism (anti-fork CFG-202).
/// <para>
/// The WORDING of a confirmation is not a key of this registry: it is a message, so a level states it
/// as the template ladder of that message (SPEC-036 TPL-116) and it reaches the user through the same
/// resolution as the product's own wording — never as a text handed past the message mechanism.
/// </para>
/// <para>
/// The key is declared in ChannelAdapter (not AuthServer), since its consumers live here: the
/// confirmation orchestrator in the pipeline
/// (<see cref="Pipeline.IEffectiveConfirmationSurfaceResolver"/>) and the channel adapters that send
/// the confirmation prompt — and ChannelAdapter cannot reference AuthServer (a reverse dependency).
/// Each key states its levels WITH THE ADDRESS OF EACH, and the ONE registrar of the deployment
/// (<c>PathConfigKeyRegistrar</c>) declares the key and binds every one of them: the core level inside
/// the global section, the levels above it inside the record of their own level, which the auth server
/// fetches — declaring a key and reading the record of a level are different owners, which is exactly
/// what the contract allows (CFG-203).
/// </para>
/// </summary>
public static class LoginConfirmationConfigKeys
{
    /// <summary>
    /// Catalog of the declarations of this registry — what the one registrar of the deployment declares
    /// and binds. It is initialized before the declarations that fill it, which is the order the
    /// initializers of this class run in.
    /// </summary>
    private static readonly ConfigKeyCatalog Declared = new();

    /// <summary>
    /// Login confirmation AS THE PRODUCT SHIPS IT — a default-constructed options object, read for one
    /// thing only: the mode a core level yields when the global section states nothing. The shipped
    /// value is taken off the options class rather than restated here, so the two cannot drift.
    /// </summary>
    private static readonly LoginConfirmationOptions Shipped = new();

    /// <summary>
    /// Canonical name of the login confirmation key.
    /// </summary>
    public const string LoginConfirmationKeyName = "LoginConfirmation.Mode";

    /// <summary>
    /// Member the confirmation surface is stated at in the record of a level above the core — the
    /// record of a tenant and the OIDC client entry of an application alike. It is written as text
    /// rather than taken from the entry's type: the entry belongs to the auth server, and this contour
    /// does not reference it (a reverse dependency).
    /// </summary>
    private const string ClientLoginConfirmationMode = "LoginConfirmationMode";

    /// <summary>
    /// Login confirmation (CFG-040…042): a plain value over the core, tenant and application levels —
    /// each level overrides the one below it freely. The core level always states a value — the mode
    /// of the global section, or the one the product ships with (<c>Default</c>): an unset core level
    /// would leave the pipeline with the default of the enum instead of the default of the setting.
    /// </summary>
    public static ConfigKey<LoginConfirmationMode> LoginConfirmation { get; } = Declared
        .Of<LoginConfirmationMode>(LoginConfirmationKeyName)
        .At(
            ConfigLevel.Core,
            LoginConfirmationOptions.SectionName + ":" + nameof(LoginConfirmationOptions.Mode))
        .At(ConfigLevel.Tenant, ClientLoginConfirmationMode)
        .At(ConfigLevel.Application, ClientLoginConfirmationMode)
        .Default(Shipped.Mode)
        .Declare();

    /// <summary>
    /// Declarations of this registry — what the one registrar of the deployment declares and binds.
    /// </summary>
    public static ConfigKeyCatalog Catalog => Declared;
}
