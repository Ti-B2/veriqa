// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.RegularExpressions;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Declaration of a single typed placeholder slot (SPEC-036 §4.1). Immutable once built. Illegal
/// combinations are impossible to construct: the factory <see cref="Create"/> guards the name syntax
/// (TPL-005), the source/attribute pairing (<c>required</c> only for caller, <c>guaranteed</c> only for
/// server, a Natural Key only for a localized string) and the presence of the mandatory constraints for
/// the type (TPL-111(c)/(d)).
/// </summary>
public sealed partial class SlotDeclaration
{
    /// <summary>
    /// Slot-name syntax (TPL-005): snake_case, ASCII, starting with a letter — <c>[a-z][a-z0-9_]*</c>.
    /// Numeric names (<c>{0}</c>, <c>{1}</c> …) are excluded, avoiding a clash with the legacy
    /// positional <c>string.Format</c> during migration.
    /// </summary>
    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SlotNamePattern();

    private SlotDeclaration(
        string name,
        SlotType type,
        SlotSource source,
        bool required,
        bool guaranteed,
        SlotConstraints constraints,
        string? naturalKey)
    {
        Name = name;
        Type = type;
        Source = source;
        Required = required;
        Guaranteed = guaranteed;
        Constraints = constraints;
        NaturalKey = naturalKey;
    }

    /// <summary>Slot name (TPL-005). The template token is <c>"{" + Name + "}"</c>.</summary>
    public string Name { get; }

    /// <summary>Slot type — the validation and render contract (TPL-010).</summary>
    public SlotType Type { get; }

    /// <summary>Where the value comes from (server sources vs caller).</summary>
    public SlotSource Source { get; }

    /// <summary>
    /// Whether a caller must supply this value (only caller slots, TPL-022). A missing required caller
    /// value fails transaction creation with <c>slot_required_missing</c>.
    /// </summary>
    public bool Required { get; }

    /// <summary>
    /// Whether a server slot is guaranteed to have a value (only server slots, fixed by the core, §4.1).
    /// Used only to validate the minimal template variant (TPL-111(e)/TPL-032), not for fail-fast.
    /// </summary>
    public bool Guaranteed { get; }

    /// <summary>Type-specific constraints.</summary>
    public SlotConstraints Constraints { get; }

    /// <summary>
    /// Natural Key the value of a <see cref="SlotSource.LocalizedText"/> slot is the translation of;
    /// null for every other source. The key is named by the CONTRACT, so which phrase a slot stands for
    /// is decided where the contract is written and never by whoever states a template variant.
    /// </summary>
    public string? NaturalKey { get; }

    /// <summary>Whether the slot value is supplied by the calling party (RP).</summary>
    public bool IsCaller => Source is SlotSource.Caller;

    /// <summary>
    /// Whether a value is ALWAYS there in a message of the given kind: a server slot the product
    /// guarantees, or a caller slot the contract makes mandatory — except in an outcome receipt, where
    /// a caller slot is never guaranteed whatever its <see cref="Required"/> says (SPEC-036 TPL-123),
    /// and a server slot is guaranteed only when it is a localized string (TPL-124).
    /// It is the single question the MINIMAL variant of a ladder is judged by (TPL-111(e)/TPL-032) —
    /// the floor of the degradation may rest on nothing else, or its token reaches a user as a literal.
    /// The question is asked twice, at startup over the declarations that exist there and again at
    /// render time over the ones that do not, so it lives on the declaration itself: two spellings of
    /// it would part company the moment the notion of "guaranteed" moves.
    /// </summary>
    /// <remarks>
    /// The kind takes part because the guarantee of a caller slot is made where its value is ACCEPTED,
    /// and that is the contract of the confirmation message of the transaction's action type, not the
    /// contract of a receipt: a required caller slot of a receipt is checked against nothing at
    /// creation, and one receipt kind answers sign-in transactions, which carry no caller values at
    /// all, as well as confirmations.
    /// <para>
    /// A server slot of a receipt is judged by its source rather than by <see cref="Guaranteed"/> for
    /// the same reason: each server value a receipt may carry is missing on at least one of its branches
    /// — a receipt without a transaction, a render point that states no moment, a confirmation that has
    /// no initiator context (SPEC-036 TPL-124). A localized string alone is there on every branch.
    /// </para>
    /// </remarks>
    /// <param name="kind">Identifier of the message kind the contract declaring this slot serves.</param>
    /// <returns><c>true</c> — the slot always carries a value in a message of that kind.</returns>
    public bool IsAlwaysPresentIn(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);

        if (MessageKinds.IsOutcomeReceipt(kind))
        {
            return Source is SlotSource.LocalizedText;
        }

