# Veriqa sample — .NET · inproc · step-up before a destructive action

Runnable sample of a **step-up confirmation**: the user works in an application, and deleting a
project cannot be undone. Before deleting, the backend asks the **owner of the account** to confirm
in Telegram ("Delete the project Q3 report with its 12 documents? This cannot be undone."), and
deletes only when the person who confirmed is that owner.

The application is both the Veriqa issuer (inproc) and the relying-party backend that calls it over
the public HTTP API, as in the [confirmation sample](../confirmation/README.md).

## What it does

1. `POST /api/transaction/confirmation` with `action_type: delete-project`, the project in
   `slot_values`, and `expected_identities: { "telegram_user_id": "<owner>" }`.
2. The page shows `channel_entry.qr`; the backend polls `GET /api/transaction/{id}/result`.
3. The project is deleted only on `outcome: confirmed` **and** `matched_type: telegram_user_id`.
   Confirmed by anyone else, the result carries `matched_type: null`, and nothing is deleted.

**Who the owner is.** A real application knows the owner's channel identity from account linking
(see *Linking from your server* below). To stay self-contained, this sample binds the account to the
first person who confirms a deletion: that confirmation is exchanged for an `id_token`, and its
`channel_user_id` becomes the owner. **Forget the owner** on the page starts over.

## The configuration that makes it work

The client entry `projects-backend` in `appsettings.json`:

- `AllowClientCredentials: true` — the backend may call the API (needs a `ClientSecret`);
- `AllowConfirmationTokenGrant: true` and `openid` in `AllowedScopes` — the exchange that binds the
  first owner;
- `IdentityMatchComparableTypes` — declares `telegram_user_id`, compared with the `channel_user_id`
  claim of the person who confirmed, by the `Exact` rule. Without this declaration the request is
  refused with `candidate_type_undeclared`. The claim is a Telegram user id only because this sample
  enables Telegram alone; with several channels, compare an identity that does not depend on the
  channel;
- `MessageTemplates:delete-project` — the action type: `Contract:Slots` and `Templates`.

Beside the client entry, the host section of `appsettings.json` words what the user sees when the
transaction ends:

- `Veriqa:Channels:OutcomeNotice:DisplayIntent: "NewMessage"` — the receipt of the outcome arrives as
  a message of its own instead of replacing the question. The shipped value is `ReplacePrompt`; a
  confirmation is the case for the other one, because the question carries the wording of the action
  and replacing it would leave the conversation with an outcome and no record of what it answered;
- `Veriqa:MessageTemplates:outcome-receipt-confirmed` / `…-declined` — the receipts name the calling
  application through the server slot `{app}`, which for a confirmation is the `ClientId`.

The client secret for Development is in `appsettings.Development.json`, on both sides
(`Veriqa:OpenIddict:Clients` and `StepUpSample`). Never ship it.

## Prerequisites

- .NET SDK 10.0+
- A trusted development certificate — the backend calls `https://localhost:7330`:

  ```bash
  dotnet dev-certs https --trust
  ```

- A Telegram bot token from [@BotFather](https://t.me/BotFather).

## Running

```bash
dotnet user-secrets --project samples/dotnet/inproc/step-up set "Veriqa:Channels:Telegram:BotToken" "<token>"
dotnet user-secrets --project samples/dotnet/inproc/step-up set "Veriqa:Channels:Telegram:Enabled" "true"
dotnet run --project samples/dotnet/inproc/step-up
```

Open `https://localhost:7330` and press **Delete** next to a project. Scan the QR, press **Start** in
the bot and confirm: the project is deleted and your Telegram account becomes the owner. Delete a
second project from another Telegram account — the confirmation succeeds, but the page answers
*Not deleted — confirmed, but not by the owner*.

## Further reading

- Server-to-server confirmation API — <https://veriqa.app/docs/reference/confirmation-api>
- Linking from your server — <https://veriqa.app/docs/scenarios/channel-linking>
