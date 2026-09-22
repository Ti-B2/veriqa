// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

// Entry point of the Veriqa Core Auth Server Host.
// Standalone host for running the Veriqa.Core.AuthServer library.

using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using StackExchange.Redis;
using Veriqa.Core.AuditTrail.DependencyInjection;
using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.AuthServer.Constants;
using Veriqa.Core.AuthServer.DependencyInjection;
using Veriqa.Core.AuthServer.Host;
using Veriqa.Core.AuthServer.Infrastructure;
using Veriqa.Core.ChannelAdapter.Constants;
using Veriqa.Core.ChannelAdapter.DependencyInjection;
using Veriqa.Core.ChannelAdapter.Pipeline;
using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.DependencyInjection;
using Veriqa.ServiceDefaults;

return HostBootstrap.Run("Veriqa Auth Server terminated with a critical error", () =>
{
    // Whether this process runs under a service manager is decided BEFORE the builder is created: the
    // content root cannot be changed afterwards (MS Docs, "Host ASP.NET Core in a Windows Service"), and
    // under a service manager the working directory of the process is not the directory of the program
    // (for a Windows service it is System32), while the localization files and the static content are
    // read relative to the content root.
    var isOsService = WindowsServiceHelpers.IsWindowsService() || SystemdHelpers.IsSystemdService();

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        // Outside a service manager the content root keeps its default resolution, so Docker, `dotnet run`
        // and a run from the IDE behave exactly as before.
        ContentRootPath = isOsService ? AppContext.BaseDirectory : null
    });

    // Lifetime under a service manager: the Windows service control handler and the systemd readiness
    // notification (Type=notify). Both calls check for themselves whether the process runs under that
    // manager and do nothing when it does not — they are safe on either OS.
    builder.Services.AddWindowsService();
    builder.Services.AddSystemd();

    // The external configuration directory of an OS-service installation (/etc/veriqa,
    // %ProgramData%\Veriqa): its files layer over the appsettings the program ships with and stay under
    // the environment variables — added before anything reads the configuration.
    builder.AddExternalConfigurationDirectory();

    // The integrator's own settings file (clients and any other Veriqa setting), layered over the shipped
    // appsettings files and the configuration directory, and under the environment — added before
    // anything reads the configuration.
    builder.AddIntegratorSettingsFile();

    // Wire up Aspire ServiceDefaults (telemetry, health checks, service discovery)
    builder.AddServiceDefaults();

    // ForwardedHeaders — critical for OpenIddict: correct HTTPS detection behind TLS termination/proxy
    // (single shared configuration — ServiceDefaults, common to all hosts).
    builder.Services.ConfigureForwardedHeadersForProxy(builder.Configuration);

    // Wire up the Transaction Engine:
    // If a connection string is set — EF Core is used on the provider Veriqa:TransactionEngine:Store:Provider
    // selects from the provider table of this host (RelationalStoreProviders); the host owns the provider
    // call and the migrations assembly (host-owned contract, SPEC-001 §10.2). Otherwise — InMemory (for
    // development).
    var transactionStoreConnectionString = builder.Configuration[InfrastructureConfigConstants.TransactionStoreConnectionStringKey];

    if (!string.IsNullOrWhiteSpace(transactionStoreConnectionString))
    {
        // The provider is validated (fail-fast): an unrecognized value is a configuration error, not a
        // silent fallback. The same resolver is reused in health checks.
        var transactionStoreProvider = ResolveTransactionStoreProvider(builder.Configuration);

        builder.Services.AddVeriqaTransactionEngine(builder.Configuration, engine =>
        {
            engine.UseEfCoreStore(transactionStoreProvider.BuildConfigurator(
                RelationalStore.Transactions,
                transactionStoreConnectionString));
        });
    }
    else
    {
        builder.Services.AddVeriqaTransactionEngine(builder.Configuration);
    }

    // Wire up the OpenIddict integration (OIDC server, EF Core, Cookie auth, ClientSeeder).
    // The store provider is owned by the host (host-owned contract, SPEC-001 §10.2): the core stays
    // provider-agnostic and receives UseXxx + the migrations assembly as a delegate. The same resolved
    // decision is reused in health checks.
    var openIddictStore = ResolveOpenIddictStore(builder.Configuration);
    var openIddictConnectionString = builder.Configuration[InfrastructureConfigConstants.OpenIddictDatabaseConnectionStringKey];

    // Only a relational provider gets a configurator (built by its entry in the provider table); it is
    // guaranteed a non-empty connection string by the resolver above (fail-fast at startup).
    Action<DbContextOptionsBuilder>? configureOpenIddictDatabase = null;

    if (openIddictStore?.RelationalProvider is { } openIddictProvider)
    {
        configureOpenIddictDatabase = openIddictProvider.BuildConfigurator(
            RelationalStore.OpenIddict,
            openIddictConnectionString!);
    }

    // Provider=InMemory in the section is the spoken choice of the volatile store in its configuration
    // form (SPEC-012 CFG-118): the host says it to the core instead of leaving it to be guessed. An
    // unspoken choice (empty section) is passed on as it is — no delegate and no opt-in — and the core's
    // startup check refuses to start outside Development (CFG-119).
    if (openIddictStore is { IsInMemory: true })
    {
        builder.Services.UseInMemoryOpenIddictStore();
    }

    builder.Services.AddVeriqaOpenIddict(builder.Configuration, builder.Environment, configureOpenIddictDatabase);

    // Wire up the audit trail (SPEC-011): the bus receiver gated by the resolved Logging.Mode, the
    // append-only journal and the retention sweep. The sink has keys of its own, Veriqa:Logging:Store,
    // each falling back to the same key of the transaction store (SPEC-012 CFG-252, SPEC-020
    // MODE-NET-3-014), so a development run without a database keeps the journal in memory.
    //
    // Registered after the OpenIddict integration on purpose: bus handlers run one after another in
    // registration order on a single reader, so the real-time sign-in notification (registered inside
    // AddVeriqaOpenIddict) has to reach the browser before the journal insert, not after it. The
    // dependency is on the order of these two calls only — the audit registration itself needs nothing
    // from OpenIddict.
    var auditStore = ResolveAuditStore(builder.Configuration);

    builder.Services.AddVeriqaAuditTrail(audit =>
    {
        if (auditStore is not null)
        {
            audit.UseEfCoreSink(auditStore.Provider.BuildConfigurator(
                RelationalStore.Audit,
                auditStore.ConnectionString));
        }
        else
        {
            audit.UseInMemorySink();
        }
    });

    // The in-memory journal was picked by the fallback, not stated by anyone: whether that is acceptable
    // for the resolved Logging.Mode is decided at startup, when the environment is known (SPEC-011 E45,
    // SPEC-012 CFG-253). A durable sink needs no check, which is why the registration is conditional.
    if (auditStore is null)
    {
        builder.Services.AddHostedService(serviceProvider => new AuditStoreStartupCheckService(
            // The environment is optional by design: it may be absent outside a generic host.
            serviceProvider.GetService<IHostEnvironment>(),
            serviceProvider.GetRequiredService<IConfigurationResolver>(),
            serviceProvider.GetRequiredService<ILogger<AuditStoreStartupCheckService>>()));
    }

    // Wire up the channel adapters (Telegram, MAX, Email)
    builder.Services.AddVeriqaChannelAdapters(builder.Configuration, adapters =>
    {
        adapters.AddTelegram();
        adapters.AddWhatsApp();
        adapters.AddMax();
        adapters.AddEmail();
    });

    // Request rate limiting: endpoint-level policies + per-user rate limiter (TASK-016, SPEC-007 §6.1)
    builder.Services.AddVeriqaRateLimiting();

    // SignalR backplane (Redis) — for multi-instance deployment
    // If the connection string is not set — the backplane is not enabled (single-instance mode)
    ConfigureSignalRBackplane(builder);

    // DataProtection — persisting keys in Redis or on disk
    ConfigureDataProtection(builder);

    // Health checks for infrastructure dependencies
    ConfigureHealthChecks(builder);

    var app = builder.Build();

    // A host can be asked what its composition DECLARES instead of being asked to serve: it states the
    // schema of the declared keys and exits, having started nothing. The answer is read out of the
    // container this host has just built, so it is the schema this very installation resolves by.
    if (ConfigSchemaDump.Requested(args))
    {
        ConfigSchemaDump.Write(Console.Out, app.Services.GetServices<ConfigKeyCatalog>());

        return;
    }

    // Middleware pipeline

    // Security headers: CSP (with nonce), X-Frame-Options, X-Content-Type-Options (SPEC-007 §6.3)
    // Placed BEFORE UseStaticFiles and UseRouting — covers all responses
    app.UseSecurityHeaders();

    app.UseForwardedHeaders();

    // Static file serving (wwwroot): SignalR JS client, localization (SPEC-007 §5, §12)
    app.UseStaticFiles();

    app.UseAuthentication();
    app.UseAuthorization();

    // Rate Limiting (SPEC-007 §6.1) — AFTER UseRouting, BEFORE MapEndpoints
    app.UseRateLimiter();

    // Register the standard Aspire endpoints (health, alive)
    app.MapDefaultEndpoints();

    // Additional health endpoints for liveness and readiness probes
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains(InfrastructureConfigConstants.LivenessTag)
    });
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains(InfrastructureConfigConstants.ReadinessTag)
    });

    // Channel state — an endpoint of its own, deliberately outside readiness: an outage on a channel
    // platform is not a reason to take a working replica out of rotation, so an operator watching the
    // channels alerts on this endpoint rather than on the readiness probe.
    // The response writer is our own: the default one answers with the status word alone, and since the
    // channel check never reports worse than Degraded, the per-channel report is the only thing that
    // says which channel is down.
    app.MapHealthChecks("/health/channels", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = r => r.Tags.Contains(ChannelHealthCheckNames.Tag),
        ResponseWriter = WriteChannelHealthResponseAsync
    });

    // Register the OIDC endpoints (authorize, callback, token, userinfo, polling status)
    app.MapVeriqaOidcEndpoints();

    // Register the endpoints of the registered channels (with rate limiting — SPEC-007 §6.1): their
    // webhooks plus whatever routes a channel maps for itself. A channel that is disabled in the
    // configuration contributes none, so the host exposes no route for it.
    app.MapChannelWebhookEndpoints(new ChannelEndpointPolicies
    {
        WebhookPolicyName = RateLimitOptions.WebhookPolicyName,
        PagePolicyName = RateLimitOptions.EmailStartPolicyName
    });

    // Run the application
    app.Run();
});

