// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Intent discriminators of the inbound result hierarchy (SPEC-003 §6.3).
/// The same codes are the JSON type discriminator of <c>ChannelInboundResult</c>, so a replay
/// fixture written against the wire form and the type map can never drift apart.
/// </summary>
public static class ChannelInboundIntents
{
    /// <summary>
    /// The user initiated the start of authentication.
    /// </summary>
    public const string AuthStart = "AuthStart";

    /// <summary>
    /// The user confirmed authentication.
    /// </summary>
    public const string AuthConfirm = "AuthConfirm";

    /// <summary>
    /// The user declined authentication.
    /// </summary>
    public const string AuthDecline = "AuthDecline";

    /// <summary>
    /// The user shared a phone number.
    /// </summary>
    public const string PhoneShared = "PhoneShared";

    /// <summary>
    /// The event is unrelated to the authentication process.
    /// </summary>
    public const string Unrelated = "Unrelated";

    /// <summary>
    /// A direct message to the bot that names no transaction, from a sender the adapter vetted.
    /// </summary>
    public const string Unaddressed = "Unaddressed";
}
