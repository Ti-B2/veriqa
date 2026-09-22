// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Abstractions;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// The OPTIONAL dependencies of the webhook pipeline, gathered into one object (Parameter Object).
/// Each of them is absent in some legitimate composition — a host without the auth server has no
/// per-user rate limiter, a polling deployment maps no webhooks, a test wires only what it measures —
/// so each is nullable and each consumer degrades on its own.
/// <para>
/// They arrive as ONE handler argument rather than being fetched out of <c>RequestServices</c> inside
/// the pipeline: what a request path needs belongs in its signature, and resolving "maybe registered"
/// services is the composition root's business. The object is built by a DI factory, which is the one
/// place where asking the container what it happens to hold is the correct thing to do.
/// </para>
/// </summary>
/// <param name="UserAuthRateLimiter">Per-user rate limiter; null — not registered.</param>
/// <param name="PromptOrchestrator">Confirmation prompt orchestrator; null — not registered.</param>
/// <param name="PromptLocalizer">Channel message localizer; null — not registered.</param>
/// <param name="PromptMessageStore">Store of the sent prompt messages; null — not registered.</param>
/// <param name="ConfigurationResolver">
/// Canonical layer resolver — what the tenant demux authorizes the tenant segment against; null —
/// the channel registration is absent altogether, which makes the demux fail closed (CA-032).
/// </param>
public sealed record ChannelWebhookSupport(
    IUserAuthRateLimiter? UserAuthRateLimiter,
    IConfirmationPromptOrchestrator? PromptOrchestrator,
    IConfirmationPromptLocalizer? PromptLocalizer,
    IChannelPromptMessageStore? PromptMessageStore,
    IConfigurationResolver? ConfigurationResolver)
{
    /// <summary>
    /// The bundle of a composition that registered none of these services — every one of them absent.
    /// It is what a host mapping the webhook endpoints without the channel DI gets, and it degrades
    /// exactly the way each consumer already degrades on a missing service: no rate limiting, no
    /// prompt orchestration, and a tenant demux that fails closed.
    /// </summary>
    public static ChannelWebhookSupport None { get; } = new(null, null, null, null, null);
}
