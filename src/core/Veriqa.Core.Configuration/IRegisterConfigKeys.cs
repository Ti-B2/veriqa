// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Hook for declaring the extraction bindings of a consumer's setting keys. Implementations state how
/// the value of THEIR key is read at each level they bind: the core level through
/// <see cref="IConfigCoreBindings"/> (a closure over the global options) and every other level
/// through <see cref="IConfigBindings"/> (a typed reader of the source record plus an extraction).
/// All implementations are applied once at application startup, before the first resolution.
/// <para>
/// A binding is registered by the assembly that owns the READER of the record, which need not be the
/// owner of the key: the OIDC client entry is visible to the auth server alone, so it binds the
/// application level of keys declared by the transaction engine.
/// </para>
/// </summary>
public interface IRegisterConfigKeys
{
    /// <summary>
    /// Declares the keys this registrar OWNS — the step that fills the schema of the deployment
    /// (<see cref="IConfigKeyDeclarations"/>). It runs for every registrar BEFORE the first binding is
    /// registered, so the order of the registrations does not decide whether a binding finds its key.
    /// <para>
    /// The member is not optional on purpose: a registrar that declares nothing says so explicitly,
    /// and the author of a new one is made to answer which keys it owns instead of finding out at the
    /// first resolution that returned a default.
    /// </para>
    /// </summary>
    /// <param name="keys">Registry of key declarations.</param>
    void DeclareKeys(IConfigKeyDeclarations keys);

    /// <summary>
    /// Registers the bindings of the consumer's keys.
    /// </summary>
    /// <param name="bindings">Registry of level bindings over source records.</param>
    /// <param name="coreBindings">Registry of core-level bindings.</param>
    void Register(IConfigBindings bindings, IConfigCoreBindings coreBindings);
}
