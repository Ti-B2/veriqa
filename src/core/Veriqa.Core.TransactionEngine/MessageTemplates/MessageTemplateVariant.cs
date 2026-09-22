// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// An edition of a step of the ladder — one of the two wordings a sink may ask a step for
/// (SPEC-036 §4.3). A mail is ONE message carrying both of them at once, so they are editions of the
/// same step rather than separate steps filtered by a mode: a sink asks a step for the edition it is
/// about to deliver, and a step that does not state that edition is simply not usable by it.
/// </summary>
public enum MessageTemplateEdition
{
    /// <summary>HTML: the markup of the text is markup, and slot values are HTML-escaped.</summary>
    Html = 0,

    /// <summary>Plain text (messenger prompt, plain-text mail part): nothing is escaped.</summary>
    Plain = 1
}

/// <summary>
/// What a text of a step IS — the unit a report about a step is written in, when what it has to name is
/// ONE text rather than one edition. The subject is not an edition of anything: a mail shows it whatever
/// edition its body is delivered in, so calling a defect of the subject a defect of "the html body"
/// sends an operator to repair a text that carries no such token.
/// </summary>
public enum MessageTemplateTextRole
{
    /// <summary>The subject of the step — it goes out with every edition of the body.</summary>
    Subject = 0,

    /// <summary>A body of the step — the text of the editions named beside it.</summary>
    Body = 1
}

/// <summary>
/// One text of a step, with what it is and which editions rest on it.
/// </summary>
/// <param name="Role">What the text is: the subject of the step, or a body of it.</param>
/// <param name="Text">The text itself.</param>
/// <param name="Editions">
/// The editions this text reaches. For a body — the editions stating exactly it, which is BOTH of them
/// for the string spelling of a step (one text stated as both editions). For the subject — every edition
/// the step states, since the subject goes out with all of them; that is NONE of them for a step stating
/// no body at all, which is why a check refusing the subject refuses the STEP rather than the editions
/// listed here: a step is not deliverable without its subject whatever it states beside it.
/// </param>
public readonly record struct MessageTemplateTextPart(
    MessageTemplateTextRole Role,
    string Text,
    IReadOnlyList<MessageTemplateEdition> Editions);

/// <summary>
/// One step of a template ladder: either a plain text — which serves any sink — or a STRUCTURE stating
/// the editions of the step and, for the channels that have one, the subject (SPEC-036 §4.3). That is
/// why the subject of a mail is not a message kind of its own: a mail is ONE message, and its subject
/// is a field of the step that states its body.
/// <para>
/// Both spellings live in one ladder on purpose: the channels that need no structure (a messenger
/// prompt, a deep-link prefill) state a step as a string, and the two converters below are what let one
/// JSON array hold either. A string step is exactly a structure whose two editions carry that one text,
/// so nothing downstream branches on which spelling a deployment used.
/// </para>
/// <para>
/// <b>A step states at least one edition, and may state only one.</b> "This mail is HTML only" is
/// expressed by no step of the ladder stating the plain edition, not by an error at render time — and
/// the two parts of a mail may therefore be chosen from DIFFERENT steps, since a step is usable by a
/// sink only when it states the edition that sink asks for.
/// </para>
/// </summary>
[TypeConverter(typeof(MessageTemplateVariantTypeConverter))]
[JsonConverter(typeof(MessageTemplateVariantJsonConverter))]
public sealed class MessageTemplateVariant
{
    /// <summary>
    /// The editions a step may state, in the order a report names them. Every check that judges a step
    /// edition by edition (the floor of a ladder, the admission of a step against the contract of its
    /// message) walks this list rather than naming the editions itself. It is not the only place the
    /// set is spelled, though: <see cref="TextOf"/> and <see cref="Without"/> map each edition onto its
    /// own property and so name them one by one — a third edition is added here AND in those two, and
    /// added here alone would be answered for with the text of another edition.
    /// </summary>
    public static IReadOnlyList<MessageTemplateEdition> Editions { get; } =
        [MessageTemplateEdition.Html, MessageTemplateEdition.Plain];

    /// <summary>
    /// Subject of the message, for the channels that have one (a mail). Null — the step states none.
    /// A subject is never markup: its slot values are substituted as text whatever edition the body of
    /// the step is delivered in.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// HTML edition of the step — the Natural Key that goes to the localizer and, after it, to the slot
    /// substitution. Null — the step does not serve an HTML sink.
    /// </summary>
    public string? Html { get; init; }

    /// <summary>
    /// Plain-text edition of the step. Null — the step does not serve a plain-text sink.
    /// </summary>
    public string? Plain { get; init; }

