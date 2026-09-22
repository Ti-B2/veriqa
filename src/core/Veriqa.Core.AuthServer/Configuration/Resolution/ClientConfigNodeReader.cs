// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration.Resolution;

/// <summary>
/// Reader of the OIDC client entry AS A NODE — the record of the <see cref="ConfigLevel.Application"/>
/// level addressed by path instead of by a property of <see cref="OidcClientOptions"/> (SPEC-012
/// CFG-225, §10.6). It is the ONLY reader of this level: every setting an application states is read
/// at the address its key declares, whether or not the entry happens to carry a property of that
/// name — a typed reader beside it would be a second way to read one record.
/// <para>
/// The entry comes from <see cref="EffectiveClientEntries"/> — the one walk of this level: the raw
/// section, because the address of such a setting exists only there and nothing binds it into a
/// property, restricted to the effective set, because an entry the binder dropped or a reload rejected
/// states nothing (CFG-210) and its settings must come from the levels below.
/// </para>
/// <para>
/// The reader is a singleton (it ends up inside the singleton binding registry) and holds only that
/// walk, itself a singleton over the configuration: no dependency with a narrower lifetime is captured.
/// </para>
/// <para>
/// It states no liveness of its own (<see cref="IConfigRecordReader.IsLive"/> stays null): the entries
/// live in the application configuration, and whether that configuration refreshes itself is a
/// property of the SOURCE serving the level rather than of a reader standing over it.
/// </para>
/// </summary>
internal sealed class ClientConfigNodeReader : IConfigRecordReader<ConfigNode>
{
    /// <summary>
    /// The one walk of the client entries of this level, narrowed here to the entry a resolution
    /// context names.
    /// </summary>
    private readonly EffectiveClientEntries _entries;

    /// <summary>
    /// Creates the node reader of the OIDC client entry.
    /// </summary>
    /// <param name="entries">The walk of the client entries of the effective set.</param>
    public ClientConfigNodeReader(EffectiveClientEntries entries)
    {
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    /// <inheritdoc />
    public string Name => "ClientNode";

    /// <inheritdoc />
    public ValueTask<ConfigNode?> ReadAsync(
        ResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The application level applies only when the context names an application.
        if (string.IsNullOrEmpty(context.ApplicationId))
        {
            return ValueTask.FromResult<ConfigNode?>(null);
        }

        // Whether the entry was REJECTED as a record (CFG-246) is not asked here: the resolver asks the
        // snapshot before it reads the level at all, and a second place deciding the fate of a record is
        // exactly what would let the two answers differ. An entry outside the effective set is answered
        // by the walk itself — the level is then unset and the resolution drops through to the levels
        // below, which is what "this client's entry is invalid" means for a setting (CFG-210).
        return ValueTask.FromResult(_entries.Find(context.ApplicationId));
    }
}
