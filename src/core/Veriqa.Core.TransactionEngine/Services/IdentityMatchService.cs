// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Configuration;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.Identity;

namespace Veriqa.Core.TransactionEngine.Services;

/// <summary>
/// The engine's side of the identity-match axis (SPEC-039 R40): it resolves what the ownership levels
/// declare, computes the verdict at the point the resolved identity is written, and answers whether
/// two creation requests state the same expectations.
/// </summary>
/// <remarks>
/// The protection port is asked for once per operation and never held, exactly as
/// <c>IChannelIdentityRepository</c> is: it is implemented by the composition that owns a key ring —
/// the auth server — and a host taking the engine alone legitimately registers none. Injecting it
/// would make that composition unbuildable and would pin a lifetime on an implementation that is not
/// this assembly's to choose.
/// </remarks>
internal sealed class IdentityMatchService
{
    /// <summary>
    /// Canonical resolver of the declared set across ownership levels.
    /// </summary>
    private readonly IConfigurationResolver _resolver;

    /// <summary>
    /// Source of the scope the protection port is asked for in.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Logger. Never carries an expectation, a claim or a restored value (N35).
    /// </summary>
    private readonly ILogger<IdentityMatchService> _logger;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="resolver">Canonical configuration resolver.</param>
    /// <param name="scopeFactory">Scope factory used to ask for the protection port.</param>
    /// <param name="logger">Logger.</param>
    public IdentityMatchService(
        IConfigurationResolver resolver,
        IServiceScopeFactory scopeFactory,
        ILogger<IdentityMatchService> logger)
    {
        _resolver = resolver;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Computes the verdict of the comparison for a transaction that is being completed (L41).
    /// </summary>
    /// <remarks>
    /// Nothing here refuses the completion: an empty container, an absent claim, a value that cannot
    /// be restored and a plain mismatch all answer "no verdict" (E43). The decision on a transaction
    /// whose confirming party did not match belongs to the relying party, not to the server.
    /// </remarks>
    /// <param name="transaction">Transaction being completed.</param>
    /// <param name="resolvedIdentity">Identity of the party that confirmed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Name of the matched declared type, or null when nothing matched.</returns>
    public async ValueTask<string?> ComputeVerdictAsync(
        Transaction transaction,
        ResolvedIdentitySnapshot resolvedIdentity,
        CancellationToken cancellationToken)
    {
        var expectations = transaction.IdentityMatch?.Expectations;
        if (expectations is not { Count: > 0 })
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var protector = scope.ServiceProvider.GetService<IIdentityValueProtector>();
        if (protector is null)
        {
            // Only a composition holding the port can have written expectations in the first place,
            // so reaching this line means the port was taken away between the two moments.
            _logger.LogWarning(
                "Identity match skipped: no value protector is registered. TransactionId: {TransactionId}",
                transaction.Id.ToString());

            return null;
        }

        var declaredTypes = await ResolveDeclaredTypesAsync(transaction, cancellationToken);

        var verdict = IdentityMatchVerdict.Compute(
            expectations,
            declaredTypes,
            resolvedIdentity,
            protector);

        if (verdict.UnreadableExpectations > 0)
        {
            // The likely cause is a key ring rotated without access to the previous keys. It is the
            // operator's problem to see; the confirmation is not cancelled by it.
            _logger.LogWarning(
                "Identity match could not restore {UnreadableCount} stored expectation(s). TransactionId: {TransactionId}",
                verdict.UnreadableExpectations,
                transaction.Id.ToString());
        }

        return verdict.MatchedType;
    }

    /// <summary>
    /// Answers whether two creation requests state the same expectations — the identity-match term of
    /// the idempotent-repeat comparison (SPEC-039 E30).
    /// </summary>
    /// <remarks>
    /// The comparison is made on the RESTORED values and never on the stored ones: the protection is
    /// randomized, so two protections of one and the same value differ, and comparing the stored form
    /// would turn every honest retry into a conflict. A value that cannot be restored is reported as
    /// "not the same": the server may not claim an equality it could not check.
    /// </remarks>
    /// <param name="existing">Expectations stored with the existing transaction.</param>
    /// <param name="requested">Expectations of the repeated request.</param>
    /// <returns><see langword="true"/> when the two state the same expectations.</returns>
    public bool ExpectationsMatch(
        IReadOnlyDictionary<string, string>? existing,
        IReadOnlyDictionary<string, string>? requested)
    {
        var existingCount = existing?.Count ?? 0;
        var requestedCount = requested?.Count ?? 0;

        if (existingCount != requestedCount)
        {
            return false;
        }

        if (existingCount is 0)
        {
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var protector = scope.ServiceProvider.GetService<IIdentityValueProtector>();
        if (protector is null)
        {
            _logger.LogWarning(
                "Idempotent repeat cannot be compared on its expectations: no value protector is registered.");

            return false;
        }

        foreach (var (typeName, storedValue) in existing!)
        {
            if (!requested!.TryGetValue(typeName, out var repeatedValue)
                || !protector.TryUnprotect(storedValue, out var stored)
                || !protector.TryUnprotect(repeatedValue, out var repeated)
                || !string.Equals(stored, repeated, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the comparable types declared for the ownership levels of the transaction.
    /// </summary>
    /// <remarks>
    /// The context carries the tenant and the application and NOT the <c>ui_config</c> selector: a
    /// record picked by a request parameter carries wording and styling, never comparison rights
    /// (SPEC-012 CFG-161).
    /// </remarks>
    /// <param name="transaction">Transaction whose ownership context is read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Declared comparable types; an empty list when none are declared.</returns>
    private async ValueTask<IReadOnlyList<ComparableIdentityType>> ResolveDeclaredTypesAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        var context = ResolutionContext.Of(
            transaction.GetTenantId(),
            transaction.GetApplicationId(),
            uiConfigSelector: null);

        var resolved = await _resolver.ResolveAsync(
            IdentityMatchConfigKeys.ComparableTypes,
            context,
            ConfigDimensionValues.None,
            cancellationToken);

        return resolved.Value ?? [];
    }
}
