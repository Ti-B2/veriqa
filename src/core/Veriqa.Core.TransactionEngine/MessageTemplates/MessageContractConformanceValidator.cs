// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// The startup check ACROSS the two settings of a message (SPEC-036 TPL-111): a template variant may
/// reference no slot the contract serving it does not declare, and the minimal variant of EVERY step
/// of the ladder — of every EDITION of every step — must rest on values that are always there.
/// <para>
/// It is a check point of its own, and the name is new for a reason: the two halves of a message used
/// to arrive as one value, so "ladder against contract" was a check inside one declaration. They are
/// two settings now, resolved apart — the ladder of a deployment over the contract the product
/// ships — and this is where the two meet before anything renders.
/// </para>
/// <para>
/// <b>Every step of the ladder, not only the finest one.</b> A step that states a ladder REPLACES it
/// whole: no merging happens between steps, so a step of one variant referencing an optional slot
/// would leave the token in the text as a literal (<see cref="SlotTokenScanner.Substitute"/>). That is
/// a defect of the configuration, not a short spelling of it. The check is two-dimensional for the same
/// reason: within one step the two editions degrade apart, so the floor is asked of every pair of
/// narrowing step and edition.
/// </para>
/// <para>
/// <b>The pairs are CROSSED, because the two halves fall back apart.</b> Each key reads the shipped
/// declarations on its own, at its own address, so a deployment stating only a ladder renders it under
/// the shipped contract — and one stating only a contract puts that contract over the ladder the
/// product ships. Both crossings are checked: skipping the second would leave the tighter contract of
/// a deployment unpaired with the very variants it will be judged against at render time, and the
/// fail-closed of the runtime half would then fire on a well-meant configuration nothing warned about.
/// </para>
/// <para>
/// <b>What it cannot reach, and why that is not a hole.</b> A level above the core states its values
/// in records this check cannot enumerate at startup, and a template whose contract lives at such a
/// level is skipped rather than reported: a rule about values that are not there yet would refuse to
/// start a deployment that is perfectly well configured. Pairs are formed BY ADDRESS for the same
/// reason — a caller narrowing the contract finer than any ladder is stated at forms a pair no
/// declaration shows, and enumerating those would mean enumerating the points a caller may ask with.
/// What holds there is the runtime half below, which is fail-closed: an unadmitted variant renders
/// nothing, it does not render badly. The <c>ui_config</c> level is unreachable by
/// construction — its records are chosen by a request parameter — and the norm that holds there is a
/// runtime one: a variant referencing a slot outside the contract is REJECTED at render time, and the
/// resolution goes on with the next variant and then with the level below.
/// </para>
/// </summary>
public sealed class MessageContractConformanceValidator : IValidateOptions<MessageTemplatesOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MessageTemplatesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var shipped = ShippedMessageTemplates.Options;

        // Every ladder a render can reach, whichever source states it — the shipped declarations are
        // an ordinary source of the core level, and a defect in them is the one defect no deployment
        // could ever repair — against the contract that would ACTUALLY serve it.
        foreach (var (node, isShipped) in Ladders(options, shipped))
        {
            var stated = MessageDeclarationWalk.FindContract(options, shipped, node.Kind, node.Point);

            if (stated is null)
            {
                continue;
            }

            // A malformed contract is reported by the validator of the FORM — of whichever of the two
            // sources states it, the deployment's section and the shipped file alike; reporting it a
            // second time here would give an operator two lines about one defect.
            var contract = MessageContract.TryBuild(stated, new List<string>());

            if (contract is null)
            {
                continue;
            }

            Check(node, contract, isShipped, errors);
        }

        return errors.Count is 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    /// <summary>
    /// Every template ladder in effect: the ones the deployment states, and the shipped ones no address
    /// of the deployment covers.
    /// </summary>
    /// <param name="stated">Declarations of the deployment.</param>
    /// <param name="shipped">Declarations the product ships.</param>
    /// <returns>The nodes stating a ladder, each with the source it came from.</returns>
    private static IEnumerable<(MessageDeclarationNode Node, bool IsShipped)> Ladders(
        MessageTemplatesOptions stated,
        MessageTemplatesOptions shipped)
    {
        foreach (var (kind, declaration) in stated.Kinds)
        {
            foreach (var node in MessageDeclarationWalk.Walk(kind, declaration))
            {
                if (node is { Addressable: true, Declaration.Templates.Count: > 0 })
                {
                    yield return (node, false);
                }
            }
        }

        foreach (var (kind, declaration) in shipped.Kinds)
        {
            foreach (var node in MessageDeclarationWalk.Walk(kind, declaration))
            {
                // A shipped ladder the deployment covers at the same address renders nowhere: the core
                // level reads the shipped file only where the host configuration holds nothing there.
                // Checking it against the contract of the deployment would refuse to start an
                // installation whose own wording is perfectly well formed.
                if (node is { Addressable: true, Declaration.Templates.Count: > 0 }
                    && !MessageDeclarationWalk.StatesLadder(stated, kind, node.Point))
                {
                    yield return (node, true);
                }
            }
        }
    }

    /// <summary>
    /// Checks one ladder against the contract serving it.
    /// </summary>
    /// <param name="node">Node of the declaration stating the ladder.</param>
    /// <param name="contract">Contract serving the node.</param>
    /// <param name="isShipped">Whether the ladder is the one the product ships.</param>
    /// <param name="errors">Accumulator of the defects found.</param>
    private static void Check(
        MessageDeclarationNode node,
        MessageContract contract,
        bool isShipped,
        ICollection<string> errors)
    {
        // Where the ladder came from is half the repair instruction: a variant of the product paired
        // with a contract of the deployment is repaired at the contract, since the variant is not the
        // operator's to edit.
        var address = isShipped ? node.Address + " (the ladder the product ships)" : node.Address;

        var variants = node.Declaration.Templates;

        // Every token a variant carries must resolve to a slot the contract declares: an unresolved
        // token would reach the recipient as literal text (TPL-111a). ALL the texts of the variant are
        // walked, both editions at once, and that asymmetry with the runtime admission — which refuses
        // the offending EDITION and keeps the other — is deliberate: here the whole declaration is
        // refused before the host starts, so there is no ladder left to promote a floor in, while the
        // runtime branch has to keep serving the levels this check cannot enumerate.
        foreach (var variant in variants)
        {
            foreach (var text in variant.Texts())
            {
                foreach (var referenced in SlotTokenScanner.ExtractSlotNames(text))
                {
                    if (contract.FindSlot(referenced) is null)
                    {
                        errors.Add(
                            $"{address}: a template variant references slot '{referenced}', which the "
                            + "contract of this message does not declare.");
                    }
                }
            }
        }

        // The MINIMAL step of this ladder is the floor the render degrades to (TPL-032): it is the one
        // that must never leave a token unfilled, because there is nothing shorter behind it and the
        // step is not merged with any other. There is a floor PER EDITION, not one per step: a sink
        // asks the ladder for the edition it is about to deliver and degrades among the steps stating
        // it, so a ladder whose last step states only the plain edition still has an HTML floor of its
        // own — and it is the HTML part of the mail that would carry the token as a literal. Hence the
        // floor may rest only on slots that are always present (TPL-111e), and the report says WHICH
        // edition ends there: the two degrade apart, and repairing the wrong one changes nothing.
        foreach (var (edition, floor) in MessageTemplateVariant.Floors(variants))
        {
            foreach (var text in floor.TextsOf(edition))
            {
                foreach (var referenced in SlotTokenScanner.ExtractSlotNames(text))
                {
                    if (contract.FindSlot(referenced) is not { } slot || slot.IsAlwaysPresentIn(node.Kind))
                    {
                        continue;
                    }

                    // The remedy differs by kind: in an outcome receipt no server or caller slot counts
                    // as always present whatever its declaration says, so advising a guaranteed server
                    // slot there would advise exactly the declaration being refused.
                    var remedy = MessageKinds.IsOutcomeReceipt(node.Kind)
                        ? "in an outcome receipt only the localized-text slot may appear in the variant the "
                            + "render degrades to — its server and caller slots are never guaranteed, whatever "
                            + "their Guaranteed or Required flag says."
                        : "only a guaranteed server slot or a required caller slot may appear in the variant "
                            + "the render degrades to.";

                    errors.Add(
                        $"{address}: the minimal template variant of the {edition} edition rests on slot "
                        + $"'{referenced}', which is not always present — {remedy}");
                }
            }
        }
    }
}
