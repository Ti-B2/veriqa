# Display-only preview form of the demo stand: sign-in via the EXTERNAL Veriqa issuer on Python.
# The only mode is remote (a client of the external issuer); one file per language (the scenario
# does not fork the file — scenario specifics live in the cell instruction, not in code).
# A runnable version is a TODO.
#
# The client is a standard OIDC Relying Party built on Authlib (+ FastAPI/Starlette). No
# Veriqa-specific SDKs: Veriqa lives on the issuer side. ISSUER and CLIENT_ID are read from
# the application environment (.env) — see the config tab. Python markers use the `#` prefix.

import os

from authlib.integrations.starlette_client import OAuth
from fastapi import FastAPI, Request

app = FastAPI()

# region:snippet
# Standard OIDC client (Authorization Code + PKCE) to the external Veriqa issuer. Provider
# metadata is fetched via server_metadata_url (OIDC discovery), credentials come from .env.
oauth = OAuth()
oauth.register(
    name="veriqa",
    server_metadata_url=f"{os.environ['ISSUER']}/.well-known/openid-configuration",
    client_id=os.environ["CLIENT_ID"],
    client_secret=os.environ.get("CLIENT_SECRET"),
    client_kwargs={"scope": "openid profile email", "code_challenge_method": "S256"},
)


@app.get("/login")
async def login(request: Request):
    redirect_uri = os.environ["REDIRECT_URI"]
    return await oauth.veriqa.authorize_redirect(request, redirect_uri)
# endregion:snippet


# region:snippet-routing
# The login scenario: after signing in via a trusted channel, "Hello, {name}!" is shown.
# The name comes from the claims issued by the external Veriqa issuer.
@app.get("/")
async def home(request: Request):
    user = request.session.get("user")
    return f"Hello, {user['name']}!" if user else "Hello!"
# endregion:snippet-routing
