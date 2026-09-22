// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Contracts;

/// <summary>
/// Veriqa-specific OAuth scope names. Their home is the free SPI package for the same reason as
/// <see cref="VeriqaClaimTypes"/>: a scope name is part of the very same public wire contract as
/// the claim names it gates. Values are stable and must not change.
/// </summary>
public static class VeriqaScopes
{
    /// <summary>
    /// Scope gating the channel claims <see cref="VeriqaClaimTypes.ChannelType"/> and
    /// <see cref="VeriqaClaimTypes.ChannelUserId"/>: a relying party that does not request it
    /// receives neither claim, in any token.
    /// </summary>
    public const string Channel = "channel";

    /// <summary>
    /// Scope gating the <see cref="VeriqaClaimTypes.Picture"/> claim: a relying party that does not
    /// request it receives no avatar in any token. When requested, the avatar travels only in the
    /// access token and is read from the userinfo endpoint, never in the identity token.
    /// </summary>
    public const string Avatar = "avatar";
}
