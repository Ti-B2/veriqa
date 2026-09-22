// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Security.Claims;
using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Interface for mapping the resolved identity and completion snapshot to OIDC claims.
/// <para>
/// The method is asynchronous because the substitution scenario the seam exists for — enriching the
/// claims from the integrator's own database or directory — is I/O bound. A synchronous contract
/// would force such an implementation into sync-over-async.
/// </para>
/// </summary>
public interface IClaimsMapper
{
    /// <summary>
    /// Maps transaction data to a set of OIDC claims with destinations.
    /// </summary>
    /// <param name="resolvedIdentity">Resolved identity from the completed transaction.</param>
    /// <param name="completion">Transaction completion data.</param>
    /// <param name="context">Context of the relying party the claims are built for.</param>
    /// <param name="cancellationToken">Token that cancels the mapping.</param>
    /// <returns>Result with the set of claims, or an error.</returns>
    Task<Result<IReadOnlyList<Claim>>> MapToClaimsAsync(
        ResolvedIdentitySnapshot resolvedIdentity,
        CompletionSnapshot completion,
        ClaimsMappingContext context,
        CancellationToken cancellationToken = default);
}
