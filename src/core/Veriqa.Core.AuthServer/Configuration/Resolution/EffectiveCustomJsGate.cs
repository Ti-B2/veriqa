// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration.Resolution;

/// <summary>
/// The deployment gate of the custom sign-in page script
/// (<see cref="AuthPageDesignOptions.CustomJsAllowApplicationOverride"/>, SPEC-012 §4.3, CFG-212) AS
/// THE RESOLUTION SEES IT: read off the core record by address, through the very hook the resolution
/// reads that level with (<see cref="AuthServerConfigKeys.ReadCoreCustomJs"/>).
/// <para>
/// It exists because the same flag has two readings that do not always agree, and only one of them
/// decides whether a client's script is refused. The hook reads several members of this level at once
/// — the script path, its SRI hash, the gate — so a NEIGHBOURING member of it the read refuses leaves
/// the level answering as a WHOLE as over a record stating nothing (SPEC-012 CFG-212): no script, and
/// the shipped gate, which is closed — while the gate's own address stays perfectly readable and
/// answers as it is written. A reader that took the
/// flag by its own address would then see a deployment as permissive while every client is being
/// refused, which is the one thing the startup report over this gate exists to prevent.
/// </para>
/// </summary>
internal sealed class EffectiveCustomJsGate
{
    /// <summary>
    /// Raw configuration — the instance the sections are BOUND from, the one the integrator hands to
    /// <c>AddVeriqaAuthServer</c>, rather than whatever the container resolves for
    /// <see cref="IConfiguration"/>: a host that supplies a subsection would otherwise be served from a
    /// different root, where the global section is simply not there.
    /// </summary>
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Creates the reader of the effective gate.
    /// </summary>
    /// <param name="configuration">The configuration instance the sections are bound from.</param>
    public EffectiveCustomJsGate(IConfiguration configuration) =>
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <summary>
    /// The gate as the resolution of the custom script sees it.
    /// </summary>
    /// <returns>
    /// <c>true</c> — the owner of the global section permits an application to state a script of its
    /// own; <c>false</c> — the section leaves the gate closed; <c>null</c> — the section states a value
    /// this level cannot be read from, so the resolution answers the level as a record stating nothing,
    /// with the shipped gate, which is closed. The third
    /// answer is kept apart from the second because they send an operator to different places: a closed
    /// gate is opened where it is written, while an unreadable level is a value elsewhere in the same
    /// section that has to be repaired first. It is reachable in a running deployment and not only in
    /// theory: a subtree written where the script path or its SRI hash belongs is refused by the read
    /// of this level and no longer stops the host on its way in, because the section is not bound into
    /// an options class any more (see <see cref="Configuration.VeriqaOptions"/>).
    /// </returns>
    /// <remarks>
    /// The node is built the way the reader of a core level builds it — over the SECTION the absolute
    /// address opens with, the rest of that address read inside it — and it is rebuilt on every call
    /// rather than captured, so a reload of the configuration reaches the next question.
    /// <para>
    /// A failed read is not a fallback of this type: it is the same outcome the resolution arrives at.
    /// Whatever the failure, the resolution answers this level as over a record stating nothing, and the
    /// gate stated there is the shipped one, which is closed — so <c>null</c> never reads as a gate the
    /// resolution holds open. The guard is as wide as the one the resolution reads a level
    /// behind, for the same reason: the reading path spans the configuration binder and the type
    /// converter of the platform, so the set of types a badly shaped section can arrive as is not one
    /// this boundary can hold a list of. The value itself is named by the read that found it, key and
    /// address included, when the resolution reaches the same level. Cancellation is not a failure of a
    /// value and travels on.
    /// </para>
    /// </remarks>
    public bool? Read()
    {
        var separator = AuthServerConfigKeys.CoreCustomJsAddress.IndexOf(ConfigNode.PathSeparator);
        var node = ConfigNode.Over(
            _configuration.GetSection(AuthServerConfigKeys.CoreCustomJsAddress[..separator]),
            ConfigLevel.Core,
            recordLabel: null);

        try
        {
            return AuthServerConfigKeys
                .ReadCoreCustomJs(node, AuthServerConfigKeys.CoreCustomJsAddress[(separator + 1)..])
                .AllowLowerOverride;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return null;
        }
    }
}
