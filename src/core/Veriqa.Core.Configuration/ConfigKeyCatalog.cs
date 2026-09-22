// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Catalog of the keys ONE owner declares (SPEC-012 §10.6, CFG-244). It is both where a declaration
/// is written — <see cref="Text"/> / <see cref="Of{T}"/> open the chain — and what the declarations
/// are read from afterwards: the registrar of the bindings walks it, and so does anything else that
/// has to know what a deployment declares.
/// <para>
/// The catalog belongs to the OWNER of the keys rather than to the mechanism, and there is one per
/// owner: the module knows no key name and no record type (SPEC-012 CFG-231), and a single global
/// catalog would make the keys of two owners — and of two tests — one another's state.
/// </para>
/// <para>
/// A catalog is filled by the static initializer of the owner's key class and read afterwards, which
/// is why it is not synchronized: the field holding it is initialized before the first key that
/// declares into it.
/// </para>
/// <example>
/// The owner declares its catalog first and its keys after it, in one file:
/// <code>
/// internal static class AuthPageKeys
/// {
///     private static readonly ConfigKeyCatalog Declared = new();
///
///     public static readonly ConfigKey&lt;string?&gt; SignText = Declared
///         .Text("AuthPage.SignText")
///         .Narrowing("transaction_type", "outcome")
///         .At(ConfigLevel.Application, ConfigLevel.UiConfig)
///         .At(ConfigLevel.Core, "Veriqa:AuthPageDesign:SignText:[ByType:{transaction_type}]")
///         .Cached(ConfigCachePolicy.ExternalStore)
///         .Declare();
///
///     public static ConfigKeyCatalog Catalog =&gt; Declared;
/// }
/// </code>
/// </example>
/// </summary>
public sealed class ConfigKeyCatalog
{
    /// <summary>
    /// Declarations in the order they were written.
    /// </summary>
    private readonly List<DeclaredConfigKey> _keys = [];

    /// <summary>
    /// Names already declared — for refusing a second declaration of one name.
    /// </summary>
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>
    /// Declarations of this owner, in the order they were written.
    /// </summary>
    public IReadOnlyList<DeclaredConfigKey> Keys => _keys;

    /// <summary>
    /// Opens the declaration of a TEXT key — the shape most settings have, and the one every
    /// configuration source states natively.
    /// </summary>
    /// <param name="name">Setting name.</param>
    /// <returns>Builder of the declaration.</returns>
    public ConfigKeyBuilder<string?> Text(string name) => new(this, name);

    /// <summary>
    /// Opens the declaration of a key of any other value type.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="name">Setting name.</param>
    /// <returns>Builder of the declaration.</returns>
    public ConfigKeyBuilder<T> Of<T>(string name) => new(this, name);

    /// <summary>
    /// Puts a finished declaration into the catalog.
    /// </summary>
    /// <param name="declared">Declaration of the key.</param>
    /// <exception cref="InvalidOperationException">
    /// The owner declares the name twice: which of the two the catalog holds would depend on the order
    /// of the fields, and the registry refuses the second declaration at startup anyway.
    /// </exception>
    internal void Add(DeclaredConfigKey declared)
    {
        if (!_names.Add(declared.Name))
        {
            throw new InvalidOperationException(
                $"Configuration key '{declared.Name}' is declared twice in one catalog.");
        }

        _keys.Add(declared);
    }
}
