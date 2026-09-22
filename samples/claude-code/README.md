# Veriqa sample — Claude Code · approving agent actions in a messenger

A Claude Code plugin that stops chosen actions until a person approves them in Telegram through a
Veriqa host running on the same machine. It works out of the box for three cases:

| Demo | What is stopped | How | Action type |
|---|---|---|---|
| 1. A tool call | `git push` and `rm -r…` in the Bash tool | `PreToolUse` hook | `agent-tool-call` |
| 2. The start of a long command cycle | `/cycle`, `/cycle-chain` — typed by you **or** started by the agent | `UserPromptSubmit` hook + `PreToolUse` on the `Skill` tool | `agent-command-start` |
| 3. A costly run with an estimate | `/veriqa-approval:costly-run` — asks to approve 1 billion input and 50 million output tokens, about 6250 USD, before it starts | the command calls the gate itself | `agent-costly-run` |

The person reads a wording declared on the Veriqa host, with the values of typed slots filled in:
"For manager: Claude Code in my-project wants to start nightly-refactor, estimated at 1000 M input
and 50 M output tokens, about 6250 USD. Start it?" Free text written by the agent never becomes the
question.

Demo 3 takes **two approvers in turn**: `developer`, then `manager`. Each confirms from their own
messenger account, enrolled once beforehand, and a confirmation from any other account refuses the
action. Demos 1 and 2 take one approval from whoever holds the link.

## Why this sample exists

The sample shows how Veriqa can be used inside a ready-made agent harness to have a person approve
sensitive agent actions — not only tool calls, but also heavy, costly runs that burn time and
tokens. Every approval, refusal and expiry is recorded in the Veriqa audit trail, so you can later
see who allowed what and when. This matters most for teams where several developers work with
agents at once.

Claude Code and development tasks are just the example chosen here. The same approach fits another
harness and other tasks: take this sample as a starting point, then rework and adapt it to your
needs — the rules, the action types and their wordings are all yours to change.

Veriqa is installed locally only to make the demo quick to set up. It is not a recommended
deployment: in a team, the approving host is a shared service that everyone's agents call.

### Why not just an "Allow" button in the harness

Claude Code already asks before it runs a tool, and an agent's chat channel can show a button too.
Approving through Veriqa adds what those buttons lack:

- **A named person, not whoever is at the keyboard.** The harness button proves only that someone
  pressed it. With an enrolled approver, the host checks which messenger account confirmed, and a
  confirmation from any other account refuses the action.
- **Someone other than the developer.** A harness button can only ask the person running the agent.
  A chain can ask a second person — here a manager approves the costly run after the developer — so
  nobody signs off their own spending.
- **A trail outside the agent's session.** The harness keeps its answers in a local transcript that
  the same user can edit or lose. Veriqa records every approval, refusal and expiry on its own host;
  with a shared host, that record sits outside the reach of every developer's machine and agent.
- **A question the agent does not write.** In an agent's own chat channel the agent phrases the
  question, and can phrase it misleadingly. Here the wording is declared on the host, and the agent
  supplies only typed values the host checks against the contract.
- **A separate device.** The answer comes from the person's phone, over a channel the agent does not
  control, so it still works when the person is away from the terminal during a long run.
- **One policy across harnesses.** The same action types and approvers serve Claude Code, another
  harness or your own agent code.

For a single developer watching their own agent, the harness button is often enough. The difference
shows when several people share agents, spending or responsibility — and only in the setups that
use it: demos 1 and 2 here take one approval from whoever holds the link, and a local host keeps
the trail on the developer's own machine.

## How it works

```
Claude Code ──hook──▶ veriqa-approval.mjs ──client credentials──▶ Veriqa (https://127.0.0.1:8443)
     ▲                       │                                        │
     └── Claude shows the QR ◀── link and QR image ◀────────── channel_entry
                             │                                        │
     the repeated call ──▶   └── polls the result ◀── confirmed / declined ◀── person, in Telegram
```

1. A hook receives the tool call or the prompt and looks it up in the rules (`rules.default.json`).
   Nothing matches — Claude Code goes on as usual.
