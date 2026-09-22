// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

// Runnable sample: the support section of a company's website. Like its neighbour streaming-tv it is
// an emulator of a PRODUCT rather than of one mechanism, and it shows the same four things in a
// different shape:
//
//   1. a public page      anyone may read the section; only the ticket list needs a name;
//   2. sign-in            the visitor is redirected to the Veriqa sign-in page and comes back known;
//   3. the person's data  the card is built from the claims their channel gave;
//   4. step-up            closing or deleting a ticket is confirmed in the channel, BY THAT PERSON.
//
// One process plays three roles at once, as in samples/dotnet/inproc/step-up: the Veriqa issuer
// (inproc), the relying party the customer signs in to, and the backend that calls the public
// confirmation API over HTTP.
//
// The ticket list is not stored — it is synthesised from the `sub` claim (TicketStore), so the same
// person sees the same tickets on every visit without the sample shipping a database.

using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using Veriqa.Core.AuthServer.DependencyInjection;
using Veriqa.Core.ChannelAdapter.DependencyInjection;
using Veriqa.Core.Contracts;
using Veriqa.Core.TransactionEngine.Identity;
using Veriqa.Sample.DotNet.Showcase.SupportDesk;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------------
// Role 1 — the Veriqa issuer, embedded in this process.
// ---------------------------------------------------------------------------------------------
builder.Services.AddVeriqaAuthServer(builder.Configuration, builder.Environment, authServer =>
{
    authServer.UseInMemoryOpenIddictStore();
    authServer.ConfigureTransactionEngine(te => te.UseInMemoryStore());

    // The three channels this sample offers. MAX is neither registered nor referenced: a channel a
    // sample does not offer is left out of the host, not switched off in configuration.
    authServer.AddChannelAdapters(adapters =>
    {
        adapters.AddTelegram();
        adapters.AddWhatsApp();
        adapters.AddEmail();
    });
});

// Channel identities (channel user id, phone, email, display name) are PII, and Veriqa ships no
// implementation of the port that stores them. Without this registration the sign-in still works,
// but nothing is kept between transactions.
builder.Services.AddSingleton<IChannelIdentityRepository, InMemoryChannelIdentityRepository>();

// Running behind an HTTP tunnel is optional here — the section is read in an ordinary browser — but
// the wiring is the same as in streaming-tv, and it costs nothing to keep: the connector forwards
// over loopback, so the KnownProxies/KnownIPNetworks restrictions are cleared.
// UseHttpsRedirection is deliberately never called: a tunnel terminates TLS, and redirecting to
// HTTPS behind it loops forever.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
});

// The page's own files are revalidated on every load (a 304 when unchanged). Without a
// Cache-Control header a browser caches them by heuristic, and a television that keeps yesterday's
// app.js next to today's index.html runs a script against markup it was not written for.
builder.Services.Configure<StaticFileOptions>(options =>
    options.OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
        Microsoft.Net.Http.Headers.CacheControlHeaderValue.NoCacheString);

var sampleOptions = builder.Configuration.GetSection(SampleOptions.SectionName)
    .Get<SampleOptions>() ?? new SampleOptions();
builder.Services.AddSingleton(sampleOptions);

