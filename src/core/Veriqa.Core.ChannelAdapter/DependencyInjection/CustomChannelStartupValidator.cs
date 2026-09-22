// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;

using Veriqa.Core.ChannelAdapter.Abstractions;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Runs the channel registration cross-check (<see cref="CustomChannelRegistry.ValidateAgainstAdapters"/>)
/// once when the host starts, before it begins serving. It is a hosted lifecycle service, not an
/// <c>IStartupFilter</c>: a startup filter is expanded only by the web host, so an outbound/polling-only
/// channel (<c>mapWebhook: false</c>) in a plain <c>Host.CreateApplicationBuilder</c> worker would start
/// green with a channel type that no adapter declares — the check would never run. <see cref="StartingAsync"/>
/// fires before any hosted service is started and before the web server binds, so the cross-check still
/// fails fast ahead of the first request under every host shape (worker, minimal API, WebApplication,
/// WebApplicationFactory).
/// </summary>
internal sealed class CustomChannelStartupValidator : IHostedLifecycleService
{
    /// <summary>
    /// Registry of the registered channels.
    /// </summary>
    private readonly CustomChannelRegistry _registry;

    /// <summary>
    /// All channel adapters registered in the container (built-in and third-party).
    /// </summary>
    private readonly IEnumerable<IChannelAdapter> _adapters;

    /// <summary>
    /// Creates the startup validator.
    /// </summary>
    /// <param name="registry">Registry of the registered channels.</param>
    /// <param name="adapters">All channel adapters registered in the container.</param>
    public CustomChannelStartupValidator(CustomChannelRegistry registry, IEnumerable<IChannelAdapter> adapters)
    {
        _registry = registry;
        _adapters = adapters;
    }

    /// <summary>
    /// Cross-checks the registrations against the resolved adapters before the host starts. A mismatch
    /// throws and stops the host from coming up, ahead of the first request.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _registry.ValidateAgainstAdapters(_adapters);
        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op: the cross-check runs in <see cref="StartingAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the cross-check runs in <see cref="StartingAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the validator holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the validator holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the validator holds no runtime state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
