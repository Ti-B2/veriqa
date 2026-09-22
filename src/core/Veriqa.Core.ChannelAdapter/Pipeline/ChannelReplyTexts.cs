// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// The texts a channel states for itself, handed to the pipeline as one object: what the sender is
/// told when processing fails, when the sign-in link it followed cannot be served, and when it wrote
/// to the bot about nothing at all.
/// </summary>
/// <remarks>
/// One parameter instead of three: they are the same kind of value from the same source — the
/// channel's registration — and a caller that drives the pipeline outside the webhook route
/// (a polling loop, an inbound mail route) states them together or not at all. A channel that
/// declares no text for an occurrence leaves it null and stays silent there.
/// </remarks>
/// <param name="ErrorMessageText">
/// Status text shown when processing fails — the one status text that stays the channel's own
/// (SPEC-036 §1.3).
/// </param>
/// <param name="StaleLinkReplyText">
/// Reply to a sign-in start whose transaction cannot be served; null — the channel stays silent.
/// </param>
/// <param name="UnaddressedReplyText">
/// Reply to a direct message that names no transaction; null — the channel stays silent.
/// </param>
public sealed record ChannelReplyTexts(
    string ErrorMessageText,
    string? StaleLinkReplyText = null,
    string? UnaddressedReplyText = null);
