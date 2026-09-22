// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Abstractions;

/// <summary>
/// Operation prefixes for the per-user authentication rate-limiter partition key.
/// A single successful sign-in touches the limiter several times (auth start → confirmation →
/// token exchange); without per-operation scoping those calls drain ONE shared window, so the
/// configured "N attempts per window" effectively shrank to N/3 sign-ins and a second sign-in
/// through the same channel within the window failed (demo-stand review 2026-07-18).
/// Prefixing the partition key with the operation gives each check its own budget while the
/// user key itself stays in the {channel_type}:{channel_user_id} format
/// (<see cref="IUserAuthRateLimiter"/>).
/// </summary>
public static class UserAuthRateLimitOperations
{
    /// <summary>Auth-start check (deep link / QR follow) — ChannelWebhookPipeline.</summary>
    public const string AuthStart = "auth-start:";

    /// <summary>Confirmation check (button press / magic link) — ChannelWebhookPipeline.</summary>
    public const string AuthConfirm = "auth-confirm:";

    /// <summary>Token-exchange check (authorization_code / refresh_token) — TokenEndpoint.</summary>
    public const string TokenExchange = "token:";
}