/// <summary>
/// Configures the SignalR Redis backplane for multi-instance deployment.
/// If Veriqa:SignalR:RedisConnectionString is not set — the backplane is not enabled.
/// </summary>
static void ConfigureSignalRBackplane(WebApplicationBuilder builder)
{
    var redisConnectionString = builder.Configuration[InfrastructureConfigConstants.SignalRRedisConnectionStringKey];

    if (!string.IsNullOrWhiteSpace(redisConnectionString))
    {
        // Enable the Redis backplane for horizontal scaling of SignalR
        builder.Services
            .AddSignalR()
            .AddStackExchangeRedis(redisConnectionString, options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal(InfrastructureConfigConstants.SignalRChannelPrefix);
            });
    }
}

/// <summary>
/// Configures DataProtection with persistent key storage.
/// Priority: Redis (Veriqa:DataProtection:RedisConnectionString) → file system (Veriqa:DataProtection:KeysDirectory).
/// If neither is set — keys are stored in memory (not suitable for production).
/// </summary>
static void ConfigureDataProtection(WebApplicationBuilder builder)
{
    var dpBuilder = builder.Services
        .AddDataProtection()
        .SetApplicationName(InfrastructureConfigConstants.DataProtectionApplicationName);

    var redisConnectionString = builder.Configuration[InfrastructureConfigConstants.DataProtectionRedisConnectionStringKey];
    var keysDirectory = builder.Configuration[InfrastructureConfigConstants.DataProtectionKeysDirectoryKey];

    if (!string.IsNullOrWhiteSpace(redisConnectionString))
    {
        // Create the multiplexer and register it as a keyed singleton to manage its lifecycle.
        // Using a keyed service ("dataprotection") avoids a conflict with the TransactionEngine
        // IConnectionMultiplexer (which is registered without a key via UseRedisStore()).
        var redis = ConnectionMultiplexer.Connect(redisConnectionString);
        builder.Services.AddKeyedSingleton<IConnectionMultiplexer>("dataprotection", redis);

        // Persist keys in Redis — optimal for multi-instance
        dpBuilder.PersistKeysToStackExchangeRedis(
            redis,
            InfrastructureConfigConstants.DataProtectionRedisKey);
    }
    else if (!string.IsNullOrWhiteSpace(keysDirectory))
    {
        // Persist keys on the file system — for single-instance or a shared volume
        dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
    }
    // Otherwise keys are stored in-memory (dev/testing only — lost on restart)
}

