// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;

namespace Veriqa.Core.TransactionEngine.Configuration;

/// <summary>
/// Resolver key registry for Transaction Engine settings (CFG-222).
/// Keys are declared declaratively with semantics (value/set/protective) and with the ADDRESS of every
/// level they live at (SPEC-012 §10.6), so the pair "key + level" is stated once — here.
/// </summary>
public static class TransactionEngineConfigKeys
{
    /// <summary>
    /// Catalog of the declarations of this registry — what the one registrar of the deployment declares
    /// and binds. It is initialized before the declarations that fill it, which is the order the
    /// initializers of this class run in.
    /// </summary>
    private static readonly ConfigKeyCatalog Declared = new();

    /// <summary>
    /// The engine settings AS THE PRODUCT SHIPS THEM — a default-constructed options object, read for
    /// one thing only: the value a core level yields when the section states nothing. The shipped value
    /// is taken off the options class rather than restated here, so the two cannot drift.
    /// </summary>
    private static readonly TransactionEngineOptions Shipped = new();

    /// <summary>
    /// Member of the OIDC client entry the per-application bot-rejection policy is stated at. It is
    /// written as text rather than taken from the entry's type: the entry belongs to the auth server,
    /// which references this assembly and not the other way round.
    /// </summary>
    private const string ClientRejectBots = "RejectBots";

    /// <summary>
    /// RejectBots (CFG-222): protective setting. The core default is true; the application — the only
    /// other level the key declares — can relax it only via a gate (CFG-212). "Stricter" = reject bots
    /// = logical OR of the values (true wins): a lower level without a gate cannot disable rejection.
    /// <para>
    /// The core level always states a value — the section's, or the shipped default — and carries the
    /// gate stated next to it in the same section. Both are the reason this key reads its levels
    /// through a hook: an unset core level would leave the resolution with the default of the type
    /// (false, bots accepted) where the deployment stated nothing at all.
    /// </para>
    /// </summary>
    public static ConfigKey<bool> RejectBots { get; } = Declared
        .Of<bool>("TransactionEngine.RejectBots")
        .At(
            ConfigLevel.Core,
            TransactionEngineOptions.SectionName + ":" + nameof(TransactionEngineOptions.RejectBots))
        .At(ConfigLevel.Application, ClientRejectBots)
        .ProtectiveCeiling(static (upper, lower) => upper || lower)
        .Parse(static (node, path, _) => ReadRejectBots(node, path))

        // The hook states the core level unconditionally — the section's value, the shipped default,
        // and the gate beside it — which no walk of the address can reproduce. The report names the
        // pairs instead of staying silent over a level that always speaks.
        .NotWalked()
        .Declare();

    /// <summary>
    /// Declarations of this registry — what the one registrar of the deployment declares and binds.
    /// </summary>
    public static ConfigKeyCatalog Catalog => Declared;

    /// <summary>
    /// Layer value of one level of the bot-rejection policy. The CORE level states the value together
    /// with the gate that may let the level above relax it (CFG-212); a level above states its own
    /// value or nothing, and the resolver then keeps the stricter one it already has.
    /// </summary>
    /// <param name="node">Subtree of the level's record.</param>
    /// <param name="path">Address the step resolved to.</param>
    /// <returns>Layer value of the level.</returns>
    private static LayerValue<bool> ReadRejectBots(ConfigNode node, string path)
    {
        var stated = node.TryRead<bool>(path, out var rejectBots);

        if (node.Level is not ConfigLevel.Core)
        {
            return stated ? LayerValue<bool>.Set(rejectBots) : LayerValue<bool>.None;
        }

        // The gate lives in the same section, next to the value it governs, and is read on every
        // resolution — a configuration reload reaches the next one.
        var gateAddress = GateAddress(path);
        var gate = node.TryRead<bool>(gateAddress, out var open) ? open : Shipped.RejectBotsAllowApplicationOverride;

        return LayerValue<bool>.Set(stated ? rejectBots : Shipped.RejectBots, gate);
    }

    /// <summary>
    /// Address of the gate standing next to the value inside the same section: the last segment of the
    /// address is replaced with the member that carries the permission.
    /// </summary>
    /// <param name="path">Address the step resolved to.</param>
    /// <returns>Address of the gate.</returns>
    private static string GateAddress(string path)
    {
        var separator = path.LastIndexOf(ConfigNode.PathSeparator);
        var gate = nameof(TransactionEngineOptions.RejectBotsAllowApplicationOverride);

        return separator < 0 ? gate : string.Concat(path.AsSpan(0, separator + 1), gate);
    }
}
