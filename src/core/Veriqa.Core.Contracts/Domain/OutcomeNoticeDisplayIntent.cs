// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// How the deployment WANTS the receipt of a terminal outcome to appear in the conversation
/// (SPEC-003 §6.2): in place of the message that asked the question, or as a message of its own.
/// <para>
/// This is a desired INTENT, not a guarantee and not an obligation of the adapter. A platform may
/// have nothing editable to replace — a message it never sent, one delivered too long ago, an API
/// with no edit operation at all — and an adapter is free to show the outcome the only way its
/// platform allows. An unhonoured intent is not a refusal: it is not logged as an error, and it does
/// not change the result of <c>IChannelAdapter.ReportOutcomeAsync</c>.
/// </para>
/// </summary>
/// <remarks>
/// The value names are read from configuration by name, so they are part of the settings contract of
/// a deployment. The default of the type is the value the product ships with, which keeps a notice
/// built without stating the intent — by an already compiled caller, for instance — behaving exactly
/// as it did before the field existed.
/// </remarks>
public enum OutcomeNoticeDisplayIntent
{
    /// <summary>
    /// The receipt should take the place of the message the question was shown in: the conversation
    /// keeps one message, and the buttons of the question go away with the text they belonged to.
    /// The shipped value — it is what the channels did before the intent existed.
    /// </summary>
    ReplacePrompt,

    /// <summary>
    /// The receipt should arrive as a separate message, and the message that asked the question stays
    /// in the conversation with its text untouched — the record of what the outcome answered — while
    /// the buttons of that question are taken away, so a settled transaction is not left with a live
    /// button under it. Where a platform cannot change the buttons of a delivered message without
    /// rewriting its text, the question is left alone: as with the intent itself, that is no refusal.
    /// </summary>
    NewMessage
}
