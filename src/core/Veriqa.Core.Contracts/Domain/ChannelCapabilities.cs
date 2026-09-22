// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// The facts a channel declares about itself (SPEC-003 §6.1, SPEC-012 §4.4.1).
/// One declared object instead of a scattering of interface members: a new fact is a new property
/// with a safe default, so an already compiled adapter keeps compiling and running. Adding a new
/// mandatory interface member for a new fact is forbidden.
/// </summary>
public sealed record ChannelCapabilities
{
    /// <summary>
    /// The channel can ask for and receive a confirmation inside itself — the single confirmation
    /// fact a channel owns (CFG-048). How it does so is the channel's own business (inline buttons,
    /// a web view inside the messenger, biometrics); the core neither knows nor asks.
    /// </summary>
    /// <remarks>
    /// The channel never names a surface of the core: whether a confirmation is needed at all is the
    /// core's policy, and the page that asks the question when the channel cannot is the core's own.
    /// The default (<c>false</c>) means "cannot" — the safe value: an already compiled adapter that
    /// knows nothing about this fact will not be handed a confirmation it cannot display, and the
    /// core routes the question to its own surface instead (SPEC-012 §4.4.2).
    /// </remarks>
    public bool SupportsInChannelConfirmation { get; init; }

    /// <summary>
    /// Content kinds the channel accepts in <c>SendMessageAsync</c>/<c>SendConfirmationPromptAsync</c>.
    /// A message whose kind is outside this set never reaches the adapter. Must be a genuinely
    /// immutable collection (<c>FrozenSet</c>).
    /// </summary>
    public required IReadOnlySet<ChannelMessageKind> SupportedMessageKinds { get; init; }

    /// <summary>
    /// The channel can update a message it sent earlier, addressed by <see cref="ChannelMessageRef"/>.
    /// The core stores the prompt reference only for such channels.
    /// </summary>
    public bool SupportsMessageUpdate { get; init; }

    /// <summary>
    /// The channel shows the user a terminal transaction status (<c>ReportOutcomeAsync</c>).
    /// The core never calls that method on a channel that has not declared this.
    /// </summary>
    public bool DeliversOutcomeNotice { get; init; }

    /// <summary>
    /// The channel can supply the recipient's locale in <c>ChannelIdentitySnapshot.Locale</c>
    /// (SPEC-017 §7.2). false — consumers go straight to the transaction fallback of the
    /// recipient-locale chain.
    /// </summary>
    public bool ProvidesRecipientLocale { get; init; }

    /// <summary>
    /// The channel renders the emoji a messenger-worded outcome receipt carries, so a receipt shown
    /// inside it is addressed to the messenger in-channel surface rather than to the plain one.
    /// Which of the two in-channel surfaces a channel carries is the rule of the CHANNEL and not of
    /// the render point (SPEC-036 TPL-123), and this is where a channel states it.
    /// </summary>
    /// <remarks>
    /// Three-state on purpose, unlike the flags above: <c>null</c> means the channel states no rule
    /// of its own, and the core answers for it from the list of messengers it ships an adapter for.
    /// A plain <c>bool</c> could not tell "this channel renders no emoji" from "this channel has
    /// said nothing", and the shipped channels say nothing — the fallback list names them, the same
    /// way the sign-in page falls back to its own list for the tab label of a channel that declared
    /// no display name.
    /// </remarks>
    public bool? RendersEmoji { get; init; }
}
