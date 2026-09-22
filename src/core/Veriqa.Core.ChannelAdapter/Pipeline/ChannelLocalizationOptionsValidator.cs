// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;

using Microsoft.Extensions.Options;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Start-up validation of the channel localization section (SPEC-017 §7.2, ICC-050): a locale tag
/// that denotes no locale, or a regional mapping nothing will ever read, stops the host.
/// </summary>
/// <remarks>
/// Everything judged here fails SILENTLY at run time, which is the reason the judgement exists at
/// all. A base locale that is not a locale is simply added to the language registry, so the
/// deployment advertises a language it has no texts for and every message falls back to the Natural
/// Key. A regional culture stated for a language the platform does not know, or stated under a key
/// that already carries a region, is dropped by the reader — dates and numbers keep the conventions
/// the operator meant to replace, and nothing says so.
/// <para>
/// A tag counts as a locale when the platform carries locale data for it (<c>predefinedOnly</c>),
/// which is the same line the message renderer draws for a recipient's locale — a merely
/// well-formed tag is not materialized as a custom culture there and is not accepted here. On top of
/// that the tag has to be the platform's own SPELLING of that locale: an alias such as <c>eng</c>
/// resolves to <c>en</c>, and a deployment writing the alias would name its locale files after a tag
/// nothing looks up.
/// </para>
/// <para>
/// An installation carrying no locale data at all (globalization-invariant) is judged by the
/// non-tag rules only — see <see cref="CarriesLocaleData"/>.
/// </para>
/// </remarks>
internal sealed class ChannelLocalizationOptionsValidator : IValidateOptions<ChannelLocalizationOptions>
{
    /// <summary>
    /// Whether this installation carries locale data at all. A globalization-invariant host has no
    /// locale for ANY tag, the language the product ships its own texts in included, so the tag rules
    /// below would refuse such a deployment its start over a value no setting could make valid — the
    /// shipped default fails there as surely as a typo does. The renderer already has an answer for
    /// that world (<c>MessageTextRenderer.ResolveCulture</c> degrades to the invariant culture), and
    /// a start-up check may not be stricter than the run time it guards. Probed by the very question
    /// the rules ask, of the one tag every installation with data has.
    /// </summary>
    private static readonly bool CarriesLocaleData =
        Predefined(ChannelLocalizationOptions.DefaultBaseLocale) is not null;

    /// <summary>
    /// Address of the locale files directory inside the host configuration.
    /// </summary>
    private static readonly string LocalesPathPath =
        ChannelLocalizationOptions.SectionName + ":" + nameof(ChannelLocalizationOptions.LocalesPath);

    /// <summary>
    /// Address of the base locale inside the host configuration.
    /// </summary>
    private static readonly string BaseLocalePath =
        ChannelLocalizationOptions.SectionName + ":" + nameof(ChannelLocalizationOptions.BaseLocale);

    /// <summary>
    /// Address of the regional culture map inside the host configuration.
    /// </summary>
    private static readonly string NeutralLocaleCulturesPath =
        ChannelLocalizationOptions.SectionName
        + ":" + nameof(ChannelLocalizationOptions.NeutralLocaleCultures);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ChannelLocalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.LocalesPath))
        {
            failures.Add(
                $"{LocalesPathPath}: the locale files directory is blank. State a path relative to the "
                + $"host content root (the shipped value is '{ChannelLocalizationOptions.DefaultLocalesPath}') "
                + "or leave the setting out.");
        }

        failures.AddRange(TagFailure(options.BaseLocale, BaseLocalePath, "base locale"));

        foreach (var (language, culture) in options.NeutralLocaleCultures)
        {
            var entryPath = $"{NeutralLocaleCulturesPath}:{language}";

            // The map answers a language stated WITHOUT a region — a tag that already carries one
            // never reaches the lookup, so an entry keyed by such a tag is read by nobody.
            if (!string.IsNullOrWhiteSpace(language) && language.Contains('-', StringComparison.Ordinal))
            {
                failures.Add(
                    $"{entryPath}: the regional culture map is keyed by a language stated without a "
                    + "region, and a tag carrying one is never looked up here. State the language "
                    + $"alone (for example '{language.Split('-')[0]}').");

                continue;
            }

            failures.AddRange(TagFailure(language, entryPath, "language of the regional culture map"));
            failures.AddRange(TagFailure(culture, entryPath, "regional culture"));
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    /// <summary>
    /// The complaint about a stated tag that denotes no locale of this installation, or denotes one
    /// under a spelling the platform does not use for it. No complaint — the tag is fine.
    /// </summary>
    /// <param name="tag">Tag the deployment stated.</param>
    /// <param name="path">Address of the tag inside the host configuration.</param>
    /// <param name="subject">What the tag denotes, for the message.</param>
    /// <returns>One message, or none.</returns>
    private static IEnumerable<string> TagFailure(string? tag, string path, string subject)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            yield return $"{path}: the {subject} is blank.";
            yield break;
        }

        // Nothing to judge where the installation carries no locale data: every tag reads the same
        // there, so the answer would be about the host rather than about the value stated.
        if (!CarriesLocaleData)
        {
            yield break;
        }

        var culture = Predefined(tag);

        if (culture is null)
        {
            yield return
                $"{path}: '{tag}' is not a locale this installation carries data for, so it names no "
                + $"{subject}. State a language tag such as 'en' or 'pt-BR'.";
            yield break;
        }

        if (!string.Equals(culture.Name, tag, StringComparison.OrdinalIgnoreCase))
        {
            yield return
                $"{path}: '{tag}' is an alias of the locale '{culture.Name}' rather than its own name, "
                + $"and the {subject} is looked up by the name. State '{culture.Name}'.";
        }
    }

    /// <summary>
    /// The culture of a tag this installation carries locale data for, or null — the same line the
    /// message renderer draws for a recipient's locale.
    /// </summary>
    /// <param name="tag">Language tag.</param>
    /// <returns>The culture, or null when the installation carries no locale by that name.</returns>
    private static CultureInfo? Predefined(string tag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
