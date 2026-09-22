// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Domain;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Settings of the receipt of a terminal outcome. Configuration section:
/// <see cref="SectionName"/> (<c>Veriqa:Channels:OutcomeNotice</c>) — the core level of the key
/// <c>Channels.OutcomeNotice.DisplayIntent</c>, read through the shared resolver (SPEC-012 §10.6)
/// like every other setting of this contour.
/// </summary>
/// <remarks>
/// A subsection of <c>Veriqa:Channels</c> that is not a channel, as <see cref="ChannelLocalizationOptions"/>
/// already is: the channels of a deployment come from the declarations of the registered adapters,
/// never from enumerating the children of that section.
/// </remarks>
public sealed class OutcomeNoticeOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Veriqa:Channels:OutcomeNotice";

    /// <summary>
    /// How the deployment WANTS the receipt to appear in the conversation. The shipped value —
    /// <see cref="OutcomeNoticeDisplayIntent.ReplacePrompt"/>: a deployment that states nothing keeps
    /// the behaviour the channels had before this setting existed.
    /// </summary>
    public OutcomeNoticeDisplayIntent DisplayIntent { get; set; } = OutcomeNoticeDisplayIntent.ReplacePrompt;
}