// ---------------------------------------------------------------------------------------------
// Role 2 — the relying party the customer signs in to. Plain OpenID Connect, pointed at the issuer
// this very process hosts.
// ---------------------------------------------------------------------------------------------
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        // The issuer's base address: the handler discovers the endpoints and signing keys from
        // {Authority}/.well-known/openid-configuration.
        options.Authority = sampleOptions.Authority;
        options.ClientId = sampleOptions.WebClientId;

        // The web client is registered without a secret — a PUBLIC client, from which Veriqa requires
        // PKCE (S256). Leaving ClientSecret unset is deliberate.
        options.UsePkce = true;
        options.ResponseType = OpenIdConnectResponseType.Code;

        // The authorization code comes back in the query of a plain redirect. The handler's default is
        // form_post, which cannot travel by redirect: the issuer then answers with an auto-submitting
        // page, and the browser shows a "Completing sign-in" screen between the sign-in and the app.
        // With code + PKCE the query is enough — the code is single-use and worthless without the
        // verifier this client holds.
        options.ResponseMode = OpenIdConnectResponseMode.Query;

        // The correlation and nonce cookies go out SameSite=Lax instead of the handler's None. Chromium
        // 51-66 — the engine of webOS 4.x televisions — rejects SameSite=None outright, and the return
        // then fails with "Correlation failed". Lax is enough here: with the code in the query the
        // return is a top-level GET to this same site, which carries Lax cookies in every engine.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SameSite = SameSiteMode.Lax;

        // Must match one of the client's AllowedRedirectUris on the issuer exactly: the comparison is
        // literal and wildcards are not supported.
        options.CallbackPath = SampleRoutes.CallbackPath;

        // Keep the claims under their OIDC names. The handler otherwise renames the well-known ones
        // to the long WS-Federation URIs, and the card below — which reads `given_name`, `picture`,
        // `channel_type` and the rest by the names Veriqa issued them under — would find nothing.
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = VeriqaClaimTypes.Name;

        // openid and profile are requested by the handler already; these are the extras, and every
        // one of them must be listed in the client's AllowedScopes on the issuer.
        options.Scope.Add(VeriqaClaimTypes.Email);
        options.Scope.Add(SampleScopes.Phone);
        options.Scope.Add(VeriqaScopes.Channel);
        options.Scope.Add(VeriqaScopes.Avatar);

        // The avatar travels in the access token only and is read from /connect/userinfo, so without
        // this line a Telegram customer's picture never reaches the card.
        options.GetClaimsFromUserInfoEndpoint = true;

        // ...and without this one it is read and thrown away: the handler copies from userinfo only
        // the claims its ClaimActions name, and the default set stops at sub, name, given_name,
        // family_name, profile and email.
        options.ClaimActions.MapUniqueJsonKey(VeriqaClaimTypes.Picture, VeriqaClaimTypes.Picture);
        options.ClaimActions.MapUniqueJsonKey(VeriqaClaimTypes.PreferredUsername, VeriqaClaimTypes.PreferredUsername);

        // Tokens are not saved into the cookie: nothing here reads them back, and the access token
        // carries the avatar a second time — a picture of tens of kilobytes, twice, in every request.
    });

// ---------------------------------------------------------------------------------------------
// Role 3 — the backend that asks Veriqa for a confirmation before a ticket is closed or deleted.
// ---------------------------------------------------------------------------------------------
builder.Services.AddSingleton<TicketStore>();
// The access token belongs to the client, not to a call: held here for the whole process, it
// survives the short-lived client the typed HttpClient hands out per call.
builder.Services.AddSingleton<VeriqaAccessTokenCache>();
builder.Services.AddHttpClient<VeriqaConfirmationClient>(http => http.BaseAddress = new Uri(sampleOptions.Authority));

var app = builder.Build();

// Before UseVeriqaAuthServer: the scheme and host of the original request have to be restored
// before anything builds an absolute address from them.
app.UseForwardedHeaders();

app.UseVeriqaAuthServer();
app.MapVeriqaAuthServer();

// ---------------------------------------------------------------------------------------------
// Pages.
// ---------------------------------------------------------------------------------------------

// One page for both states of the section — the public text and the signed-in ticket list. Which
// one it shows is decided in the browser from /api/session, so an anonymous visitor is never
// redirected away: a support section has to be readable before anyone signs in.
app.MapGet("/", (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.WebRootPath, SampleRoutes.IndexFileName), SampleRoutes.HtmlContentType));

app.MapGet(SampleRoutes.SignIn, (ClaimsPrincipal user) =>
    user.Identity?.IsAuthenticated is true
        ? Results.Redirect("/")
        : Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }));

