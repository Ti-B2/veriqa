// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

// Display-only demo-stand preview form: sign-in (login) via an EXTERNAL Veriqa issuer.
// One form for two modes — self-hosted (a separate Veriqa instance) and Veriqa Cloud:
// the client application code is identical, ONLY the configuration differs (Authority + channels).
// Illustrative configuration sample; not a runnable host.
//
// The client is a standard OIDC Relying Party on Microsoft.AspNetCore.Authentication.OpenIdConnect
// (Authorization Code + PKCE). No Veriqa-specific packages: Veriqa lives on the issuer side.
// Fragments between `region:snippet` / `region:snippet-routing` are the source of truth for the code snippet.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// region:snippet
// Standard OIDC client pointed at the external Veriqa issuer (self-hosted or Cloud). Authority and
// ClientId are read from the application configuration (Oidc section) — see the config tab.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        options.Authority = builder.Configuration["Oidc:Authority"];
        options.ClientId = builder.Configuration["Oidc:ClientId"];
        options.ClientSecret = builder.Configuration["Oidc:ClientSecret"];
        options.ResponseType = "code";
        options.UsePkce = true;
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.GetClaimsFromUserInfoEndpoint = true;
        options.SaveTokens = true;

        // Veriqa issues the user's name as the short OIDC `name` claim, while User.Identity.Name
        // looks up ClaimTypes.Name (the long WS-Federation URI) by default — without this line the
        // name stays empty after a successful sign-in.
        options.TokenValidationParameters.NameClaimType = "name";
    });
// endregion:snippet

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// region:snippet-routing
// Login scenario: after signing in via a trusted channel, "Hello, {name}!" is displayed.
// The name is taken from claims issued by the external Veriqa issuer.
app.MapGet("/", (ClaimsPrincipal user) =>
    user.Identity?.IsAuthenticated is true
        ? Results.Text($"Hello, {user.Identity.Name}!")
        : Results.Challenge());
// endregion:snippet-routing

app.Run();
