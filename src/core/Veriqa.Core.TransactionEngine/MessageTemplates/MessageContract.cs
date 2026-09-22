// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;

using Veriqa.Core.Configuration;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// The CONTRACT of a message: the slots it declares — their names, types, constraints, sources and
/// whether a value is guaranteed (SPEC-036 §4.1, §4.6). It is the value of one of the two settings a
/// message is made of (<see cref="MessageTemplateConfigKeys.Contract"/>): the contract says which
/// slots exist, and the template ladder resolved beside it may reference no other one.
/// <para>
/// Immutable and correct by construction: a contract exists only where every slot of it is a valid
/// declaration, so a render point never has to ask whether the allowlist it matches against is
/// well-formed.
/// </para>
/// <para>
/// The allowlist may be EMPTY, and an empty one is not the same as no contract at all. A message that
/// substitutes nothing — the terminal outcome receipt as shipped today (SPEC-036 TPL-123) — states a
/// contract of no slots, and it is that statement the cross-key check refuses a slot-bearing variant against
/// (TPL-111a), wherever the variant came from. A kind NO level states a contract for has no allowlist
/// to judge anything by and is left alone instead.
/// </para>
/// </summary>
public sealed class MessageContract
{
    /// <summary>
    /// Slot types the contract FORWARD-DECLARES and a v1 deployment may not state (SPEC-036 §5,
    /// TPL-015). The mechanism derives the whole dictionary of the enum and judges nothing; which of
    /// its members a deployment may write is the owner's, and it is stated here once — so that the
    /// refusal of a reserved type and the list of the admitted ones cannot disagree.
    /// </summary>
    private static readonly SlotType[] ReservedSlotTypes = [SlotType.Money];

    /// <summary>
    /// The types a refusal names as admitted: the tokens of the derivation less the reserved ones, in
    /// the order the derivation states them. A refusal listing the whole dictionary would send an
    /// integrator who mistyped a type straight into the second refusal, and the same set is what the
    /// declaration of the member documents (<c>MessageSlotOptions.Type</c>).
    /// </summary>
    private static readonly string AdmittedSlotTypes = string.Join(
        ", ",
        Enum.GetValues<SlotType>()
            .Where(type => !ReservedSlotTypes.Contains(type))
            .Select(type => type.ToToken()));

    /// <summary>
    /// Slots by name, for the lookups a render point and the caller-value validator make.
    /// </summary>
    private readonly FrozenDictionary<string, SlotDeclaration> _slotsByName;

    /// <summary>
    /// Creates the contract over already validated slots.
    /// </summary>
    /// <param name="slots">Declared slots.</param>
    /// <param name="slotsByName">The same slots keyed by name.</param>
    private MessageContract(
        IReadOnlyList<SlotDeclaration> slots,
        FrozenDictionary<string, SlotDeclaration> slotsByName)
    {
        Slots = slots;
        _slotsByName = slotsByName;
    }

    /// <summary>
    /// Declared slots, in the order the contract states them. Names are unique within a contract.
    /// </summary>
    public IReadOnlyList<SlotDeclaration> Slots { get; }

    /// <summary>
    /// Looks a declared slot up by name; null when the contract does not declare it — the token is
    /// then outside the allowlist and stays literal (TPL-033).
    /// </summary>
    /// <param name="name">Slot name.</param>
    /// <returns>The slot declaration, or null.</returns>
    public SlotDeclaration? FindSlot(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return _slotsByName.TryGetValue(name, out var slot) ? slot : null;
    }

