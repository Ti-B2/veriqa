// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Registry of the canonical resolver key of the outcome receipt (SPEC-012 §10.6): the display
/// intent of the ownership the receipt is shown for (<see cref="DisplayIntent"/>). It is resolved as
/// a plain value through the shared resolver, and no precedence of its own is built here (anti-fork
/// CFG-235).
/// <para>
/// The key is declared in ChannelAdapter because its consumers live here: the port that reads it
/// (<see cref="Pipeline.IOutcomeNoticeDisplayIntentSource"/>) and the render points of the receipt.
/// </para>
/// </summary>
public static class OutcomeNoticeConfigKeys
{
    /// <summary>
    /// Catalog of the declarations of this registry — what the one registrar of the deployment declares
    /// and binds. It is initialized before the declarations that fill it, which is the order the
    /// initializers of this class run in.
    /// </summary>
    private static readonly ConfigKeyCatalog Declared = new();

    /// <summary>
    /// The outcome receipt AS THE PRODUCT SHIPS IT — a default-constructed options object, read for one
    /// thing only: the intent a core level yields when the section states nothing. The shipped value is
    /// taken off the options class rather than restated here, so the two cannot drift.
    /// </summary>
    private static readonly OutcomeNoticeOptions Shipped = new();

    /// <summary>
    /// Canonical name of the display intent key.
    /// </summary>
    public const string DisplayIntentKeyName = "Channels.OutcomeNotice.DisplayIntent";

    /// <summary>
    /// Member of the record stating this setting above the core level — the record of a tenant and the
    /// OIDC client entry of an application alike. It is written as text rather than taken from the
    /// entry's type: the entry belongs to the auth server, and this contour does not reference it (a
    /// reverse dependency). The record is flat and shared by every owner writing into it, so the
    /// member carries the name of its subject.
    /// </summary>
    private const string RecordDisplayIntentMember = "OutcomeNoticeDisplayIntent";

    /// <summary>
    /// Desired display of the outcome receipt: in place of the message that asked the question, or as
    /// a message of its own. A plain value over the core, tenant and application levels — each level
    /// overrides the one below it freely. The core level always states a value: the intent of the
    /// global section, or the one the product ships with — an unset core level would leave the
    /// pipeline with the default of the enum instead of the default of the setting.
    /// <para>
    /// The <c>ui_config</c> level is NOT declared: in this shipping the record states no display
    /// intent. No domain of admitted values is declared either, and that one is on purpose: a misspelt
    /// member then reads as nothing stated and the level below answers, instead of stopping the host
    /// over a setting about the look of a receipt.
    /// </para>
    /// </summary>
    public static ConfigKey<OutcomeNoticeDisplayIntent> DisplayIntent { get; } = Declared
        .Of<OutcomeNoticeDisplayIntent>(DisplayIntentKeyName)
        .At(
            ConfigLevel.Core,
            OutcomeNoticeOptions.SectionName + ":" + nameof(OutcomeNoticeOptions.DisplayIntent))
        .At(ConfigLevel.Tenant, RecordDisplayIntentMember)
        .At(ConfigLevel.Application, RecordDisplayIntentMember)
        .Default(Shipped.DisplayIntent)
        .Declare();

    /// <summary>
    /// Declarations of this registry — what the one registrar of the deployment declares and binds.
    /// </summary>
    public static ConfigKeyCatalog Catalog => Declared;
}