// Local sign-out: drops this application's cookie. Veriqa exposes no end-session endpoint, so the
// issuer's own session is not ended by this.
app.MapGet(SampleRoutes.SignOut, () =>
    Results.SignOut(
        properties: new AuthenticationProperties { RedirectUri = "/" },
        authenticationSchemes: [CookieAuthenticationDefaults.AuthenticationScheme]));

// ---------------------------------------------------------------------------------------------
// API. Every route but /api/session answers an anonymous caller with 401 rather than a redirect:
// these are read by fetch(), and a sign-in page arriving as the body of an XHR helps no one.
// ---------------------------------------------------------------------------------------------

app.MapGet(SampleRoutes.Session, (ClaimsPrincipal user) =>
{
    var profile = ViewerProfile.FromClaims(user);

    return Results.Ok(new SessionResponse(profile is not null, profile));
});

app.MapGet(SampleRoutes.Tickets, (ClaimsPrincipal user, TicketStore tickets) =>
{
    var subject = Subject(user);

    return subject is null
        ? Unauthenticated()
        : Results.Ok(new TicketsResponse(tickets.TicketsFor(subject)));
});

app.MapPost(SampleRoutes.Resolution, async (
    string ticketId,
    [FromBody] ResolutionRequest request,
    ClaimsPrincipal user,
    TicketStore tickets,
    VeriqaConfirmationClient veriqa,
    SampleOptions options,
    CancellationToken cancellationToken) =>
{
    var subject = Subject(user);
    if (subject is null)
    {
        return Unauthenticated();
    }

    var action = ParseAction(request);
    if (action is null)
    {
        return Results.Json(new ErrorResponse(SampleErrors.UnknownAction), statusCode: StatusCodes.Status400BadRequest);
    }

    // A ticket of somebody else does not exist for this session: the list is derived from the asking
    // customer's own subject, so there is nothing to leak by answering 404.
    var ticket = tickets.Find(subject, ticketId);
    if (ticket is null)
    {
        return Results.NotFound();
    }

    if (action is TicketAction.Resolve && ticket.Status is TicketStatus.Resolved)
    {
        return Results.Json(new ErrorResponse(SampleErrors.AlreadyResolved), statusCode: StatusCodes.Status409Conflict);
    }

    // The step-up itself: the confirmation is addressed to the very person holding this session, by
    // the channel identity Veriqa issued them. A confirmation by anyone else comes back with no
    // match, and the ticket is left alone.
    var expected = ExpectedIdentity(user);
    if (expected is null)
    {
        // Both claims arrive with the `channel` scope, which the web client requests and its
        // AllowedScopes admit. Nothing to address the step-up with means that wiring is broken, not
        // that the customer did something unusual.
        return Results.Json(
            new ErrorResponse(SampleErrors.ChannelIdentityUnavailable),
            statusCode: StatusCodes.Status500InternalServerError);
    }

    // Closing a ticket and deleting it are different questions, so they are different action types
    // with wordings of their own — not one action with a parameter.
    var actionType = action is TicketAction.Delete ? options.DeleteActionType : options.ResolveActionType;

    try
    {
        var created = await veriqa.CreateAsync(
            actionType,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SampleSlots.Ticket] = ticket.Id,
                [SampleSlots.Subject] = ticket.Subject
            },
            expected,
            // The confirmation goes to the channel this session came through — the only one where
            // the expected identity can confirm at all. Naming it makes the QR a deep link straight
            // into that channel; left out, with several channels enabled, the QR would open the
            // channel-choice page instead.
            ViewerProfile.Value(user, VeriqaClaimTypes.ChannelType),
            cancellationToken);

        // What a confirmed transaction does, and to which ticket, is remembered HERE: the page is
        // never asked, so no browser can delete one ticket by confirming another.
        tickets.StartResolution(created.TransactionId, ticket.Id, subject, action.Value);

        return Results.Ok(new ResolutionStartedResponse(
            created.TransactionId,
            created.ChannelEntry.Qr,
            created.ChannelEntry.Url,
            action.Value,
            created.ChannelEntry.ChannelType));
    }
    catch (VeriqaCallException refused)
    {
        return Results.Content(refused.Body, SampleRoutes.JsonContentType, statusCode: refused.StatusCode);
    }
});