    /// <summary>
    /// A step in its string form: one text that serves any sink.
    /// </summary>
    /// <param name="body">Text of the step.</param>
    /// <returns>The step.</returns>
    public static MessageTemplateVariant Text(string body) => new() { Html = body, Plain = body };

    /// <summary>
    /// A text IS a step — the string spelling of one (<see cref="Text"/>). The conversion is implicit
    /// because the two spellings are one concept in the model rather than two types a caller picks
    /// between: a ladder of texts is written as a ladder of texts, in C# exactly as in JSON.
    /// </summary>
    /// <param name="body">Text of the step.</param>
    public static implicit operator MessageTemplateVariant(string body) => Text(body);

    /// <summary>
    /// The MINIMAL step of the ladder <b>per edition</b> — the floor the render degrades to for a sink
    /// asking that edition.
    /// <para>
    /// The selection runs among the steps stating the asked edition, so the floor is per edition and is
    /// not simply the last step of the ladder. A ladder spelled
    /// <c>[{Html: …{browser}…, Plain: minimal}, {Html: minimal}]</c> has a plain floor resting on
    /// nothing optional and an HTML floor resting on <c>{browser}</c> — and it is the HTML part of the
    /// mail that would carry the token as a literal (TPL-032).
    /// </para>
    /// </summary>
    /// <param name="ladder">Steps as the ladder states them.</param>
    /// <returns>The floor of every edition SOME step of the ladder states; an edition no step states
    /// has no floor and is absent from the result.</returns>
    public static IReadOnlyDictionary<MessageTemplateEdition, MessageTemplateVariant> Floors(
        IEnumerable<MessageTemplateVariant> ladder)
    {
        ArgumentNullException.ThrowIfNull(ladder);

        var steps = ladder as IReadOnlyList<MessageTemplateVariant> ?? [.. ladder];
        var floors = new Dictionary<MessageTemplateEdition, MessageTemplateVariant>(Editions.Count);

        foreach (var edition in Editions)
        {
            for (var index = steps.Count - 1; index >= 0; index--)
            {
                if (steps[index]?.TextOf(edition) is not null)
                {
                    floors[edition] = steps[index];

                    break;
                }
            }
        }

        return floors;
    }

    /// <summary>
    /// The text this step states for <paramref name="edition"/>; null when it does not state that
    /// edition and is therefore not usable by a sink asking for it.
    /// </summary>
    /// <param name="edition">Edition the sink asks for.</param>
    /// <returns>The text, or null.</returns>
    public string? TextOf(MessageTemplateEdition edition) =>
        edition is MessageTemplateEdition.Html ? Html : Plain;

    /// <summary>
    /// The same step with <paramref name="edition"/> taken off it — what a check that refuses ONE
    /// edition of a step leaves behind, so the other edition of the same step stays usable by the sink
    /// asking for it. Null when the step states no other edition and is therefore left with nothing.
    /// </summary>
    /// <param name="edition">Edition to take off the step.</param>
    /// <returns>The step without that edition, or null when nothing is left of it.</returns>
    public MessageTemplateVariant? Without(MessageTemplateEdition edition)
    {
        var html = edition is MessageTemplateEdition.Html ? null : Html;
        var plain = edition is MessageTemplateEdition.Plain ? null : Plain;

        return html is null && plain is null
            ? null
            : new MessageTemplateVariant { Subject = Subject, Html = html, Plain = plain };
    }

    /// <summary>
    /// Every text this step states, each ONCE, with what it is and which editions rest on it — what a
    /// check that refuses a TEXT reports, and what it takes off the step.
    /// <para>
    /// It is the step's business rather than its caller's because both halves of it are decided by the
    /// SHAPE of the step, which only the step knows: the string spelling states one text as both
    /// editions, so a caller walking the editions would name that text twice, and the subject is stated
    /// once for editions that deliver it together, so the same caller would report a defect of the
    /// subject as a defect of a body — twice over, of a body that does not contain the token.
    /// </para>
    /// </summary>
    /// <returns>The stated texts, in the order a report names them.</returns>
    public IEnumerable<MessageTemplateTextPart> TextParts()
    {
        if (Subject is not null)
        {
            yield return new MessageTemplateTextPart(
                MessageTemplateTextRole.Subject,
                Subject,
                [.. Editions.Where(edition => TextOf(edition) is not null)]);
        }

        var named = new List<string>(Editions.Count);

        foreach (var edition in Editions)
        {
            // The string spelling states one text as both editions, and a report must not name it twice:
            // it is named once, carrying every edition that rests on it.
            if (TextOf(edition) is not { } body || named.Contains(body, StringComparer.Ordinal))
            {
                continue;
            }

            named.Add(body);

            yield return new MessageTemplateTextPart(
                MessageTemplateTextRole.Body,
                body,
                [.. Editions.Where(other => string.Equals(TextOf(other), body, StringComparison.Ordinal))]);
        }
    }

