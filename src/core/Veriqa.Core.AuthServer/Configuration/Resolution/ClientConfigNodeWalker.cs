// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration.Resolution;

/// <summary>
/// Walker of the OIDC client entries — the records of the <see cref="ConfigLevel.Application"/> level
/// (SPEC-012 §10.6). It stands next to <see cref="ClientConfigNodeReader"/> and hands out the same
/// nodes it does: the reader answers a resolution context with ONE entry, this one enumerates them all
/// for the walk of a configuration snapshot. Both take them from the same
/// <see cref="EffectiveClientEntries"/>, which is what keeps the walk and the resolution over ONE set
/// of entries — the raw section restricted to the effective set (CFG-210).
/// <para>
/// The accessor behind that walk never throws and serves the last snapshot that did validate, so a
/// reload breaking a rule of this section leaves the walk on that snapshot instead of naming the
/// section unreadable. The monitor is taken for the snapshot boundary alone — it announces a reload
/// after re-binding and re-validating the section, which is when the walk is repeated.
/// </para>
/// </summary>
internal sealed class ClientConfigNodeWalker : IConfigNodeWalker
{
    /// <summary>
    /// Live OIDC clients configuration — subscribed to for the snapshot boundary.
    /// </summary>
    private readonly IOptionsMonitor<OidcClientsOptions> _monitor;

    /// <summary>
    /// The one walk of the client entries of this level: the raw entries of the effective set.
    /// </summary>
    private readonly EffectiveClientEntries _entries;

    /// <summary>
    /// Creates the walker of the client entries.
    /// </summary>
    /// <param name="monitor">Live OIDC clients configuration.</param>
    /// <param name="entries">The walk of the client entries of the effective set.</param>
    public ClientConfigNodeWalker(
        IOptionsMonitor<OidcClientsOptions> monitor,
        EffectiveClientEntries entries)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    /// <inheritdoc />
    public ConfigLevel Level => ConfigLevel.Application;

    /// <inheritdoc />
    /// <remarks>
    /// The entries live in the application configuration of the host, which no contour replaces: this
    /// level's source is the same one in every deployment that has the auth server at all.
    /// </remarks>
    public bool CanWalk => true;

    /// <inheritdoc />
    public string SectionKey => OidcClientsOptions.SectionName;

    /// <inheritdoc />
    public IDisposable? Subscribe(Action onSnapshot)
    {
        ArgumentNullException.ThrowIfNull(onSnapshot);

        return _monitor.OnChange(_ => onSnapshot());
    }

    /// <inheritdoc />
    public IEnumerable<ConfigNode> Walk() => _entries.Walk();
}
