// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.RegularExpressions;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Single source for scanning and substituting <c>{slot_name}</c> tokens in a template. Used by both
/// the configuration validator (extracting referenced slot names) and the renderer (single-pass
/// substitution). Centralizing the token grammar here is what makes the CONTRACT of a message the ONLY
/// source for the matcher (TPL-110) — there is no second regex/switch hardcoded per placeholder set
/// (TPL-093).
/// </summary>
public static partial class SlotTokenScanner
{
    /// <summary>
    /// Token grammar: a brace pair around a valid slot name (<see cref="SlotDeclaration"/> syntax).
    /// A lone <c>{</c> or a brace pair around anything else is literal text, never a token.
    /// </summary>
    [GeneratedRegex("\\{([a-z][a-z0-9_]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <summary>
    /// Returns the set of slot names referenced by <paramref name="template"/> (each <c>{name}</c> whose
    /// <c>name</c> is a syntactically valid slot name). Used at configuration time to detect unknown
    /// (TPL-111(a)) tokens.
    /// </summary>
    /// <param name="template">Template text.</param>
    /// <returns>The distinct referenced slot names.</returns>
    public static IReadOnlySet<string> ExtractSlotNames(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in TokenPattern().Matches(template))
        {
            names.Add(match.Groups[1].Value);
        }

        return names;
    }

    /// <summary>
    /// Substitutes tokens in a single pass: each <c>{name}</c> present in <paramref name="values"/> is
    /// replaced by its value; any other token stays literal. Substituted values are NEVER rescanned, so a
    /// value that itself contains <c>{other_slot}</c> does not get expanded (TPL-030, security invariant).
    /// </summary>
    /// <param name="template">Template text with tokens.</param>
    /// <param name="values">Slot name → final replacement string (already typed/sanitized/escaped).</param>
    /// <returns>The substituted text.</returns>
    public static string Substitute(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        // Regex.Replace scans the ORIGINAL string left to right and never re-examines the replacement
        // text — the single-pass guarantee we rely on for TPL-030.
        return TokenPattern().Replace(
            template,
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }
}
