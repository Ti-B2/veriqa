// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Domain;

/// <summary>
/// Reference to a message the channel has already sent (SPEC-003 §6.2). Both parts are opaque and
/// defined by the channel: the core stores the reference and hands it back, it never interprets it.
/// </summary>
public sealed record ChannelMessageRef
{
    /// <summary>
    /// Chat identifier the message was sent to (channel-specific string form).
    /// </summary>
    public required string ChatId { get; init; }

    /// <summary>
    /// Identifier of the sent message (channel-specific string form).
    /// </summary>
    public required string MessageId { get; init; }
}
