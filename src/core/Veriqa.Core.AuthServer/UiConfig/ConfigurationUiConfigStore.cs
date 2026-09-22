// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;
using Veriqa.Core.AuthServer.Configuration;
using Veriqa.Core.TransactionEngine.Common;

namespace Veriqa.Core.AuthServer.UiConfig;

/// <summary>
/// Self-hosted implementation of <see cref="IUiConfigStore"/> (§8 assumption #2): the source of records is
/// the configuration catalog <see cref="UiConfigurationsOptions"/>. The assignment check preserves
/// the prior semantics: the code must be in the client's AllowedUiConfigs (reconciling the static side against the dynamic one).
/// No mandatory database (R2: on-premise simplicity).
/// </summary>
internal sealed class ConfigurationUiConfigStore : IUiConfigStore
{
    /// <summary>
    /// Catalog of ui_config records from configuration.
    /// </summary>
    private readonly IOptionsMonitor<UiConfigurationsOptions> _uiConfigurations;

    /// <summary>
    /// Guarded access to the OIDC clients snapshot (for checking a record's assignment to an
    /// application): a failed reload of a neighbouring client's entry must not turn this per-request
    /// read into an exception (CFG-203).
    /// </summary>
    private readonly OidcClientsOptionsAccessor _clients;

    /// <summary>
    /// Creates the self-hosted store of ui_config records.
    /// </summary>
    /// <param name="uiConfigurations">Record catalog.</param>
    /// <param name="clients">Guarded access to the OIDC clients snapshot.</param>
    public ConfigurationUiConfigStore(
        IOptionsMonitor<UiConfigurationsOptions> uiConfigurations,
        OidcClientsOptionsAccessor clients)
    {
        _uiConfigurations = uiConfigurations;
        _clients = clients;
    }

    /// <summary>
    /// Empty ui_config record (default schema version only): synthesized for a code
    /// assigned to an application but not described in the Veriqa:UiConfigurations catalog.
    /// All fields null ⇒ the renderer performs feature detection and uses the global defaults (R2: 1:1).
    /// </summary>
    private static readonly UiConfigRecord EmptyRecord = new();

    /// <inheritdoc />
    /// <remarks>
    /// The catalog is read through <see cref="IOptionsMonitor{TOptions}"/>: the configuration provider
    /// reloads the file itself, so an edit of <c>Veriqa:UiConfigurations</c> reaches the next read at
    /// once and the resolver must not cache this level.
    /// </remarks>
    public bool IsLive => true;

    /// <inheritdoc />
    public Task<Result<UiConfigRecord?>> GetAsync(
        string? selector,
        string? tenantId,
        string? applicationId,
        CancellationToken cancellationToken = default)
    {
        // The method determines the selector's validity by membership in the client's AllowedUiConfigs (CFG-203,
        // reconciling the static side against the dynamic one) and returns a record from the Veriqa:UiConfigurations catalog.
        // Self-hosted ignores tenantId (the single implicit tenant ≡ core).

        // An empty/whitespace selector is invalid (as in the former ValidateUiConfig)
        if (string.IsNullOrWhiteSpace(selector))
        {
            return Task.FromResult(Result<UiConfigRecord?>.Success(null));
        }

        // Assignment check for the application: the code must be in the client's AllowedUiConfigs.
        // This is the very criterion of selector validity (CFG-203). Without an application context
        // (applicationId == null) there is nothing to validate the code against → invalid (null → 400).
        // A code not assigned to the application is also invalid.
        if (applicationId is null || !IsAssignedToApplication(selector, applicationId))
        {
            return Task.FromResult(Result<UiConfigRecord?>.Success(null));
        }

        var catalog = _uiConfigurations.CurrentValue.Records;

        // The code is assigned, but there is no record in the Veriqa:UiConfigurations catalog (typical self-hosted without
        // the new section): synthesize an empty record → global styling 1:1, and the code remains valid (R2).
        if (!catalog.TryGetValue(selector, out var record))
        {
            return Task.FromResult(Result<UiConfigRecord?>.Success(EmptyRecord));
        }

        // The record exists in the catalog, but its schema version is invalid (explicit 0/negative, §8 assumption #5):
        // the record is ignored, but the code itself is assigned — degrade to an empty record (global defaults).
        if (!record.IsSchemaVersionValid())
        {
            return Task.FromResult(Result<UiConfigRecord?>.Success(EmptyRecord));
        }

        return Task.FromResult(Result<UiConfigRecord?>.Success(record));
    }

    /// <summary>
    /// Checks whether a ui_config record is assigned to the application (membership in the client's AllowedUiConfigs).
    /// </summary>
    private bool IsAssignedToApplication(string selector, string applicationId)
    {
        var client = _clients.Current.Clients
            .FirstOrDefault(c => string.Equals(c.ClientId, applicationId, StringComparison.Ordinal));

        if (client?.AllowedUiConfigs is null || client.AllowedUiConfigs.Count is 0)
        {
            return false;
        }

        return client.AllowedUiConfigs
            .Any(code => string.Equals(code, selector, StringComparison.Ordinal));
    }
}