/// <summary>
/// Resolves the transaction store provider from Veriqa:TransactionEngine:Store:Provider against the
/// provider table of this host. Empty/unset — PostgreSQL (the default). An unrecognized value is a
/// configuration exception (fail-fast), so a typo ("Postgres", "MariaDB") does not lead to silently
/// picking the wrong provider (InMemory is selected by the absence of a connection string).
/// </summary>
static RelationalStoreProvider ResolveTransactionStoreProvider(IConfiguration configuration)
{
    var providerValue = configuration[InfrastructureConfigConstants.TransactionStoreProviderKey];

    return string.IsNullOrWhiteSpace(providerValue)
        ? RelationalStoreProviders.PostgreSql
        : RelationalStoreProviders.Resolve(InfrastructureConfigConstants.TransactionStoreProviderKey, providerValue);
}

/// <summary>
/// Resolves the OpenIddict store from Veriqa:OpenIddict:Database:Provider.
/// (SPEC-012 §5.2, CFG-117) — the host is the reader of this section in standalone mode.
/// Empty/unset — null: the choice was not spoken, and there is no default (SPEC-012 CFG-118).
/// The caller states nothing to the core in that case, and the core's startup check decides what
/// an unspoken choice means here (refusal outside Development, a warning inside it — CFG-119).
/// A relational provider requires a connection string, and an unrecognized value is a configuration
/// exception (fail-fast) — so a typo ("Postgres", an extra letter) does not lead to silently running
/// on InMemory.
/// </summary>
static OpenIddictStoreSelection? ResolveOpenIddictStore(IConfiguration configuration)
{
    var providerValue = configuration[InfrastructureConfigConstants.OpenIddictDatabaseProviderKey];

    if (string.IsNullOrWhiteSpace(providerValue))
    {
        return null;
    }

    if (string.Equals(providerValue.Trim(), RelationalStoreProviders.InMemoryName, StringComparison.OrdinalIgnoreCase))
    {
        return OpenIddictStoreSelection.InMemory;
    }

    var provider = RelationalStoreProviders.Find(providerValue)
        ?? throw new InvalidOperationException(
            $"Unsupported value '{InfrastructureConfigConstants.OpenIddictDatabaseProviderKey}' = '{providerValue}'. " +
            $"Allowed: '{RelationalStoreProviders.InMemoryName}', {RelationalStoreProviders.AllowedNames}.");

    if (string.IsNullOrWhiteSpace(configuration[InfrastructureConfigConstants.OpenIddictDatabaseConnectionStringKey]))
    {
        throw new InvalidOperationException(
            $"With '{InfrastructureConfigConstants.OpenIddictDatabaseProviderKey}' = '{provider.Name}' " +
            $"you must set '{InfrastructureConfigConstants.OpenIddictDatabaseConnectionStringKey}'.");
    }

    return new OpenIddictStoreSelection(provider);
}

