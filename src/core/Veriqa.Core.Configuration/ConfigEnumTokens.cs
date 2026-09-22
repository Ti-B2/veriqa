// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Veriqa.Core.Configuration;

/// <summary>
/// The ONE derivation of a token dictionary of an enum, for ANY enum: the token of a member, the
/// member of a token, and the whole dictionary in the order of the members' VALUES
/// (SPEC-012 §10.6, CFG-240).
/// <para>
/// It exists because the standard binder of the platform reads a member of an enum by its own NAME
/// and by nothing else. A setting whose tokens are written in another convention would therefore cost
/// its owner a class of constants, a map in each direction and a parse of its own — the same
/// code from key to key, all of it derivable from the names of the members. Here it is derived once,
/// and every consumer of the dictionary asks this one place: the reading of a value, the refusal of a
/// start that names what is admitted, a page of the documentation, and any code that needs the
/// canonical spelling of a member.
/// </para>
/// <para>
/// The derivation knows no enum of the product: there is no switch over members and no list of types
/// — a dictionary comes from <c>TEnum</c> and its metadata alone. A key that wants the tokens states
/// so itself (<see cref="ConfigKeyBuilder{T}.EnumTokens{TEnum}"/>), and a key that does not is read by
/// the binder, by the names of the members.
/// </para>
/// </summary>
public static class ConfigEnumTokens
{
    /// <summary>
    /// What separates the words of a token.
    /// </summary>
    private const char WordSeparator = '_';

    /// <summary>
    /// Derived dictionaries by enum type. The derivation walks the members of a type and builds two
    /// maps, so it is done once per type and not once per read; the map is immutable afterwards, which
    /// is what lets it be shared without a lock.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, object> Dictionaries = new();

