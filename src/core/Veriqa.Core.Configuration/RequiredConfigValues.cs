// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;

namespace Veriqa.Core.Configuration;

/// <summary>
/// The ONE refusal of a deployment that states no value for a setting whose owner declared one
/// obligatory (<see cref="ConfigKeyBuilder{T}.Required"/>, SPEC-012 §10.6 CFG-240).
/// <para>
/// It exists so that there is exactly one of it. Before the requirement could be declared, an owner
/// who needed it wrote the pass itself — the loop over the objects the setting is asked about, the
/// collecting of the ones nobody stated a value for, and the sentence naming where to write it and
/// what may be written there — and every such pass was the same pass with different words. Here the
/// words are built once, from what the DECLARATION of the key states: the addresses its levels are
/// read at, and the values it admits.
/// </para>
/// <para>
/// The requirement is carried out in EVERY composition that reads the key, and that is the whole of
/// the guarantee: the declaration travels with the key the caller holds
/// (<c>ConfigKey{T}.Declaration</c>), so a contour reading a key whose catalog another contour
/// registers refuses on the same terms as the one that registers it. Asking the container what the
/// deployment declared would give the opposite: where the catalog is absent the requirement would be
/// silently absent too, and the host would start exactly where its owner wrote that it must not.
/// </para>
/// <para>
/// What stays with the OWNER is the question, and that is deliberate. The mechanism keeps no list of
/// the objects a setting is asked about — no list of channels, no list of tenants — so a key with a
/// dimension is asked here per point of that dimension, supplied by the caller. And WHETHER the
/// question is worth asking about an object at all (is it switched on, is it of the kind this setting
/// governs) is domain knowledge: a shipped configuration that deliberately says nothing about what it
/// does not use must go on starting.
/// </para>
/// </summary>
public sealed class RequiredConfigValues
{
    /// <summary>
    /// What separates the points a refusal names.
    /// </summary>
    private const string PointSeparator = ", ";

    /// <summary>
    /// What separates one dimension value from another inside a point.
    /// </summary>
    private const string DimensionSeparator = " ";

    /// <summary>
    /// The TWO ways a deployment ends up owing a value, named together because this refusal cannot
    /// tell them apart: a level that stated nothing and a level that stated what the setting is not
    /// read from leave the same trace on the resolution path — no level answered
    /// (<see cref="ResolvedValue{T}.SourceLevel"/>). Saying only the first of them would assert the one
    /// thing that is false about the second: an operator looking at the very value they wrote would be
    /// told that nobody wrote one. The address holding an unreadable value is named where it is known —
    /// by the warning of the read and by the report of the snapshot — so the refusal sends them there
    /// instead of inventing a diagnosis it does not have.
    /// </summary>
    private const string EitherWayItIsOwed =
        "no level writes a value for it, or what a level writes is not something the setting can be "
        + "read from — a value that could not be read is named, together with the address holding it, "
        + "by a warning of the read";

    /// <summary>
    /// What a message tells an operator about the spelling of the addresses it printed.
    /// </summary>
    private const string SpellingNote =
        "a segment standing for a dimension is the value of that dimension, and section names are "
        + "matched without regard to case, so a section spelled with a capital letter is the very "
        + "same address";

    /// <summary>
    /// Canonical resolver of the effective value — the same one every consumer of the setting reads
    /// through, so what this asks is what a request would be served.
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Creates the refusal.
    /// </summary>
    /// <param name="resolver">Canonical resolver of the effective value.</param>
    public RequiredConfigValues(IConfigurationResolver resolver) =>
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    /// <summary>
    /// Refuses the deployment where the key states no value at one of the points asked about. A key
    /// without a dimension is asked at the single point that addresses nothing
    /// (<see cref="ConfigDimensionValues.None"/>); a key with one is asked at the values of that
    /// dimension the caller owns.
    /// <para>
    /// A key whose owner did NOT declare the requirement is not checked at all: the answer to "must a
    /// value exist" is the owner's, and this is where it is carried out rather than where it is made.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="key">Setting key.</param>
    /// <param name="context">Ownership context the value is resolved in.</param>
    /// <param name="points">Points of the chain the setting is owed a value at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when every point has been checked.</returns>
    /// <exception cref="InvalidOperationException">
    /// No level leaves the key with a value at one or more of the points — either because none states
    /// one there, or because what a level states is not read into a value.
    /// </exception>
    public async Task EnsureStatedAsync<T>(
        ConfigKey<T> key,
        ResolutionContext context,
        IEnumerable<ConfigDimensionValues> points,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(points);

        // The requirement is read off the DECLARATION the key carries, and not off the catalogs a
        // deployment happened to register in its container: a contour that reads the key without
        // registering its catalog — the very contour this refusal exists for — would find nothing
        // there and start where it was written to stop. A key with no declaration at all was built by
        // a factory of its own, and a factory declares no requirement, so there is nothing to carry
        // out.
        if (key.Declaration is not { IsRequired: true } declared)
        {
            return;
        }

        var unstated = new List<ConfigDimensionValues>();

        foreach (var point in points)
        {
            var resolved = await _resolver.ResolveAsync(key, context, point, cancellationToken);

            // The LEVEL is what says whether anybody stated a value, and not the value itself: the
            // default of the type is a perfectly ordinary value for most settings, and a key that had
            // to be nullable merely to carry the absence is exactly the cost this member removes.
            if (resolved.SourceLevel is null)
            {
                unstated.Add(point);
            }
        }

        if (unstated.Count > 0)
        {
            throw new InvalidOperationException(RefusalOf(declared, unstated));
        }
    }

