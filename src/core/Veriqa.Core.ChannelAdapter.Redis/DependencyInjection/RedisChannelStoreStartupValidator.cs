// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using StackExchange.Redis;

using Veriqa.Core.ChannelAdapter.Redis;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Fails the host at start-up when the Redis connection of the channel stores is configured
/// ambiguously. Two shapes are caught, and both otherwise surface only as wrong data in the wrong
/// Redis, hours later:
/// <list type="bullet">
/// <item>a connection string is set while another <c>IConnectionMultiplexer</c> registration wins
/// the resolution — the registered one is used and the configured string is silently ignored;</item>
/// <item>no connection string is set and nothing else registered a multiplexer — there is nothing to
/// connect to.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// A hosted lifecycle service rather than an <c>IStartupFilter</c>, for the same reason as the
/// channel registration cross-check: <see cref="StartingAsync"/> runs before any hosted service
/// starts and before the web server binds, under every host shape.
/// </para>
/// <para>
/// It also runs late enough for the question to have a final answer: which multiplexer registration
/// wins is decided by every module that configured the container, not only by those that ran before
/// <c>UseRedisChannelStores</c>.
/// </para>
/// <para>
/// Connections are named by the allow-listed settings that identify them — a Redis connection
/// string carries credentials, and a start-up failure lands in the log.
/// </para>
/// </remarks>
internal sealed class RedisChannelStoreStartupValidator : IHostedLifecycleService
{
    /// <summary>
    /// Placeholder used when the multiplexer that wins the resolution cannot be resolved — it is a
    /// configuration error either way, and the message must not be swallowed by a connection failure.
    /// </summary>
    private const string UnavailableConfiguration = "<connection could not be established>";

    /// <summary>
    /// Service provider — the already-registered multiplexer is resolved lazily, and only on the
    /// error path, so a healthy host is not forced to open a Redis connection at start-up.
    /// </summary>
    private readonly IServiceProvider _services;

    /// <summary>
    /// Where the Redis connection comes from — asked at start-up, when the container is complete.
    /// </summary>
    private readonly RedisChannelStoreConnectionSource _connectionSource;

    /// <summary>
    /// Channel store options.
    /// </summary>
    private readonly IOptions<RedisChannelStoreOptions> _options;

    /// <summary>
    /// Creates the start-up validator.
    /// </summary>
    /// <param name="services">Service provider.</param>
    /// <param name="connectionSource">Origin of the Redis connection.</param>
    /// <param name="options">Channel store options.</param>
    public RedisChannelStoreStartupValidator(
        IServiceProvider services,
        RedisChannelStoreConnectionSource connectionSource,
        IOptions<RedisChannelStoreOptions> options)
    {
        _services = services;
        _connectionSource = connectionSource;
        _options = options;
    }

    /// <summary>
    /// Checks the Redis connection configuration before the host starts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        var configuration = _options.Value.Configuration;
        var configurationSet = !string.IsNullOrWhiteSpace(configuration);
        var foreignConnectionWins = _connectionSource.ForeignConnectionWins();

        if (foreignConnectionWins && configurationSet)
        {
            throw new InvalidOperationException(
                "Redis channel stores: RedisChannelStoreOptions.Configuration points at Redis "
                + $"'{RedisConnectionDescription.Describe(configuration)}', but another IConnectionMultiplexer "
                + "registration wins the resolution in the container and points at "
                + $"'{ReadRegisteredConnection()}'. The registered connection wins, so the configured one "
                + "would be ignored and the channel stores would land in a different Redis than expected. "
                + "Leave RedisChannelStoreOptions.Configuration empty to reuse the registered connection "
                + "deliberately, or drop the other registration if the channel stores must own their connection. "
                + "(Only the endpoints, logical database, TLS settings and sentinel master name are shown: "
                + "connection strings carry credentials.)");
        }

        if (!foreignConnectionWins && !configurationSet)
        {
            throw new InvalidOperationException(
                RedisChannelStoreConnectionSource.MissingConnectionMessage);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op: the check runs in <see cref="StartingAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// No-op: the check runs in <see cref="StartingAsync"/>.
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

    /// <summary>
    /// Describes the multiplexer that wins the resolution, so the error can name both sides of the
    /// conflict. Resolving it opens the connection, which may itself fail — that must not replace
    /// the configuration error with a connection error. The credentials of that connection are never
    /// printed: <c>IConnectionMultiplexer.Configuration</c> echoes the password verbatim.
    /// </summary>
    /// <returns>The description of the registered connection, or a placeholder.</returns>
    private string ReadRegisteredConnection()
    {
        try
        {
            return RedisConnectionDescription.Describe(
                _services.GetRequiredService<IConnectionMultiplexer>().Configuration);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return UnavailableConfiguration;
        }
    }
}
