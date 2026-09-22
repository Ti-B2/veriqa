// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;
using Veriqa.Core.ChannelAdapter.UI;

namespace Veriqa.Core.AuthServer.UI;

/// <summary>
/// Allowed shape of the integrator's footer block of the sign-in page (<c>AuthPageDesign.FooterHtml</c>).
/// The block is the one place the page emits markup it did not build, and it is emitted UNSANITIZED:
/// this check does not repair a value, it refuses it whole. It admits a strict subset of inline HTML —
/// text, the tags <c>a</c>, <c>span</c>, <c>div</c>, <c>p</c>, <c>strong</c>, <c>em</c> and <c>br</c>,
/// the attributes <c>href</c>, <c>target</c>, <c>rel</c> and <c>class</c> in double quotes — so that a
/// value cannot close the container of the window, take the identifier of an element the script of the
/// page reads (<c>id</c>), restyle it (<c>style</c>) or carry a handler (<c>on*</c>).
/// <para>
/// <c>class</c> is admitted, but not the namespace of the contour: a value is refused whole when one of
/// its class tokens is <c>veriqa</c> or begins with <c>veriqa-</c>. The script of the page reaches its
/// own elements by class as well as by identifier (<c>.veriqa-tab</c>, <c>.veriqa-panel</c>,
/// <c>.veriqa-tooltip-host</c>, <c>.veriqa-tooltip</c>), and so does the attribution line of the page —
/// a block naming one of those classes would reach the behavior of the page it stands on. A word that
/// merely starts with <c>veriqa</c> (<c>veriqable</c>) is not the namespace and is admitted.
/// </para>
/// <para>
/// A regular expression reads the tokens; the balance of the tags is a pass with a stack, which no
/// regular expression can make. Names are compared as ASCII only: the tokenizer of the browser folds
/// the case of ASCII letters and of nothing else, so a Unicode case equivalence here would admit a tag
/// the browser does not recognize.
/// </para>
/// </summary>
internal static partial class AuthPageFooterMarkup
{
    /// <summary>
    /// One token of the block, anchored at the position the pass has reached: a run of text without
    /// <c>&lt;</c> and <c>&gt;</c>, or a tag with double-quoted attributes. Whitespace inside a tag is
    /// the ASCII whitespace the HTML tokenizer separates attributes with — a wider class would admit a
    /// separator the browser reads as part of the tag name.
    /// </summary>
    private const string TokenPattern =
        $$"""\G(?:(?<{{TextGroup}}>[^<>]+)|<(?<{{CloseGroup}}>/)?(?<{{NameGroup}}>[A-Za-z]+)(?:[ \t\n\f\r]+(?<{{AttributeGroup}}>[A-Za-z]+)[ \t\n\f\r]*=[ \t\n\f\r]*"(?<{{ValueGroup}}>[^"<>]*)")*[ \t\n\f\r]*(?<{{SelfClosingGroup}}>/)?>)""";

    /// <summary>
    /// Group of <see cref="TokenPattern"/> matched by a run of text.
    /// </summary>
    private const string TextGroup = "text";

    /// <summary>
    /// Group of <see cref="TokenPattern"/> matched by the slash of a closing tag.
    /// </summary>
    private const string CloseGroup = "close";

    /// <summary>
    /// Group of <see cref="TokenPattern"/> holding the tag name.
    /// </summary>
    private const string NameGroup = "name";

    /// <summary>
    /// Group of <see cref="TokenPattern"/> holding each attribute name, in order.
    /// </summary>
    private const string AttributeGroup = "attribute";

    /// <summary>
    /// Group of <see cref="TokenPattern"/> holding each attribute value, in the order of the names.
    /// </summary>
    private const string ValueGroup = "value";

    /// <summary>
    /// Group of <see cref="TokenPattern"/> matched by the slash of a self-closing tag.
    /// </summary>
    private const string SelfClosingGroup = "self";

    /// <summary>
    /// A character reference left in an <c>href</c> after decoding: a numeric one without its closing
    /// semicolon, or a named one the decoder of the platform does not know (<c>&amp;sol;</c>). The
    /// browser decodes both, so a value carrying one would be judged by a form the browser never sees.
    /// </summary>
    private const string UndecodedReferencePattern = "&(?:#|[A-Za-z][A-Za-z0-9]*;)";

