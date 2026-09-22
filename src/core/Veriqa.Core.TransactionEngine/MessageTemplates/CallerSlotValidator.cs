// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text;

using Microsoft.Extensions.Logging;

using Veriqa.Core.TransactionEngine.Common;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Fail-fast validator of caller-supplied slot values at the engine level (SPEC-036 §4.4, §4.5, §5.1).
/// <para>
/// <b>No production carrier in this scope.</b> Track A (TASK-078 + its future SPEC) will introduce the
/// wire transport of caller values into <c>CreateTransactionRequest</c> and call this validator from the
/// creation path; until then the ONLY consumers are the unit tests (synthetic caller values, TPL-090)
/// and this documented seam (§4.4). <c>CreateTransactionRequest</c>/<c>/authorize</c>/sandbox are
/// deliberately left untouched — caller values never arrive there today (avoiding a dead/miswired path).
/// This type is a public engine contract precisely so the trek-A entry point can consume it verbatim.
/// </para>
/// </summary>
public sealed class CallerSlotValidator
{
    private readonly IConfigurationResolver _resolver;
    private readonly ILogger<CallerSlotValidator>? _logger;

    /// <summary>
    /// Creates the validator over the canonical resolver.
    /// </summary>
    /// <param name="resolver">Canonical configuration resolver (contract resolution, §5.1 step 1).</param>
    /// <param name="logger">Logger for slot security events (null — none).</param>
    public CallerSlotValidator(IConfigurationResolver resolver, ILogger<CallerSlotValidator>? logger = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger;
    }

    /// <summary>
    /// Resolves the CONTRACT of <paramref name="kind"/> by (tenant, application) and validates the
    /// caller values against its caller slots (§5.1). Server slots are never fail-fast validated (TPL-024).
    /// </summary>
    /// <param name="kind">Message kind identifier (<see cref="MessageKinds"/>).</param>
    /// <param name="context">Resolution context (tenant/application).</param>
    /// <param name="callerValues">Slot name → raw string value supplied by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validated frozen set on success, or a structured failure (§4.5).</returns>
    public async ValueTask<Result<ValidatedCallerValues>> ValidateAsync(
        string kind,
        ResolutionContext context,
        IReadOnlyDictionary<string, string> callerValues,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(callerValues);

        // Step 1 — resolve the contract of the message. A kind no level declares a contract for is an
        // infrastructure condition, not a business code (SPEC-001 §9.2, §6): without a contract there is
        // no validation contract at all, so creating a transaction WITH caller values cannot proceed.
        var contract = (await _resolver.ResolveAsync(
            MessageTemplateConfigKeys.Contract,
            context,
            ConfigDimensionValues.Of((MessageTemplateConfigKeys.KindDimensionName, kind)),
            cancellationToken)).Value;

        if (contract is null)
        {
            throw new InvalidOperationException(
                $"No message contract resolved for kind '{kind}'; caller values cannot be validated.");
        }

        return Validate(kind, contract, callerValues, _logger);
    }

    /// <summary>
    /// Validates caller values against an already-resolved contract (§5.1 steps 2–6). Pure and
    /// directly unit-testable with a synthetic contract.
    /// </summary>
    /// <param name="kind">Message kind identifier — what a failure names the message by.</param>
    /// <param name="contract">The effective contract of the message.</param>
    /// <param name="callerValues">Slot name → raw string value.</param>
    /// <param name="logger">Logger for enum security events (null — none).</param>
    /// <returns>The validated frozen set, or a structured failure.</returns>
    public static Result<ValidatedCallerValues> Validate(
        string kind,
        MessageContract contract,
        IReadOnlyDictionary<string, string> callerValues,
        ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(callerValues);

        // Step 2 — every supplied name must be a declared CALLER slot. Server-slot names do not exist for
        // a caller (§6): they resolve to slot_undeclared, and the whole operation is rejected (no partial
        // accept).
        foreach (var name in callerValues.Keys)
        {
            var slot = contract.FindSlot(name);
            if (slot is null || !slot.IsCaller)
            {
                return Failure(
                    TransactionErrorCodes.SlotUndeclared,
                    $"Slot '{name}' is not a declared caller slot of kind '{kind}'.");
            }
        }

        // Step 3 — every required caller slot must have a value.
        foreach (var slot in contract.Slots)
        {
            if (slot is { IsCaller: true, Required: true } && !callerValues.ContainsKey(slot.Name))
            {
                return Failure(
                    TransactionErrorCodes.SlotRequiredMissing,
                    $"Required caller slot '{slot.Name}' of kind '{kind}' has no value.");
            }
        }

        // Step 5 — aggregate size guard (TPL-025, D4). Ordered AFTER the undeclared/required business
        // checks so slot_undeclared/slot_required_missing win the precedence mandated by SPEC-036 §5.1,
        // and BEFORE the per-value type validation so an oversize aggregate never reaches the expensive
        // per-value checks (rough cutoff on huge input).
        if (ExceedsSizeLimit(callerValues))
        {
            return Failure(
                TransactionErrorCodes.SlotValuesTooLarge,
                $"The caller values exceed the aggregate size limit of {MessageTemplateLimits.MaxCallerValuesBytes} bytes.");
        }

        // Step 4 — validate every value by type; collect ALL violations so the RP fixes them in one pass.
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var (name, rawValue) in callerValues)
        {
            var slot = contract.FindSlot(name)!;
            var result = SlotValueValidator.Validate(slot, rawValue, kind, logger);
            if (result.IsSuccess)
            {
                normalized[name] = result.Value;
            }
            else
            {
                violations.Add($"{name}: {result.Error.Code}");
            }
        }

        if (violations.Count > 0)
        {
            return Failure(
                TransactionErrorCodes.SlotValueInvalid,
                $"Invalid caller values: {string.Join(", ", violations)}.");
        }

        // Step 6 — success: the values are frozen for the append-only transaction.
        return Result<ValidatedCallerValues>.Success(new ValidatedCallerValues(normalized));
    }

    /// <summary>
    /// Computes whether the aggregate UTF-8 size of the caller values exceeds the limit (TPL-025).
    /// </summary>
    private static bool ExceedsSizeLimit(IReadOnlyDictionary<string, string> callerValues)
    {
        long total = 0;
        foreach (var (name, value) in callerValues)
        {
            total += Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(value);
            if (total > MessageTemplateLimits.MaxCallerValuesBytes)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds a failed result with the given code and message.
    /// </summary>
    private static Result<ValidatedCallerValues> Failure(string code, string message) =>
        Result<ValidatedCallerValues>.Failure(code, message);
}
