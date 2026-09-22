// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Veriqa.Core.Configuration;

namespace Veriqa.Core.ChannelAdapter.UI;

/// <summary>
/// Single registration of the per-request services behind the generated pages of the "Core" UI
/// contour.
/// <para>
/// Two composition roots need them: the channel contour, which owns the render points of the
/// service pages, and Veriqa.Core.AuthServer, because a host may run the auth server with no
/// channel adapter enabled at all. Both call this method instead of repeating the registrations,
/// so the copies cannot silently drift apart — with TryAdd on both sides a divergence in lifetime
/// or implementation would not fail, it would just let whichever root ran first win.
/// </para>
/// <para>
/// The method is internal and shared with the auth server through InternalsVisibleTo, by the same
/// reasoning as <see cref="CorePageStyles"/>: composing the contour is not a seam integrators are
/// meant to reach into.
/// </para>
/// </summary>
internal static class CorePageUiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the branding of the generated core pages (SPEC-007 UI-101), the scope of the
    /// external resources the page of a request actually links, which the security-headers
    /// middleware turns into style-src/script-src (UI-054, UI-040).
    /// </summary>
    /// <remarks>
    /// The branding and the resources are per-request: the branding is resolved over the context of
    /// the page's own transaction, and the resources it resolved belong to that request alone. Both are
    /// registered by their own type — there is nothing to substitute, so the branding can only come
    /// from the level model of its keys (anti-fork CFG-235). The resolver is built by a factory because
    /// its constructor is internal, and the container only activates public constructors on its own.
    /// </remarks>
    /// <param name="services">Service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddCorePageUiServices(this IServiceCollection services)
    {
        services.TryAddScoped<CorePageResourceScope>();
        services.TryAddScoped(serviceProvider => new CorePageBrandingResolver(
            serviceProvider.GetRequiredService<IConfigurationResolver>(),
            serviceProvider.GetRequiredService<CorePageResourceScope>(),
            serviceProvider.GetRequiredService<ILogger<CorePageBrandingResolver>>()));

        return services;
    }
}