    /// <summary>
    /// Builds a contract out of slot declarations, guarding that the names are unique.
    /// </summary>
    /// <param name="slots">Declared slots.</param>
    /// <returns>The immutable contract.</returns>
    /// <exception cref="ArgumentException">A slot name is declared twice.</exception>
    public static MessageContract Of(IReadOnlyList<SlotDeclaration> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        var byName = new Dictionary<string, SlotDeclaration>(StringComparer.Ordinal);

        foreach (var slot in slots)
        {
            if (!byName.TryAdd(slot.Name, slot))
            {
                throw new ArgumentException(
                    $"A message contract declares slot '{slot.Name}' more than once.", nameof(slots));
            }
        }

        return new MessageContract([.. slots], byName.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// Builds the contract a level STATED, refusing a declaration that is not well-formed. It is the
    /// reading of the setting's value: a broken declaration leaves the step of the level unset and is
    /// reported by the mechanism (SPEC-012 CFG-240), while the startup validation names the same
    /// defect with the address an operator has to repair.
    /// </summary>
    /// <param name="stated">The contract as the level states it.</param>
    /// <returns>The immutable contract.</returns>
    /// <exception cref="InvalidOperationException">The declaration is not well-formed.</exception>
    public static MessageContract Read(MessageContractOptions stated)
    {
        ArgumentNullException.ThrowIfNull(stated);

        var errors = new List<string>();
        var contract = TryBuild(stated, errors);

        return contract ?? throw new InvalidOperationException(string.Join(" ", errors));
    }

    /// <summary>
    /// Builds the contract and collects every defect of the declaration instead of stopping at the
    /// first one — what the startup validation reports to an operator.
    /// </summary>
    /// <param name="stated">The contract as the level states it.</param>
    /// <param name="errors">Accumulator of the defects found.</param>
    /// <returns>The contract, or null when the declaration is not well-formed.</returns>
    internal static MessageContract? TryBuild(MessageContractOptions stated, ICollection<string> errors)
    {
        var slots = new List<SlotDeclaration>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var failed = false;

        foreach (var slotOptions in stated.Slots)
        {
            var slot = TryBuildSlot(slotOptions, seen, errors);

            if (slot is null)
            {
                failed = true;

                continue;
            }

            slots.Add(slot);
        }

        if (failed)
        {
            return null;
        }

        // A contract of NO slots is a statement, not an omission: it says this message substitutes
        // nothing, and it is what the cross-key check refuses a slot-bearing variant against
        // (SPEC-036 TPL-111a). The two are told apart by whether a contract is stated at all — a kind
        // no level states one for has no allowlist to judge a variant by, and is skipped.
        return Of(slots);
    }

    /// <summary>
    /// Builds one slot of a stated contract (TPL-005, TPL-111(b)(c)(d), D3), reporting every defect.
    /// </summary>
    /// <param name="stated">Slot as the level states it.</param>
    /// <param name="seen">Slot names already taken in this contract.</param>
    /// <param name="errors">Accumulator of the defects found.</param>
    /// <returns>The slot declaration, or null when the declaration is not well-formed.</returns>
    private static SlotDeclaration? TryBuildSlot(
        MessageSlotOptions stated,
        HashSet<string> seen,
        ICollection<string> errors)
    {
        var name = stated.Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("a slot has no name.");

            return null;
        }

        // A contract names each slot once: two declarations of one name leave no way to tell which
        // one a token in the template refers to (TPL-006b).
        if (!seen.Add(name))
        {
            errors.Add($"slot '{name}': duplicate slot name — a contract may state each name only once.");

            return null;
        }

        // The type names a member of SlotType, and the spellings that name one are derived from the
        // members themselves (ConfigEnumTokens): the six the configuration of a deployment carries are
        // the tokens the derivation produces, so each of them names its member.
        // The list of admitted types in the refusal comes from the same derivation, less what this
        // owner reserves, which is why a type added to the enum needs no second place updated.
        if (!ConfigEnumTokens.TryParse<SlotType>(stated.Type, out var type))
        {
            errors.Add($"slot '{name}': unknown type '{stated.Type}' — admitted types are {AdmittedSlotTypes}.");

            return null;
        }

        if (ReservedSlotTypes.Contains(type))
        {
            errors.Add($"slot '{name}': type '{type.ToToken()}' is reserved and not supported in v1 (D3).");

            return null;
        }

        // A deployment declaring a slot of its own declares what the calling party supplies: the
        // server sources belong to the values the product itself collects.
        var source = stated.Source ?? SlotSource.Caller;

        var constraints = BuildConstraints(name, type, source, stated, errors);

        if (constraints is null)
        {
            return null;
        }

        try
        {
            return SlotDeclaration.Create(
                name,
                type,
                source,
                required: stated.Required,
                guaranteed: stated.Guaranteed,
                constraints: constraints,
                naturalKey: stated.NaturalKey);
        }
        catch (ArgumentException exception)
        {
            errors.Add($"slot '{name}': {exception.Message}");

            return null;
        }
    }

    /// <summary>
    /// Builds and range-checks the type-specific constraints of a stated slot (TPL-111(d)).
    /// </summary>
    /// <param name="name">Slot name.</param>
    /// <param name="type">Slot type.</param>
    /// <param name="source">Where the value comes from.</param>
    /// <param name="stated">Slot as the level states it.</param>
    /// <param name="errors">Accumulator of the defects found.</param>
    /// <returns>The constraints, or null when a mandatory one is missing or out of range.</returns>
    private static SlotConstraints? BuildConstraints(
        string name,
        SlotType type,
        SlotSource source,
        MessageSlotOptions stated,
        ICollection<string> errors)
    {
        switch (type)
        {
            case SlotType.String:
            case SlotType.EntityRef:
                // A CALLER slot states its own ceiling: it bounds a value that arrives from outside,
                // and how long that value may be is the declaring party's decision (TPL-006d). A
                // SERVER slot the product collects itself takes the global ceiling when it states
                // none — the one number the whole core caps a substituted value by (TPL-057).
                var maxLength = stated.MaxLength
                    ?? (source is SlotSource.Caller ? null : MessageTemplateLimits.MaxSlotValueLength);

                if (maxLength is not int statedMaxLength)
                {
                    errors.Add($"slot '{name}': a caller-supplied {type} slot must state max_length.");

                    return null;
                }

                if (statedMaxLength is <= 0 or > MessageTemplateLimits.MaxSlotValueLength)
                {
                    errors.Add(
                        $"slot '{name}': max_length must be in 1..{MessageTemplateLimits.MaxSlotValueLength}.");

                    return null;
                }

                return new SlotConstraints { MaxLength = statedMaxLength };

            case SlotType.Enum:
                // An enum slot is checked against the set it names, so the set is what makes the
                // declaration meaningful: without it nothing constrains the value (TPL-006d).
                if (stated.Values is not { Count: > 0 })
                {
                    errors.Add($"slot '{name}': an enum slot must state a non-empty set of allowed values.");

                    return null;
                }

                // SlotConstraints copies the set into an immutable array on init (csharp-rules §2).
                return new SlotConstraints { EnumValues = [.. stated.Values] };

            case SlotType.Number:
                return new SlotConstraints { NumberMin = stated.Min, NumberMax = stated.Max };

            case SlotType.Datetime:
                return SlotConstraints.None;

            default:
                errors.Add($"slot '{name}': type '{type}' cannot be declared in configuration.");

                return null;
        }
    }
}
