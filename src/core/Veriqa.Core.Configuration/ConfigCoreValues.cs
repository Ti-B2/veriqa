// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Synchronous reader of CORE-level bindings, for the composition steps a framework performs
/// synchronously BEFORE the DI container exists — the OpenIddict server options and the rate-limiter
/// policies. Both read the core level and nothing else, and the core level is pure memory (a closure
/// over <c>IOptions</c>): there is no input/output there and, by the way the level is built, there
/// cannot be.
/// <para>
/// This is NOT a resolver and not a second entry of resolution (SPEC-012 §10.6 — one
/// resolution, no fork). It has no precedence, no
/// level other than <see cref="ConfigLevel.Core"/>, none of the key semantics
/// (<see cref="ConfigKeyKind.Set"/> / <see cref="ConfigKeyKind.ProtectiveCeiling"/> /
/// <see cref="ConfigKeyKind.GatedValue"/>), no cache, no degradation and no dimensions. It evaluates
/// one core binding, which is the same closure the resolver itself would evaluate.
/// </para>
/// <para>
/// Everywhere an asynchronous seam exists, the single asynchronous
/// <see cref="IConfigurationResolver.ResolveAsync{T}(ConfigKey{T}, ResolutionContext, ConfigDimensionValues, CancellationToken)"/> is used instead — that is the rule, and this
/// reader is the exception the framework's synchronous composition leaves no way around.
/// </para>
/// </summary>
public sealed class ConfigCoreValues
{
    /// <summary>
    /// Registry holding the declared core bindings.
    /// </summary>
    private readonly ConfigBindingRegistry _bindings;

    /// <summary>
    /// Creates the reader over an already filled registry.
    /// </summary>
    private ConfigCoreValues(ConfigBindingRegistry bindings) => _bindings = bindings;

    /// <summary>
    /// Declares the core getters of the keys to be read and returns the reader over them.
    /// </summary>
    /// <param name="declare">Declaration of the core getters.</param>
    /// <returns>Reader of the declared core values.</returns>
    public static ConfigCoreValues Declare(Action<IConfigCoreBindings> declare)
    {
        ArgumentNullException.ThrowIfNull(declare);

        return new ConfigCoreValues(ConfigBindingRegistry.ForCoreValues([new DelegateCoreKeys(declare)]));
    }

    /// <summary>
    /// Reads the core-level value of a key.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <returns>Core-level value; <c>default</c> when the key declared no core getter.</returns>
    public T Read<T>(ConfigKey<T> key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // The core getter is a closure over global options — nothing is awaited here, because there is
        // nothing to await: the asynchronous binding of this very level is built from the same closure.
        var getter = _bindings.FindCoreValue(key);
        if (getter is null)
        {
            return default!;
        }

        var value = getter(ConfigDimensionValues.None);

        return value.HasValue ? value.Value! : default!;
    }

    /// <summary>
    /// Registrar built from a delegate — the reader declares its core getters inline, through the same
    /// hook a consumer assembly uses.
    /// </summary>
    /// <param name="declare">Declaration of the core getters.</param>
    private sealed class DelegateCoreKeys(Action<IConfigCoreBindings> declare) : IRegisterConfigKeys
    {
        /// <inheritdoc />
        /// <remarks>
        /// Nothing is declared into a schema here: this registrar serves the throw-away registry of a
        /// single synchronous read, which is not the schema of the deployment (see
        /// <c>ConfigBindingRegistry.ForCoreValues</c>). The keys read are the ones the caller names in
        /// the delegate above.
        /// </remarks>
        public void DeclareKeys(IConfigKeyDeclarations keys)
        {
        }

        /// <inheritdoc />
        public void Register(IConfigBindings bindings, IConfigCoreBindings coreBindings) => declare(coreBindings);
    }
}
