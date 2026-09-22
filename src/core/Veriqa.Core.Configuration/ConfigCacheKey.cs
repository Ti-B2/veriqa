// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text;

namespace Veriqa.Core.Configuration;

/// <summary>
/// Composite key of one cache entry of the resolution layer (SPEC-012 §10.6). It is made of the
/// setting, the level, the part of the context THAT LEVEL addresses and — for a composite key — the
/// point of the fallback chain. The tenant is always part of it, which is what bounds the number of
/// entries by tenants rather than leaving it unbounded, and what keeps the value of one tenant from
/// ever answering for another.
/// <para>
/// Both stores index by the key TEXT, so the text has to be injective: two different combinations of
/// parts must never produce the same string. Values of the parts are free-form strings of the
/// integrator (client_id, tenant, ui_config selector, dimension values), so a delimiter can occur
/// INSIDE a part; every part is therefore escaped when it is appended, and the delimiters are never
/// assumed to be absent from a value. Without that, a tenant with a delimiter in its name and an
/// application with the delimiter in its own would share one entry — exactly the cross-owner leak the
/// key exists to prevent.
/// </para>
/// <para>
/// The value TYPE is deliberately not part of the key: one setting name has one value type, and
/// adding the type would only split the entry of the same setting across generic instantiations.
/// </para>
/// <para>
/// It is public for one reason: every member of <see cref="IConfigResolutionCache"/> takes it, and
/// that port is replaceable outside this assembly. An implementation of the port treats the key as
/// OPAQUE — its text is what both stores index by, and nothing else about it is a contract.
/// </para>
/// </summary>
public readonly record struct ConfigCacheKey
{
    /// <summary>
    /// Separator of the parts of the key text.
    /// </summary>
    private const char Separator = '|';

    /// <summary>
    /// Separator between two dimension values inside the point of the chain.
    /// </summary>
    private const char DimensionSeparator = ',';

    /// <summary>
    /// Sign between the name of a dimension and its value.
    /// </summary>
    private const char DimensionAssignment = '=';

    /// <summary>
    /// Prefix that makes an occurrence of a delimiter inside a part's value a plain character.
    /// </summary>
    private const char EscapeCharacter = '\\';

    /// <summary>
    /// Text of the key — built once at construction, because it is what both stores index by.
    /// </summary>
    private readonly string _text;

    /// <summary>
    /// Builds the composite key of an entry.
    /// </summary>
    /// <param name="keyName">Setting name.</param>
    /// <param name="level">Level the value belongs to.</param>
    /// <param name="context">Resolution context.</param>
    /// <param name="dimensions">Point of the fallback chain; empty for a key without dimensions.</param>
    public ConfigCacheKey(
        string keyName,
        ConfigLevel level,
        ResolutionContext context,
        ConfigDimensionValues dimensions)
    {
        var text = new StringBuilder();

        AppendEscaped(text, keyName);
        AppendPart(text, level.ToString());
        AppendPart(text, context.TenantId);

        // Only the part of the context the level actually addresses takes part: an entry of the core
        // level must not multiply by the applications that happened to ask for it. The ui_config level
        // addresses TWO of them — the selector alone does not identify a record, because it is the
        // owning application that makes a selector resolvable (CFG-203), and a key built on the
        // selector alone would let the entry of one application answer for another. The two go in as
        // SEPARATE parts rather than as one glued string: the level is itself a part of the key, so it
        // is the level that fixes how many parts follow, and no gluing of its own is needed.
        switch (level)
        {
            case ConfigLevel.Application:
                AppendPart(text, context.ApplicationId);
                break;

            case ConfigLevel.UiConfig:
                AppendPart(text, context.ApplicationId);
                AppendPart(text, context.UiConfigSelector);
                break;

            case ConfigLevel.UserOverride:
                AppendPart(text, context.UserOverride?.UserId);
                break;

            default:
                break;
        }

        AppendPoint(text, dimensions);

        _text = text.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => _text;

    /// <summary>
    /// Appends the next part of the key, separated from the previous one.
    /// </summary>
    /// <param name="builder">Key text being built.</param>
    /// <param name="value">Value of the part; null and empty are the same absent part.</param>
    private static void AppendPart(StringBuilder builder, string? value)
    {
        builder.Append(Separator);
        AppendEscaped(builder, value);
    }

    /// <summary>
    /// Appends the point of the fallback chain as the last part: the dimension values, ordered by
    /// dimension name so that the same point always gives the same text. An empty set gives an empty
    /// part rather than no part at all.
    /// </summary>
    /// <param name="builder">Key text being built.</param>
    /// <param name="dimensions">Point of the chain.</param>
    private static void AppendPoint(StringBuilder builder, ConfigDimensionValues dimensions)
    {
        builder.Append(Separator);

        if (dimensions.Count == 0)
        {
            return;
        }

        var first = true;
        foreach (var dimension in dimensions.Values.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append(DimensionSeparator);
            }

            first = false;

            AppendEscaped(builder, dimension.Key);
            builder.Append(DimensionAssignment);
            AppendEscaped(builder, dimension.Value);
        }
    }

    /// <summary>
    /// Appends a value with every delimiter occurring inside it escaped, which is what keeps the text
    /// of the whole key injective.
    /// </summary>
    /// <param name="builder">Key text being built.</param>
    /// <param name="value">Value to append; null and empty append nothing.</param>
    private static void AppendEscaped(StringBuilder builder, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (var character in value)
        {
            if (character is Separator or DimensionSeparator or DimensionAssignment or EscapeCharacter)
            {
                builder.Append(EscapeCharacter);
            }

            builder.Append(character);
        }
    }
}
