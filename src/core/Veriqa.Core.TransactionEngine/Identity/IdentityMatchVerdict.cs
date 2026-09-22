// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Security.Cryptography;
using System.Text;

using Veriqa.Core.TransactionEngine.Configuration;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.Identity;

/// <summary>
/// The comparison itself (SPEC-039 L41): for every declared type the claim it names is taken off the
/// resolved identity, brought to a canonical form by the rule of the same declaration and compared
/// with the expectation of the relying party in constant time. The first type that matches is the
/// verdict — there is nothing to enumerate, a type carries one candidate (C23).
/// </summary>
internal static class IdentityMatchVerdict
{
    /// <summary>
    /// Computes the verdict of the comparison.
    /// </summary>
    /// <remarks>
    /// Nothing here is a gate (E43): an absent claim, an expectation that cannot be restored and a
    /// value that does not match are all "no verdict", and the confirmation goes through either way.
    /// The order of the walk is the order of the DECLARATION and not of the stated expectations, so
    /// the verdict of one and the same pair does not depend on how the relying party ordered its JSON.
    /// </remarks>
    /// <param name="expectations">Protected expectations stored with the transaction.</param>
    /// <param name="declaredTypes">Comparable types the effective configuration declares.</param>
    /// <param name="resolvedIdentity">Identity of the party that confirmed.</param>
    /// <param name="protector">Reversible protection the values were stored with.</param>
    /// <returns>The verdict: the name of the matched type and whether anything could not be read.</returns>
    public static IdentityMatchVerdictResult Compute(
        IReadOnlyDictionary<string, string> expectations,
        IReadOnlyList<ComparableIdentityType> declaredTypes,
        ResolvedIdentitySnapshot resolvedIdentity,
        IIdentityValueProtector protector)
    {
        var unreadable = 0;

        foreach (var type in declaredTypes)
        {
            if (!expectations.TryGetValue(type.Name, out var stored))
            {
                continue;
            }

            if (!protector.TryUnprotect(stored, out var expected))
            {
                // A key ring rotated without access to the previous keys. Reported by the caller as a
                // warning; the confirmation itself is not cancelled by it.
                unreadable++;
                continue;
            }

            // The claim name comes from the declaration and is matched spelling for spelling: the
            // resolved identity keeps the names its resolution produced (WithFrozenClaims), so a
            // case-insensitive lookup here would compare against a claim the owner never named.
            if (resolvedIdentity.Claims is null
                || !resolvedIdentity.Claims.TryGetValue(type.ClaimName, out var actual))
            {
                continue;
            }

            if (!IdentityValueNormalizer.TryNormalize(type.Normalization, actual, out var normalized))
            {
                continue;
            }

            if (ValuesEqual(normalized, expected))
            {
                return new IdentityMatchVerdictResult(type.Name, unreadable);
            }
        }

        return new IdentityMatchVerdictResult(null, unreadable);
    }

    /// <summary>
    /// Equality of two canonical values in constant time — the platform's own comparison, never a
    /// loop of ours: the duration of a comparison must not tell an observer how far two values agree.
    /// </summary>
    /// <param name="actual">Canonical value of the confirming party.</param>
    /// <param name="expected">Canonical value the relying party expected.</param>
    /// <returns><see langword="true"/> when the two are the same value.</returns>
    private static bool ValuesEqual(string actual, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expected));
}

/// <summary>
/// Outcome of one comparison: the verdict, and how many expectations could not be restored.
/// </summary>
/// <param name="MatchedType">Name of the declared type whose value matched; null — no match.</param>
/// <param name="UnreadableExpectations">
/// Number of stored expectations the protection could not restore. It is diagnostics for the
/// operator, not an error of the transaction (E43).
/// </param>
internal readonly record struct IdentityMatchVerdictResult(string? MatchedType, int UnreadableExpectations);
