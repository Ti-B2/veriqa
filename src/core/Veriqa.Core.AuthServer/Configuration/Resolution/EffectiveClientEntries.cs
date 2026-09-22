// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration.Resolution;

/// <summary>
/// The one walk of the OIDC client entries of the <see cref="ConfigLevel.Application"/> level: the RAW
/// entries of the clients section, restricted to the EFFECTIVE set of clients (SPEC-012 §10.6,
/// CFG-210). Everything that reads the entries of this level reads them through here — the node reader
/// answering one resolution context (<see cref="ClientConfigNodeReader"/>), the walker handing the
/// snapshot report all of them (<see cref="ClientConfigNodeWalker"/>) and the startup report on the
/// refused sign-in page script (<c>AuthPageCustomJsStartupDiagnosticsService</c>).
/// <para>
/// Both halves are needed to keep every reader over ONE set of entries: reading the raw SECTION is what
/// reaches a value that has no property to be bound into, and asking the guarded SNAPSHOT is what
/// leaves out an entry the binder dropped or a rule of the section rejected — such an entry states
/// nothing to the resolution of this level, so nothing above may see it either.
/// </para>
/// <para>
/// The effective set is taken ONCE per walk rather than per entry: the snapshot behind it is one and
/// the same for the whole walk, and asking it per entry would re-bind the section as many times as
/// there are entries, a cost paid at the start of a deployment.
/// </para>
/// <para>
/// The catalog of the entries the binder DROPPED (<c>OidcClientEntryBindingCatalog</c>) deliberately
/// does not read through here: its unit is the record that is missing from the effective set, and a
/// walk restricted to that set would throw away the very entries the catalog exists to name.
/// </para>
/// </summary>
internal sealed class EffectiveClientEntries
{
    /// <summary>
    /// Configuration key of the clients array — the section the entries are read from.
    /// </summary>
    private const string ClientsSectionKey =
        OidcClientsOptions.SectionName + ":" + nameof(OidcClientsOptions.Clients);

    /// <summary>
    /// Raw configuration — the instance the clients section is BOUND from, the one the integrator hands
    /// to <c>AddVeriqaAuthServer</c>, rather than whatever the container resolves for
    /// <see cref="IConfiguration"/>: a host that supplies a subsection would otherwise be served from a
    /// different root, where the entries are simply not there.
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Guarded access to the OIDC clients snapshot — the EFFECTIVE set of entries, which decides WHICH
    /// raw entries are handed out.
    /// </summary>
    private readonly OidcClientsOptionsAccessor _clients;

    /// <summary>
    /// Creates the walk of the client entries.
    /// </summary>
    /// <param name="configuration">The configuration instance the clients section is bound from.</param>
    /// <param name="clients">Guarded access to the OIDC clients snapshot.</param>
    public EffectiveClientEntries(IConfiguration configuration, OidcClientsOptionsAccessor clients)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
    }

    /// <summary>
    /// Walks the raw entries of the effective set, in the order the section states them.
    /// </summary>
    /// <returns>
    /// A node over every raw entry whose client identifier is part of the effective set, labelled with
    /// that identifier. An entry stating no <see cref="OidcClientOptions.ClientId"/> is left out: there
    /// is nothing to match it against the effective set with.
    /// </returns>
    public IEnumerable<ConfigNode> Walk()
    {
        var effective = EffectiveClientIds();

        foreach (var entry in _configuration.GetSection(ClientsSectionKey).GetChildren())
        {
            var clientId = entry[nameof(OidcClientOptions.ClientId)];

            // An entry outside the effective set states nothing to the resolution of this level, so it
            // states nothing to a reader or a report of that level either (CFG-210).
            if (clientId is null || !effective.Contains(clientId))
            {
                continue;
            }

            // The label of the record is the client identifier — an entry of an ARRAY is addressed in
            // the configuration by its POSITION, so the sign an operator recognizes it by travels
            // beside the address rather than inside it.
            yield return ConfigNode.Over(entry, ConfigLevel.Application, clientId);
        }
    }

    /// <summary>
    /// The entry one application is addressed by, or null when this level states none for it. It is the
    /// same walk narrowed to one entry rather than a second reading of the section: a point question
    /// answered by another path is exactly what would let the two answers differ.
    /// </summary>
    /// <param name="applicationId">Client identifier the resolution context names.</param>
    /// <returns>Node over the entry, or null when the effective set holds no such entry.</returns>
    public ConfigNode? Find(string applicationId) =>
        Walk().FirstOrDefault(
            node => string.Equals(node.RecordLabel, applicationId, StringComparison.Ordinal));

    /// <summary>
    /// Client identifiers of the effective set — the entries the resolution of this level reads.
    /// </summary>
    /// <returns>Client identifiers of the effective entries.</returns>
    private HashSet<string> EffectiveClientIds()
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var client in _clients.Current.Clients)
        {
            if (!string.IsNullOrEmpty(client.ClientId))
            {
                identifiers.Add(client.ClientId);
            }
        }

        return identifiers;
    }
}
