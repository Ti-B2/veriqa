// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// The startup SIGNAL over the message declarations: a ladder is a net under missing data, not a
/// substitute for wording that was never written (SPEC-036 §4.3), and this says out loud — once, at
/// startup — when a deployment has most likely traded the net away without meaning to.
/// <para>
/// <b>Why a warning and not a refusal.</b> Both declarations it reports are LEGITIMATE: a short ladder
/// is a valid ladder, and a mail stating no plain-text edition is the way "this mail is HTML only" is
/// spelled (SPEC-016 §4.3, where the two parts of a mail are required by default and the default is
/// lifted by an explicit declaration). Refusing to start would forbid what the specification allows;
/// staying silent would leave the most common configuration mistake undiagnosed. The middle is to say
/// it once.
/// </para>
/// <para>
/// <b>Why a hosted service and not a third <see cref="IValidateOptions{TOptions}"/>.</b> A validator
/// answers with errors — there is no warning in its result — and it runs whenever the options are
/// read. A report belongs to the start of the host, once per kind and address, never per render.
/// </para>
/// <para>
/// <b>What it cannot see, and why the message says so.</b> Only the two sources of the CORE level can
/// be enumerated at startup: the section a deployment writes and the file the product ships. A tenant,
/// an application and a <c>ui_config</c> record state their values in records this check cannot list —
/// the same limit <see cref="MessageContractConformanceValidator"/> names. Catching those would mean
/// reporting on every resolution, which is noise rather than a signal, so the messages state the limit
/// instead of implying that everything was checked.
/// </para>
/// </summary>
internal sealed class MessageTemplateDegradationStartupDiagnostics : IHostedService
{
    /// <summary>
    /// Message declarations of the deployment — the only source this check reports on: a defect of the
    /// shipped file is not an operator's to repair, and the checks of the form and of the contract
    /// already hold it.
    /// </summary>
    private readonly IOptions<MessageTemplatesOptions> _options;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<MessageTemplateDegradationStartupDiagnostics> _logger;

    /// <summary>
    /// Creates the diagnostics.
    /// </summary>
    /// <param name="options">Message declarations of the deployment.</param>
    /// <param name="logger">Logger.</param>
    public MessageTemplateDegradationStartupDiagnostics(
        IOptions<MessageTemplatesOptions> options,
        ILogger<MessageTemplateDegradationStartupDiagnostics> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var stated = _options.Value;
        var shipped = ShippedMessageTemplates.Options;

        foreach (var (kind, declaration) in stated.Kinds)
        {
            // A node no resolution can reach renders nothing whatever it states, and the check of the
            // FORM already reports it: repeating it here would say one defect twice in two wordings.
            foreach (var node in MessageDeclarationWalk.Walk(kind, declaration))
            {
                if (node is not { Addressable: true, Declaration.Templates.Count: > 0 })
                {
                    continue;
                }

                ReportShortenedLadder(node, shipped);
                ReportUnstatedPlainEdition(node);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The ladder of the product at this address was replaced by a shorter one. Comparison and report
    /// are BY ADDRESS: the steps of the narrowing ladder are independent of one another, nothing is
    /// merged between them, and a kind stated at several points is therefore several declarations.
    /// </summary>
    /// <param name="node">Node of the deployment stating a ladder.</param>
    /// <param name="shipped">Declarations the product ships.</param>
    private void ReportShortenedLadder(MessageDeclarationNode node, MessageTemplatesOptions shipped)
    {
        var statedSteps = node.Declaration.Templates.Count;
        var shippedSteps = MessageDeclarationWalk.LadderSizeAt(shipped, node.Kind, node.Point);

        // Nothing shipped at this address — nothing was lost; a ladder as long as the shipped one, or
        // longer, replaced it without dropping a step.
        if (shippedSteps <= statedSteps)
        {
            return;
        }

        _logger.LogWarning(
            "Message templates: '{Address}' states {StatedSteps} template step(s) where the product "
            + "ships {ShippedSteps} for message kind '{Kind}'. A stated ladder REPLACES the shipped one "
            + "whole — the two are never merged — so the degradation steps of the product no longer "
            + "render at this address. If only the wording was meant to change, state the shorter "
            + "wordings alongside the new one. Startup sees the core level only: what a tenant, an "
            + "application or a ui_config record states cannot be enumerated here.",
            node.Address,
            statedSteps,
            shippedSteps,
            node.Kind);
    }

    /// <summary>
    /// No step of this ladder states the plain-text edition — for a mail, "HTML only". The condition
    /// needs no notion of which kinds are mail kinds: a step spelled as a string states BOTH editions,
    /// so a ladder can reach this state only by stating its steps as objects naming the HTML edition
    /// alone, which is the very declaration the report is about.
    /// </summary>
    /// <param name="node">Node of the deployment stating a ladder.</param>
    private void ReportUnstatedPlainEdition(MessageDeclarationNode node)
    {
        const MessageTemplateEdition edition = MessageTemplateEdition.Plain;

        foreach (var step in node.Declaration.Templates)
        {
            if (step?.TextOf(edition) is not null)
            {
                return;
            }
        }

        _logger.LogWarning(
            "Message templates: no step of the ladder at '{Address}' states the {Edition} edition for "
            + "message kind '{Kind}', so a mail of this kind goes out without its plain-text part. A "
            + "one-part message is a legitimate declaration and the start is not blocked — but in the "
            + "data it is indistinguishable from a wording that was simply not written, so state that "
            + "edition on at least one step unless the one-part message is the intent. Startup sees the "
            + "core level only: what a tenant, an application or a ui_config record states cannot be "
            + "enumerated here.",
            node.Address,
            edition,
            node.Kind);
    }
}
