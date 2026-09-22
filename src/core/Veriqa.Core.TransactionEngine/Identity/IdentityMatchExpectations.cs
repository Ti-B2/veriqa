// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Configuration;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Services;

namespace Veriqa.Core.TransactionEngine.Identity;

/// <summary>
/// Acceptance of the expectations a relying party states about who is going to confirm
/// (SPEC-039 C23, L20). It lives next to the verdict that reads them back
/// (<see cref="IdentityMatchVerdict"/>): what is written and what is compared are two halves of one
/// rule, and splitting them across assemblies is how the two would come to disagree.
/// </summary>
public static class IdentityMatchExpectations
{
    /// <summary>
    /// Turns the stated expectations into the container that is stored with the transaction: every
    /// type has to be declared, every value has to have a canonical form under the rule of its type,
    /// and every value is stored protected.
    /// </summary>
    /// <remarks>
    /// A refusal rejects the whole operation — a partial accept is not a thing on this path (L20) —
    /// and the message of a refusal carries neither the value nor the type it came from: the failure
    /// travels outward to the relying party, and the values are exactly what must not (N31, N35).
    /// The two causes of <c>candidate_type_undeclared</c> — "this type is not declared" and "nothing
    /// is declared at all" — deliberately share a code (E42).
    /// </remarks>
    /// <param name="expectations">Stated expectations: type name → expected value.</param>
    /// <param name="declaredTypes">Comparable types the effective configuration declares.</param>
    /// <param name="protector">Reversible protection of the values.</param>
    /// <returns>The container to store, or the refusal of the operation.</returns>
    public static Result<IdentityMatchState> Accept(
        IReadOnlyDictionary<string, string> expectations,
        IReadOnlyList<ComparableIdentityType> declaredTypes,
        IIdentityValueProtector protector)
    {
        ArgumentNullException.ThrowIfNull(expectations);
        ArgumentNullException.ThrowIfNull(declaredTypes);
        ArgumentNullException.ThrowIfNull(protector);

        var declared = declaredTypes.ToDictionary(static type => type.Name, StringComparer.Ordinal);
        var accepted = new Dictionary<string, string>(expectations.Count, StringComparer.Ordinal);

        foreach (var (typeName, expected) in expectations)
        {
            if (!declared.TryGetValue(typeName, out var type))
            {
                return Result<IdentityMatchState>.Failure(
                    TransactionErrorCodes.CandidateTypeUndeclared,
                    "An expected identity type is not declared for this application.");
            }

            if (!IdentityValueNormalizer.TryNormalize(type.Normalization, expected, out var normalized))
            {
                return Result<IdentityMatchState>.Failure(
                    TransactionErrorCodes.CandidateValueInvalid,
                    "An expected identity value has no canonical form under the rule of its type.");
            }

            accepted[typeName] = protector.Protect(normalized);
        }

        return Result<IdentityMatchState>.Success(new IdentityMatchState
        {
            Expectations = accepted.ToFrozenDictionary(StringComparer.Ordinal)
        });
    }
}
