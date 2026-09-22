// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Bindings of the core level — WITHOUT a reader: the level has no record, and the value is read by a
/// closure over <c>IOptionsMonitor</c> (pure memory; there is no input/output here and there cannot
/// be). This is the existing registration mechanism of core getters, carried over as is together with
/// the gate getter of a protective setting (SPEC-012 §10.3). Hence the answer on assembly
/// boundaries as well: a core binding is registered by the OWNER of the key, from its own assembly,
/// seeing nothing but its own types.
/// </summary>
public interface IConfigCoreBindings
{
    /// <summary>
    /// Registers the core-level value getter of a key.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <param name="extract">Getter of the current value from the global options.</param>
    void RegisterCore<T>(ConfigKey<T> key, Func<T> extract);

    /// <summary>
    /// Registers the core-level value getter together with a gate getter (SPEC-012 §10.3). The gate is
    /// evaluated on every resolution, so a configuration reload takes effect without a restart.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <param name="extract">Getter of the current value from the global options.</param>
    /// <param name="gate">Getter of the core-level gate flag.</param>
    void RegisterCore<T>(ConfigKey<T> key, Func<T> extract, Func<bool> gate);

    /// <summary>
    /// Registers the core-level getter of a COMPOSITE key (dimensions): the level has no record, so
    /// the steps of the chain are tried in memory over the same closure — the resolver hands the
    /// binding a chain point, and <see cref="LayerValue{T}"/> lets a step that declares nothing yield
    /// to the next one.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <param name="extract">Getter of the value at a chain point.</param>
    /// <param name="gate">Getter of the core-level gate flag; null — the level states no gate.</param>
    void RegisterCore<T>(
        ConfigKey<T> key,
        Func<ConfigDimensionValues, LayerValue<T>> extract,
        Func<bool>? gate = null);
}