2. A rule matches — the script gets a token, creates a confirmation
   (`POST /api/transaction/confirmation`) with the rule's action type and slot values, and hands
   Claude the link and a PNG of its QR code. The action has not run.
3. Claude shows you the QR code Veriqa returned: the plugin ships a small MCP server whose tool
   `show_approval_qr` takes only the transaction id and returns that PNG unchanged, so the agent
   copies nothing. Without the tool Claude sends the file, and gives the link only when neither
   works. You scan the code and answer in Telegram. Claude repeats the call, and this time the
   hook waits: it polls `GET /api/transaction/{id}/result`.

   Claude Code's auto permission mode may refuse to show the image, taking it for data leaving the
   machine. Allow the tools in the project's `.claude/settings.json` (`"permissions": { "allow":
   ["mcp__plugin_veriqa-approval_veriqa__show_approval_qr", "SendUserFile"] }`); a rule you set is
   applied before the auto mode decides.
4. `confirmed` — the action runs, or, in a chain, the next approver is asked. Anything else
   **refuses it**: declined, expired, confirmed by an account other than the enrolled approver,
   Veriqa unreachable, a request the host rejected, the plugin not configured. The gate never lets an
   action through because something failed.

### Approval chains

An action type listed under `chains` in the rules needs every approver of its list, in order:

```json
"chains": {
  "agent-costly-run": ["developer", "manager"]
}
```

- **Every step is a transaction of its own** that names the expected account in
  `expected_identities` and keeps to that account's messenger (`allowed_channel_types`). The next
  step is created only after the previous one is confirmed.
- **The gate checks who confirmed.** Veriqa answers with `matched_type`, the identity type that
  matched, and it does not refuse a mismatch by itself: a step confirmed by somebody else still ends
  `confirmed`. The script refuses unless `matched_type` is `channel_user_id`.
- **Veriqa does not deliver the question to the second person.** The link is a bearer one and goes
  nowhere by itself. For the demo, Claude shows the step 2 code in the same conversation and says
  so; in a real setup the link reaches that person another way — their own chat, e-mail, a ticket.
  If somebody else opens it and confirms, the check above refuses.
- **Every wait fits the caller's timeout.** One call waits for one step: a hook for up to 780 s (its
  timeout is 900 s), the costly-run command for up to 570 s (it runs under the Bash tool, whose
  limit is 600 s). A step's TTL is cut to that.
- **One account cannot hold two places in a chain.** Two approvals from one phone are one approval.

An action type without a chain keeps one approval from whoever holds the link.

### Why an approval takes two calls

A hook cannot print to the conversation while it waits: Claude Code shows what a hook returns only
once it has finished, and until then Claude waits for it too. So the QR cannot appear while the
hook waits for your answer. Instead the approval spans two calls:

- **A tool call (demo 1)** — the first call is refused with the link; Claude shows the QR and repeats
  the call exactly, and the repeated call waits for the answer. A call with a changed command is a
  new call and needs its own approval.
- **A typed command (demo 2)** — the prompt reaches Claude together with the link. Claude shows the
  QR, and its next tool call waits for the answer; if the answer is not `confirmed`, every tool stays
  closed until your next prompt. Showing the QR is itself a tool call, so the tools that display
  things are let through (`display_tools` in the rules).
- **The costly run (demo 3)** — `ask` creates the approval and prints the link, Claude shows it, and
  `wait` waits for the answer.

What ties the two calls together is a small file per approval in the temporary directory, readable
by you only. Nothing goes through without `confirmed`: if Claude never repeats the call, the approval
expires and nothing runs.

`VERIQA_APPROVAL_SHOW=browser` keeps the older behaviour: the hook waits at once and opens a page
with the QR in the browser of the machine that runs Claude Code. It needs that machine to have a
screen in front of you, which a remote or headless session does not.

## What you need

- The Veriqa auth server installed as an OS service — see
  [Install as an OS service](https://veriqa.app/docs/quickstart/os-service). Running it from the
  repository works as well.
- Node.js 18 or later on the machine where Claude Code runs.
- A Telegram bot token from [@BotFather](https://t.me/BotFather). The host uses long polling, so no
  public address is needed. If the bot has a webhook set, remove it first
  (`https://api.telegram.org/bot<token>/deleteWebhook`), otherwise polling gets `409 Conflict`.
