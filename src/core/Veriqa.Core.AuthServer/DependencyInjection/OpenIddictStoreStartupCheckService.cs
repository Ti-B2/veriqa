// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veriqa.Core.AuthServer.DependencyInjection;

/// <summary>
/// Startup check of the OpenIddict store choice (SPEC-012 CFG-118, CFG-119). Registered only when
/// the caller supplied no EF Core provider delegate, i.e. when the store falls back to the volatile
/// in-memory one. It then reads whether that choice was spoken
/// (<see cref="VolatileOpenIddictStoreOptIn"/>) and either lets the host start, warns, or refuses:
/// <list type="bullet">
///   <item>choice spoken — the host starts and a warning states that the store is volatile;</item>
///   <item>choice unspoken in <c>Development</c> — the host starts and a warning names the cure;</item>
///   <item>choice unspoken anywhere else — the host fails to start with the same text.</item>
/// </list>
/// <para>
/// A hosted service rather than a check at registration time: the environment is a property of the
/// running host, and a caller assembling services by hand may have no <see cref="IHostEnvironment"/>
/// at that moment. A missing environment counts as "not Development" — treating it as development
/// would hand the silent volatile store back to exactly the callers this check exists for.
/// </para>
/// </summary>
internal sealed class OpenIddictStoreStartupCheckService : IHostedService
{
    /// <summary>
    /// Text of the refusal and of the Development warning. It names the cure — which method to call —
    /// rather than the diagnosis alone (SPEC-012 CFG-119).
    /// </summary>
    internal const string StoreNotConfiguredMessage =
        "Veriqa: the OpenIddict store provider is not configured. Call "
        + "authServer.UseOpenIddictDatabase(ef => ef.UseNpgsql(...)) for production, or "
        + "authServer.UseInMemoryOpenIddictStore() to accept the volatile in-memory store.";

    /// <summary>
    /// Text of the warning for a store whose volatility was accepted explicitly.
    /// </summary>
    internal const string VolatileStoreAcceptedMessage =
        "Veriqa: the volatile in-memory OpenIddict store is in use — it was selected explicitly. "
        + "Clients, tokens and authorizations do not survive a restart and are not shared between "
        + "replicas; a production deployment needs a relational provider.";

    /// <summary>
    /// Host environment, or <see langword="null"/> when the services were assembled outside a host.
    /// </summary>
    private readonly IHostEnvironment? _environment;

    /// <summary>
    /// The spoken-choice marker, or <see langword="null"/> when nobody spoke the choice.
    /// </summary>
    private readonly VolatileOpenIddictStoreOptIn? _optIn;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<OpenIddictStoreStartupCheckService> _logger;

    /// <summary>
    /// Creates the check instance.
    /// </summary>
    /// <param name="environment">Host environment, or null when there is none.</param>
    /// <param name="optIn">Spoken-choice marker, or null when the choice was not spoken.</param>
    /// <param name="logger">Logger.</param>
    public OpenIddictStoreStartupCheckService(
        IHostEnvironment? environment,
        VolatileOpenIddictStoreOptIn? optIn,
        ILogger<OpenIddictStoreStartupCheckService> logger)
    {
        _environment = environment;
        _optIn = optIn;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The choice was spoken: the store stays volatile, and the only thing left to do is to say so.
        if (_optIn is not null)
        {
            _logger.LogWarning(VolatileStoreAcceptedMessage);
            return Task.CompletedTask;
        }

        // Nobody spoke it. Development keeps starting (a developer without a connection string is the
        // scenario the volatile store exists for); anywhere else a forgotten provider stops the host
        // instead of quietly issuing tokens into a store that dies with the process.
        if (_environment is not null && _environment.IsDevelopment())
        {
            _logger.LogWarning(StoreNotConfiguredMessage);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException(StoreNotConfiguredMessage);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
