// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Well-known reasons why a confirmation prompt carries no initiator details (SPEC-017 §7.1).
/// Rendering is the same for every reason; the distinction exists for diagnostics.
/// </summary>
public static class ContextSuppressionReasons
{
    /// <summary>
    /// The initiator context snapshot was never collected for the transaction.
    /// </summary>
    public const string InitiatorSnapshotUnavailable = "initiator_snapshot_unavailable";

    /// <summary>
    /// Displaying the initiator context is switched off by the application configuration (ICC-081).
    /// </summary>
    public const string DisplayDisabledByConfiguration = "display_disabled_by_configuration";

    /// <summary>
    /// No message declares the subject of a confirmation transaction, so the question was not asked at
    /// all (SPEC-039 E28). Unlike its two neighbours this reason never reaches a user: a prompt that
    /// carries it is not sent, and the transaction ends with the reason code of the same value.
    /// <para>
    /// The value is stated HERE, in the package both halves of that one fact can see: the diagnostic
    /// half travels on the prompt context, and the transaction half is the reason code the core ends
    /// the transaction with, so an operator correlating the two greps one string rather than two.
    /// </para>
    /// </summary>
    public const string ConfirmationTemplateUnavailable = "confirmation_template_unavailable";
}
