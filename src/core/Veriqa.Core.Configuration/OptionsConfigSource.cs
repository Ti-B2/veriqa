// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// The application-configuration source: files, environment variables, User Secrets, Key Vault — all
/// of them providers of <c>IConfiguration</c> behind Options. It is ONE source serving several levels
/// at once; which of them it serves in a given contour is stated by the registration of that contour
/// (a host without the auth server declares <c>{ Core }</c>, a host with it adds the application and
/// <c>ui_config</c> levels).
/// <para>
/// The source is LIVE: freshness is guaranteed by the configuration provider, so caching its levels
/// would only hide a hot reload. The demo sandbox does not introduce a source of its own — it swaps
/// the store behind the binding of the <c>UiConfig</c> level, and the source stays this one.
/// </para>
/// </summary>
internal sealed class OptionsConfigSource : IConfigSource
{
    /// <summary>
    /// Levels declared by the registrations of this contour. Mutable through
    /// <see cref="DeclareLevels"/> alone, and only while DI is being composed — by the time the map
    /// of the contour is built the set is final.
    /// </summary>
    private readonly HashSet<ConfigLevel> _levels = [];

    /// <inheritdoc />
    public string Name => "Options";

    /// <inheritdoc />
    public IReadOnlySet<ConfigLevel> Levels => _levels;

    /// <inheritdoc />
    public bool IsLive => true;

    /// <summary>
    /// Declares that this contour serves the listed levels from the application configuration. The
    /// call is idempotent and UNIONS the sets, so the result does not depend on the order in which the
    /// DI extensions were called.
    /// </summary>
    /// <param name="levels">Levels served from the application configuration.</param>
    public void DeclareLevels(params ConfigLevel[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        foreach (var level in levels)
        {
            _levels.Add(level);
        }
    }
}
