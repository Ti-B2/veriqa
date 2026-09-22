// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Primitives;

using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration.Snapshot;

/// <summary>
/// Catalog of the entries of the OIDC clients array that the configuration binder could not build.
/// Unlike a catalog of one setting, its unit is the RECORD: an entry that is gone states nothing, so
/// every setting it carried cedes to the level below it in the resolution order (SPEC-012 §10.3,
/// CFG-210) — which is what makes the loss of an entry a fact of its own.
/// <para>
/// The binder treats a failure while building an ELEMENT of a collection as a failure of that element
/// alone: the element is skipped and nothing is reported. A single value that does not convert to the
/// type its property declares (<c>"yes"</c> for a <c>bool?</c> or for an enum) is enough, and the whole
/// entry then never exists. Nobody downstream can notice — the validator of the set validates what it
/// was given, <c>ValidateOnStart</c> sees a section that bound cleanly, and the per-entry rejection of
/// <see cref="OidcClientsOptionsPostConfigure"/> walks the same already-bound list — so this catalog is
/// the only place the fact can come from, and that is why it reads the RAW configuration section rather
/// than the bound options: the bound options are precisely what the entry is missing from.
/// </para>
/// <para>
/// Each raw entry is bound on its own to find out whether it is one of the dropped ones. Binding a
/// single entry raises the failure the collection binder swallows, and the failure names the offending
/// configuration path and value itself — which is the line an operator acts on. Verified by measurement
/// on Microsoft.Extensions.* 10.0.11 rather than derived from documentation: the behaviour belongs to
/// another library's contract and may change with its version.
/// </para>
/// <para>
/// The snapshot boundary is taken from the reload token of the configuration itself rather than from
/// the monitor of the clients section. The monitor notifies its listeners only after re-binding AND
/// re-validating, so an edit that breaks a rule of the set never reaches a listener of it — and an edit
/// can do both at once, dropping one entry inside the binder while breaking a rule on what is left. The
/// reload token has no such gap: it is raised by the configuration source, before anything reads it.
/// </para>
/// </summary>
internal sealed class OidcClientEntryBindingCatalog : IConfigSnapshotCatalog
{
    /// <summary>
    /// Configuration key of the clients array — the section whose entries are walked, and the prefix an
    /// operator sees in the report.
    /// </summary>
    private const string ClientsSectionKey =
        OidcClientsOptions.SectionName + ":" + nameof(OidcClientsOptions.Clients);

    /// <summary>
    /// Raw configuration: the entries are read from it directly, because an entry dropped by the binder
    /// exists nowhere else. This is the instance the clients section is BOUND from — the one the
    /// integrator hands to <c>AddVeriqaAuthServer</c> — rather than whatever the container resolves for
    /// <see cref="IConfiguration"/>. A host that supplies a subsection would otherwise get from this
    /// catalog exactly the silence it exists to remove: a different root, no entries under the key, no
    /// findings.
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Creates the catalog of the client entries.
    /// </summary>
    /// <param name="configuration">
    /// The configuration instance the clients section is bound from (supplied at registration).
    /// </param>
    public OidcClientEntryBindingCatalog(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public ConfigLevel Level => ConfigLevel.Application;

    /// <inheritdoc />
    /// <remarks>
    /// The client entries live in the application configuration this catalog was handed, and no contour
    /// substitutes that source: the catalog is always the one that walks them.
    /// </remarks>
    public bool CanWalk => true;

    /// <inheritdoc />
    public string SectionKey => ClientsSectionKey;

    /// <inheritdoc />
    public string? ReadThroughSectionKey => null;

    /// <summary>
    /// No setting is covered: the unit of this catalog is the record, and a record that is gone takes
    /// every setting it stated with it rather than one.
    /// </summary>
    public IReadOnlyCollection<string> SettingKeys => [];

    /// <inheritdoc />
    public IDisposable? Subscribe(Action onSnapshot)
    {
        ArgumentNullException.ThrowIfNull(onSnapshot);

        return ChangeToken.OnChange(_configuration.GetReloadToken, onSnapshot);
    }

    /// <inheritdoc />
    public IEnumerable<ConfigSnapshotFinding> Walk()
    {
        foreach (var entry in _configuration.GetSection(ClientsSectionKey).GetChildren())
        {
            var reason = FailureOf(entry);

            if (reason is null)
            {
                continue;
            }

            // The ClientId is read as a raw value rather than taken from the bound entry: the entry is
            // the one that did not bind. It is absent when the entry does not state it at all — the
            // position then stays the only way to address the entry.
            var clientId = entry[nameof(OidcClientOptions.ClientId)];

            yield return new ConfigSnapshotFinding(
                ConfigSnapshotFact.RecordNotBound,
                Level,
                entry.Path,
                string.IsNullOrWhiteSpace(clientId) ? null : clientId,
                SettingKey: null,
                Value: null,
                reason);
        }
    }

    /// <summary>
    /// Binds one raw entry and returns why it could not be built; null when it builds.
    /// </summary>
    /// <param name="entry">Raw configuration section of the entry.</param>
    /// <returns>Reason the entry did not bind, or null.</returns>
    private static string? FailureOf(IConfigurationSection entry)
    {
        try
        {
            entry.Get<OidcClientOptions>();

            return null;
        }
        // Exactly the failure the collection binder swallows for this element. It is as wide as the
        // guard of OidcClientsOptionsAccessor.Current and for the same reason: a value that does not
        // convert to the declared type surfaces as an InvalidOperationException out of the binder, but
        // the guard must not depend on which exception a third-party binder chooses to raise for a
        // broken value. Cancellation is not a configuration failure and stays unhandled.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex.Message;
        }
    }
}
