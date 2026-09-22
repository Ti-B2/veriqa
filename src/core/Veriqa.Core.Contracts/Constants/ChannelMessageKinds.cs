// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Well-known content kinds a channel message can carry (SPEC-003 §6.2).
/// The set is open: a third-party adapter may declare its own code, which is why the codes live
/// here as constants and the kind itself is an extensible value, not a closed enum.
/// </summary>
public static class ChannelMessageKinds
{
    /// <summary>
    /// Plain text without markup — the only kind every channel is required to accept.
    /// </summary>
    public const string PlainText = "plain_text";
}
