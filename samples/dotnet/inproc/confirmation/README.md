# Veriqa sample — .NET · inproc · server-to-server confirmation

Runnable sample of a **server-to-server confirmation**: a backend asks a person to approve an action
("Approve a payment of 42.00 EUR to ACME Ltd?") in Telegram, with no sign-in and no browser redirect.

To stay one process, the application is two things at once:

- **the Veriqa issuer** (inproc, `AddVeriqaAuthServer`), configured in `appsettings.json`;
- **the relying-party backend** (`VeriqaConfirmationClient.cs`), which talks to that issuer only over
  the public HTTP API — point `ConfirmationSample:Authority` at a standalone Veriqa and it works the
  same.

## What it does

1. `POST /connect/token` with `grant_type=client_credentials`.
2. `POST /api/transaction/confirmation` with `action_type: approve-payment` and `slot_values`.
3. The page shows `channel_entry.qr` — the PNG the API returns — as an image to scan with the phone.
4. The backend polls `GET /api/transaction/{id}/result`.
5. On `confirmed` it exchanges the transaction once for the `id_token` of the person who confirmed
   (`grant_type=urn:veriqa:params:oauth:grant-type:confirmation`) and shows its claims.

## The configuration that makes it work

The client entry `payments-backend` in `appsettings.json`:

- `AllowClientCredentials: true` — the backend may get a token for the API (needs a `ClientSecret`);
- `AllowConfirmationTokenGrant: true` and `openid` in `AllowedScopes` — step 5;
- `MessageTemplates:approve-payment` — the **declaration** of the action type: `Contract:Slots` and
  `Templates`. It must be in the client entry: the host section `Veriqa:MessageTemplates` is the core
  level, and an action type declared only there is refused with `action_type_unknown`.

Beside the client entry, the host section of `appsettings.json` words what the user sees when the
transaction ends:

- `Veriqa:Channels:OutcomeNotice:DisplayIntent: "NewMessage"` — the receipt of the outcome arrives as
  a message of its own instead of replacing the question. The shipped value is `ReplacePrompt`; a
  confirmation is the case for the other one, because the question carries the wording of the action
  and replacing it would leave the conversation with an outcome and no record of what it answered;
- `Veriqa:MessageTemplates:outcome-receipt-confirmed` / `…-declined` — the receipts name the calling
  application through the server slot `{app}`, which for a confirmation is the `ClientId`.

The client secret for Development is in `appsettings.Development.json`, on both sides
(`Veriqa:OpenIddict:Clients` and `ConfirmationSample`). Never ship it.

## Prerequisites

- .NET SDK 10.0+
- A trusted development certificate — the backend calls `https://localhost:7310`, and the token
  endpoint accepts HTTPS only:

  ```bash
  dotnet dev-certs https --trust
  ```

- A Telegram bot token from [@BotFather](https://t.me/BotFather).

## Running

Pass the bot token through user secrets or the environment, not through a committed file:

```bash
dotnet user-secrets --project samples/dotnet/inproc/confirmation set "Veriqa:Channels:Telegram:BotToken" "<token>"
dotnet user-secrets --project samples/dotnet/inproc/confirmation set "Veriqa:Channels:Telegram:Enabled" "true"
dotnet run --project samples/dotnet/inproc/confirmation
```

In Development the bot runs in `Polling` mode, so no public URL is needed. Open
`https://localhost:7310`, press **Ask for approval**, scan the QR with the phone that has Telegram,
press **Start** in the bot and confirm. The page shows `Outcome: confirmed` and the claims of the
`id_token` (`sub`, `channel_type`, `channel_user_id`).

Without a channel enabled, creating a confirmation answers `503 channel_display_failed` — there is no
way in to show.

## Further reading

- Server-to-server confirmation API — <https://veriqa.app/docs/reference/confirmation-api>
- Linking from your server — <https://veriqa.app/docs/scenarios/channel-linking>
