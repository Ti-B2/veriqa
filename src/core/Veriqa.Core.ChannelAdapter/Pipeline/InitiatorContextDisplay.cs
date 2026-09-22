// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.Configuration;
using Veriqa.Core.TransactionEngine.Configuration;
using Veriqa.Core.TransactionEngine.Domain;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// The display decision over the initiator context of a transaction (SPEC-017 ICC-081): whether the
/// context is shown at all, and which of its personal fields are. <c>Enabled</c> and
/// <c>DisplayFields</c> are resolved per application over the ownership of the transaction, and read
/// here and nowhere else, so the question of a sign-in and the receipt of its outcome never disagree on
/// what may be shown — a field hidden in the question is not revealed in the answer.
/// <para>
/// A helper and deliberately not a seam: the narrowing is stated by the keys' own declarations and
/// applied by the canonical resolver, exactly as <c>ChannelsEnabledResolution</c> reads its capability.
/// </para>
/// <para>
/// The application name is not a field of this decision: it is the attribution of the transaction and
/// not a personal datum of its initiator, so neither the flag nor the field set touches it.
/// </para>
/// </summary>
/// <param name="Enabled">Whether the initiator context is shown at all.</param>
/// <param name="ShownFields">Fields of <see cref="InitiatorContextFieldTable"/> that are shown; null —
/// none (the <c>default</c> decision).</param>
internal readonly record struct InitiatorContextDisplay(
    bool Enabled,
    IReadOnlyList<InitiatorContextFieldRow>? ShownFields)
{
    /// <summary>
    /// The field set a core level contributes when nothing answers at all — the set the product ships,
    /// which is the declared default of the key.
    /// </summary>
    private static readonly IReadOnlyList<string> ShippedDisplayFields =
        new InitiatorContextOptions().DisplayFields;

    /// <summary>
    /// Resolves the display decision over the ownership of a transaction.
    /// </summary>
    /// <param name="resolver">Canonical configuration resolver.</param>
    /// <param name="ownership">Ownership context of the transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The display decision; a disabled one shows no field.</returns>
    public static async ValueTask<InitiatorContextDisplay> ResolveAsync(
        IConfigurationResolver resolver,
        ResolutionContext ownership,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(ownership);

        // Enabled controls specifically the DISPLAY of the context: when false for the application, the
        // context is not shown (the snapshot is collected separately), and the field set is not asked.
        var enabled = (await resolver.ResolveAsync(
            InitiatorContextConfigKeys.Enabled, ownership, ConfigDimensionValues.None, cancellationToken)).Value;
        if (!enabled)
        {
            return default;
        }

        // The resolver always returns at least the core level (the key declares a default); safeguard
        // against a resolution that answers nothing.
        var displayFields = (await resolver.ResolveAsync(
            InitiatorContextConfigKeys.DisplayFields, ownership, ConfigDimensionValues.None, cancellationToken)).Value
            ?? ShippedDisplayFields;

        var shownFields = InitiatorContextFieldTable.Rows
            .Where(row => displayFields.Contains(row.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return new InitiatorContextDisplay(Enabled: true, ShownFields: shownFields);
    }

    /// <summary>
    /// The snapshot with the members of the fields this decision hides cleared. A member no field of
    /// <see cref="InitiatorContextFieldTable"/> owns is kept as collected.
    /// </summary>
    /// <param name="snapshot">Initiator context as collected.</param>
    /// <returns>The snapshot as it may be shown.</returns>
    public InitiatorContextSnapshot Apply(InitiatorContextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return InitiatorContextFieldTable.Filter(snapshot, ShownFields ?? []);
    }
}
