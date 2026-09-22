// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections;
using System.Globalization;

namespace Veriqa.Core.Configuration;

/// <summary>
/// The ONE rendering of a setting value as TEXT, for everything the mechanism prints: the value a
/// snapshot report names as rejected, and the answer a key declares for its core level as the schema
/// of declared keys states it (<see cref="ConfigDeclaredDefault"/>).
/// <para>
/// It is one place because the two have to read alike: an operator comparing the answer a schema
/// states with the value a report quotes is looking at one setting, and two renderings of one number
/// would differ by the culture of the machine that printed them.
/// </para>
/// <para>
/// The rule is written here rather than chosen at each call, because the text reaches an operator
/// and a page of the client documentation, and a value spelled one way in the documentation and
/// another in the file being edited is worse than no text at all.
/// </para>
/// </summary>
internal static class ConfigValueText
{
    /// <summary>
    /// What separates the elements of a collection.
    /// </summary>
    private const string ElementSeparator = ", ";

    /// <summary>
    /// What opens the rendering of a collection.
    /// </summary>
    private const string CollectionOpen = "[";

    /// <summary>
    /// What closes the rendering of a collection.
    /// </summary>
    private const string CollectionClose = "]";

    /// <summary>
    /// Text of one value, by the rule of the mechanism, in this order:
    /// <list type="number">
    /// <item><description>no value at all — no text (<see langword="null"/>);</description></item>
    /// <item><description>
    /// a BOOLEAN — <c>true</c> / <c>false</c>, the spelling every configuration source states it in,
    /// rather than the <c>True</c> of the platform's own <c>ToString()</c>;
    /// </description></item>
    /// <item><description>
    /// a TEXT — itself, whatever it happens to look like;
    /// </description></item>
    /// <item><description>
    /// a value with a culture-sensitive form (a number, a date) — its INVARIANT form, so the text
    /// does not depend on the locale the host runs under;
    /// </description></item>
    /// <item><description>
    /// a COLLECTION — its elements by this same rule, separated by a comma and wrapped in brackets;
    /// an empty collection is therefore <c>[]</c>, which is a value and not an absence;
    /// </description></item>
    /// <item><description>
    /// anything else — what the value says about itself (<c>ToString()</c>), which for an enum is the
    /// name of its member and for a composite value is whatever its type states.
    /// </description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="T">Type of the setting value.</typeparam>
    /// <param name="value">Value to render.</param>
    /// <returns>Text of the value; null when there is none.</returns>
    public static string? Of<T>(T value) => Render(value);

    /// <summary>
    /// Renders one value of an unknown type by the rule above.
    /// </summary>
    /// <param name="value">Value to render.</param>
    /// <returns>Text of the value; null when there is none.</returns>
    private static string? Render(object? value) => value switch
    {
        null => null,
        bool flag => flag ? bool.TrueString.ToLowerInvariant() : bool.FalseString.ToLowerInvariant(),
        string text => text,
        IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
        IEnumerable elements => CollectionOpen
            + string.Join(ElementSeparator, elements.Cast<object?>().Select(Render))
            + CollectionClose,
        _ => value.ToString()
    };
}
