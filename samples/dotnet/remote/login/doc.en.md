#### Prerequisites

- .NET SDK 10.0+
- `dotnet add package Microsoft.AspNetCore.Authentication.OpenIdConnect`
- A reachable external issuer: a deployed Veriqa instance (self-hosted) or Veriqa Cloud
- A client registered on the issuer side: the `ClientId` and the `redirect_uri` of your application

#### Run steps

1. Paste the integration fragment into your application's `Program.cs`.
2. Set `Oidc:Authority` (the issuer address) and `Oidc:ClientId` in the configuration — see the
   configuration tab.
3. Start your application and open `/`: an unauthenticated visit is redirected to the Veriqa
   sign-in page.

#### About this combination

login — standard user sign-in through a trusted channel. What is shown here is the **client**
code: your application is an ordinary OIDC relying party on
`Microsoft.AspNetCore.Authentication.OpenIdConnect` (Authorization Code + PKCE). It references no
Veriqa package at all — Veriqa lives on the issuer side, and the application talks to it over
plain OpenID Connect, exactly as it would to any conformant provider.

The self-hosted and cloud modes share **the very
same client code**: only the configuration differs — `Authority` points either at your own
instance or at the managed one, and the set of channels is decided on the issuer side. That is
why both modes map to a single code form.

The `telegram`, `whatsapp`, `max` and `email` channels are enabled and configured on the issuer; the
client knows nothing about them and can only narrow the choice with `acr_values=channel:{type}`.

:::note This form is display-only: it is a configuration fragment to embed in your application,
not a runnable host — no build ZIP is offered here. For a runnable "issuer + client" pair to try
locally, see `samples/dotnet/inproc/login` and `samples/dotnet/inproc/login-client`.
