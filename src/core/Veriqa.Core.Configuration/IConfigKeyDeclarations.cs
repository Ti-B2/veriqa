// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Registry of key DECLARATIONS — the step that fills the schema of the deployment (SPEC-012 §10.6).
/// It is deliberately separate from the registry of extraction bindings
/// (<see cref="IConfigBindings"/>): a key exists because its owner declared it, not because somebody
/// happened to bind a level to it.
/// <para>
/// The separation is what makes a check over the schema AS A WHOLE possible: a key nobody has bound
/// yet is present in the schema and can be checked, whereas a schema filled by first bindings can
/// only ever describe the keys that already have one — and says nothing about the key whose binding
/// was forgotten.
/// </para>
/// <para>
/// Declarations are applied BEFORE any binding is registered, so the order of the registrars in the
/// container does not decide whether a binding finds its key.
/// </para>
/// <para>
/// A key declared HERE AND NOWHERE ELSE — by a registrar written by hand, without a
/// <see cref="ConfigKeyCatalog"/> — is a key the runtime resolves and the SCHEMA does not have. The
/// schema of a deployment is assembled from the registered catalogs, so such a key is absent from
/// <c>--dump-config-schema</c>, from the configuration tables of the client documentation built out
/// of that answer, and from the walk of a configuration snapshot, whose catalogs are derived from
/// declarations too. Nothing reports the absence: an answer that never mentions a key looks exactly
/// like an answer about a key that does not exist. The norm is therefore the catalog, and this
/// interface is what the catalog's own registrar declares THROUGH.
/// </para>
/// </summary>
public interface IConfigKeyDeclarations
{
    /// <summary>
    /// Declares a setting key: its name, its levels and every other boundary it carries enter the
    /// schema of the deployment.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <param name="declaredDefault">
    /// What the key declares its CORE level answers where the record of that level states nothing, as
    /// a schema states it; null — the owner declared no such answer. It is handed in beside the key
    /// rather than taken off it, because it belongs to the DECLARATION of the key — the same place its
    /// reading does (<see cref="DeclaredConfigKey{T}"/>) — while the key itself is the object every
    /// consumer resolves by.
    /// </param>
    /// <param name="notWalked">
    /// The walk of a configuration snapshot does not reproduce the value of this key, so the report of
    /// the snapshot NAMES its pairs as standing outside it instead of saying nothing about them. Like
    /// the answer above it belongs to the DECLARATION of the key rather than to the key itself.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The key has already been declared: a second declaration of one name would make the schema
    /// depend on the order of the registrations.
    /// </exception>
    void Declare<T>(ConfigKey<T> key, ConfigDeclaredDefault? declaredDefault = null, bool notWalked = false);
}
