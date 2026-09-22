// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

using Veriqa.Core.Configuration;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Startup validator of the FORM of the <c>Veriqa:MessageTemplates</c> section (SPEC-036 TPL-111,
/// SPEC-012 §8). Registered with ValidateOnStart, so a malformed declaration fails the application at
/// startup (CFG-151) rather than surprising the runtime (TPL-033).
/// <para>
/// It checks what one declaration is in itself: a slot is a well-formed slot, a step of a ladder states
/// at least one non-blank edition, and a node stands where a step of the corresponding ladder looks — narrowed by the axes of
/// some step AND nested in the canonical order of the groups
/// (TPL-111, the "step of a ladder named by an undeclared dimension" rule) — a group nobody reads is a declaration that would be silently ignored. The FORM of
/// the kind identifier is deliberately not checked, and neither is a collision with a kind the product
/// ships: the kind is a value of the identity dimension now, and spelling one the product also ships
/// is an ordinary override of it (TPL-114). Whether a ladder AGREES WITH ITS CONTRACT is a different question, asked across
/// the two settings by <see cref="MessageContractConformanceValidator"/>: the halves of a message are
/// two values now, and one validator holding both questions would be the old single-value schema
/// under a new name.
/// </para>
/// <para>
/// <b>Both sources of the core level, not only the host's section.</b> The declarations the product
/// ships are read at the same addresses and in the same shape, and they are the ones no deployment
/// could ever repair — so their form is checked here too, with the address saying which of the two
/// files is at fault. Nowhere else asks: the conformance check across the two settings skips a contract
/// it cannot build, so a shipped declaration nothing else reads would pass startup in silence and throw
/// on the first send.
/// </para>
/// </summary>
public sealed class MessageTemplatesOptionsValidator : IValidateOptions<MessageTemplatesOptions>
{
    /// <summary>
    /// What an address inside the deployment's own section carries — nothing: it already IS the
    /// address an operator opens the configuration at.
    /// </summary>
    private const string DeploymentAddressSuffix = "";

    /// <summary>
    /// What an address inside the shipped file carries, so a defect of the product's own declarations
    /// is never read as a defect of the deployment's configuration — the two states the same shape at
    /// the same addresses, and only this suffix tells an operator which of them to go and repair.
    /// </summary>
    private const string ShippedAddressSuffix = " (the declaration the product ships)";

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MessageTemplatesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        Validate(options, DeploymentAddressSuffix, errors);

        // The declarations the product ships are checked HERE and nowhere else. They are an ordinary
        // source of the core level and are read by the very same walk, so a defect in them is a defect
        // of the same form — but nothing else asks the question: the conformance check across the two
        // settings deliberately says nothing about the form of a declaration, and skips a contract it
        // cannot build. A misspelling in the shipped file would otherwise pass startup in silence and
        // surface as a throw on the first send of a kind no test happens to cover.
        Validate(ShippedMessageTemplates.Options, ShippedAddressSuffix, errors);

        return errors.Count is 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    /// <summary>
    /// Validates every node of one source of declarations.
    /// </summary>
    /// <param name="declarations">Declarations of the source.</param>
    /// <param name="suffix">What an address of this source carries, so a report names the file to repair.</param>
    /// <param name="errors">Accumulator of the defects found.</param>
    private static void Validate(
        MessageTemplatesOptions declarations,
        string suffix,
        ICollection<string> errors)
    {
        foreach (var (kind, declaration) in declarations.Kinds)
        {
            foreach (var node in MessageDeclarationWalk.Walk(kind, declaration))
            {
                Validate(node, node.Address + suffix, errors);
            }
        }
    }

    /// <summary>
    /// Validates one node of a declaration.
    /// </summary>
    /// <param name="node">Node of the declaration.</param>
    /// <param name="address">Address of the node as a report names it.</param>
    /// <param name="errors">Accumulator of the defects found.</param>
    private static void Validate(MessageDeclarationNode node, string address, ICollection<string> errors)
    {
        if (node.Declaration.Contract is { } contract)
        {
            ValidateStep(node, address, MessageTemplateConfigKeys.Contract.Dimensions, "contract", errors);

            var defects = new List<string>();

            if (MessageContract.TryBuild(contract, defects) is null)
            {
                foreach (var defect in defects)
                {
                    errors.Add($"{address}: {defect}");
                }
            }
        }

        if (node.Declaration.Templates.Count is 0)
        {
            return;
        }

        ValidateStep(node, address, MessageTemplateConfigKeys.Template.Dimensions, "template ladder", errors);

        for (var index = 0; index < node.Declaration.Templates.Count; index++)
        {
            var variant = node.Declaration.Templates[index];

            // A step states at least one edition — that is what makes it usable by some sink at all. A
            // step stating none is not a short spelling of anything, and it is also what a declaration
            // written against the FORMER shape of the value (a render mode and one body) binds to, since
            // neither member exists any more. Saying so by the address of the ladder and the number of
            // the step is the whole repair instruction; ignoring the unknown members in silence would
            // leave a deployment convinced its wording is in effect.
            if (variant is null || (variant.Html is null && variant.Plain is null))
            {
                errors.Add(
                    $"{address}: template variant {index} states neither the "
                    + $"'{nameof(MessageTemplateVariant.Html)}' nor the "
                    + $"'{nameof(MessageTemplateVariant.Plain)}' edition. A step states at least one of "
                    + "them; a step spelled as a plain string states both.");

                continue;
            }

            // A step spelled as a string carries one text in both editions, so a blank one is a single
            // defect and is reported once.
            if (IsBlank(variant.Html) || IsBlank(variant.Plain))
            {
                errors.Add($"{address}: template variant {index} is blank.");
            }
        }
    }

    /// <summary>
    /// Whether a STATED text is blank. An edition the step does not state at all is not blank — it is
    /// the legitimate "this step does not serve that sink".
    /// </summary>
    /// <param name="text">Text of an edition, or null when the step states none.</param>
    /// <returns><c>true</c> when the edition is stated and holds nothing but whitespace.</returns>
    private static bool IsBlank(string? text) => text is not null && string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// Checks that a node stands where a step of the ladder of its setting actually looks. That is two
    /// questions, and a declaration has to pass both: the SET of axes it is narrowed by has to be one
    /// of the steps, and the ORDER the groups are nested in has to be the canonical one. A node failing
    /// either would never be resolved — the deployment would have written a declaration nothing ever
    /// answers with, and silence is the worst possible report of that.
    /// <para>
    /// The order is a question of its own because the resolution does not search: the address of a key
    /// enters the groups in one fixed order and once each
    /// (<see cref="MessageTemplateConfigKeys"/>), so <c>BySurface:…:ByType:…</c> holds the very axes of
    /// a step and is still unreachable. Checking the set alone would pass it at startup and leave the
    /// wording it states silently unread.
    /// </para>
    /// </summary>
    /// <param name="node">Node of the declaration.</param>
    /// <param name="address">Address of the node as a report names it.</param>
    /// <param name="dimensions">Dimensions of the setting stated at the node.</param>
    /// <param name="setting">What is stated at the node, as the message names it.</param>
    /// <param name="errors">Accumulator of the defects found.</param>
    private static void ValidateStep(
        MessageDeclarationNode node,
        string address,
        ConfigKeyDimensions? dimensions,
        string setting,
        ICollection<string> errors)
    {
        if (!node.Addressable)
        {
            errors.Add(
                $"{address}: a {setting} stated here is nested by groups written out of the order "
                + "an address of this setting reads them (or by one group twice), so nothing would ever "
                + $"resolve to it. The groups nest in the order {MessageDeclarationWalk.GroupOrder}.");

            return;
        }

        foreach (var step in dimensions!.Fallback)
        {
            if (step.Count == node.Point.Count && step.IsSupersetOf(node.Point.Keys))
            {
                return;
            }
        }

        errors.Add(
            $"{address}: a {setting} stated here is narrowed by a combination of axes no step of its "
            + "ladder addresses, so nothing would ever resolve to it.");
    }
}
