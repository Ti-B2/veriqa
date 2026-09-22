// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.AspNetCore.Routing;

using Veriqa.Core.ChannelAdapter.Pipeline;

namespace Veriqa.Core.ChannelAdapter.Abstractions;

/// <summary>
/// Seam "a channel maps the HTTP endpoints of its own" (public SPI). Every implementation registered
/// in the container is called once, from <c>MapChannelWebhookEndpoints</c>, with the host's endpoint
/// router — so a channel that needs more than the generic webhook route (a page of its own, an
/// inbound route outside the path convention) contributes it itself, and the core never names the
/// channel to map it.
/// </summary>
/// <remarks>
/// The seam is open to a third-party channel on the same terms as to a built-in one: register an
/// implementation in the container from the channel's own <c>Add*</c> extension method, and its
/// routes appear wherever the host maps the Veriqa channel endpoints. Registration is conditional
/// where the channel is: a channel that is disabled in the configuration registers no registrar, and
/// its routes are then never mapped.
/// </remarks>
public interface IChannelEndpointRegistrar
{
    /// <summary>
    /// Maps the channel's own endpoints onto the host's router.
    /// </summary>
    /// <param name="endpoints">Endpoint router of the host.</param>
    /// <param name="policies">Rate-limit policy names the host offers to channel endpoints.</param>
    void MapEndpoints(IEndpointRouteBuilder endpoints, ChannelEndpointPolicies policies);
}