app.MapGet(SampleRoutes.ResolutionResult, async (
    string transactionId,
    ClaimsPrincipal user,
    TicketStore tickets,
    VeriqaConfirmationClient veriqa,
    CancellationToken cancellationToken) =>
{
    var subject = Subject(user);
    if (subject is null)
    {
        return Unauthenticated();
    }

    var resolution = tickets.FindResolution(transactionId, subject);
    if (resolution is null)
    {
        return Results.NotFound();
    }

    // An operation already decided keeps its verdict: polling it again costs Veriqa nothing.
    if (resolution.State is not ResolutionState.Pending)
    {
        return Results.Ok(resolution);
    }

    try
    {
        var result = await veriqa.GetResultAsync(transactionId, cancellationToken);

        if (result.Outcome == TicketStore.PendingOutcome)
        {
            return Results.Ok(resolution);
        }

        if (result.Outcome != VeriqaConfirmationClient.ConfirmedOutcome)
        {
            return Results.Ok(tickets.Conclude(transactionId, result.Outcome, ResolutionState.Refused));
        }

        // The decision a step-up exists for: confirmed is not enough, it has to be the expected
        // person. Confirmed by anyone else, the result carries no match and the ticket is untouched.
        var expectedType = ExpectedIdentityType(user);
        var state = expectedType is not null && result.MatchedType == expectedType
            ? ResolutionState.Applied
            : ResolutionState.Mismatched;

        return Results.Ok(tickets.Conclude(transactionId, result.Outcome, state));
    }
    catch (VeriqaCallException refused)
    {
        return Results.Content(refused.Body, SampleRoutes.JsonContentType, statusCode: refused.StatusCode);
    }
});

app.Run();

// The customer this session belongs to, or null when nobody is signed in.
static string? Subject(ClaimsPrincipal user) => ViewerProfile.Value(user, SampleClaims.Subject);

// The answer an anonymous caller of an API route gets: a status the page can act on, not a redirect
// to a sign-in page it cannot render inside a fetch.
static IResult Unauthenticated() =>
    Results.Json(new ErrorResponse(SampleErrors.Unauthenticated), statusCode: StatusCodes.Status401Unauthorized);

// The operation the body asked for, or null when it named one the desk does not have.
static TicketAction? ParseAction(ResolutionRequest? request)
{
    if (request?.Action is null)
    {
        return null;
    }

    if (string.Equals(request.Action, SampleActionNames.Resolve, StringComparison.Ordinal))
    {
        return TicketAction.Resolve;
    }

    return string.Equals(request.Action, SampleActionNames.Delete, StringComparison.Ordinal)
        ? TicketAction.Delete
        : null;
}

// Who the confirmation expects, as Veriqa reads it: the declared comparable type of the channel this
// session came through, carrying this customer's identity in that channel.
static Dictionary<string, string>? ExpectedIdentity(ClaimsPrincipal user)
{
    var identityType = ExpectedIdentityType(user);
    var channelUserId = ViewerProfile.Value(user, VeriqaClaimTypes.ChannelUserId);

    return identityType is null || channelUserId is null
        ? null
        : new Dictionary<string, string>(StringComparer.Ordinal) { [identityType] = channelUserId };
}

// The comparable type declared for this session's channel, or null for a channel the sample does not
// offer. The mapping is one table (SampleIdentityTypes), never a literal at the point of use.
static string? ExpectedIdentityType(ClaimsPrincipal user)
{
    var channelType = ViewerProfile.Value(user, VeriqaClaimTypes.ChannelType);

    return channelType is null ? null : SampleIdentityTypes.ForChannel(channelType);
}
