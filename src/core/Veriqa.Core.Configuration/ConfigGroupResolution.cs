// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Reader handed to the assembler of a domain group (<see cref="ConfigKeyGroup{TResult}"/>): it
/// resolves the group's keys inside the SHARED scope of the group, so a record serving several keys
/// is read once for the whole aggregate.
/// <para>
/// Every member is typed by the key it is given, so nothing is erased on the way into the aggregate:
/// the type of a value is the type parameter of its key, and the compiler checks the assignment at
/// the point where the owner of the group writes it.
/// </para>
/// <para>
/// The cancellation token of the group's resolution is held here rather than passed member by member:
/// a token cancelled in the middle of the filling surfaces out of the key being resolved, so the
/// assembler never returns a half-filled aggregate.
/// </para>
/// </summary>
public sealed class ConfigGroupResolution
{
    /// <summary>
    /// The canonical resolver the group is built over.
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Scope shared by every key of the group.
    /// </summary>
    private readonly ConfigResolutionScope _scope;

    /// <summary>
    /// Cancellation token of the group's resolution.
    /// </summary>
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Creates the reader of a group's keys.
    /// </summary>
    /// <param name="resolver">Canonical resolver.</param>
    /// <param name="scope">Scope shared by the keys of the group.</param>
    /// <param name="cancellationToken">Cancellation token of the group's resolution.</param>
    internal ConfigGroupResolution(
        IConfigurationResolver resolver,
        ConfigResolutionScope scope,
        CancellationToken cancellationToken)
    {
        _resolver = resolver;
        _scope = scope;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Resolves one key of the group, reporting the level the value was determined at.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <param name="dimensions">Point of the chain for a composite key; empty for a key without dimensions.</param>
    /// <returns>Effective value together with the level it was determined at.</returns>
    public ValueTask<ResolvedValue<T>> ResolveAsync<T>(ConfigKey<T> key, ConfigDimensionValues dimensions) =>
        _resolver.ResolveAsync(key, _scope, dimensions, _cancellationToken);

    /// <summary>
    /// Effective value of a key whose lowest level always supplies one.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <returns>Effective value.</returns>
    public async ValueTask<T> ValueOfAsync<T>(ConfigKey<T> key) =>
        (await ResolveAsync(key, ConfigDimensionValues.None)).Value;

    /// <summary>
    /// Effective value of a key that may be left unset by every level. "Unset" is reported by the
    /// resolver as the absence of a source level and is NOT the same as the default of the value's
    /// type — for an enum or a lifetime the default is a legitimate value.
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <returns>Effective value, or null when no level stated one.</returns>
    public async ValueTask<T?> OptionalValueOfAsync<T>(ConfigKey<T> key)
        where T : struct
    {
        var resolved = await ResolveAsync(key, ConfigDimensionValues.None);

        return resolved.SourceLevel is null ? null : resolved.Value;
    }

    /// <summary>
    /// Effective TEXT values of a key cut by one dimension, resolved for each of the given values of
    /// that dimension and returned as the map "dimension value → effective text".
    /// <para>
    /// The map holds an entry only for a dimension value some level actually stated a text for; a
    /// blank text counts as unstated, exactly as it does inside a level. An empty map is returned as
    /// <c>null</c>, so an aggregate that carries "no values at all" as an absent map gets it without
    /// a check of its own.
    /// </para>
    /// <para>
    /// It lives here rather than at the owner of every such key because it names no key, no dimension
    /// and no value of one: it is told which key to ask, which dimension to cut by and which values to
    /// ask about (CFG-231). Values of the dimension are compared the way a level's own map compares
    /// them — case-insensitively (<see cref="DimensionedConfigValue"/>) — because both come from
    /// configuration written by hand.
    /// </para>
    /// </summary>
    /// <param name="key">Setting key declaring the dimension.</param>
    /// <param name="dimensionName">Name of the dimension to cut by.</param>
    /// <param name="dimensionValues">Values of the dimension to resolve the key for.</param>
    /// <returns>Map "dimension value → effective text", or null when no value resolved to a text.</returns>
    public async ValueTask<Dictionary<string, string>?> TextByDimensionAsync(
        ConfigKey<string?> key,
        string dimensionName,
        IReadOnlyList<string> dimensionValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dimensionName);
        ArgumentNullException.ThrowIfNull(dimensionValues);

        var resolved = new Dictionary<string, string>(dimensionValues.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var dimensionValue in dimensionValues)
        {
            var value = await ResolveAsync(key, ConfigDimensionValues.Of((dimensionName, dimensionValue)));

            if (!string.IsNullOrWhiteSpace(value.Value))
            {
                resolved[dimensionValue] = value.Value;
            }
        }

        return resolved.Count == 0 ? null : resolved;
    }
}
