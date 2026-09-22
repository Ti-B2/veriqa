// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using Veriqa.Core.Contracts;

namespace Veriqa.Sample.DotNet.Showcase.StreamingTv;

/// <summary>Addresses the sample serves, in one place instead of as literals along the routes.</summary>
public static class SampleRoutes
{
    /// <summary>Where the OpenID Connect handler receives the authorization response.</summary>
    public const string CallbackPath = "/signin-oidc";

    /// <summary>Starts the sign-in.</summary>
    public const string SignIn = "/signin";

    /// <summary>Ends this application's session (not the issuer's).</summary>
    public const string SignOut = "/signout";

    /// <summary>Who is watching, and whether they have been welcomed.</summary>
    public const string Session = "/api/session";

    /// <summary>Marks the welcome screen as seen.</summary>
    public const string WelcomeSeen = "/api/session/welcome-seen";

    /// <summary>The catalogue as this viewer sees it.</summary>
    public const string Catalog = "/api/catalog";

    /// <summary>Starts buying a title.</summary>
    public const string Purchase = "/api/catalog/{titleId}/purchase";

    /// <summary>State of a purchase in flight.</summary>
    public const string PurchaseResult = "/api/purchases/{transactionId}";

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
    /// <summary>Standard OIDC subject claim — the viewer this session belongs to.</summary>
    public const string Subject = "sub";
}

/// <summary>Values of the <c>error</c> member the API answers refusals with.</summary>
public static class SampleErrors
{
    /// <summary>Nobody is signed in.</summary>
    public const string Unauthenticated = "unauthenticated";

    /// <summary>This viewer already owns the title.</summary>
    public const string AlreadyOwned = "already_owned";

    /// <summary>The session carries no channel identity to address a step-up to.</summary>
    public const string ChannelIdentityUnavailable = "channel_identity_unavailable";
}

/// <summary>Caller slots of the <c>purchase-title</c> action, as its contract declares them.</summary>
public static class SampleSlots
{
    /// <summary>Name of the title being bought.</summary>
    public const string Title = "title";

    /// <summary>Its price, currency included.</summary>
    public const string Price = "price";
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
/// <param name="WelcomeSeen">Whether this viewer has already seen the welcome screen.</param>
/// <param name="Profile">The card of the viewer, or null when nobody is signed in.</param>
public sealed record SessionResponse(bool Authenticated, bool WelcomeSeen, ViewerProfile? Profile);

/// <summary>Answer of <c>GET /api/catalog</c>.</summary>
/// <param name="Titles">The catalogue, with this viewer's titles marked as owned.</param>
public sealed record CatalogResponse(IReadOnlyList<CatalogTitleView> Titles);

/// <summary>Answer of <c>POST /api/catalog/{titleId}/purchase</c>: the way into the channel.</summary>
/// <param name="TransactionId">The confirmation transaction to poll.</param>
/// <param name="Qr">The way into the channel as a PNG data URI — put it straight into an img src. The
/// deep link itself is not passed on: the page offers the QR alone, not a link to open the bot.</param>
/// <param name="ChannelType">Channel the confirmation was addressed to.</param>
public sealed record PurchaseStartedResponse(string TransactionId, string? Qr, string? ChannelType);

/// <summary>A refusal the page can act on.</summary>
/// <param name="Error">One of <see cref="SampleErrors"/>.</param>
public sealed record ErrorResponse(string Error);
