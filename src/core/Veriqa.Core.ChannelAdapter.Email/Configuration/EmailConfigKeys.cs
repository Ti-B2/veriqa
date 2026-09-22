// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.Email.Configuration;

/// <summary>
/// Canonical resolver keys owned by the Email channel (TASK-040, SPEC-012 §10.6). The channel owns
/// the CATALOG its keys are declared into and binds their core level itself
/// (<see cref="EmailConfigKeyRegistrar"/>), so no shared registry has to list the channels the
/// product happens to ship.
/// <para>
/// The catalog is registered where the channel is COMPOSED — <c>AddEmail()</c> — and that is what
/// carries the key into the schema of a deployment that added this channel and leaves it out of one
/// that did not. Declaring a key and binding a level of it are different acts (CFG-203): the
/// declaration lives here, the core-level getter in the registrar named above.
/// </para>
/// </summary>
public static class EmailConfigKeys
{
    /// <summary>
    /// Catalog of the declarations of this channel — what its registration puts into the schema of
    /// the deployment. It is initialized before the declarations that fill it, which is the order the
    /// initializers of this class run in.
    /// </summary>
    private static readonly ConfigKeyCatalog Declared = new();

    /// <summary>
    /// Email tenant-credential group of the layer (<c>Channels.email.Credentials</c>).
    /// </summary>
    public static ConfigKey<EmailTenantCredentials> Credentials { get; } =
        ChannelConfigKeys.CredentialKey<EmailTenantCredentials>(Declared, ChannelTypes.Email);

    /// <summary>
    /// Declarations of this channel — what <c>AddEmail()</c> registers into the container, and what
    /// the schema of a deployment carries because of it.
    /// </summary>
    public static ConfigKeyCatalog Catalog => Declared;
}
