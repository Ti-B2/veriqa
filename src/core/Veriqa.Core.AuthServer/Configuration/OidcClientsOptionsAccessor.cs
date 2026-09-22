// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// The single guarded entry point to the OIDC clients snapshot (SPEC-012 CFG-203, CFG-210).
/// <para>
/// <see cref="IOptionsMonitor{TOptions}.CurrentValue"/> re-BINDS the section and re-runs
/// <see cref="IValidateOptions{TOptions}"/> after every reload of a watched configuration source, and
/// the resulting exception is thrown straight out of the reading site — the core contour has no
/// exception-handler middleware, so one broken deployment-side edit turns into a 500 for the requests
/// of EVERY application, not just the broken one. Both halves of that read can fail and BOTH are
/// guarded here: a rule of the validator reports an <see cref="OptionsValidationException"/>, while a
/// value that does not convert to the declared property type (<c>"yes"</c> for a <c>bool?</c> or an
/// enum) fails earlier, inside the binder, as an <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// That binder failure reaches this guard only for a value stated at the ROOT of the section — the
/// deployment gate <see cref="OidcClientsOptions.CompatibilityQuirksAllowApplicationOverride"/> is the
/// one such value here. The same kind of value INSIDE an entry of the Clients array never reaches it:
/// the binder treats a failure while building an element of a collection as a failure of that element
/// alone, drops the entry and reports nothing, so the read succeeds on a set the entry is simply
/// missing from. Nothing on this path can guard what did not fail; the fact is reported instead, by
/// <see cref="Snapshot.OidcClientEntryBindingCatalog"/>, whose findings the snapshot report of the
/// configuration mechanism names.
/// </para>
/// <para>
/// This accessor keeps the last successfully validated snapshot and serves it while the current one
/// cannot be read. Consumers therefore observe the neighbours' configuration exactly as it was before
/// the failed reload instead of an exception. The floor is seeded at construction from the start-time
/// snapshot, so an edit that breaks the section before the first read here degrades to the start-time
/// configuration rather than to "no such client". A start-time failure is unaffected: ValidateOnStart
/// stops the process, which is the correct outcome for a deployment error.
/// </para>
/// </summary>
internal sealed class OidcClientsOptionsAccessor
{
    /// <summary>
    /// Empty snapshot served when a read fails and there is no snapshot to fall back on at all —
    /// neither an earlier successful read nor the start-time one. Consumers then see "no such client",
    /// which is their existing not-found behaviour, rather than an exception escaping into the request
    /// pipeline.
    /// </summary>
    private static readonly OidcClientsOptions EmptySnapshot = new();

    /// <summary>
    /// Live OIDC clients configuration.
    /// </summary>
    private readonly IOptionsMonitor<OidcClientsOptions> _clientsOptions;

    /// <summary>
    /// Logger of degradation facts.
    /// </summary>
    private readonly ILogger<OidcClientsOptionsAccessor> _logger;

    /// <summary>
    /// Snapshot served while the current configuration cannot be read: the start-time one until the
    /// first successful read, that read's result afterwards. Null only when even the start-time snapshot
    /// could not be read.
    /// </summary>
    private OidcClientsOptions? _lastValid;

    /// <summary>
    /// Creates the guarded accessor.
    /// </summary>
    /// <param name="clientsOptions">Live OIDC clients configuration.</param>
    /// <param name="startupSnapshot">Start-time configuration, used to seed the degradation floor.</param>
    /// <param name="logger">Logger of degradation facts.</param>
    public OidcClientsOptionsAccessor(
        IOptionsMonitor<OidcClientsOptions> clientsOptions,
        IOptions<OidcClientsOptions> startupSnapshot,
        ILogger<OidcClientsOptionsAccessor> logger)
    {
        _clientsOptions = clientsOptions;
        _logger = logger;

        // Seed the floor from the start-time snapshot rather than from the monitor: IOptions binds once,
        // at its first read (the client seeder makes it at host start), and never re-binds, so it still
        // carries the start-time configuration even while
        // the current one is broken. Seeding from the monitor would fail in exactly the window this
        // seeding exists to close — a broken edit landing before the first read of this accessor, after
        // which every consumer would treat every client as unknown for as long as the edit stays broken.
        _lastValid = TryReadStartupSnapshot(startupSnapshot);
    }

    /// <summary>
    /// Current OIDC clients snapshot, or the last valid one when the current configuration does not
    /// validate. Never throws.
    /// </summary>
    public OidcClientsOptions Current
    {
        get
        {
            try
            {
                var current = _clientsOptions.CurrentValue;

                // Publish the snapshot only after a successful read: a failed read must leave the
                // previous one in place, and readers on other threads must never observe a torn state.
                Volatile.Write(ref _lastValid, current);
                return current;
            }
            // Deliberately as wide as ConfigurationResolver.GetLayerValue: the read is a whole
            // bind-and-validate pass, and narrowing the guard to the validation half lets the binder
            // half through into the request pipeline — the exact 500 this accessor exists to prevent.
            // Cancellation is not a configuration failure and stays unhandled.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var lastValid = Volatile.Read(ref _lastValid);

                // The failure text of every rule carries the position and the ClientId of the offending
                // entry, so the log names the client together with the reason.
                if (lastValid is not null)
                {
                    _logger.LogError(
                        ex,
                        "OIDC clients configuration could not be read; serving the last valid snapshot. Reason: {Reason}",
                        DescribeFailure(ex));

                    return lastValid;
                }

                _logger.LogError(
                    ex,
                    "OIDC clients configuration could not be read and no snapshot is available to fall back on, "
                    + "not even the start-time one; treating every client as unknown. Reason: {Reason}",
                    DescribeFailure(ex));

                return EmptySnapshot;
            }
        }
    }

    /// <summary>
    /// Reads the start-time snapshot for the initial floor. The client seeder and the compatibility
    /// quirks startup report read it at host start, which binds and validates it, so the read normally
    /// returns that cached instance; the guard covers the case where nothing has read
    /// it yet and the current configuration is already broken — there is then no floor to seed, and the
    /// empty snapshot stays the answer until a read succeeds.
    /// </summary>
    /// <param name="startupSnapshot">Start-time configuration.</param>
    /// <returns>The start-time snapshot, or null when even it cannot be read.</returns>
    private OidcClientsOptions? TryReadStartupSnapshot(IOptions<OidcClientsOptions> startupSnapshot)
    {
        try
        {
            return startupSnapshot.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Start-time OIDC clients configuration could not be read; the accessor starts without a "
                + "fallback snapshot and treats every client as unknown until a read succeeds. Reason: {Reason}",
                DescribeFailure(ex));

            return null;
        }
    }

    /// <summary>
    /// Reduces a failed read to the line an operator acts on: the failures of every broken validation
    /// rule when the validator spoke, and the exception message otherwise (a binder failure names the
    /// offending path and value itself).
    /// </summary>
    private static string DescribeFailure(Exception ex) =>
        ex is OptionsValidationException validation
            ? string.Join("; ", validation.Failures)
            : ex.Message;
}
