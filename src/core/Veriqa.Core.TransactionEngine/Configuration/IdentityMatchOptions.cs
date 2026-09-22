// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.TransactionEngine.Configuration;

/// <summary>
/// Declaration of the identity types an application is entitled to compare the confirming party
/// against the expectations of the relying party (SPEC-012 §4.12, SPEC-039 C22).
/// Configuration section: <c>Veriqa:IdentityMatch</c>.
/// </summary>
/// <remarks>
/// The default is an EMPTY set (CFG-157): an application that declared nothing gets no comparison at
/// all, and every candidate it is sent is refused as undeclared. That is secure by default — the
/// alternative would let a relying party probe identifiers of a person who need not be its user.
/// </remarks>
public sealed class IdentityMatchOptions
{
    /// <summary>
    /// Configuration section name (SPEC-012 §6.11).
    /// </summary>
    public const string SectionName = "Veriqa:IdentityMatch";

    /// <summary>
    /// Member of a record above the core level the same set is stated at. Written as text rather than
    /// taken from the entry's type: the OIDC client entry belongs to the auth server, which references
    /// this assembly and not the other way round.
    /// </summary>
    public const string RecordMember = "IdentityMatchComparableTypes";

    /// <summary>
    /// Declared comparable types. An empty list — the comparison is not performed (CFG-157).
    /// </summary>
    public IReadOnlyList<ComparableIdentityType> ComparableTypes { get; set; } = [];
}

/// <summary>
/// One declared comparable identity type: the three things a comparison needs and none of which the
/// request is allowed to state for itself (CFG-158, the same allowlist principle as
/// <c>action_type</c>).
/// </summary>
public sealed class ComparableIdentityType
{
    /// <summary>
    /// Name of the type. It is the key the relying party states its expectation under
    /// (<c>expected_identities</c>) and the value returned as <c>matched_type</c>. Unique in the set
    /// (CFG-160).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Name of the <c>ResolvedIdentitySnapshot</c> claim the FACTUAL value of the confirming party is
    /// taken from. The spelling is significant: the resolved identity keeps claim names as the
    /// resolution produced them, so the name comes from this declaration rather than from a
    /// case-insensitive guess.
    /// </summary>
    public string ClaimName { get; set; } = string.Empty;

    /// <summary>
    /// Rule both values are brought to a canonical form by BEFORE they are compared. The default is
    /// the strictest one: a weakening rule widens the class of values the server calls equal, and a
    /// false match here means a dangerous action was allowed (CFG-159).
    /// </summary>
    public IdentityNormalizationRule Normalization { get; set; } = IdentityNormalizationRule.Exact;

    /// <summary>
    /// What the declaration says about itself — the text a snapshot report quotes when the set it
    /// belongs to is refused. It names the three members the domain of the axis judges (CFG-160), so
    /// that an operator reading the report sees WHICH declaration is broken and by WHICH rule, instead
    /// of the name of a CLR type. None of the three can hold a secret: a type name, a claim name and a
    /// rule of the closed set are all names of the axis rather than values taken from a person.
    /// </summary>
    /// <returns>Text of the declaration.</returns>
    public override string ToString() =>
        $"'{Name}' from claim '{ClaimName}' by {Normalization}";
}

/// <summary>
/// Closed set of normalization rules shipped by the core (SPEC-012 §4.12). Extending it is an edit of
/// this enumeration and a revision of that section — never a configuration value: the rule decides
/// what the server accepts as one and the same person.
/// </summary>
public enum IdentityNormalizationRule
{
    /// <summary>
    /// Equality of the values as they are, after trimming surrounding whitespace. Widens nothing —
    /// the rule for opaque identifiers (a messenger user id), where any folding is harmful.
    /// </summary>
    Exact = 0,

    /// <summary>
    /// Reduction to the E.164 form (a leading <c>+</c> and digits only): the relying party and the
    /// channel spell one and the same phone number differently.
    /// </summary>
    E164Phone = 1,

    /// <summary>
    /// Case folding of the DOMAIN part of an address only; the local part is left as it is — the
    /// domain is case-insensitive by RFC 5321 and the local part is not.
    /// </summary>
    EmailDomainCaseFold = 2
}
