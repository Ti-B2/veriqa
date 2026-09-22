// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Detection of the request language for channel messages built during an HTTP request
/// (SPEC-017 ICC-050). Mirrors how the auth window detects the page language
/// (<c>AuthPageStrings.DetectLanguage</c>, Accept-Language), but lives on the channel layer:
/// Veriqa.Core.ChannelAdapter must not reference Veriqa.Core.AuthServer (the dependency runs the
/// other way), so the auth-page helper cannot be reused here.
/// The detected tag is only ever used as a dictionary key by
/// <see cref="IConfirmationPromptLocalizer"/> — never to build a file path (path traversal, core-rules §10).
/// </summary>
internal static class ChannelRequestLocale
{
    /// <summary>
    /// Maximum Accept-Language length taken into account (protection against malformed headers).
    /// </summary>
    private const int MaxAcceptLanguageLength = 256;

    /// <summary>
    /// Detects the request language from the Accept-Language header.
    /// </summary>
    /// <remarks>
    /// Takes the first header segment as a whole tag, region included ("pt-BR,en;q=0.9" → "pt-br").
    /// The region is deliberately NOT stripped: the localizer looks the full tag up first and falls
    /// back to the primary subtag itself ("pt-br" → "pt"), so truncating here would not widen the
    /// match — it would only make a regional translation (pt-br.json, zh-hans.json) unreachable from
    /// the channel path, while adding a second, competing fallback step.
    /// q-weights are not honored — clients already order segments by preference. Unlike the auth
    /// page, the result is not matched against a registry of supported languages: the channel layer
    /// has no such registry, and an unknown tag degrades in the localizer to the base language
    /// anyway — the same documented fallback as no locale at all.
    /// </remarks>
    /// <param name="acceptLanguage">Accept-Language header value.</param>
    /// <returns>IETF language tag, or null when the header is absent/empty (base language).</returns>
    public static string? Detect(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage))
        {
            return null;
        }

        var headerValue = acceptLanguage.Length > MaxAcceptLanguageLength
            ? acceptLanguage[..MaxAcceptLanguageLength]
            : acceptLanguage;

        foreach (var segment in headerValue.Split(','))
        {
            // Drop the q-weight ("ru;q=0.9" → "ru"), keep the tag itself intact ("pt-BR" → "pt-br")
            var tag = segment.Split(';')[0].Trim();
            if (tag.Length > 0)
            {
                return tag.ToLowerInvariant();
            }
        }

        return null;
    }
}
