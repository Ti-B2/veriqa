// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Veriqa.Core.Configuration;

/// <summary>
/// The explicit extension points of the configuration resolver (SPEC-012 §10.6, CFG-235). What a
/// deployment may put in place of a part of the mechanism is stated HERE, as members of this builder,
/// and nowhere else: the resolver itself is registered unconditionally, so replacing it is not
/// something a stray registration can do by accident.
/// <para>
/// The form is the platform's own — a policy of a registration rather than a property of the place it
/// takes effect, exactly as <c>OptionsBuilder.Validate</c> / <c>ValidateOnStart</c> state the
/// validation of an options type at its registration.
/// </para>
/// <para>
/// Two points are open today: the caching and degradation layer
/// (<see cref="IConfigResolutionCache"/>) and the clock (<see cref="TimeProvider"/>). A third one
/// named by the works carrier — the reporter of diagnostics — has no port yet: it is two concrete
/// hosted services, and a replacement point for it needs the port designed first. Adding it later is
/// a method on this builder and breaks nothing.
/// </para>
/// </summary>
public sealed class ConfigurationResolverBuilder
{
    /// <summary>
    /// Service collection the registrations are applied to.
    /// </summary>
    private readonly IServiceCollection _services;

    /// <summary>
    /// Creates the builder over the collection the resolver has just been registered into.
    /// </summary>
    /// <param name="services">Service collection.</param>
    internal ConfigurationResolverBuilder(IServiceCollection services) => _services = services;

    /// <summary>
    /// Puts an implementation of the caching and degradation layer in place of the one shipped with
    /// the module. It wins whatever else is registered for the port: this is the point where the
    /// substitution is stated, so a registration made here is the answer rather than a candidate.
    /// <para>
    /// The implementation is resolved from the container by its type, so a primitive that needs
    /// dependencies of its own (a connection to a distributed cache) registers them as usual.
    /// </para>
    /// </summary>
    /// <typeparam name="TCache">Type of the caching layer.</typeparam>
    /// <returns>Builder for chaining.</returns>
    public ConfigurationResolverBuilder UseCache<TCache>()
        where TCache : class, IConfigResolutionCache
    {
        _services.Replace(ServiceDescriptor.Singleton<IConfigResolutionCache, TCache>());

        return this;
    }

    /// <summary>
    /// Puts a clock in place of <see cref="TimeProvider.System"/>. The mechanism reads time in one
    /// place — the lifetime of a cache entry and the maximum age of a last valid secret — so a
    /// deployment that controls time controls both.
    /// </summary>
    /// <param name="timeProvider">Clock.</param>
    /// <returns>Builder for chaining.</returns>
    public ConfigurationResolverBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _services.Replace(ServiceDescriptor.Singleton(timeProvider));

        return this;
    }
}
