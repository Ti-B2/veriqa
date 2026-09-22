# Veriqa showcase — Nova Stream, a streaming service on a television

A runnable **emulator of a product**, not of one mechanism. The other samples of the gallery each
show a single thing — a sign-in, a confirmation, a step-up. This one shows what a product actually
strings together inside one scenario:

1. **Sign-in** — the viewer is redirected to the Veriqa sign-in page and comes back known.
2. **The person's data** — the card on screen is built from the claims their channel gave: display
   name, first and last name, picture, @username and masked user id (Telegram), masked phone
   (WhatsApp), address (Email).
3. **Recognition** — a viewer who has been here before lands straight in the catalogue; a first-time
   viewer is welcomed by name.
4. **Step-up** — buying a title is confirmed in the channel, **by the viewer who asked**. A
   confirmation from anybody else buys nothing.

The two leave different receipts in the chat. A sign-in's question is replaced by
"Sign-in to Nova Stream confirmed ✅" (`ReplacePrompt`, the global `OutcomeNotice` section). A
purchase keeps its question and gets the receipt as a message of its own (`NewMessage`, stated in
the backend client's entry — the client that creates the confirmations), so what was approved
stays readable.

The interface belongs to the fictional service: a ten-foot layout, remote-control navigation, and no
interaction that needs a pointer. The sign-in page and the confirmation window are Veriqa's own
screens, shown as they are — the sample does not dress its own pages up as the product's.

## How it is put together

One process plays three roles, as in [`inproc/step-up`](../../inproc/step-up/README.md):

| Role | What it is |
|---|---|
| Veriqa issuer (inproc) | `AddVeriqaAuthServer` → `UseVeriqaAuthServer` → `MapVeriqaAuthServer` |
| Relying party | the standard `AddOpenIdConnect` handler, pointed at this very process |
| Backend | `VeriqaConfirmationClient`, calling the public confirmation API over HTTP |

Splitting the three into separate hosts changes nothing but the addresses.

### The step-up, exactly

`POST /api/catalog/{titleId}/purchase` creates a confirmation that names **one** expected identity —
the one belonging to the channel the current session came through:

| Channel of the session | Declared comparable type | Normalisation |
|---|---|---|
| `telegram` | `telegram_user_id` | `Exact` |
| `whatsapp` | `whatsapp_phone` | `E164Phone` |
| `email` | `email_address` | `EmailDomainCaseFold` |

All three compare the same claim, `channel_user_id`; only the rule for reading an identity differs.
The names live in `SampleIdentityTypes` and in `IdentityMatchComparableTypes` of the
`nova-stream-backend` client — two halves of one declaration, because a confirmation naming an
undeclared type is refused with `candidate_type_undeclared`.

The verdict is then:

| Outcome from Veriqa | `matched_type` | State of the purchase |
|---|---|---|
| `confirmed` | the expected type | `purchased` — the title is in the library |
| `confirmed` | absent or another type | `mismatched` — nothing is bought |
| `declined` / `expired` / `failed` | — | `refused` — nothing is bought |

Which title a transaction pays for is remembered **on the server**: the page is never asked, so no
browser can buy one title by confirming another.

### What stays on the server

`channel_user_id` never leaves it in full — it addresses the step-up. The card shows a Telegram user
id masked (`12*****89`, `ViewerProfile.MaskIdentifier`), and the phone number is masked the same way
(`ViewerProfile.MaskPhone`) **before** either is put into a response, so the browser is never handed
the full value; masking in the page would mean shipping it to the page first.

## The API

Every route but `GET /api/session` answers an anonymous caller `401 {"error":"unauthenticated"}` —
these are read by `fetch`, and a sign-in page arriving as the body of an XHR helps no one.

```
GET  /api/session              → { authenticated, welcomeSeen, profile }
POST /api/session/welcome-seen → 204
GET  /api/catalog              → { titles: [ { id, title, genre, year, price, currency, owned } ] }
POST /api/catalog/{id}/purchase→ { transactionId, qr, channelType }
                                 404 unknown title · 409 {"error":"already_owned"}
GET  /api/purchases/{txn}      → { transactionId, titleId, state, outcome }
                                 404 — unknown, or started by another session
```

`state` is `pending` · `purchased` · `refused` · `mismatched`. A refusal from Veriqa is passed
through with its own status and body.

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
dotnet user-secrets --project samples/dotnet/showcase/streaming-tv set "Veriqa:Channels:Telegram:BotToken" "<token>"
dotnet user-secrets --project samples/dotnet/showcase/streaming-tv set "Veriqa:Channels:Telegram:Enabled" "true"
dotnet run --project samples/dotnet/showcase/streaming-tv
```

Open `https://localhost:7350`. WhatsApp and Email are switched on the same way, by filling their
sections of `appsettings.json` (`Veriqa:Channels:WhatsApp:*`, `Veriqa:Channels:Email:*`) and setting
`Enabled` to `true`; the sign-in page then offers exactly the channels that are on.

**An empty sign-in page** — no way in offered at all — means no channel is enabled. That is the
first thing to check.

### The two channels of the shop window

`appsettings.Development.json` switches WhatsApp and Email on with values that are not credentials
(`sample-display-only`, `signin@sample.invalid`). They exist so a showcase run shows what a
multi-channel sign-in page looks like — a strip of channels to choose from, which a page carrying
one channel cannot show. **Neither of them can carry a login through**: the message goes to a phone
number and a mailbox that do not exist, and Telegram is the one channel that works. Both stay off
in `appsettings.json`, so a deployment that copies the sample gets nothing of this.

Email runs in **Push** mode here rather than Pull. Pull asks the person to type their address, and
across a room there is no keyboard to type it with; Push addresses the letter for them, so the panel
carries a QR code like the other two channels instead of a form.

### Wording and brand of the sign-in page

The same page of the core opens for two different clients, and each words it its own way. The
sign-in is started by `nova-stream-tv`, the shared QR of a purchase by `nova-stream-backend`; each
names its own record in `Veriqa:UiConfigurations:Records` as `DefaultUiConfig`, so the sign-in reads
"Sign in via messenger" and the purchase "Confirm your purchase". The records state the title only:
an instruction stated by a record is a single wording, while the core's own comes as a pair — with a
QR and without one — and the page picks the half that matches what it shows on a television or a
phone.

The instruction is reworded through the locale files of the host instead, `wwwroot/locales/en.json`:
where the QR is on screen the channel buttons are hidden, so the wording promises the QR alone. A key
the file leaves out keeps the core's own text.

Over the card stands a row of two: `Veriqa:AuthPageDesign:LogoUrl` points at `wwwroot/veriqa-mark.svg`
— the mark of Veriqa, the same rounded square the core draws itself — and
`Veriqa:AuthPageDesign:BrandName` puts the shop's name beside it. Name neither, and the page shows
the product's mark alone, which is right for a bare installation and wrong for a sample pretending
to be a streaming service. The attribution line under the card stays in either case — it is not a
branding setting.

### The sign-in page on a ten-foot screen

`wwwroot/veriqa-auth-page-tv.css` takes the "Open in …" buttons off the page of the core: across a
room nobody can follow them, and the way in is the QR code scanned with a phone. It is attached
through `Veriqa:AuthPageDesign:CustomCssPath` as a rooted path, so it follows the sample behind a
tunnel without a variable of its own.

A phone that opens the page keeps the buttons and loses the QR instead: the sample sets
`Veriqa:AuthPageDesign:QrCode:ShowQrCode` to `DesktopOnly`, the core marks a phone on the page
(`data-device="mobile"` — a coarse pointer and a phone User-Agent together), and the sheet hides the
buttons only where that mark is absent. A television is not a phone by either sign and keeps its QR.

The same sheet does two more things. It enlarges the card on screens 1600 CSS pixels wide or wider:
the core draws the card in pixels, and a full-HD television renders 1920 of them at a device pixel
ratio of 1, so the page is otherwise a stamp in the middle of the screen. And it restates the card's
width and padding for engines older than the core's own CSS — webOS 4.x runs Chromium 53, which
reads neither `min()` nor logical padding.

The core offers no setting for this, which is why it takes a stylesheet — and the price is real:
attaching ANY sheet makes the core ignore the design preset (CFG-023) and render its base look. The
same goes for showing every channel's QR at once instead of behind tabs: the page of the core lays
several channels out as tabs and nothing configures that, so this sample does not attempt it. Both
are known limitations of the sign-in page on a large screen, raised with the product; when either
becomes a setting, this sheet gets smaller or goes away.

MAX is not part of this sample: its package is not referenced and its adapter is not registered, so
there is nothing to switch on.

### On the television

The remote:

| Key | What it does |
|---|---|
| arrows | move the focus |
| `Enter` | select |
| `Backspace`, Tizen `10009`, webOS `461` | close the purchase window |
| `F` (desktop) | full screen — the Fullscreen API needs a gesture of the person's own |

There is no full-screen button on the page: on a television the page is full-screen already, and a
button standing over every screen got in the way. Full screen from `F` lasts only until the next
page: the Fullscreen API belongs to one document, so going to the sign-in page leaves it, and nothing
can carry it over. The browser's own full-screen
control (in the toolbar of the webOS browser) survives navigation — use that one for a filmed run.

The QR of the purchase window is drawn at 12 px per module
(`Veriqa:AuthPageDesign:QrCode:PixelsPerModule`) rather than the default 6: it is scanned off the
screen with a phone camera, from across the room.

## Behind a tunnel

A television cannot reach `localhost`. Put the sample behind an HTTP tunnel — nothing in the code
changes, only the environment:

```bash
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://localhost:5350 \
Veriqa__OpenIddict__Server__Issuer=https://<public-host> \
Veriqa__OpenIddict__Clients__0__AllowedRedirectUris__0=https://<public-host>/signin-oidc \
StreamingTvSample__Authority=https://<public-host> \
Veriqa__Channels__Email__PublicBaseUrl=https://<public-host> \
Veriqa__Channels__Telegram__UpdateMode=Webhook \
Veriqa__Channels__Telegram__WebhookBaseUrl=https://<public-host> \
Veriqa__Channels__Telegram__WebhookSecretToken=<any-long-random-string> \
dotnet run --project samples/dotnet/showcase/streaming-tv
```

Set **all** of them together. With the issuer left at `localhost`, discovery hands the television an
address it cannot reach and the sign-in goes nowhere.

The environment stays `Development` on purpose: outside it Veriqa asks for production PFX signing
and encryption certificates. A public address is configuration, not a different environment.

Two things in `Program.cs` serve the tunnel, and both are commented there:
`ForwardedHeadersOptions` with `XForwardedFor | XForwardedProto` and cleared `KnownProxies`
(the connector forwards over loopback), and `app.UseForwardedHeaders()` **before**
`app.UseVeriqaAuthServer()`. `UseHttpsRedirection` is never called — the tunnel terminates TLS and
the redirect would loop.

**Caching behind a tunnel.** The page's files go out with `Cache-Control: no-cache`, but the edge
of a tunnel may rewrite that header and keep `app.js` for hours; a television then runs an old script
against a new page and fails at the first element that no longer exists. `index.html` therefore names
both files with a `?v=` suffix — raise it whenever `app.js` or `styles.css` changes.

**Telegram update mode.** Behind a tunnel the channel runs in `Webhook` mode (above); without one it
runs in `Polling`, which is what `appsettings.Development.json` ships. Switching back to `Polling`
while the bot still has a webhook registered silently delivers nothing — remove it first with
`deleteWebhook` in the Bot API.

## Television browsers

Everything under `wwwroot` targets **ES2018** and the CSS of Chromium 68, with fallbacks for the
Chromium 53 of webOS 4.x where a layout depends on it (the catalogue's grid, the sign-in card). The
same engine is why the sign-in's correlation and nonce cookies go out `SameSite=Lax`: Chromium 51-66
rejects `SameSite=None`, and the return from sign-in then fails with "Correlation failed".
Tizen 5.5 and webOS 5 ship Chromium 68-69, where `?.`, `??`, top-level `await` and an optional catch binding are *parse* errors:
the file does not load at all, and nothing on screen says why. `:has()`, `:is()`, `:focus-visible`,
CSS nesting, logical properties, `clamp()`, `aspect-ratio`, flex `gap` and the `<dialog>` element
are out for the same reason — they fail quietly instead.

The limit is checked rather than remembered: the root `eslint.config.mjs` pins
`ecmaVersion: 2018` for `samples/dotnet/showcase/**/*.js`.

```bash
npx eslint samples/dotnet/showcase
```

Nothing is loaded from outside the origin — no CDN fonts, no image files. Covers are generated as
inline SVG from a hash of the title's id, so the same title always gets the same cover.

## Further reading

- Server-to-server confirmation API — <https://veriqa.app/docs/reference/confirmation-api>
- Linking from your server — <https://veriqa.app/docs/scenarios/channel-linking>