    /// <summary>
    /// Tags that open and close.
    /// </summary>
    private static readonly FrozenSet<string> ContainerTags =
        new[] { "a", "span", "div", "p", "strong", "em" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Attributes a tag may carry, each at most once.
    /// </summary>
    private static readonly FrozenSet<string> Attributes =
        new[] { HrefAttribute, TargetAttribute, "rel", ClassAttribute }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Class names of the element — the attribute whose tokens are checked against the namespace of the
    /// contour.
    /// </summary>
    private const string ClassAttribute = "class";

    /// <summary>
    /// Namespace of the contour as a class token of its own.
    /// </summary>
    private const string ContourNamespace = "veriqa";

    /// <summary>
    /// Prefix of every class name of the contour.
    /// </summary>
    private const string ContourNamespacePrefix = ContourNamespace + "-";

    /// <summary>
    /// Separators of the tokens of a <c>class</c> value — the ASCII whitespace the browser splits class
    /// names at, the same class of characters <see cref="TokenPattern"/> separates attributes with.
    /// </summary>
    private static readonly char[] ClassTokenSeparators = [' ', '\t', '\n', '\f', '\r'];

    /// <summary>
    /// The void tag — <c>&lt;br&gt;</c>, <c>&lt;br/&gt;</c>, <c>&lt;br /&gt;</c>, without attributes.
    /// </summary>
    private const string LineBreakTag = "br";

    /// <summary>
    /// The link tag — the one whose attributes are checked beyond the list.
    /// </summary>
    private const string LinkTag = "a";

    /// <summary>
    /// Address a link leads to.
    /// </summary>
    private const string HrefAttribute = "href";

    /// <summary>
    /// Browsing context of a link.
    /// </summary>
    private const string TargetAttribute = "target";

    /// <summary>
    /// The only admitted target: leaving the page in the same tab ends the live sign-in, and coming back
    /// creates a new transaction.
    /// </summary>
    private const string NewTabTarget = "_blank";

    /// <summary>
    /// The last C0 control character (U+001F). The URL parser of the browser drops tabs and newlines
    /// from anywhere in a URL and C0 controls from its ends, so a control inside an address can turn a
    /// path into a host.
    /// </summary>
    private const char LastControlCharacter = '\u001F';

    /// <summary>
    /// Checks the footer block against the admitted shape: every token is text or a tag of the lists,
    /// every link has an <c>href</c> of an allowed scheme and <c>target="_blank"</c>, and the tags are
    /// balanced.
    /// </summary>
    /// <param name="markup">Markup of the block — the value of the setting or its translation.</param>
    /// <returns><c>true</c> if the markup may be emitted into the page as it is.</returns>
    public static bool IsValid([NotNullWhen(true)] string? markup)
    {
        // The method walks the markup token by token and keeps the open tags on a stack: a closing tag
        // must close the innermost open one, and none may stay open at the end.
        if (string.IsNullOrWhiteSpace(markup))
        {
            return false;
        }

        var open = new Stack<string>();
        var position = 0;

        while (position < markup.Length)
        {
            var token = TokenRegex().Match(markup, position);

            if (!token.Success || token.Index != position)
            {
                return false;
            }

            position += token.Length;

            if (!token.Groups[TextGroup].Success && !IsAdmissibleTag(token, open))
            {
                return false;
            }
        }

        return open.Count is 0;
    }

    /// <summary>
    /// Checks one tag and applies it to the stack of open tags.
    /// </summary>
    /// <param name="tag">Token of the tag.</param>
    /// <param name="open">Tags opened and not yet closed, innermost on top.</param>
    /// <returns><c>true</c> if the tag is admitted.</returns>
    private static bool IsAdmissibleTag(Match tag, Stack<string> open)
    {
        var name = tag.Groups[NameGroup].Value.ToLowerInvariant();
        var attributes = tag.Groups[AttributeGroup].Captures;
        var selfClosing = tag.Groups[SelfClosingGroup].Success;

        if (tag.Groups[CloseGroup].Success)
        {
            return attributes.Count is 0
                && !selfClosing
                && open.TryPop(out var innermost)
                && innermost == name;
        }

        if (name == LineBreakTag)
        {
            return attributes.Count is 0;
        }

        // A self-closing slash on a tag that is not void is ignored by the browser, which then keeps
        // the tag open — so it is refused rather than counted as closed.
        if (!ContainerTags.Contains(name) || selfClosing || !AreAdmissibleAttributes(name, tag))
        {
            return false;
        }

        open.Push(name);

        return true;
    }

    /// <summary>
    /// Checks the attributes of an opening tag: names from the list, none twice, and — on a link — an
    /// admissible <c>href</c> and <c>target="_blank"</c>.
    /// </summary>
    /// <param name="name">Tag name, lower case.</param>
    /// <param name="tag">Token of the tag.</param>
    /// <returns><c>true</c> if the attributes are admitted.</returns>
    private static bool AreAdmissibleAttributes(string name, Match tag)
    {
        var names = tag.Groups[AttributeGroup].Captures;
        var values = tag.Groups[ValueGroup].Captures;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? href = null;
        string? target = null;

        for (var i = 0; i < names.Count; i++)
        {
            var attribute = names[i].Value.ToLowerInvariant();

            if (!Attributes.Contains(attribute) || !seen.Add(attribute))
            {
                return false;
            }

            if (attribute == HrefAttribute)
            {
                href = values[i].Value;
            }
            else if (attribute == TargetAttribute)
            {
                target = values[i].Value;
            }
            else if (attribute == ClassAttribute && !IsAdmissibleClass(values[i].Value))
            {
                return false;
            }
        }

        return name != LinkTag
            || (href is not null
                && IsAdmissibleHref(href)
                && string.Equals(target, NewTabTarget, StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks the target of a link in the form the browser reads it — decoded. The decoded value must
    /// carry no backslash and no C0 control (the browser reads <c>/\host</c> and <c>/&lt;TAB&gt;/host</c>
    /// as <c>//host</c>), no reference the decoder left undecoded, and an allowed scheme.
    /// </summary>
    /// <param name="href">Raw value of the attribute.</param>
    /// <returns><c>true</c> if the target is admitted.</returns>
    private static bool IsAdmissibleHref(string href)
    {
        var decoded = WebUtility.HtmlDecode(href);

        return !decoded.Any(static c => c is '\\' or <= LastControlCharacter)
            && !UndecodedReferenceRegex().IsMatch(decoded)
            && CorePageHead.IsAllowedUrlScheme(decoded);
    }

    /// <summary>
    /// Checks the class names of a tag in the form the browser reads them — decoded, split at ASCII
    /// whitespace. No token may be the namespace of the contour or carry its prefix, compared ASCII
    /// case-insensitively: the document mode of the page is not observable here, and in quirks mode the
    /// browser matches class names without regard to ASCII case. A reference the decoder left undecoded
    /// is refused, as in an <c>href</c>: the browser would decode it into a name this check never saw.
    /// </summary>
    /// <param name="value">Raw value of the attribute.</param>
    /// <returns><c>true</c> if the class names are admitted; a value with no token is admitted.</returns>
    private static bool IsAdmissibleClass(string value)
    {
        var decoded = WebUtility.HtmlDecode(value);

        return !UndecodedReferenceRegex().IsMatch(decoded)
            && !decoded
                .Split(ClassTokenSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(ToAsciiLowerCase)
                .Any(static token => token == ContourNamespace
                    || token.StartsWith(ContourNamespacePrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Folds the ASCII upper-case letters of a token and leaves every other character as it is — the
    /// folding the browser applies, and not a Unicode case mapping that would equate a non-ASCII letter
    /// with an ASCII one.
    /// </summary>
    /// <param name="token">Class token.</param>
    /// <returns>The token with ASCII letters in lower case.</returns>
    private static string ToAsciiLowerCase(string token) =>
        string.Create(token.Length, token, static (folded, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                folded[i] = char.IsAsciiLetterUpper(source[i]) ? char.ToLowerInvariant(source[i]) : source[i];
            }
        });

    /// <summary>
    /// Compiled matcher of one token of the block.
    /// </summary>
    [GeneratedRegex(TokenPattern, RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    /// <summary>
    /// Compiled matcher of a character reference left undecoded.
    /// </summary>
    [GeneratedRegex(UndecodedReferencePattern, RegexOptions.CultureInvariant)]
    private static partial Regex UndecodedReferenceRegex();
}