- `openssl` for the local certificate. Git for Windows ships it.

## 1. Give the host a local HTTPS address

The token endpoint accepts HTTPS only, and the service starts on plain `http://127.0.0.1:8080`.
Make a self-signed certificate for `localhost` and `127.0.0.1`:

```bash
openssl req -x509 -newkey rsa:2048 -nodes -days 365 -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" \
  -keyout veriqa-local.key -out veriqa-local.crt
openssl pkcs12 -export -inkey veriqa-local.key -in veriqa-local.crt \
  -out veriqa-local.pfx -passout pass:<pfx password>
```

- `veriqa-local.pfx` goes to the host. On Linux, put it next to the token certificates, readable
  only by the service account:
  `sudo install -o veriqa -g veriqa -m 0600 veriqa-local.pfx /var/lib/veriqa/certs/`.
- `veriqa-local.crt` stays with Claude Code: the plugin trusts it through `VERIQA_CA_FILE`.
- Delete `veriqa-local.key` once the `.pfx` is made.

## 2. Add the fragment to the host

Copy [`host/appsettings.json`](host/appsettings.json) into the configuration directory of the
service as `appsettings.json` (`/etc/veriqa/` on Linux, `%ProgramData%\Veriqa\` on Windows). Next to
it is `appsettings.Production.json`, which the installer wrote, and the two do not overlap. Replace
every `REPLACE_ME`:

| Key | Value |
|---|---|
| `Kestrel:Endpoints:Https:Certificate:Path` / `Password` | the `.pfx` from step 1 |
| `Veriqa:Channels:Telegram:BotToken` | your bot token |
| `Veriqa:OpenIddict:Clients:[0]:ClientSecret` | a long random string, the plugin's secret |

What the fragment declares:

- `Kestrel:Endpoints` — `https://127.0.0.1:8443` for the plugin, and `http://127.0.0.1:8080` kept for
  the health probes. The section replaces `ASPNETCORE_URLS`, so both endpoints are listed.
- the client `claude-code` with `AllowClientCredentials` and, in its `MessageTemplates`, the three
  action types with their slots and wordings, plus `agent-approver-enroll` for enrolling approvers.
  `AllowConfirmationTokenGrant` with the scopes `openid` and `channel` lets the script learn, once
  per enrolment, which messenger account confirmed; `IdentityMatchComparableTypes` declares
  `channel_user_id`, the identity each step of a chain is checked against. An action type has to be declared **in the client
  entry**; the host section `Veriqa:MessageTemplates` only holds the receipts of the outcome.
- `OutcomeNotice:DisplayIntent: NewMessage` — the answer arrives as a new message and the question
  stays readable above it.

If your configuration already declares clients, add the `claude-code` entry to your own `Clients`
array. Configuration files merge arrays by index, so a second file with its own `Clients:[0]` would
overwrite fields of your first client instead of adding a new one.

Restart the service and check it:

```bash
sudo systemctl restart veriqa
curl --cacert veriqa-local.crt https://127.0.0.1:8443/health/live
```

## 3. Install the plugin

In Claude Code, from a clone of this repository:

```
/plugin marketplace add <path to the clone>/samples/claude-code
/plugin install veriqa-approval@veriqa-samples
```

Then give the plugin its settings. `env` in `~/.claude/settings.json` applies them to every project:

```json
{
  "env": {
    "VERIQA_URL": "https://127.0.0.1:8443",
    "VERIQA_CLIENT_SECRET_FILE": "/home/me/.config/veriqa/claude-code.secret",
    "VERIQA_CA_FILE": "/home/me/.config/veriqa/veriqa-local.crt"
  }
}
```

| Variable | Default | Meaning |
|---|---|---|
| `VERIQA_URL` | `https://127.0.0.1:8443` | Address of the Veriqa host |
| `VERIQA_CLIENT_ID` | `claude-code` | `ClientId` of the entry in the fragment |
| `VERIQA_CLIENT_SECRET_FILE` | — | File holding the client secret. Preferred: keep it out of `settings.json` |
| `VERIQA_CLIENT_SECRET` | — | The secret itself, when a file is not an option |
| `VERIQA_CA_FILE` | — | PEM certificate to trust, for the self-signed one from step 1 |
| `VERIQA_APPROVAL_RULES` | see below | A rules file to use instead of the default |
| `VERIQA_APPROVAL_TTL_SECONDS` | `300` | How long each approver has to answer, 60…1800. Each step is cut to 780 s in a hook (its timeout is 900 s, and a hook killed by its timeout would let the action through) and to 570 s in the costly-run command |
| `VERIQA_APPROVAL_APPROVERS` | `~/.claude/veriqa-approvers.json` | The file of enrolled approvers |
| `VERIQA_APPROVAL_LOCALE` | host default | Language of the question, a BCP 47 tag |
| `VERIQA_APPROVAL_SHOW` | `chat` | `chat` — Claude shows the QR in the conversation. `browser` — the hook waits at once and opens a page with the QR on this machine. `none` — it waits at once and shows nothing |
| `VERIQA_APPROVAL_PRICE_INPUT` | `5` | USD per million input tokens, for the cost in demo 3. The default is the Claude Opus 5 list price |
| `VERIQA_APPROVAL_PRICE_OUTPUT` | `25` | USD per million output tokens, the same |

Restart Claude Code once the settings are in place.

> **The plugin refuses what it guards until it is configured.** Installed without a secret, it
> stops `git push`, `rm -r…`, `/cycle` and `/cycle-chain` with "Veriqa is not configured". That is
> the fail-closed rule, not an error.

## 4. Enroll the approvers

Demo 3 needs two people with two messenger accounts. Enroll each of them once, from a terminal
where the `VERIQA_*` variables of step 3 are exported with the same values:

```bash
node <path to the clone>/samples/claude-code/veriqa-approval/scripts/veriqa-approval.mjs enroll developer
```

```bash
node <path to the clone>/samples/claude-code/veriqa-approval/scripts/veriqa-approval.mjs enroll manager
```

The script prints its own path when an approver is missing. Each command prints the link and the
path of a page with its QR, and opens the page with `VERIQA_APPROVAL_SHOW=browser`. The person to be enrolled scans it with their
own phone and confirms "register this messenger account as the approver …". Whoever confirms is
enrolled, so the link has to reach exactly that person. The accounts are stored in
`~/.claude/veriqa-approvers.json`, readable by you only; `VERIQA_APPROVAL_APPROVERS` names another
file. Enrolling a name again replaces its account.

Until both are enrolled, demo 3 is refused with "the approver … is not enrolled". To try it alone,
remove `chains` from a copy of the rules (see [Rules](#rules)).

## 5. Try the three demos

1. **A tool call.** Ask Claude to push the current branch. Before `git push` runs, Claude shows a QR.
   Scan it and answer in Telegram: confirming lets the push run, declining returns the refusal to Claude.
2. **A command cycle.** Type `/cycle TASK-1` (or any command listed in the rules). Claude shows a QR
   before it does anything else, and its first tool call waits for your answer. Declined, the tools
   stay closed and the cycle does nothing. If the agent itself decides to start `/cycle` through the
   Skill tool, the same question is asked, with `started_by: agent`.
3. **A costly run.** Type `/veriqa-approval:costly-run nightly refactor`. The command estimates the
   run (the demo estimate is 1 billion input and 50 million output tokens), asks you, and starts only
   when you confirm. `input=` and `output=` in the arguments, in millions of tokens, replace the estimate.
   The question also names the cost in whole USD, which the gate computes from the estimate and the
   prices above: 1000 × 5 + 50 × 25 = 6250 USD. It is a list-price ceiling: prompt caching and batch
   discounts make a real run cheaper, and the gate does not guess how much. Two approvers answer in
   turn: Claude shows the QR for `developer`, then, once developer has confirmed, the QR for
   `manager` in the same conversation — scan it from the manager's account.

## Rules

The rules are looked up in this order: the file in `VERIQA_APPROVAL_RULES`, then
`.claude/veriqa-approval.rules.json` in the project, then `rules.default.json` of the plugin. The first
matching rule wins.

```json
{
  "id": "git-push",
  "event": "PreToolUse",
  "tool": "^Bash$",
  "input": { "command": "\\bgit\\s+push\\b" },
  "action_type": "agent-tool-call",
  "slots": {
    "action": "push the branch to the remote repository",
    "tool": "{tool}", "target": "{input.command}", "project": "{project}"
  }
}
```

The person reads what is being done first — "Claude Code in my-project wants to push the branch to
the remote repository" — and the command only as a detail below it: `action` is a fixed phrase of
the rule, not text from the agent.

| Member | Meaning |
|---|---|
| `event` | `PreToolUse` or `UserPromptSubmit` |
| `tool` | `PreToolUse`: a regular expression over the tool name |
| `input` | `PreToolUse`: regular expressions over fields of the tool input. All must match. For the `Skill` tool, `skill` and `args` are always present |
| `prompt` | `UserPromptSubmit`: a regular expression over the typed prompt. Its groups are `{match.1}`, `{match.2}`, … |
| `action_type` | An action type declared in the client entry on the host |
| `slots` | Slot values. Placeholders: `{tool}`, `{input.<field>}`, `{match.<n>}`, `{project}` (the project directory name). An empty value is not sent, so the wording falls back to a step without that slot |

A new action type means a new entry in the host fragment (contract and wordings) plus a rule here.
The host checks every value against the contract: a value over `MaxLength`, a number that is not a
number, or a line break in a string refuses the creation, and with it the action.

## What this sample does not do

- **The rules are not a sandbox.** A regular expression over a Bash command catches the command as
  written; a script or an alias that pushes is not caught. Use the rules to put a person in the loop
  for actions you know, not to contain a hostile agent.
- **The costly-run gate is cooperative.** Demos 1 and 2 are enforced by Claude Code: the hook decides
  and the model cannot skip it. In demo 3 the estimate is known only to the command, so the command
  calls the gate itself, and a model that ignored its instructions could skip the call. To enforce
  it, put a `PreToolUse` rule on the tool that starts the costly work.
- **The link is a bearer one.** Whoever opens it first becomes the approver. The QR image and the page
  are written to the temporary directory, readable by you only. Do not share them.
- **The files that tie two calls together are yours, not the gate's.** They sit in the temporary
  directory under your user account, where Claude's own Bash tool can write too. They hold a
  transaction id, and the gate lets a call through only after Veriqa answers `confirmed` for it; but a
  model set on getting round the gate could swap in the id of another approval you confirmed. This is
  the same line as the rules above: the gate puts a person in the loop, it does not contain a hostile
  agent.
- **Demos 1 and 2 have one approver and no identity check.** Whoever opens the link first approves.
  To require enrolled people there too, add `agent-tool-call` or `agent-command-start` to `chains` and
  give its contract the optional `approver` slot, as `agent-costly-run` has.
- **Enrolment trusts the person who confirms.** The script cannot tell who scanned the enrolment
  QR; it enrols whoever confirmed. Hand the link to that person only.
- **The approvers file is local.** A second computer, or a colleague's Claude Code, needs its own
  enrolment. Anybody who can edit `~/.claude/veriqa-approvers.json` can put their own account in
  place of the manager's — the file is only as safe as your user account.
- **Local only.** The host runs on `localhost` for a quick demo, so Claude Code in the cloud cannot
  reach it.

## Further reading

- This sample on the documentation site — <https://veriqa.app/docs/research/claude-code-approvals>
- Approving AI-agent actions — <https://veriqa.app/docs/research/agent-approvals>
- Server-to-server confirmation API — <https://veriqa.app/docs/reference/confirmation-api>
- The same gate inside your own agent code — [`samples/dotnet/inproc/agent-approval`](../dotnet/inproc/agent-approval/README.md)
