# Veriqa showcase — Helio, the support section of a website

A runnable **emulator of a product**, not of one mechanism — the sibling of
[`showcase/streaming-tv`](../streaming-tv/README.md) in a different shape. It shows the same four
things an ordinary web product strings together:

1. **A public page** — the support section is readable by anyone; nobody is redirected on arrival.
2. **Sign-in** — the visitor is redirected to the Veriqa sign-in page and comes back known.
3. **The person's data** — the card is built from the claims their channel gave: display name, first
   and last name, picture, @username and masked user id (Telegram), masked phone (WhatsApp), address
   (Email).
4. **Step-up** — closing or deleting a ticket is confirmed in the channel, **by that person**.

The two leave different receipts in the chat. A sign-in's question is replaced by
"Sign-in to Helio Support confirmed ✅" (`ReplacePrompt`, the global `OutcomeNotice` section). A
ticket action keeps its question and gets the receipt as a message of its own (`NewMessage`, stated
in the backend client's entry — the client that creates the confirmations), so what was approved
stays readable.

The page belongs to the fictional company. The sign-in page and the confirmation window are Veriqa's
own screens, shown as they are.

## How it is put together

One process plays three roles, as in [`inproc/step-up`](../../inproc/step-up/README.md):

| Role | What it is |
|---|---|
| Veriqa issuer (inproc) | `AddVeriqaAuthServer` → `UseVeriqaAuthServer` → `MapVeriqaAuthServer` |
| Relying party | the standard `AddOpenIdConnect` handler, pointed at this very process |
| Backend | `VeriqaConfirmationClient`, calling the public confirmation API over HTTP |

### Where the tickets come from

They are **synthesised**, not stored: `TicketStore.Synthesize` is a pure function of the `sub` claim,
so the same person sees the same four to seven tickets on every visit and the sample ships no
database. The hash behind it is FNV-1a written out in the file — `string.GetHashCode` is randomised
per process and would hand the same person a different list after every restart.

What *is* kept in memory is the difference the customer has made since: which tickets they closed and
which they deleted. A restart forgets that difference and the list returns to its synthesised state.

### The step-up, exactly

Closing a ticket and deleting it are **two action types**, `resolve-ticket` and `delete-ticket`, with
wordings of their own — not one type with a parameter. Each confirmation names **one** expected
identity, the one belonging to the channel the current session came through:

| Channel of the session | Declared comparable type | Normalisation |
|---|---|---|
| `telegram` | `telegram_user_id` | `Exact` |
| `whatsapp` | `whatsapp_phone` | `E164Phone` |
| `email` | `email_address` | `EmailDomainCaseFold` |

All three compare the same claim, `channel_user_id`; only the rule for reading an identity differs.
The names live in `SampleIdentityTypes` and in `IdentityMatchComparableTypes` of the
`helio-support-backend` client — two halves of one declaration, because a confirmation naming an
undeclared type is refused with `candidate_type_undeclared`.

| Outcome from Veriqa | `matched_type` | State of the operation |
|---|---|---|
| `confirmed` | the expected type | `applied` — the ticket is closed or deleted |
| `confirmed` | absent or another type | `mismatched` — the ticket is untouched |
| `declined` / `expired` / `failed` | — | `refused` — the ticket is untouched |

Which ticket a transaction acts on, and how, is remembered **on the server**: the page is never
asked, so no browser can delete one ticket by confirming another.

The customer is asked twice on purpose. The dialog in the page is the cheap question — it prevents a
misclick. The question in the channel is the one that decides.

### What stays on the server

`channel_user_id` never leaves it in full — it addresses the step-up. The card shows a Telegram user
id masked (`12*****89`, `ViewerProfile.MaskIdentifier`), and the phone number is masked the same way
(`ViewerProfile.MaskPhone`) **before** either is put into a response, so the browser is never handed
the full value.

## The API

Every route but `GET /api/session` answers an anonymous caller `401 {"error":"unauthenticated"}`.

```
GET  /api/session                        → { authenticated, profile }
GET  /api/tickets                        → { tickets: [ { id, subject, status, createdAt, lastMessage } ] }
POST /api/tickets/{id}/resolution        body { "action": "resolve" | "delete" }
                                         → { transactionId, qr, url, action, channelType }
                                           400 {"error":"unknown_action"} · 404 not your ticket
                                           409 {"error":"already_resolved"}
GET  /api/tickets/resolutions/{txn}      → { transactionId, ticketId, action, state, outcome }
                                           404 — unknown, or started by another session
```

`status` is `open` · `waiting` · `resolved`; `state` is `pending` · `applied` · `refused` ·
`mismatched`. A refusal from Veriqa is passed through with its own status and body.

**Two operations on one ticket at once.** Both are created normally. The one confirmed first is
applied; the second then reports `applied` as well and changes nothing more — applying is
idempotent, and the end state it asks for has already been reached. A *new* request for that ticket
afterwards answers by its state: `404` when it was deleted, `409 already_resolved` when it was
closed.

## Prerequisites

- .NET SDK 10.0+
- A trusted development certificate — the application calls **itself** over HTTPS (the OIDC handler
  reads discovery, the backend calls `/connect/token`):

  ```bash
  dotnet dev-certs https --trust
  ```

- Credentials of at least one channel. A Telegram bot token from
  [@BotFather](https://t.me/BotFather) is the shortest path.

## Running

Channels ship **disabled with empty credentials**, as everywhere in this gallery: a channel switched
on without its credentials fails the start, and `dotnet run` has to work out of the box. Switch on
the ones you have:

```bash
dotnet user-secrets --project samples/dotnet/showcase/support-desk set "Veriqa:Channels:Telegram:BotToken" "<token>"
dotnet user-secrets --project samples/dotnet/showcase/support-desk set "Veriqa:Channels:Telegram:Enabled" "true"
dotnet run --project samples/dotnet/showcase/support-desk
```

Open `https://localhost:7360`. WhatsApp and Email are switched on the same way, by filling their
sections of `appsettings.json` (`Veriqa:Channels:WhatsApp:*`, `Veriqa:Channels:Email:*`) and setting
`Enabled` to `true`; the sign-in page then offers exactly the channels that are on.

**An empty sign-in page** — no way in offered at all — means no channel is enabled. That is the
first thing to check.

Over the sign-in card stands a row of two: `Veriqa:AuthPageDesign:LogoUrl` points at
`wwwroot/veriqa-mark.svg` — the mark of Veriqa, the same rounded square the core draws itself — and
`Veriqa:AuthPageDesign:BrandName` puts the desk's name, Helio, beside it. Name neither, and the page
shows the product's mark alone. The attribution line under the card stays either way: it is not a
branding setting.

MAX is not part of this sample: its package is not referenced and its adapter is not registered, so
there is nothing to switch on.

### Behind a tunnel

Not needed here — the section is read in an ordinary browser — but the wiring is the same as in
`streaming-tv`, so it works identically:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://localhost:5360 \
Veriqa__OpenIddict__Server__Issuer=https://<public-host> \
Veriqa__OpenIddict__Clients__0__AllowedRedirectUris__0=https://<public-host>/signin-oidc \
SupportDeskSample__Authority=https://<public-host> \
Veriqa__Channels__Telegram__UpdateMode=Webhook \
Veriqa__Channels__Telegram__WebhookBaseUrl=https://<public-host> \
Veriqa__Channels__Telegram__WebhookSecretToken=<any-long-random-string> \
dotnet run --project samples/dotnet/showcase/support-desk
```

Set all of them together, and keep the environment at `Development`: outside it Veriqa asks for
production PFX signing and encryption certificates. A public address is configuration, not a
different environment.

**Telegram update mode.** Behind a tunnel the channel runs in `Webhook` mode; without one it runs in
`Polling`, which is what `appsettings.Development.json` ships. Switching back to `Polling` while the
bot still has a webhook registered silently delivers nothing — remove it first with `deleteWebhook`
in the Bot API.

## The page

Plain responsive HTML, CSS and JavaScript, with a dark variant through
`prefers-color-scheme`. Nothing is loaded from outside the origin — no CDN fonts, no image files;
the three-step drawing on the public page is inline SVG built in the script.

The syntax level is **ES2018**, pinned for `samples/dotnet/showcase/**/*.js` in the root
`eslint.config.mjs`. This page would survive a newer level on its own; its neighbour running on
Tizen 5.5 and webOS 5 televisions would not, and the two samples share their plumbing.

```bash
npx eslint samples/dotnet/showcase
```

## Further reading

- Server-to-server confirmation API — <https://veriqa.app/docs/reference/confirmation-api>
- Linking from your server — <https://veriqa.app/docs/scenarios/channel-linking>
