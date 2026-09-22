// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.DependencyInjection;

using Veriqa.Core.AuthServer.UiConfig;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration.Resolution;

/// <summary>
/// Walker of the <c>ui_config</c> records of the self-hosted contour, where the catalog of records
/// lives in the application configuration (SPEC-012 §10.6). It stands next to
/// <see cref="UiConfigNodeReader"/> and hands out the same nodes it does — built over the record by
/// that reader, so both go through ONE spelling of the record's JSON contract.
/// <para>
/// Only the records of the EFFECTIVE set are walked — the codes allowed by at least one client of the
/// effective set (CFG-210) — the same way the walker of the client entries stays inside the effective
/// set of clients. A record no client is allowed to select is refused by the store on every read
/// (<see cref="ConfigurationUiConfigStore"/>), so it reaches no page and states nothing to the
/// resolution of this level; a finding about it would name a value no page can ever be given.
/// </para>
/// <para>
/// Where a contour serves the level from an external store instead, nothing walks it: enumerating every
/// record of a database on each reload is a cost the level's owner decides about, and the report names
/// the level as being outside the walk instead of pretending to have covered it. This walker is
/// registered before such a contour can substitute the store, so the substitution is answered at RUN
/// time — see <see cref="CanWalk"/>.
/// </para>
/// </summary>
internal sealed class UiConfigNodeWalker : IConfigNodeWalker
{
    /// <summary>
    /// Catalog of <c>ui_config</c> records of the self-hosted contour.
    /// </summary>
    private readonly IOptionsMonitor<UiConfigurationsOptions> _uiConfigurations;

    /// <summary>
    /// Guarded access to the OIDC clients snapshot — where the EFFECTIVE set of this level comes from.
    /// It is the same accessor the store reads the assignment of a record from
    /// (<see cref="ConfigurationUiConfigStore"/>), so the walk and the store answer over ONE snapshot
    /// of the clients rather than two that could differ. The PREDICATE is not the store's: the store
    /// answers about one application, while a walk of the snapshot runs outside any application context
    /// and asks whether ANY effective client may select the record.
    /// </summary>
    private readonly OidcClientsOptionsAccessor _clients;

    /// <summary>
    /// Whether the <c>ui_config</c> level is served by the store this walker reads. Resolved once and
    /// remembered: which store serves the level is fixed when the container is built.
    /// </summary>
    private readonly Lazy<bool> _servedByConfiguration;

    /// <summary>
    /// Creates the walker of the <c>ui_config</c> records.
    /// </summary>
    /// <param name="uiConfigurations">Catalog of <c>ui_config</c> records.</param>
    /// <param name="clients">Guarded access to the OIDC clients snapshot.</param>
    /// <param name="scopeFactory">Scope factory for asking which store serves the level.</param>
    public UiConfigNodeWalker(
        IOptionsMonitor<UiConfigurationsOptions> uiConfigurations,
        OidcClientsOptionsAccessor clients,
        IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _uiConfigurations = uiConfigurations ?? throw new ArgumentNullException(nameof(uiConfigurations));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _servedByConfiguration = new Lazy<bool>(() => IsServedByConfiguration(scopeFactory));
    }

    /// <inheritdoc />
    public ConfigLevel Level => ConfigLevel.UiConfig;

    /// <inheritdoc />
    /// <remarks>
    /// The <c>ui_config</c> level is the one level of the auth server whose source is a REPLACEABLE
    /// store (<see cref="IUiConfigStore"/>), and a contour replaces it after the auth server has
    /// registered this walker (the demo sandbox and the cloud contour both do). The section read here
    /// is then empty and unread by anyone — so the walk would report nothing and, worse, would still
    /// count as covering the level and silence the line that names it as unwalked.
    /// </remarks>
    public bool CanWalk => _servedByConfiguration.Value;

    /// <inheritdoc />
    public string SectionKey => UiConfigurationsOptions.SectionName;

    /// <inheritdoc />
    public IDisposable? Subscribe(Action onSnapshot)
    {
        ArgumentNullException.ThrowIfNull(onSnapshot);

        return _uiConfigurations.OnChange(_ => onSnapshot());
    }

    /// <inheritdoc />
    public IEnumerable<ConfigNode> Walk()
    {
        var allowed = AllowedCodes();

        foreach (var (code, record) in _uiConfigurations.CurrentValue.Records)
        {
            // A record no effective client is allowed to select states nothing to the resolution of this
            // level — the store refuses the code on every read — so it states nothing to a report of
            // that level either (CFG-210).
            if (!allowed.Contains(code))
            {
                continue;
            }

            // The record code is a segment of the configuration key AND the identity the resolution
            // addresses the record by (ResolutionContext.UiConfigSelector), so it is stated as the
            // record of the finding rather than left to be parsed back out of the key.
            yield return UiConfigNodeReader.NodeOver(record, code, RecordSectionKeyOf(code));
        }
    }

    /// <summary>
    /// Codes of the effective set of this level: the union of the codes allowed by the clients of the
    /// effective set. The union is what the walk needs — it runs outside any application context, so a
    /// record is live when at least ONE effective client may select it. The set is taken once per walk:
    /// the snapshot behind it is one and the same for the whole walk.
    /// </summary>
    /// <returns>Codes at least one effective client allows.</returns>
    private HashSet<string> AllowedCodes()
    {
        // Ordinal, as in the assignment check of the store: a code is matched as it is spelled.
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var client in _clients.Current.Clients)
        {
            if (client.AllowedUiConfigs is not { } allowed)
            {
                continue;
            }

            foreach (var code in allowed)
            {
                if (!string.IsNullOrEmpty(code))
                {
                    codes.Add(code);
                }
            }
        }

        return codes;
    }

    /// <summary>
    /// Configuration key of one <c>ui_config</c> record — the records form a dictionary, so the record
    /// code is a segment of it.
    /// </summary>
    /// <param name="code">Code the record is stated under.</param>
    /// <returns>Configuration key of the record.</returns>
    private static string RecordSectionKeyOf(string code) =>
        $"{UiConfigurationsOptions.SectionName}:{nameof(UiConfigurationsOptions.Records)}:{code}";

    /// <summary>
    /// Tells whether the registered <c>ui_config</c> store is the self-hosted one — the store whose
    /// records ARE the configuration section this walker enumerates. The store is asked for through a
    /// scope, never through the constructor: its lifetime is decided by the contour, and the cloud one
    /// registers it as Scoped.
    /// </summary>
    /// <param name="scopeFactory">Scope factory.</param>
    /// <returns><c>true</c> when the level is served by the section this walker reads.</returns>
    private static bool IsServedByConfiguration(IServiceScopeFactory scopeFactory)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();

            return scope.ServiceProvider.GetRequiredService<IUiConfigStore>() is ConfigurationUiConfigStore;
        }
        // A store that cannot even be built is certainly not the one this walker reads, and finding
        // that out must not fail the host over a diagnostic: the level is named as being outside the
        // report, which is what actually happened to it.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