/// <summary>
/// Resolves the audit sink from Veriqa:Logging:Store (SPEC-012 CFG-252). Each key falls back on its own
/// to the same key of the transaction store: no ConnectionString — the transaction connection string;
/// no Provider — the transaction provider (empty there too — PostgreSQL). An empty effective connection
/// string returns null: the journal stays in memory. A stated Provider is validated even then, so an
/// unrecognized value always refuses the start.
/// <para>
/// A stated sink Provider over a borrowed connection string must match the transaction provider: that
/// string belongs to the transaction database, and a different provider would silently point the
/// journal at a database of another kind.
/// </para>
/// </summary>
static AuditStoreSelection? ResolveAuditStore(IConfiguration configuration)
{
    var ownProviderValue = configuration[InfrastructureConfigConstants.AuditStoreProviderKey];
    var ownProvider = string.IsNullOrWhiteSpace(ownProviderValue)
        ? null
        : RelationalStoreProviders.Resolve(InfrastructureConfigConstants.AuditStoreProviderKey, ownProviderValue);

    var ownConnectionString = configuration[InfrastructureConfigConstants.AuditStoreConnectionStringKey];
    var borrowsConnectionString = string.IsNullOrWhiteSpace(ownConnectionString);
    var connectionString = borrowsConnectionString
        ? configuration[InfrastructureConfigConstants.TransactionStoreConnectionStringKey]
        : ownConnectionString;

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return null;
    }

    if (ownProvider is null)
    {
        return new AuditStoreSelection(ResolveTransactionStoreProvider(configuration), connectionString);
    }

    if (borrowsConnectionString)
    {
        var transactionProvider = ResolveTransactionStoreProvider(configuration);

        if (!ReferenceEquals(ownProvider, transactionProvider))
        {
            throw new InvalidOperationException(
                $"'{InfrastructureConfigConstants.AuditStoreProviderKey}' = '{ownProviderValue}' differs from " +
                $"'{InfrastructureConfigConstants.TransactionStoreProviderKey}', which resolves to '{transactionProvider.Name}', " +
                $"while the audit store borrows '{InfrastructureConfigConstants.TransactionStoreConnectionStringKey}': " +
                $"set '{InfrastructureConfigConstants.AuditStoreConnectionStringKey}' or use the same provider.");
        }
    }

    return new AuditStoreSelection(ownProvider, connectionString);
}

