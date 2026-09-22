// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Registry of extraction bindings. The unit of the registry is the PAIR "key + level": one source
/// serves several levels and extracts them with different bindings, so the level is a mandatory
/// argument of a registration — it is also what the startup checks key off ("registered twice",
/// "level declared but unbound", "bound to a level the source does not serve").
/// <para>
/// Both type parameters of a registration — <c>T</c> of the value and <c>TRecord</c> of
/// the record — are known STATICALLY at the point of registration, so the pair "reader + extraction"
/// is folded into a typed closure right here. Erasing the type stays an implementation detail of one
/// internal registry and never reaches a public boundary.
/// </para>
/// </summary>
public interface IConfigBindings
{
    /// <summary>
    /// Registers the extraction of a key's value at one level over a record of the level's source.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <typeparam name="TRecord">Type of the source record.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <param name="level">Level being bound.</param>
    /// <param name="reader">Reader of the source record.</param>
    /// <param name="extract">Extraction of the level value from the record.</param>
    void Register<T, TRecord>(
        ConfigKey<T> key,
        ConfigLevel level,
        IConfigRecordReader<TRecord> reader,
        Func<TRecord, LayerValue<T>> extract)
        where TRecord : class;

    /// <summary>
    /// Registers the extraction for a composite key (dimensions): the binding is handed the point of
    /// the fallback chain together with the record.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <typeparam name="TRecord">Type of the source record.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <param name="level">Level being bound.</param>
    /// <param name="reader">Reader of the source record.</param>
    /// <param name="extract">Extraction of the level value from the record at a chain point.</param>
    void Register<T, TRecord>(
        ConfigKey<T> key,
        ConfigLevel level,
        IConfigRecordReader<TRecord> reader,
        Func<TRecord, ConfigDimensionValues, LayerValue<T>> extract)
        where TRecord : class;
}
