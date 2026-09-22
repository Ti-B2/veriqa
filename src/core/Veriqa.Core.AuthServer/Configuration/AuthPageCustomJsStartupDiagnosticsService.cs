// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.ChannelAdapter.UI;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Startup report on the client entries whose custom sign-in page script is refused by the closed
/// deployment gate <see cref="AuthPageDesignOptions.CustomJsAllowApplicationOverride"/> (SPEC-012
/// §4.3, CFG-031/CFG-212).
/// <para>
/// The risky customization mode is enabled in two steps — the entry names the script AND the owner of
/// the global section opens the gate — so an operator who only did the first step would otherwise get
/// a sign-in page without the script and no way to tell why. Hence the Warning level and one record
/// per start; a deployment where no entry names a script, or where the gate is open, stays quiet.
/// </para>
/// <para>
/// The gate is read here rather than the effective script of the resolver: unlike the client
/// compatibility quirks, the refusal this report explains comes from the level that CARRIES the gate —
/// it states the gate closed, or states nothing at all — so resolving the script once per client could
/// only restate that, at a resolution per client. Naming the right entry rests on the uniqueness of
/// <see cref="OidcClientOptions.ClientId"/> (SPEC-012 CFG-155): the entry this report names is the one
/// the resolver reads, because a set where two entries claim the same ClientId does not start.
/// </para>
/// <para>
/// It is the gate the RESOLUTION reads, off the record by address (<see cref="EffectiveCustomJsGate"/>),
/// and not the flag bound into the options class: the two part exactly where the read refuses a member
/// of the global section, and a report that judged by the bound flag would fall silent in the one
/// deployment where every client is being refused (SPEC-012 CFG-212). Because the level then resolves
/// with the shipped closed gate rather than the one the section states, the report tells "the owner
/// closed it" apart from "the section could not be read": the two send an operator to different places.
/// </para>
/// </summary>
internal sealed class AuthPageCustomJsStartupDiagnosticsService : IHostedService
{
    /// <summary>
    /// Name of the deployment gate as it is spelled in configuration (named in the log so the operator
    /// sees which key to open).
    /// </summary>
    private const string GateConfigurationKey =
        AuthPageDesignOptions.SectionName + ":" + nameof(AuthPageDesignOptions.CustomJsAllowApplicationOverride);

    /// <summary>
    /// The one walk of the client entries — how the script of an application is read now that the entry
    /// carries no property for it: by the address the key declares, inside the entry the resolution
    /// itself reads (SPEC-012 §10.6). Walking the same entries the resolution does is what keeps this
    /// report naming the entry the resolver would take, and walking them ONCE is what keeps the cost of
    /// this report independent of how many clients the deployment states.
    /// </summary>
    private readonly EffectiveClientEntries _entries;

    /// <summary>
    /// The deployment gate as the resolution of the script sees it, read live so the report reflects
    /// the configuration as it stands at start.
    /// </summary>
    private readonly EffectiveCustomJsGate _gate;

    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<AuthPageCustomJsStartupDiagnosticsService> _logger;

    /// <summary>
    /// Creates the startup report service.
    /// </summary>
    /// <param name="entries">The walk of the client entries of the effective set.</param>
    /// <param name="gate">The deployment gate as the resolution sees it.</param>
    /// <param name="logger">Logger.</param>
    public AuthPageCustomJsStartupDiagnosticsService(
        EffectiveClientEntries entries,
        EffectiveCustomJsGate gate,
        ILogger<AuthPageCustomJsStartupDiagnosticsService> logger)
    {
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var gate = _gate.Read();

        // An open gate means every configured entry is honored — the ClientId that addresses it is
        // unique or the host would not have started — so there is nothing to report.
        if (gate is true)
        {
            return Task.CompletedTask;
        }

        var suppressed = new List<string>();

        // ONE pass over the section for the whole report: the entries of the effective set are walked
        // once, instead of asking the level for the entry of every client in turn.
        foreach (var entry in _entries.Walk())
        {
            // The walk labels every entry it hands out with the ClientId it matched against the
            // effective set, so the report names the entry by the identity the resolution addresses it
            // by rather than by its position in the array.
            if (entry.RecordLabel is { } clientId && StatesScript(entry, clientId))
            {
                suppressed.Add(clientId);
            }
        }

        if (suppressed.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Which of the two the operator is in decides where they go next: a gate the owner left closed
        // is opened where it is written, while a global section the read refuses is a value elsewhere in
        // that section to be repaired first — and until it is, the section states no script either, so
        // these clients get none at all.
        if (gate is false)
        {
            _logger.LogWarning(
                "A custom sign-in page script is configured but NOT in effect for {Clients}: the deployment gate {Gate} is closed, so the script of the global section (or none at all) applies to these clients",
                string.Join(", ", suppressed),
                GateConfigurationKey);
        }
        else
        {
            _logger.LogWarning(
                "A custom sign-in page script is configured but NOT in effect for {Clients}: the global section {Section} states a value that cannot be read, so that level answers as a record stating nothing — no script, and the deployment gate {Gate} closed as shipped — and these clients get no script at all; WHICH member of that section it is this report does not know, and no warning of the start names it either — the read that refuses the value names it when the sign-in page first resolves the script",
                string.Join(", ", suppressed),
                AuthPageDesignOptions.SectionName,
                GateConfigurationKey);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether a client entry names a script of its own. The address is the one the key declares for
    /// the application level, so a report and a resolution never look at different members.
    /// <para>
    /// A value the read cannot take — a path, or the SRI hash beside it, stated as a section where the
    /// setting is a text — leaves the answer at "the entry states no script", which is what the same
    /// value does to the resolution itself: the pair is ONE value, so a member of it the read refuses
    /// leaves the WHOLE level of this entry unset and the script comes from the level below (SPEC-012
    /// CFG-210/CFG-240). The entry therefore has no script the closed gate could be suppressing, and
    /// the unreadable value itself is reported by the read that found it, key and address included.
    /// This read happens inside the start of a hosted service, so a failure let out would stop the
    /// deployment over an entry the resolution merely skips.
    /// </para>
    /// </summary>
    /// <param name="entry">Node over the client entry.</param>
    /// <param name="clientId">Client identifier of the entry, for the log of a failed read.</param>
    /// <returns><c>true</c> when the entry states a script path.</returns>
    private bool StatesScript(ConfigNode entry, string clientId)
    {
        try
        {
            return CorePageDesignNode.CustomResource(
                entry,
                nameof(AuthPageDesignOptions.CustomJsPath),
                nameof(AuthPageDesignOptions.JsSriHash),
                nameof(AuthPageDesignOptions.JsSriHash)).HasValue;
        }
        // As wide as the guard the resolution reads a level's value behind: the reading path spans the
        // configuration binder and the type converter of the platform, so the set of types a badly
        // shaped entry can arrive as is not one this boundary can hold a list of. Cancellation is not a
        // failure of a value and travels on.
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            _logger.LogWarning(
                failure,
                "The custom sign-in page script of client {ClientId} could not be read and is left out of this report; the resolution leaves the level of this entry unset for such a value as well",
                clientId);

            return false;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