/// <summary>
/// Registers health checks for infrastructure dependencies.
/// Each check is added conditionally — only when the corresponding dependency is configured.
/// OpenIddict DB: Veriqa:OpenIddict:Database:Provider (+ConnectionString) — the check is aligned
/// with the provider selected for the store, so InMemory registers no relational check.
/// Transaction DB: Veriqa:TransactionEngine:Store:ConnectionString (+Provider) — the check
/// is aligned with the provider selected for the store.
/// Audit DB: Veriqa:Logging:Store (with the fallback to the transaction store) — a separate check only
/// when the effective connection string differs from the transaction one (SPEC-012 CFG-252).
/// Check names are &lt;provider&gt;-&lt;store&gt; from the provider table (for example postgresql-transactions).
/// Redis (SignalR): Veriqa:SignalR:RedisConnectionString.
/// Redis (DataProtection): Veriqa:DataProtection:RedisConnectionString (a separate check when it differs from SignalR).
/// RabbitMQ: Veriqa:Events:RabbitMq:ConnectionString (event publisher).
/// </summary>
static void ConfigureHealthChecks(WebApplicationBuilder builder)
{
    var checks = builder.Services.AddHealthChecks();

    // OpenIddict DB: the health check follows the same provider decision as the store itself
    // (single resolver with validation). A relational check is registered only for a relational
    // provider — with InMemory, or with a provider nobody spoke, there is nothing to probe.
    var openIddictStore = ResolveOpenIddictStore(builder.Configuration);
    var dbConnectionString = builder.Configuration[InfrastructureConfigConstants.OpenIddictDatabaseConnectionStringKey];

    if (openIddictStore?.RelationalProvider is { } openIddictProvider)
    {
        openIddictProvider.AddHealthCheck(checks, RelationalStore.OpenIddict, dbConnectionString!);
    }

    // Transaction store DB: the health check follows the same provider configuration
    // as the store itself (Veriqa:TransactionEngine:Store:Provider)
    var transactionDbConnectionString = builder.Configuration[InfrastructureConfigConstants.TransactionStoreConnectionStringKey];

    if (!string.IsNullOrWhiteSpace(transactionDbConnectionString))
    {
        ResolveTransactionStoreProvider(builder.Configuration)
            .AddHealthCheck(checks, RelationalStore.Transactions, transactionDbConnectionString);
    }

    // Audit DB: the journal on the transaction database is already probed by the check above, so a
    // check of its own is registered only when the sink points elsewhere (ordinal comparison, as the
    // Redis pair below does it).
    var auditStore = ResolveAuditStore(builder.Configuration);

    if (auditStore is not null
        && !string.Equals(auditStore.ConnectionString, transactionDbConnectionString, StringComparison.Ordinal))
    {
        auditStore.Provider.AddHealthCheck(checks, RelationalStore.Audit, auditStore.ConnectionString);
    }

    // Redis (SignalR backplane): verify Redis availability for SignalR
    var signalRRedis = builder.Configuration[InfrastructureConfigConstants.SignalRRedisConnectionStringKey];

    if (!string.IsNullOrWhiteSpace(signalRRedis))
    {
        checks.AddRedis(
            signalRRedis,
            name: "redis-signalr",
            tags: [InfrastructureConfigConstants.ReadinessTag]);
    }

    // Redis (DataProtection): verify Redis availability for encryption key storage.
    // Register a separate check only when the connection string differs from the SignalR Redis
    // (to avoid duplicating an identical check when one Redis serves both services).
    var dpRedis = builder.Configuration[InfrastructureConfigConstants.DataProtectionRedisConnectionStringKey];

    if (!string.IsNullOrWhiteSpace(dpRedis) && dpRedis != signalRRedis)
    {
        checks.AddRedis(
            dpRedis,
            name: "redis-dataprotection",
            tags: [InfrastructureConfigConstants.ReadinessTag]);
    }

    // RabbitMQ: verify RabbitMQ availability (event publisher)
    var rabbitConnectionString = builder.Configuration[InfrastructureConfigConstants.RabbitMqConnectionStringKey];

    if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
    {
        var amqpUri = new Uri(rabbitConnectionString);
        checks.AddRabbitMQ(
            factory: _ =>
            {
                var connectionFactory = new RabbitMQ.Client.ConnectionFactory
                {
                    Uri = amqpUri,
                    AutomaticRecoveryEnabled = true
                };
                return connectionFactory.CreateConnectionAsync();
            },
            name: "rabbitmq-events",
            tags: [InfrastructureConfigConstants.ReadinessTag]);
    }
}

