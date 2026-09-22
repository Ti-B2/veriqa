// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.Domain;
using Veriqa.Core.TransactionEngine.Domain;
using Veriqa.Core.TransactionEngine.MessageTemplates;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// The one place the slot values of a transaction are assembled (SPEC-036 TPL-124): the caller values
/// the contract of a message admits, then the server values the core states about the transaction —
/// the application, the displayable fields of the initiator context and the moment of the outcome. The
/// question of a confirmation, the outcome receipt, the sign-in prompt of a messenger and the sign-in
/// mail ask the same question of the same transaction, so which value fills which server slot is
/// decided here once.
/// <para>
/// A best-effort value that is absent is left out of the map rather than passed as an empty value: the
/// absence is what makes the template ladder degrade onto a shorter variant (TPL-031), while an empty
/// string would keep the fullest one and print a gap.
/// </para>
/// </summary>
internal static class TransactionSlotValues
{
    /// <summary>
    /// The sanitizer the fields of <see cref="InitiatorContextFieldTable"/> read their values through.
    /// </summary>
    private static readonly Func<string?, string?> Sanitize =
        static value => MessageValueSanitizer.Sanitize(value);

    /// <summary>
    /// Builds the slot values of a message reporting a transaction.
    /// </summary>
    /// <remarks>
    /// Everything the render point knows is supplied, and nothing decides here whether it reaches the
    /// text: <see cref="MessageTextRenderer"/> walks the CONTRACT rather than the values, so a value of a
    /// slot the contract does not declare changes no text.
    /// <para>
    /// A caller value is taken only for a slot the contract declares with the CALLER source, and it is
    /// READ into the type that slot declares (<see cref="StoredSlotValue.Read"/>): the snapshot stores the
    /// canonical textual form of the declared type, while the render takes typed values. The server
    /// values are written last, so a value of the calling party never fills a slot the core states.
    /// </para>
    /// </remarks>
    /// <param name="source">What the render point knows about the transaction; null — nothing, and no
    /// transaction value is stated at all.</param>
    /// <param name="contract">Contract of the message being rendered.</param>
    /// <param name="outcomeMoment">Moment the transaction ended; null when the render point states
    /// none.</param>
    /// <returns>Slot name → value.</returns>
    public static Dictionary<string, object?> Of(
        TransactionSlotSource? source,
        MessageContract contract,
        DateTimeOffset? outcomeMoment)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (source?.CallerSlotValues is { } callerSlotValues)
        {
            foreach (var (name, value) in callerSlotValues)
            {
                if (contract.FindSlot(name) is { IsCaller: true } slot)
                {
                    values[name] = StoredSlotValue.Read(slot, value);
                }
            }
        }

        if (source is not null)
        {
            values[SlotNames.App] = ApplicationOf(source);

            if (source.InitiatorContext is { } initiator)
            {
                foreach (var field in InitiatorContextFieldTable.Rows)
                {
                    Add(values, field.SlotName, field.SnapshotValue(initiator, Sanitize));
                }
            }
        }

        if (outcomeMoment is not null)
        {
            values[SlotNames.OutcomeAt] = outcomeMoment.Value;
        }

        return values;
    }

    /// <summary>
    /// Builds the slot values of the initiator details a sign-in message shows — the details the display
    /// decision already let through (SPEC-017 ICC-081).
    /// </summary>
    /// <param name="details">Confirmation context carrying the initiator details.</param>
    /// <returns>Slot name → value, for the fields that have one.</returns>
    public static Dictionary<string, object?> Of(DetailedConfirmationPromptContext details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [SlotNames.App] = ApplicationNameOf(details.ClientApplicationName),
        };

        foreach (var field in InitiatorContextFieldTable.Rows)
        {
            Add(values, field.SlotName, field.PromptValue(details, Sanitize));
        }

        return values;
    }

    /// <summary>
    /// The value of the application slot, by the type of the transaction (SPEC-036 TPL-124): a
    /// confirmation names the application by its attribution (TPL-102); any other transaction names it
    /// by the human-readable name its initiator context carries, and by the attribution when it has no
    /// initiator context.
    /// </summary>
    /// <remarks>
    /// Always there, an empty string when the source states no value: the prompt has always put the
    /// application slot in that way, and the minimal variant of a wording may rest on it.
    /// </remarks>
    /// <param name="source">What the render point knows about the transaction.</param>
    /// <returns>The value of the application slot.</returns>
    private static string ApplicationOf(TransactionSlotSource source)
    {
        if (string.Equals(source.TransactionType, TransactionTypes.Confirmation, StringComparison.Ordinal))
        {
            return source.ApplicationId ?? string.Empty;
        }

        return source.InitiatorContext is { } initiator
            ? ApplicationNameOf(initiator.ClientApplicationName)
            : source.ApplicationId ?? string.Empty;
    }

    /// <summary>
    /// The application name of an initiator context, sanitized; an empty string when it is blank.
    /// </summary>
    /// <param name="name">Application name the initiator context carries.</param>
    /// <returns>The value of the application slot.</returns>
    private static string ApplicationNameOf(string? name) =>
        MessageValueSanitizer.Sanitize(name) ?? string.Empty;

    /// <summary>
    /// Adds a best-effort value when there is one.
    /// </summary>
    /// <param name="values">Accumulator of the slot values.</param>
    /// <param name="name">Slot name.</param>
    /// <param name="value">Sanitized value, or null when the field is absent.</param>
    private static void Add(Dictionary<string, object?> values, string name, string? value)
    {
        if (value is not null)
        {
            values[name] = value;
        }
    }
}
