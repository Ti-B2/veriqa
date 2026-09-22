// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Rate-limit policy names a host offers to the channel endpoints it maps. The policies themselves
/// belong to the host — it is the one that defines them on its rate limiter; a channel only names
/// which of them its route belongs to.
/// </summary>
/// <remarks>
/// Two kinds of channel route need telling apart, and they need different budgets: a webhook, called
/// by the channel platform and protected by a signature or a secret; and a page a person opens in a
/// browser, which is exposed to plain spam. A policy name left <c>null</c> means the host defined no
/// policy for that kind, and the routes are mapped without rate limiting.
/// </remarks>
public sealed record ChannelEndpointPolicies
{
    /// <summary>
    /// Policy of the webhook routes (called by the channel platform). <c>null</c> — no limit.
    /// </summary>
    public string? WebhookPolicyName { get; init; }

    /// <summary>
    /// Policy of the user-facing pages a channel serves (called from a browser). <c>null</c> — no limit.
    /// </summary>
    public string? PagePolicyName { get; init; }
}
