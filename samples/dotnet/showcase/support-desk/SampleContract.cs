// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

using Veriqa.Core.Contracts;

namespace Veriqa.Sample.DotNet.Showcase.SupportDesk;

/// <summary>Addresses the sample serves, in one place instead of as literals along the routes.</summary>
public static class SampleRoutes
{
    /// <summary>Where the OpenID Connect handler receives the authorization response.</summary>
    public const string CallbackPath = "/signin-oidc";

    /// <summary>Starts the sign-in.</summary>
    public const string SignIn = "/signin";

    /// <summary>Ends this application's session (not the issuer's).</summary>
    public const string SignOut = "/signout";

    /// <summary>Who the customer is.</summary>
    public const string Session = "/api/session";

    /// <summary>The tickets of this customer.</summary>
    public const string Tickets = "/api/tickets";

    /// <summary>Starts resolving or deleting a ticket.</summary>
    public const string Resolution = "/api/tickets/{ticketId}/resolution";

    /// <summary>State of an operation in flight.</summary>
    public const string ResolutionResult = "/api/tickets/resolutions/{transactionId}";

    /// <summary>The single page of the interface, inside the web root.</summary>
    public const string IndexFileName = "index.html";

    /// <summary>Content type of that page.</summary>
    public const string HtmlContentType = "text/html; charset=utf-8";

    /// <summary>Content type a refusal of Veriqa is passed through with.</summary>
    public const string JsonContentType = "application/json";
}

/// <summary>Standard OIDC scopes the sample asks for beyond the ones the handler adds itself.</summary>
public static class SampleScopes
{
    /// <summary>Standard OIDC scope gating the <c>phone_number</c> claim.</summary>
    public const string Phone = "phone";
}

/// <summary>Claim names the sample reads that are not part of the Veriqa claim contract.</summary>
public static class SampleClaims
{
    /// <summary>Standard OIDC subject claim — the customer this session belongs to.</summary>
    public const string Subject = "sub";
}

/// <summary>Values of the <c>error</c> member the API answers refusals with.</summary>
public static class SampleErrors
{
    /// <summary>Nobody is signed in.</summary>
    public const string Unauthenticated = "unauthenticated";

    /// <summary>The body named an operation the desk does not have.</summary>
    public const string UnknownAction = "unknown_action";

    /// <summary>The ticket is closed already.</summary>
    public const string AlreadyResolved = "already_resolved";

    /// <summary>The session carries no channel identity to address a step-up to.</summary>
    public const string ChannelIdentityUnavailable = "channel_identity_unavailable";
}

/// <summary>
/// The two operations, as the browser spells them in a request body and as they travel on the wire.
/// The same two strings are what <see cref="TicketAction"/> serialises to — one spelling, declared
/// once, instead of a literal in the parser and another in the enumeration.
/// </summary>
public static class SampleActionNames
{
    /// <summary>Close the ticket.</summary>
    public const string Resolve = "resolve";

    /// <summary>Remove the ticket from the list for good.</summary>
    public const string Delete = "delete";
}

/// <summary>Caller slots both ticket actions declare in their contracts.</summary>
public static class SampleSlots
{
    /// <summary>Identifier of the ticket, as the customer sees it.</summary>
    public const string Ticket = "ticket";

    /// <summary>Its one-line summary.</summary>
    public const string Subject = "subject";
}

/// <summary>
/// The comparable identity types the backend client declares in <c>IdentityMatchComparableTypes</c>,
/// one per channel the sample offers. All three compare the same claim — <c>channel_user_id</c> —
/// and differ only in the normalisation rule that suits the channel's way of writing an identity.
/// <para>
/// The map lives here, as one table, because a confirmation that names a type the client did not
/// declare is refused with <c>candidate_type_undeclared</c>: the names in this file and the names in
/// <c>appsettings.json</c> are two halves of one declaration.
/// </para>
/// </summary>
public static class SampleIdentityTypes
{
    /// <summary>Telegram: the channel user id, compared exactly.</summary>
    public const string TelegramUserId = "telegram_user_id";

    /// <summary>WhatsApp: the phone number, compared in E.164 form.</summary>
    public const string WhatsAppPhone = "whatsapp_phone";

    /// <summary>Email: the address, compared with a case-folded domain.</summary>
    public const string EmailAddress = "email_address";

    private static readonly Dictionary<string, string> ByChannel = new(StringComparer.OrdinalIgnoreCase)
    {
        ["telegram"] = TelegramUserId,
        ["whatsapp"] = WhatsAppPhone,
        ["email"] = EmailAddress
    };

    /// <summary>The declared type for a channel, or null for a channel this sample does not offer.</summary>
    /// <param name="channelType">Value of the <see cref="VeriqaClaimTypes.ChannelType"/> claim.</param>
    public static string? ForChannel(string channelType) => ByChannel.GetValueOrDefault(channelType);
}

/// <summary>Answer of <c>GET /api/session</c>.</summary>
/// <param name="Authenticated">Whether anybody is signed in.</param>
/// <param name="Profile">The card of the customer, or null when nobody is signed in.</param>
public sealed record SessionResponse(bool Authenticated, ViewerProfile? Profile);

/// <summary>Answer of <c>GET /api/tickets</c>.</summary>
/// <param name="Tickets">The tickets of this customer, newest first.</param>
public sealed record TicketsResponse(IReadOnlyList<Ticket> Tickets);

/// <summary>
/// Body of <c>POST /api/tickets/{ticketId}/resolution</c>. The action arrives as a STRING rather
/// than as the enumeration: a body naming an operation the desk does not have must be answered
/// <c>400 unknown_action</c>, and a binder told to parse an enumeration would refuse it first, with
/// a shape of its own.
/// </summary>
/// <param name="Action">Either <c>resolve</c> or <c>delete</c>.</param>
public sealed record ResolutionRequest([property: JsonPropertyName("action")] string? Action);

/// <summary>Answer of <c>POST /api/tickets/{ticketId}/resolution</c>: the way into the channel.</summary>
/// <param name="TransactionId">The confirmation transaction to poll.</param>
/// <param name="Qr">The way into the channel as a PNG data URI — put it straight into an img src. The
/// page shows it where the confirming phone is another device.</param>
/// <param name="Url">The same way in as a link. The page offers it as a button on a phone, where the
/// person confirms on the very device and a QR could not be scanned.</param>
/// <param name="Action">The operation awaiting confirmation.</param>
/// <param name="ChannelType">Channel the confirmation was addressed to.</param>
public sealed record ResolutionStartedResponse(
    string TransactionId,
    string? Qr,
    string Url,
    TicketAction Action,
    string? ChannelType);

/// <summary>A refusal the page can act on.</summary>
/// <param name="Error">One of <see cref="SampleErrors"/>.</param>
public sealed record ErrorResponse(string Error);