/// <summary>
/// Writes the channel health report as JSON: the overall status plus, for every check in it, its
/// name, status, description and per-channel data. The default writer answers with the status word
/// alone — and since the channel check never reports worse than Degraded, that word would leave an
/// operator with no way to tell which channel is down.
/// </summary>
/// <param name="context">HTTP context of the probe request.</param>
/// <param name="report">Report of the checks selected by the endpoint predicate.</param>
/// <returns>Task that completes once the response body is written.</returns>
static async Task WriteChannelHealthResponseAsync(
    HttpContext context,
    Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = MediaTypeNames.Application.Json;

    // The document is written field by field rather than serialized from a model: the report values
    // are either a string of the check's closed value set or a number of milliseconds, and writing
    // them explicitly keeps anything else from leaking into a body an operator reads.
    await using var writer = new Utf8JsonWriter(context.Response.BodyWriter);

    writer.WriteStartObject();
    writer.WriteString("status", report.Status.ToString());
    writer.WriteStartArray("checks");

    foreach (var (name, entry) in report.Entries)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("status", entry.Status.ToString());
        writer.WriteString("description", entry.Description);
        writer.WriteStartObject("data");

        foreach (var (key, value) in entry.Data)
        {
            if (value is long milliseconds)
            {
                writer.WriteNumber(key, milliseconds);
            }
            else
            {
                // Null-safe on purpose: the writer runs after the status line and the headers are
                // already on the wire, so an exception here would leave a truncated body behind an
                // HTTP 200. HealthCheckResult.Data is a dictionary of object, and a check other
                // than this one may put a null in it.
                writer.WriteString(key, value?.ToString() ?? string.Empty);
            }
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    writer.WriteEndArray();
    writer.WriteEndObject();

    await writer.FlushAsync(context.RequestAborted);
}
