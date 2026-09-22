// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using OpenIddict.Abstractions;

using Veriqa.Core.AuthServer.Configuration;

namespace Veriqa.Core.AuthServer.Infrastructure;

/// <summary>
/// Background service that prunes the OpenIddict store: token records first, then authorization
/// records, both through the OpenIddict managers. It runs on every store provider; the EF Core
/// InMemory provider is made able to prune by the options configurator registered with the
/// OpenIddict database context.
/// </summary>
internal sealed class OpenIddictTokenPruneService : BackgroundService
{
    /// <summary>
    /// Scope factory: every pass resolves the scoped OpenIddict managers from a scope of its own.
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// OIDC server options holding the prune interval and threshold.
    /// </summary>
    private readonly IOptions<OidcServerOptions> _options;

    /// <summary>
    /// Clock behind the pass schedule and the prune threshold.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<OpenIddictTokenPruneService> _logger;

    /// <summary>
    /// Creates an instance of the prune service.
    /// </summary>
    /// <param name="scopeFactory">Scope factory.</param>
    /// <param name="options">OIDC server options.</param>
    /// <param name="timeProvider">Clock.</param>
    /// <param name="logger">Logger.</param>
    public OpenIddictTokenPruneService(
        IServiceScopeFactory scopeFactory,
        IOptions<OidcServerOptions> options,
        TimeProvider timeProvider,
        ILogger<OpenIddictTokenPruneService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The server settings are global and read once, like the rest of the server configuration;
        // they are validated at startup, so both values are positive here and the interval is within
        // the range the timer accepts.
        var serverOptions = _options.Value;
        var interval = TimeSpan.FromSeconds(serverOptions.TokenPruneIntervalSeconds);
        var minimumAge = TimeSpan.FromSeconds(serverOptions.TokenPruneThresholdSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Waiting comes first: the pass never races the startup migration of a fresh database.
                await Task.Delay(interval, _timeProvider, stoppingToken);
                await PruneAsync(minimumAge, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception exception)
            {
                // A failed pass (for example, the database is unavailable) must not stop the service:
                // the next pass runs on schedule.
                _logger.LogError(exception, "Pruning of the OpenIddict token and authorization records failed");
            }
        }
    }

    /// <summary>
    /// Runs one prune pass: tokens first, then authorizations.
    /// </summary>
    /// <param name="minimumAge">Minimum age of a record, counted from its creation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    private async Task PruneAsync(TimeSpan minimumAge, CancellationToken cancellationToken)
    {
        var threshold = _timeProvider.GetUtcNow() - minimumAge;

        // The managers and the stores behind them are scoped: resolving them in a scope of the pass
        // keeps the singleton service free of a captive database context.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var authorizationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();

        // Tokens first: OpenIddict does not remove an authorization that still has a token attached.
        var prunedTokens = await tokenManager.PruneAsync(threshold, cancellationToken);
        var prunedAuthorizations = await authorizationManager.PruneAsync(threshold, cancellationToken);

        if (prunedTokens is 0 && prunedAuthorizations is 0)
        {
            _logger.LogDebug("OpenIddict prune pass: nothing to remove");
            return;
        }

        _logger.LogInformation(
            "OpenIddict prune pass: {PrunedTokens} token records and {PrunedAuthorizations} authorization records removed",
            prunedTokens,
            prunedAuthorizations);
    }
}
