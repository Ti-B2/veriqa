// Display-only preview form of the demo stand: sign-in via the EXTERNAL Veriqa issuer on Node.js.
// The only mode is remote (a client of the external issuer); one file per language (the scenario
// does not fork the file — scenario specifics live in the cell instruction, not in code).
// A runnable version is a TODO.
//
// The client is a standard OIDC Relying Party built on the `openid-client` package (+ express).
// No Veriqa-specific SDKs: Veriqa lives on the issuer side.
// Fragments between `region:snippet` / `region:snippet-routing` are the source of truth for the code snippet.

import express from "express";
import * as client from "openid-client";

const app = express();

// region:snippet
// Standard OIDC client (Authorization Code + PKCE) to the external Veriqa issuer.
// ISSUER and CLIENT_ID are read from the application environment (.env) — see the config tab.
const config = await client.discovery(
  new URL(process.env.ISSUER!),
  process.env.CLIENT_ID!,
  process.env.CLIENT_SECRET,
);

app.get("/login", (req, res) => {
  const codeVerifier = client.randomPKCECodeVerifier();
  // code_challenge and state are stored in the application session (omitted here for brevity).
  const url = client.buildAuthorizationUrl(config, {
    redirect_uri: process.env.REDIRECT_URI!,
    scope: "openid profile email",
    code_challenge: client.calculatePKCECodeChallenge(codeVerifier) as unknown as string,
    code_challenge_method: "S256",
  });
  res.redirect(url.href);
});
// endregion:snippet

// region:snippet-routing
// The login scenario: after signing in via a trusted channel, "Hello, {name}!" is shown.
// The name comes from the claims issued by the external Veriqa issuer.
app.get("/", (req, res) => {
  const name = (req as { user?: { name?: string } }).user?.name;
  res.send(name ? `Hello, ${name}!` : "Hello!");
});

app.listen(3000);
// endregion:snippet-routing
