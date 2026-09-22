// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Registration record of a channel added through <c>ChannelAdapterBuilder.AddChannel</c> — the one
/// path every channel takes, built-in and third-party alike.
/// </summary>
/// <param name="ChannelType">Validated channel type (<c>\A[a-z][a-z0-9-]{0,63}\z</c>).</param>
/// <param name="AdapterType">
/// Adapter type this channel type was registered for. Kept so the startup cross-check can verify a
/// one-to-one match (this adapter declares exactly this channel type) instead of a set inclusion,
/// which two channels declaring each other's type would silently satisfy.
/// </param>
/// <param name="WebhookPath">Generic webhook path built by the SPI convention.</param>
/// <param name="Options">Settings the channel declared at registration (routes and status texts).</param>
internal sealed record CustomChannelRegistration(
    string ChannelType,
    Type AdapterType,
    string WebhookPath,
    ChannelRegistrationOptions Options);
