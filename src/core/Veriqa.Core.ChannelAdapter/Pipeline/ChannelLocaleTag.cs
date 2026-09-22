// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// The single normalization of an IETF language tag for the channel track (SPEC-017 ICC-050).
/// </summary>
/// <remarks>
/// Every place that compares a language tag — the locale-file localizer, the mail body template and
/// the served pages — must agree on what "the same tag" means, otherwise <c>"pt_BR"</c> and
/// <c>"pt-br"</c> silently address different translation sets. The scheme was previously duplicated
/// as three hand-synchronized private copies; it lives here so a change to it cannot apply to some
/// of the comparisons only.
/// </remarks>
internal static class ChannelLocaleTag
{
    /// <summary>
    /// Normalizes a raw language tag: trims it, replaces the underscore separator with a hyphen
    /// (<c>"pt_BR"</c> → <c>"pt-br"</c>) and lowercases it.
    /// </summary>
    /// <param name="locale">Raw language tag (null/blank — no locale).</param>
    /// <returns>The normalized tag, or null when the value carries no locale.</returns>
    public static string? Normalize(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        return locale.Trim().Replace('_', '-').ToLowerInvariant();
    }
}
