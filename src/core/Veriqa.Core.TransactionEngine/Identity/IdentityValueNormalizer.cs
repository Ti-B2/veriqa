// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

using Veriqa.Core.TransactionEngine.Configuration;

namespace Veriqa.Core.TransactionEngine.Identity;

/// <summary>
/// The closed set of normalization rules of the core, as code (SPEC-012 §4.12). It is the single
/// place both sides of a comparison are brought to a canonical form: the expectation of the relying
/// party when it is accepted, and the claim of the confirming party when the verdict is computed. A
/// second implementation of either side would be a second definition of "the same person".
/// </summary>
internal static class IdentityValueNormalizer
{
    /// <summary>
    /// Maximum number of digits an E.164 number carries (ITU-T E.164).
    /// </summary>
    private const int MaxE164Digits = 15;

    /// <summary>
    /// Characters a relying party writes a phone number with for readability. They carry no value
    /// and are dropped before the number is judged.
    /// </summary>
    private const string PhoneSeparators = " -().";

    /// <summary>
    /// Brings a value to the canonical form of its declared rule.
    /// </summary>
    /// <param name="rule">Normalization rule of the declared type.</param>
    /// <param name="value">Value as it was stated.</param>
    /// <param name="normalized">The canonical form.</param>
    /// <returns><see langword="false"/> when the value has no canonical form under this rule.</returns>
    public static bool TryNormalize(
        IdentityNormalizationRule rule,
        string? value,
        [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;

        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        return rule switch
        {
            IdentityNormalizationRule.Exact => Exact(trimmed, out normalized),
            IdentityNormalizationRule.E164Phone => E164Phone(trimmed, out normalized),
            IdentityNormalizationRule.EmailDomainCaseFold => EmailDomainCaseFold(trimmed, out normalized),

            // A rule outside the enumeration cannot be compared by, and refusing here rather than
            // falling back to the strictest one keeps a broken declaration from silently comparing by
            // a rule its author did not choose. The startup validation refuses such a value already.
            _ => false
        };
    }

    /// <summary>
    /// The value as it stands, trimmed.
    /// </summary>
    /// <param name="trimmed">Trimmed value.</param>
    /// <param name="normalized">The canonical form.</param>
    /// <returns>Always <see langword="true"/> — a non-empty value is its own canonical form.</returns>
    private static bool Exact(string trimmed, [NotNullWhen(true)] out string? normalized)
    {
        normalized = trimmed;

        return true;
    }

    /// <summary>
    /// Reduction to E.164: readability separators are dropped, and what remains has to be an optional
    /// leading plus and 1 to 15 digits whose first one is not zero.
    /// </summary>
    /// <param name="trimmed">Trimmed value.</param>
    /// <param name="normalized">The canonical form, always with the leading plus.</param>
    /// <returns><see langword="false"/> when the value is not a number in any spelling.</returns>
    private static bool E164Phone(string trimmed, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;

        var body = trimmed.StartsWith('+') ? trimmed.AsSpan(1) : trimmed.AsSpan();
        var digits = new StringBuilder(MaxE164Digits + 1);
        digits.Append('+');

        foreach (var character in body)
        {
            if (PhoneSeparators.Contains(character, StringComparison.Ordinal))
            {
                continue;
            }

            if (!char.IsAsciiDigit(character) || digits.Length > MaxE164Digits)
            {
                return false;
            }

            digits.Append(character);
        }

        // A country code never starts with zero, so a leading zero means a national spelling — a
        // different number space, which this rule is not entitled to guess the country of.
        if (digits.Length is 1 || digits[1] is '0')
        {
            return false;
        }

        normalized = digits.ToString();

        return true;
    }

    /// <summary>
    /// Case folding of the domain part alone: the local part of an address is case-SENSITIVE by
    /// RFC 5321, so folding it would call two different mailboxes one and the same.
    /// </summary>
    /// <param name="trimmed">Trimmed value.</param>
    /// <param name="normalized">The canonical form.</param>
    /// <returns><see langword="false"/> when the value is not an address of one local part and one domain.</returns>
    private static bool EmailDomainCaseFold(string trimmed, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;

        var separator = trimmed.LastIndexOf('@');
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            return false;
        }

        var local = trimmed[..separator];
        var domain = trimmed[(separator + 1)..];

        // An address holding a second '@' in its domain is not one this rule can fold: the domain is
        // what is being lowercased, and there would be two candidates for it.
        if (domain.Contains('@'))
        {
            return false;
        }

        normalized = string.Concat(local, "@", domain.ToLower(CultureInfo.InvariantCulture));

        return true;
    }
}