    /// <summary>
    /// The sentence a refusal carries: what is missing, where to write it and what may be written
    /// there. All three come from the declaration of the key, so an owner declaring the requirement
    /// gets them without writing a message. What it does NOT claim is why the value is missing
    /// (<see cref="EitherWayItIsOwed"/>): the declaration says nothing about that, and neither does
    /// the resolution the check reads.
    /// </summary>
    /// <param name="declared">Declaration of the key.</param>
    /// <param name="unstated">Points no level stated a value at.</param>
    /// <returns>Message of the refusal.</returns>
    private static string RefusalOf(DeclaredConfigKey declared, IReadOnlyList<ConfigDimensionValues> unstated)
    {
        var about = string.Join(PointSeparator, unstated.Select(TextOf).Where(text => text.Length > 0));
        var addresses = AddressesOf(declared, unstated);

        return string.Format(
            CultureInfo.InvariantCulture,
            "Setting '{0}' is left without a value by this deployment{1}: {2}. Its declaration says "
            + "that a deployment stating no value for it does not start. {3} at {4} ({5}).",
            declared.Name,
            about.Length > 0 ? " for " + about : string.Empty,
            EitherWayItIsOwed,
            declared.AdmittedValues is { } admitted
                ? "State one of " + admitted
                : string.Format(
                    CultureInfo.InvariantCulture, "State a value of type '{0}'", declared.ValueType),
            addresses,
            SpellingNote);
    }

    /// <summary>
    /// The addresses an operator writes the missing value at: one per level the key declares an
    /// address for, per point that is missing one. A level addressed by the IDENTITY of the key alone
    /// has no address to name — its values are rows of a store rather than members of a document — and
    /// is left out.
    /// </summary>
    /// <param name="declared">Declaration of the key.</param>
    /// <param name="unstated">Points no level stated a value at.</param>
    /// <returns>Addresses, as a message lists them.</returns>
    private static string AddressesOf(DeclaredConfigKey declared, IReadOnlyList<ConfigDimensionValues> unstated) =>
        string.Join(
            PointSeparator,
            declared.Levels
                .SelectMany(level => unstated.Select(point => AddressOf(level, point)))
                .Where(address => address is not null)
                .Distinct(StringComparer.Ordinal));

    /// <summary>
    /// The address of one level at one point, named together with the level it belongs to: the same
    /// address is relative to a different record on every level, and a bare path would send an
    /// operator to whichever of them they happened to think of.
    /// </summary>
    /// <param name="level">Level of the key.</param>
    /// <param name="point">Point of the chain.</param>
    /// <returns>Address of the level; null when the level has none.</returns>
    private static string? AddressOf(ConfigLevelAddress level, ConfigDimensionValues point) =>
        level.Template?.Resolve(point) is { } path ? $"{level.Level}: '{path}'" : null;

    /// <summary>
    /// One point of the chain as a message names it — the dimension values it was asked with. An empty
    /// point (a key without dimensions) names nothing: there is one question, and it needs no
    /// qualifier.
    /// </summary>
    /// <param name="point">Point of the chain.</param>
    /// <returns>Text of the point; empty for a point addressing nothing.</returns>
    private static string TextOf(ConfigDimensionValues point) =>
        string.Join(DimensionSeparator, point.Values.Select(value => $"{value.Key}={value.Value}"));
}
