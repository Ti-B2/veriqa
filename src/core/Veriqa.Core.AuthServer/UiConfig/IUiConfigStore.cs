// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.AuthServer.UiConfig;

/// <summary>
/// Store of ui_config records (SPEC-012 CFG-203, SPEC-002 §4.6). Abstraction over the record source:
/// self-hosted (from configuration), Cloud (PostgreSQL JSONB), Demo (HybridCache, ephemeral).
/// Selector validity (CFG-203): "the record exists AND belongs to the tenant / is assigned to the
/// application" — replaces the static membership check against the AllowedUiConfigs string list.
/// </summary>
public interface IUiConfigStore
{
    /// <summary>
    /// Whether the store is LIVE — that is, whether its freshness is somebody else's job. <c>true</c>
    /// for a store over the application configuration: the provider reloads the file itself and an
    /// edit reaches the next read immediately. <c>false</c> for a store over an external medium (a
    /// database), whose reads the resolver may cache by the lifetime the setting's key declares
    /// (SPEC-012 §10.6).
    /// <para>
    /// The answer belongs HERE, to whoever holds the record, and not to the reader above: one reader
    /// class stands over every contour's store, so a lifetime declared at the reader would also cover
    /// the deployment whose store is a configuration file — and an edit of that file would stop
    /// reaching the sign-in page for the length of the lifetime.
    /// </para>
    /// <para>
    /// Caching what a NOT-live store answers comes with a named trade-off: the resolver has no
    /// invalidation, so a record changed outside this store — a migration, a manual statement, a
    /// future editor — reaches the page no sooner than the lifetime expires.
    /// </para>
    /// </summary>
    bool IsLive { get; }

    /// <summary>
    /// Returns the ui_config record by selector within the (tenant, application) scope.
    /// </summary>
    /// <param name="selector">Code (self-hosted/Cloud) or configId (Demo). Empty/null — invalid.</param>
    /// <param name="tenantId">Tenant identifier. Null — self-hosted/core.</param>
    /// <param name="applicationId">Application identifier (client_id). Null — no assignment check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>Result.Success(record)</c> when present and owned; <c>Result.Success(null)</c> if the
    /// record is not found / does not belong to the tenant / is not assigned to the application
    /// (NOT an error — the resolver skips the ui_config level); <c>Result.Failure</c> only on an
    /// infrastructure failure of the source.
    /// </returns>
    Task<Result<UiConfigRecord?>> GetAsync(
        string? selector,
        string? tenantId,
        string? applicationId,
        CancellationToken cancellationToken = default);
}
