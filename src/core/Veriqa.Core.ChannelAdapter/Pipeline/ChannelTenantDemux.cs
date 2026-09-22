// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using Veriqa.Core.ChannelAdapter.MultiTenancy;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Tenant demux of an inbound webhook route (SPEC-003 §17.6, CA-167/CA-168/CA-185): the optional
/// <c>{tenant}</c> route segment, the <c>channels_enabled</c> gate over it, and the mapping that
/// registers a handler on both the base path and the path with the segment.
/// <para>
/// It is ONE implementation for every channel — the generic webhook routes of
/// <see cref="ChannelWebhookPipeline"/> and the routes a channel maps for itself through
/// <see cref="Abstractions.IChannelEndpointRegistrar"/> (Email's inbound route). A channel that
/// copied the extraction, the gate or the route key would be free to answer a segment differently
/// from the pipeline — a second demux, which is exactly what a shared one prevents. Hence
/// <c>internal</c> and visible to the satellite channel packages: this is a host-side mechanism,
/// not part of the adapter SPI, so it adds nothing to the public surface.
/// </para>
/// </summary>
internal static class ChannelTenantDemux
{
    /// <summary>
    /// Name of the webhook tenant-segment route parameter (CA-167).
    /// </summary>
    internal const string TenantRouteKey = "tenant";

    /// <summary>
    /// Suffix of the optional tenant route segment: <c>/{tenant}</c>.
    /// Tenant demux is opt-in: the default route without the segment remains valid (CA-168).
    /// </summary>
    internal const string TenantRouteSuffix = "/{" + TenantRouteKey + "}";

    /// <summary>
    /// Maps the handler onto the base path (default tenant) and onto the path with the optional
    /// tenant segment <c>{path}/{tenant}</c> (tenant demux). Two route templates rather than one
    /// optional parameter, so that the base route keeps exactly the shape it had.
    /// </summary>
    /// <param name="endpoints">Endpoint router.</param>
    /// <param name="httpMethod">HTTP method of the route (GET/POST).</param>
    /// <param name="path">Base path of the route, without the tenant segment.</param>
    /// <param name="handler">The one handler both routes lead to.</param>
    /// <param name="rateLimitPolicyName">Rate-limit policy applied to both routes; null — no limit.</param>
    internal static void MapWithOptionalTenant(
        IEndpointRouteBuilder endpoints,
        string httpMethod,
        string path,
        Delegate handler,
        string? rateLimitPolicyName)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Base path (without the tenant segment) — default tenant, 1:1 behavior (CA-168).
        var baseEndpoint = MapByMethod(endpoints, httpMethod, path, handler);

        // Optional path with the tenant segment — tenant demux (opt-in, CA-167).
        var tenantEndpoint = MapByMethod(endpoints, httpMethod, path + TenantRouteSuffix, handler);

        if (!string.IsNullOrEmpty(rateLimitPolicyName))
        {
            baseEndpoint.RequireRateLimiting(rateLimitPolicyName);
            tenantEndpoint.RequireRateLimiting(rateLimitPolicyName);
        }
    }

    /// <summary>
    /// Tenant demux (CA-167/CA-168): extracts the tenant from the optional
    /// <c>{tenant}</c> route segment. A missing segment ⇒ default tenant (null), 1:1 behavior.
    /// If the tenant is set but the channel is not allowed for it (<c>channels_enabled</c>) —
    /// the request must be refused with 403 (details only in the logs, CA-032). The tenant is
    /// determined BEFORE credential selection.
    /// </summary>
    /// <param name="httpContext">HTTP request context.</param>
    /// <param name="configurationResolver">
    /// Canonical layer resolver — what the tenant segment is authorized against; null — the channel
    /// registration is absent, and the demux fails closed (CA-032).
    /// </param>
    /// <param name="channelType">Channel type.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Whether the request may continue, and the resolved tenant (null — the default implicit one).
    /// </returns>
    internal static async ValueTask<(bool Allowed, string? TenantId)> ResolveTenantAsync(
        HttpContext httpContext,
        IConfigurationResolver? configurationResolver,
        string channelType,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(logger);

        // Extract the tenant route segment (if set). Empty/missing ⇒ default tenant.
        var routeTenant = httpContext.Request.RouteValues.TryGetValue(TenantRouteKey, out var raw)
            ? raw?.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(routeTenant))
        {
            // Default tenant (CA-168) — demux not activated.
            return (true, null);
        }

        // Tenant set explicitly: the channel-to-tenant authorizer MUST be available (fail-closed, CA-032).
        // The resolver comes with the channel registration, so it is absent only in a host that mapped
        // these routes without registering the channels at all — and accepting the request there would
        // expose the multi-tenant webhook without authorization.
        if (configurationResolver is null)
        {
            logger.LogWarning(
                "Webhook demux rejected (fail-closed): tenant segment is set but the channel registration "
                + "is absent, so there is nothing to authorize the tenant against for channel {ChannelType}",
                channelType);

            return (false, null);
        }

        // Check that the channel is allowed for the tenant (channels_enabled). Repeating the question
        // inside one request costs nothing: caching lives inside the resolver now and is available on
        // every path — including the polling ones, which never had a request scope to memoize in.
        var isEnabled = await configurationResolver.IsChannelEnabledAsync(routeTenant, channelType, cancellationToken);

        if (!isEnabled)
        {
            logger.LogWarning(
                "Webhook demux rejected: channel {ChannelType} is not allowed for the tenant (tenant segment is set)",
                channelType);

            return (false, null);
        }

        return (true, routeTenant);
    }

    /// <summary>
    /// Maps the delegate onto the given path by HTTP method (GET/POST).
    /// </summary>
    /// <param name="endpoints">Endpoint router.</param>
    /// <param name="httpMethod">HTTP method of the route.</param>
    /// <param name="path">Route path.</param>
    /// <param name="handler">Route handler.</param>
    /// <returns>The mapped endpoint, for the conventions applied above.</returns>
    private static IEndpointConventionBuilder MapByMethod(
        IEndpointRouteBuilder endpoints,
        string httpMethod,
        string path,
        Delegate handler)
    {
        return HttpMethods.IsGet(httpMethod)
            ? endpoints.MapGet(path, handler)
            : endpoints.MapPost(path, handler);
    }
}
