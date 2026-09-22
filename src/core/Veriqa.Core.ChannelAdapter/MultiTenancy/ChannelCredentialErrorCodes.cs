// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Contracts;

namespace Veriqa.Core.ChannelAdapter.MultiTenancy;

/// <summary>
/// Error codes of per-tenant channel credential and client resolution (SPEC-003 §17.4, §17.6).
/// Both codes cross the free-SPI package boundary, so they alias <see cref="VeriqaErrorCodes"/>:
/// <see cref="ChannelNotAvailable"/> and its namesake in <c>ChannelAdapterErrorCodes</c> are the same
/// constant, so the two cannot drift apart.
/// </summary>
public static class ChannelCredentialErrorCodes
{
    /// <summary>
    /// Credentials for the tenant/channel not found (no resolution layer set the credentials).
    /// </summary>
    public const string ChannelCredentialsMissing = VeriqaErrorCodes.ChannelCredentialsMissing;

    /// <summary>
    /// The channel type does not resolve (no client factory / provider for this channel type).
    /// </summary>
    public const string ChannelNotAvailable = VeriqaErrorCodes.ChannelNotAvailable;
}
