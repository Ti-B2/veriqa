// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.Constants;

/// <summary>
/// Configuration constants for infrastructure components: SignalR backplane, DataProtection, health checks.
/// TASK-012.
/// </summary>
public static class InfrastructureConfigConstants
{
    /// <summary>
    /// Configuration section name for the SignalR backplane.
    /// Example: Veriqa:SignalR:RedisConnectionString = "localhost:6379"
    /// </summary>
    public const string SignalRSectionName = "Veriqa:SignalR";

    /// <summary>
    /// Redis connection string key for the SignalR backplane.
    /// Full path: Veriqa:SignalR:RedisConnectionString
    /// If not set — the backplane is not enabled (suitable for single-instance).
    /// </summary>
    public const string SignalRRedisConnectionStringKey = "Veriqa:SignalR:RedisConnectionString";

    /// <summary>
    /// Configuration section name for DataProtection.
    /// </summary>
    public const string DataProtectionSectionName = "Veriqa:DataProtection";

    /// <summary>
    /// Redis connection string key for storing DataProtection keys.
    /// Full path: Veriqa:DataProtection:RedisConnectionString
    /// If not set — KeysDirectory is checked.
    /// </summary>
    public const string DataProtectionRedisConnectionStringKey = "Veriqa:DataProtection:RedisConnectionString";

    /// <summary>
    /// Key for the directory storing DataProtection keys on disk.
    /// Full path: Veriqa:DataProtection:KeysDirectory
    /// Used as a fallback if Redis is not configured.
    /// </summary>
    public const string DataProtectionKeysDirectoryKey = "Veriqa:DataProtection:KeysDirectory";

    /// <summary>
    /// Application name for DataProtection (key isolation between applications).
    /// </summary>
    public const string DataProtectionApplicationName = "veriqa-authserver";

    /// <summary>
    /// Redis key name for storing DataProtection keys.
    /// </summary>
    public const string DataProtectionRedisKey = "veriqa:dataprotection:keys";

    /// <summary>
    /// Redis channel prefix for the SignalR backplane (used with AddStackExchangeRedis).
    /// Isolates Veriqa messages from other SignalR applications on a shared Redis server.
    /// </summary>
    public const string SignalRChannelPrefix = "veriqa-signalr";

    /// <summary>
    /// Configuration section name for health checks.
    /// </summary>
    public const string HealthChecksSectionName = "Veriqa:HealthChecks";

    /// <summary>
    /// Database connection string key for OpenIddict (relational provider).
    /// Full path: Veriqa:OpenIddict:Database:ConnectionString
    /// Used in the health check to verify database availability.
    /// </summary>
    public const string OpenIddictDatabaseConnectionStringKey = "Veriqa:OpenIddict:Database:ConnectionString";

    /// <summary>
    /// Database provider key for OpenIddict (InMemory or a relational provider of the host; the shipped
    /// host accepts PostgreSQL / SqlServer).
    /// Full path: Veriqa:OpenIddict:Database:Provider
    /// Used to select the health check type.
    /// </summary>
    public const string OpenIddictDatabaseProviderKey = "Veriqa:OpenIddict:Database:Provider";

    /// <summary>
    /// RabbitMQ connection string key for the event publisher.
    /// Full path: Veriqa:Events:RabbitMq:ConnectionString
    /// Used in the RabbitMQ health check to verify broker availability.
    /// </summary>
    public const string RabbitMqConnectionStringKey = "Veriqa:Events:RabbitMq:ConnectionString";

    /// <summary>
    /// Transaction store connection string key (EF Core).
    /// Full path: Veriqa:TransactionEngine:Store:ConnectionString
    /// If not set — the InMemory store is used.
    /// </summary>
    public const string TransactionStoreConnectionStringKey = "Veriqa:TransactionEngine:Store:ConnectionString";

    /// <summary>
    /// Transaction store provider key (the shipped host accepts PostgreSQL / SqlServer; empty — PostgreSQL).
    /// Full path: Veriqa:TransactionEngine:Store:Provider
    /// The provider is chosen by configuration from the set baked into the image at build time (SPEC-020).
    /// </summary>
    public const string TransactionStoreProviderKey = "Veriqa:TransactionEngine:Store:Provider";

    /// <summary>
    /// Audit sink connection string key (secret), read by the standalone host (SPEC-012 CFG-252).
    /// Full path: Veriqa:Logging:Store:ConnectionString
    /// If not set — Veriqa:TransactionEngine:Store:ConnectionString is used; empty there too — the journal
    /// stays in memory (SPEC-012 CFG-253).
    /// </summary>
    public const string AuditStoreConnectionStringKey = "Veriqa:Logging:Store:ConnectionString";

    /// <summary>
    /// Audit sink provider key, read by the standalone host (SPEC-012 CFG-252).
    /// Full path: Veriqa:Logging:Store:Provider
    /// If not set — Veriqa:TransactionEngine:Store:Provider is used; empty there too — PostgreSQL.
    /// </summary>
    public const string AuditStoreProviderKey = "Veriqa:Logging:Store:Provider";

    /// <summary>
    /// Liveness check tag (alive only — the process is running).
    /// </summary>
    public const string LivenessTag = "live";

    /// <summary>
    /// Readiness check tag (ready to serve traffic — dependencies are available).
    /// </summary>
    public const string ReadinessTag = "ready";
}