        return IsCaller ? Required : Guaranteed;
    }

    /// <summary>
    /// Builds a slot declaration, guarding every invariant. Throws <see cref="ArgumentException"/> for a
    /// malformed name, an illegal source/attribute pairing, or missing mandatory constraints — the core
    /// registry is correct by construction, while the configuration surface validates fields itself
    /// before calling this so no user input reaches an exception path.
    /// </summary>
    /// <param name="name">Slot name (TPL-005).</param>
    /// <param name="type">Slot type.</param>
    /// <param name="source">Value source.</param>
    /// <param name="required">Required flag (caller only).</param>
    /// <param name="guaranteed">Guaranteed flag (server only; a localized string is guaranteed anyway).</param>
    /// <param name="constraints">Type-specific constraints (null → <see cref="SlotConstraints.None"/>).</param>
    /// <param name="naturalKey">Natural Key the value is the translation of
    /// (<see cref="SlotSource.LocalizedText"/> only; mandatory there and illegal elsewhere).</param>
    /// <returns>The immutable slot declaration.</returns>
    public static SlotDeclaration Create(
        string name,
        SlotType type,
        SlotSource source,
        bool required = false,
        bool guaranteed = false,
        SlotConstraints? constraints = null,
        string? naturalKey = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var effectiveConstraints = constraints ?? SlotConstraints.None;
        var isLocalized = source is SlotSource.LocalizedText;

        if (!SlotNamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                $"Slot name '{name}' is invalid: expected snake_case matching [a-z][a-z0-9_]*.",
                nameof(name));
        }

        if (required && source is not SlotSource.Caller)
        {
            throw new ArgumentException(
                $"Slot '{name}': only caller slots may be required.", nameof(required));
        }

        if (guaranteed && source is SlotSource.Caller)
        {
            throw new ArgumentException(
                $"Slot '{name}': only server slots may be guaranteed.", nameof(guaranteed));
        }

        if (isLocalized && string.IsNullOrWhiteSpace(naturalKey))
        {
            throw new ArgumentException(
                $"Slot '{name}': a localized-string slot states the Natural Key its value is the "
                + "translation of.",
                nameof(naturalKey));
        }

        if (!isLocalized && naturalKey is not null)
        {
            throw new ArgumentException(
                $"Slot '{name}': a Natural Key belongs to a localized-string slot; the value of this "
                + "one comes from its own source.",
                nameof(naturalKey));
        }

        if (isLocalized && type is not SlotType.String)
        {
            throw new ArgumentException(
                $"Slot '{name}': a localized-string slot is of type string — a translation is text.",
                nameof(type));
        }

        GuardConstraints(name, type, effectiveConstraints);

        // A localized string is there whatever happens: it degrades onto the base text of its key, and
        // the base text is the key itself. Making the declaration state the guarantee as well would let
        // a contract spell "not guaranteed" over a value nothing can take away — and the minimal variant
        // of a ladder would then be refused the one slot it may always rest on (TPL-111(e)).
        return new SlotDeclaration(
            name, type, source, required, guaranteed || isLocalized, effectiveConstraints, naturalKey);
    }

    /// <summary>
    /// Guards that the mandatory constraints for the slot's type are present and within bounds.
    /// </summary>
    private static void GuardConstraints(string name, SlotType type, SlotConstraints constraints)
    {
        switch (type)
        {
            case SlotType.String:
            case SlotType.EntityRef:
                if (constraints.MaxLength is not int maxLength)
                {
                    throw new ArgumentException(
                        $"Slot '{name}': max_length is required for string/entity_ref slots.", nameof(constraints));
                }

                if (maxLength is <= 0 or > MessageTemplateLimits.MaxSlotValueLength)
                {
                    throw new ArgumentException(
                        $"Slot '{name}': max_length must be in 1..{MessageTemplateLimits.MaxSlotValueLength}.",
                        nameof(constraints));
                }

                break;

            case SlotType.Enum:
                if (constraints.EnumValues is not { Count: > 0 })
                {
                    throw new ArgumentException(
                        $"Slot '{name}': a non-empty values set is required for enum slots.", nameof(constraints));
                }

                break;

            case SlotType.Money:
                if (constraints.MoneyCurrencies is not { Count: > 0 })
                {
                    throw new ArgumentException(
                        $"Slot '{name}': a non-empty ISO 4217 currency allowlist is required for money slots.",
                        nameof(constraints));
                }

                break;

            case SlotType.Number:
            case SlotType.Datetime:
                // No mandatory constraints.
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown slot type.");
        }
    }
}