    /// <summary>
    /// The canonical token of a member — the single spelling that reaches a configuration, a record of
    /// a journal and the documentation alike.
    /// </summary>
    /// <typeparam name="TEnum">Type of the enum.</typeparam>
    /// <param name="value">Member of the enum.</param>
    /// <returns>Canonical token of the member.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not a declared member of the enum — a number cast to the type, or a combination of
    /// flags. There is no name to derive a token from, and inventing one would put a spelling nothing
    /// reads back into a configuration and into a journal.
    /// </exception>
    public static string ToToken<TEnum>(this TEnum value)
        where TEnum : struct, Enum
    {
        if (!DictionaryOf<TEnum>().TokenOf.TryGetValue(value, out var token))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"The value is not a declared member of '{typeof(TEnum)}', so it has no token.");
        }

        return token;
    }

    /// <summary>
    /// Reads a value as a level states it, in either of the TWO spellings that name a member — the
    /// canonical token of the dictionary (<c>outbound_fetch</c>) and the NAME of the member as the CLR
    /// spells it (<c>OutboundFetch</c>) — and WITHOUT regard to case, so a deployment writing
    /// <c>Plain_Text</c> means the same thing as one writing <c>plain_text</c>.
    /// <para>
    /// The whitespace AROUND the text is not part of the spelling either: no member derives a token
    /// holding any, so trimming it can only widen what is read, and a value that picked up a stray
    /// space on its way into a document names the member it spells rather than nothing at all. It is
    /// one rule of the mechanism and not a tolerance a single consumer grants itself.
    /// </para>
    /// <para>
    /// The second spelling is admitted and never PRINTED: <see cref="ToToken{TEnum}"/> and
    /// <see cref="All{TEnum}"/> stay one set, so the values a refusal names and the column of the
    /// documentation do not double. It exists because the tolerance was already there by halves — the
    /// comparison ignores case, so a member of one word (<c>Signature</c>) was read in the spelling of
    /// its own name while a member of two (<c>SharedSecret</c>) was not, and an integrator writing the
    /// names of the members by the example of the neighbouring settings of one section got half of
    /// them working.
    /// </para>
    /// <para>
    /// A spelling the dictionary holds under NEITHER form — a typo, an empty text, a name of something
    /// this version does not know — names no member. What the LEVEL that stated it then states is not
    /// decided here: this answers the one question it can, and the reading of the key
    /// (<see cref="ConfigKeyBuilder{T}.EnumTokens{TEnum}"/>) tells "the level wrote nothing" from "the
    /// level wrote something that names no member".
    /// </para>
    /// </summary>
    /// <typeparam name="TEnum">Type of the enum.</typeparam>
    /// <param name="token">Value as the level states it.</param>
    /// <param name="value">Member the spelling names.</param>
    /// <returns>true when the spelling names a member of the enum.</returns>
    public static bool TryParse<TEnum>(string? token, [MaybeNullWhen(false)] out TEnum value)
        where TEnum : struct, Enum
    {
        if (token is null)
        {
            value = default;

            return false;
        }

        return DictionaryOf<TEnum>().ByToken.TryGetValue(token.Trim(), out value);
    }

    /// <summary>
    /// Token of every member, in the order of the members' VALUES — the order the platform lists the
    /// names of an enum in (<see cref="Enum.GetNames{TEnum}()"/>), and the only order the derivation
    /// can state: the order the members were WRITTEN in is not recoverable from the metadata of a type.
    /// It is what a message prints so that an operator reads the whole dictionary instead of looking it
    /// up, and an owner who wants a particular reading order gives the members values in that order.
    /// </summary>
    /// <typeparam name="TEnum">Type of the enum.</typeparam>
    /// <returns>Tokens of the members.</returns>
    public static IReadOnlyList<string> All<TEnum>()
        where TEnum : struct, Enum =>
        DictionaryOf<TEnum>().Tokens;

    /// <summary>
    /// The token of ONE member name, by the single rule of the mechanism. A token is the name split
    /// into WORDS, lowercased, and joined with an underscore; a new word starts at an upper-case letter
    /// that either follows a character which is not upper-case, or is itself followed by a lower-case
    /// one. Digits belong to the word they follow.
    /// <para>
    /// The rule is defined for every shape a member name can take, and that is deliberate: the token
    /// lands in the configuration of an integrator, so "it will split the words somehow" is not an
    /// answer. What it does, spelled out:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>Plain</c> → <c>plain</c>;</description></item>
    /// <item><description><c>PlainText</c> → <c>plain_text</c>;</description></item>
    /// <item><description>
    /// an ABBREVIATION stays one word, and the word after it starts where the abbreviation ends:
    /// <c>IOError</c> → <c>io_error</c>, <c>XmlHttpRequest</c> → <c>xml_http_request</c>;
    /// </description></item>
    /// <item><description>
    /// a DIGIT continues the word it follows and does not open one: <c>Sha256</c> → <c>sha256</c>,
    /// while <c>Base64Url</c> → <c>base64_url</c>, because the letter after the digits is upper-case
    /// and therefore opens a word;
    /// </description></item>
    /// <item><description>
    /// an underscore already written in the name is kept as the separator it is: <c>Plain_Text</c> →
    /// <c>plain_text</c>.
    /// </description></item>
    /// </list>
    /// </summary>
    /// <param name="name">Name of the member.</param>
    /// <returns>Token of the member.</returns>
    private static string TokenOfName(string name)
    {
        var token = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            // A word opens at an upper-case letter that either ends a run of something else (a
            // lower-case letter, a digit) or ends an abbreviation, which is what an upper-case letter
            // followed by a lower-case one means. Everything else continues the word it is in.
            var opensWord = i > 0
                && char.IsUpper(current)
                && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));

            if (opensWord && token.Length > 0 && token[^1] != WordSeparator)
            {
                token.Append(WordSeparator);
            }

            token.Append(char.ToLowerInvariant(current));
        }

        return token.ToString();
    }

    /// <summary>
    /// The dictionary of one enum, derived once and kept.
    /// </summary>
    /// <typeparam name="TEnum">Type of the enum.</typeparam>
    /// <returns>Dictionary of the enum.</returns>
    /// <exception cref="InvalidOperationException">Two members of the enum derive one token.</exception>
    private static EnumTokenDictionary<TEnum> DictionaryOf<TEnum>()
        where TEnum : struct, Enum =>
        (EnumTokenDictionary<TEnum>)Dictionaries.GetOrAdd(typeof(TEnum), static _ => Derive<TEnum>());

    /// <summary>
    /// Derives the dictionary of one enum from the names of its members, in the order of their values.
    /// The two spellings a member is read by are collected in two passes and not in one, so that a
    /// collision between the canonical TOKENS is reported as such wherever it stands: in a single pass
    /// the name of an early member could take the spelling a later member's token needs, and the defect
    /// of the declaration would be named the wrong way round.
    /// </summary>
    /// <typeparam name="TEnum">Type of the enum.</typeparam>
    /// <returns>Dictionary of the enum.</returns>
    /// <exception cref="InvalidOperationException">
    /// Two members of the enum derive one token, two names of the enum stand for one value, or the name
    /// of one member is the token of another.
    /// </exception>
    private static EnumTokenDictionary<TEnum> Derive<TEnum>()
        where TEnum : struct, Enum
    {
        var names = Enum.GetNames<TEnum>();
        var values = Enum.GetValues<TEnum>();
        var tokens = new List<string>(names.Length);

        // Room for both spellings of every member: the name of a member of one word is the token of it
        // and takes no second entry, so the count is an upper bound rather than the size.
        var byToken = new Dictionary<string, TEnum>(names.Length * 2, StringComparer.OrdinalIgnoreCase);
        var tokenOf = new Dictionary<TEnum, string>(names.Length);

        for (var i = 0; i < names.Length; i++)
        {
            var token = TokenOfName(names[i]);

            // Two members deriving one token is a defect of the DECLARATION and is refused here rather
            // than resolved by whichever member came first: one of the two would then be unreachable
            // from a configuration, and nothing would say which.
            if (byToken.TryGetValue(token, out var taken))
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Members '{0}' and '{1}' of '{2}' both derive the token '{3}': a token names one member, "
                        + "and a dictionary holding two would make the second unreachable from a configuration.",
                        taken,
                        names[i],
                        typeof(TEnum),
                        token));
            }

            // Two names standing for ONE value (an alias) is refused for the same reason as the
            // collision above, seen from the other side: the value has to be written back out — into a
            // configuration, into a record of a journal — and there is no ground on which one of two
            // equally declared names is the canonical spelling of it. The order the platform lists
            // names of equal value in is not defined, so picking "the first" would make the spelling a
            // deployment reads back depend on nothing anybody stated.
            if (tokenOf.TryGetValue(values[i], out var spelled))
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Tokens '{0}' and '{1}' of '{2}' name one value: a value is written out under a single "
                        + "canonical token, and nothing would say which of the two that is.",
                        spelled,
                        token,
                        typeof(TEnum)));
            }

            tokens.Add(token);
            byToken.Add(token, values[i]);
            tokenOf.Add(values[i], token);
        }

        // The SECOND spelling a member is read by: its own name. It is added after every token is in
        // place, and only where it is not the token already — the name of a member of one word differs
        // from its token by case alone, and the dictionary matches without regard to case.
        for (var i = 0; i < names.Length; i++)
        {
            if (!byToken.TryGetValue(names[i], out var taken))
            {
                byToken.Add(names[i], values[i]);

                continue;
            }

            // The name of one member standing for the token of ANOTHER is refused for the same reason
            // as a collision of two tokens: a spelling names one member, and the value a deployment
            // would be read as depends on nothing anybody stated. It is a defect the tokens alone do
            // not catch — `IoError` derives `io_error` while `Ioerror` derives `ioerror`, so the two
            // tokens differ and the NAME of the first is the token of the second.
            if (!EqualityComparer<TEnum>.Default.Equals(taken, values[i]))
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The name '{0}' of '{1}' is the token of member '{2}': a spelling names one member, and "
                        + "a dictionary holding two would make a configuration stating it read whichever of them "
                        + "the derivation saw first.",
                        names[i],
                        typeof(TEnum),
                        taken));
            }
        }

        return new EnumTokenDictionary<TEnum>(
            [.. tokens],
            byToken.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            tokenOf.ToFrozenDictionary());
    }

    /// <summary>
    /// The derived dictionary of one enum: the tokens in the order of the members' values, and the two
    /// directions of the single mapping between a token and a member. Every one of the three is
    /// immutable — the dictionary is derived once and shared by every consumer of the process, so a
    /// collection a caller could cast and edit would be that consumer editing everybody else's copy.
    /// </summary>
    /// <typeparam name="TEnum">Type of the enum.</typeparam>
    /// <param name="Tokens">Tokens of the members, in the order of the members' values.</param>
    /// <param name="ByToken">
    /// Members by the spellings that name them — the canonical token of each member and the name of
    /// each member — matched without regard to case. It is the only one of the three that holds the
    /// second spelling: what is READ is wider than what is printed.
    /// </param>
    /// <param name="TokenOf">Canonical token of each member.</param>
    private sealed record EnumTokenDictionary<TEnum>(
        ImmutableArray<string> Tokens,
        FrozenDictionary<string, TEnum> ByToken,
        FrozenDictionary<TEnum, string> TokenOf)
        where TEnum : struct, Enum;
}
