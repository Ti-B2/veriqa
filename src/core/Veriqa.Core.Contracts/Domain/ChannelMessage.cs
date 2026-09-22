// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// An outbound message the core asks a channel to deliver (SPEC-003 §6.2).
/// Extensible by construction: a richer content kind is a new <see cref="ChannelMessageKind"/> plus
/// new properties with defaults — never a new interface member.
/// </summary>
public sealed record ChannelMessage
{
    /// <summary>
    /// Recipient identifier within the channel.
    /// </summary>
    public required string ChannelUserId { get; init; }

    /// <summary>
    /// Message text, already localized by the caller.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Content kind; plain text by default.
    /// </summary>
    public ChannelMessageKind Kind { get; init; } = ChannelMessageKind.PlainText;
}