    /// <summary>
    /// Every text this step states, each once — the subject and the editions of the body. It is what
    /// the checks across a step and a contract walk: a slot referenced by the subject is as much a slot
    /// of the message as one referenced by a body. A check that has to NAME the text it refuses asks
    /// <see cref="TextParts"/> instead, which is where the deduplication of the two lives.
    /// </summary>
    /// <returns>The stated texts, in the order a report names them.</returns>
    public IEnumerable<string> Texts() => TextParts().Select(part => part.Text);

    /// <summary>
    /// The texts a sink asking <paramref name="edition"/> actually renders: the subject, which a mail
    /// shows whatever edition its body is in, and the body of that edition. This is what the coverage
    /// of a step by values is judged on, and what the floor rule is asked of — per edition, since the
    /// two editions degrade apart.
    /// </summary>
    /// <param name="edition">Edition the sink asks for.</param>
    /// <returns>The texts, in the order a report names them.</returns>
    public IEnumerable<string> TextsOf(MessageTemplateEdition edition)
    {
        if (Subject is not null)
        {
            yield return Subject;
        }

        if (TextOf(edition) is { } body)
        {
            yield return body;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Html ?? Plain ?? string.Empty;
}

/// <summary>
/// How the configuration binder reads a step spelled as a STRING. The binder converts a stated scalar
/// through the type's converter, so this is what makes <c>"Templates": [ "text" ]</c> bind onto a
/// structured type — and it is asked only for the string spelling: a step stated as an object has no
/// scalar value and is bound member by member.
/// </summary>
internal sealed class MessageTemplateVariantTypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text
            ? MessageTemplateVariant.Text(text)
            : base.ConvertFrom(context, culture, value);
}

/// <summary>
/// How the shipped declarations — a JSON file read by <c>System.Text.Json</c> rather than by the
/// configuration binder — read the same two spellings. The two readers are separate on purpose (the
/// product's own file and a deployment's section are different sources), and this is the pair of the
/// converter above that keeps one shape readable from both.
/// </summary>
internal sealed class MessageTemplateVariantJsonConverter : JsonConverter<MessageTemplateVariant>
{
    /// <inheritdoc />
    public override MessageTemplateVariant? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.String)
        {
            return MessageTemplateVariant.Text(reader.GetString()!);
        }

        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException(
                "A template variant is either a text or an object stating the subject and the editions "
                + "of the step.");
        }

        string? subject = null;
        string? html = null;
        string? plain = null;

        while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
        {
            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                continue;
            }

            var member = reader.GetString();
            reader.Read();

            // Members are matched case-insensitively, exactly as the configuration binder matches them
            // on the other source: one shape is written once and read alike from both. A member neither
            // reader knows is skipped here and caught by the startup validation, which then sees a step
            // stating no edition at all and names the kind and the step.
            if (string.Equals(member, nameof(MessageTemplateVariant.Subject), StringComparison.OrdinalIgnoreCase))
            {
                subject = reader.GetString();
            }
            else if (string.Equals(member, nameof(MessageTemplateVariant.Html), StringComparison.OrdinalIgnoreCase))
            {
                html = reader.GetString();
            }
            else if (string.Equals(member, nameof(MessageTemplateVariant.Plain), StringComparison.OrdinalIgnoreCase))
            {
                plain = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return new MessageTemplateVariant { Subject = subject, Html = html, Plain = plain };
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        MessageTemplateVariant value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        // The string spelling round-trips as a string: one text stated as both editions and no subject
        // is exactly what the reader above turns a JSON string into.
        if (value.Subject is null
            && value.Html is not null
            && string.Equals(value.Html, value.Plain, StringComparison.Ordinal))
        {
            writer.WriteStringValue(value.Html);

            return;
        }

        writer.WriteStartObject();

        if (value.Subject is not null)
        {
            writer.WriteString(nameof(MessageTemplateVariant.Subject), value.Subject);
        }

        if (value.Html is not null)
        {
            writer.WriteString(nameof(MessageTemplateVariant.Html), value.Html);
        }

        if (value.Plain is not null)
        {
            writer.WriteString(nameof(MessageTemplateVariant.Plain), value.Plain);
        }

        writer.WriteEndObject();
    }
}
