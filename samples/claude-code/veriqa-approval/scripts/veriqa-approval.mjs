#!/usr/bin/env node
// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT
//
// Veriqa approval gate for Claude Code. One file, no dependencies, Node 18+.
//
//   node veriqa-approval.mjs hook                      — Claude Code hook (PreToolUse / UserPromptSubmit), JSON on stdin
//   node veriqa-approval.mjs ask <action_type> k=v ... — ask a person from a command or a script;
//                                                        exit 0 on "confirmed", 1 on anything else,
//                                                        3 when the link has to be shown first
//   node veriqa-approval.mjs wait <ticket>             — wait for the answer to a pending "ask"
//   node veriqa-approval.mjs enroll <name>              — register the messenger account of an approver
//
// Every path that does not end in "confirmed" denies: Veriqa unreachable, a refused request, a
// declined or expired transaction, a timeout, a step of an approval chain confirmed by the wrong
// account. The gate never lets an action through on a failure.

import { spawn, spawnSync } from 'node:child_process';
import { randomBytes, createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { homedir, tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const Env = {
  Url: 'VERIQA_URL',
  ClientId: 'VERIQA_CLIENT_ID',
  ClientSecret: 'VERIQA_CLIENT_SECRET',
  ClientSecretFile: 'VERIQA_CLIENT_SECRET_FILE',
  CaFile: 'VERIQA_CA_FILE',
  NodeExtraCa: 'NODE_EXTRA_CA_CERTS',
  Rules: 'VERIQA_APPROVAL_RULES',
  TtlSeconds: 'VERIQA_APPROVAL_TTL_SECONDS',
  Show: 'VERIQA_APPROVAL_SHOW',
  Locale: 'VERIQA_APPROVAL_LOCALE',
  Approvers: 'VERIQA_APPROVAL_APPROVERS',
  PriceInput: 'VERIQA_APPROVAL_PRICE_INPUT',
  PriceOutput: 'VERIQA_APPROVAL_PRICE_OUTPUT',
  ProjectDir: 'CLAUDE_PROJECT_DIR',
};

const Defaults = {
  Url: 'https://127.0.0.1:8443',
  ClientId: 'claude-code',
  TtlSeconds: 300,
  // The host clamps a transaction's lifetime to 60…1800 s.
  MinTtlSeconds: 60,
  MaxTtlSeconds: 1800,
  // A wait has to end before its caller gives up: in chat mode one call waits for one step, in the
  // browser and none modes one call waits for the whole chain.
  // The hooks run with a 900 s timeout (hooks/hooks.json), and Claude Code treats a hook killed by
  // its timeout as a non-blocking error — the action would go through. "ask" runs under the Bash
  // tool, whose longest timeout is 600 s.
  HookBudgetSeconds: 780,
  AskBudgetSeconds: 570,
  // A result arrives a moment after the deadline of the transaction; polling a little past it
  // keeps a last-second answer from being read as "expired".
  DeadlineGraceMs: 15_000,
  PollIntervalMs: 2_000,
  ThrottledPollIntervalMs: 10_000,
  RequestTimeoutMs: 15_000,
  ProjectRulesFile: join('.claude', 'veriqa-approval.rules.json'),
  // Approvers are people, not projects: one file per user serves every project.
  ApproversFile: join(homedir(), '.claude', 'veriqa-approvers.json'),
  // USD per million tokens: Claude Opus 5 list prices, no cache and no batch discount
  // (platform.claude.com/docs/en/about-claude/pricing, 2026-09-21). Override them for another model.
  PriceInputUsdPerMtok: 5,
  PriceOutputUsdPerMtok: 25,
  // While a typed command waits for its approval, every tool call waits too — except the tools
  // the agent needs to show the link. Rules can replace the pattern with "display_tools".
  DisplayTools: '^(SendUserFile|ToolSearch|mcp__.+__(show_widget|read_me|show_approval_qr))$',
};

// A token estimate in "ask" gets its cost added under this slot, unless the caller passed one.
const EstimateSlot = { InputMtok: 'input_mtok', OutputMtok: 'output_mtok', CostUsd: 'cost_usd' };

const Api = {
  Token: '/connect/token',
  Create: '/api/transaction/confirmation',
  Result: (id) => `/api/transaction/${encodeURIComponent(id)}/result`,
};

const HttpTooManyRequests = 429;
const Outcome = { Pending: 'pending', Confirmed: 'confirmed', Expired: 'expired' };
// The comparable identity type declared for the client (IdentityMatchComparableTypes in the host
// fragment): the messenger account of the person who confirms.
const IdentityType = 'channel_user_id';
const ConfirmationGrant = {
  Type: 'urn:veriqa:params:oauth:grant-type:confirmation',
  Scope: 'openid channel',
};
const EnrollActionType = 'agent-approver-enroll';
const ApproverSlot = 'approver';
const HookEvent = { PreToolUse: 'PreToolUse', UserPromptSubmit: 'UserPromptSubmit' };
// Claude Code blocks the tool call or the prompt on exit code 2 and treats any other non-zero code
// as a non-blocking error — the action would go through. A hook that fails unexpectedly exits 2.
const ExitCode = { Approved: 0, NotApproved: 1, Usage: 2, HookBlock: 2, Pending: 3 };
// chat — the agent shows the link in the conversation, and the wait starts on the next call;
// browser — the hook waits at once and opens a page with the QR on this computer;
// none — the hook waits at once and shows nothing (tests, or a link delivered some other way).
const ShowMode = { Chat: 'chat', Browser: 'browser', None: 'none' };
const PendingKind = { Call: 'call', Turn: 'turn', Ask: 'ask' };

const scriptDir = dirname(fileURLToPath(import.meta.url));
const defaultRulesFile = join(scriptDir, '..', 'rules.default.json');

class GateError extends Error {}

// ---------------------------------------------------------------------------------------------
// Rules
// ---------------------------------------------------------------------------------------------

// Rules are looked up in this order: an explicit file, the project's own file, the plugin default.
function loadRules() {
  const projectFile = process.env[Env.ProjectDir]
    ? join(process.env[Env.ProjectDir], Defaults.ProjectRulesFile)
    : null;
  const file = process.env[Env.Rules]
    || (projectFile && existsSync(projectFile) ? projectFile : defaultRulesFile);

  const parsed = JSON.parse(readFileSync(file, 'utf8'));
  if (!Array.isArray(parsed.rules)) {
    throw new GateError(`The rules file ${file} has no "rules" array.`);
  }
  return {
    rules: parsed.rules,
    chains: parsed.chains ?? {},
    displayTools: new RegExp(parsed.display_tools ?? Defaults.DisplayTools),
  };
}

// The approvers an action type needs, in order. No chain means one step that anybody holding the
// link may confirm — the behaviour without step 3.
function chainFor(chains, actionType) {
  const chain = chains[actionType];
  if (chain === undefined) return [];
  if (!Array.isArray(chain) || chain.some((name) => typeof name !== 'string' || name === '')) {
    throw new GateError(`The approval chain of "${actionType}" is not a list of approver names.`);
  }
  return chain;
}

// ---------------------------------------------------------------------------------------------
// Approvers
// ---------------------------------------------------------------------------------------------

function approversFile() {
  return process.env[Env.Approvers] || Defaults.ApproversFile;
}

function loadApprovers() {
  const file = approversFile();
  if (!existsSync(file)) return {};
  const parsed = JSON.parse(readFileSync(file, 'utf8'));
  return parsed.approvers ?? {};
}

function saveApprover(name, approver) {
  const file = approversFile();
  const approvers = { ...loadApprovers(), [name]: approver };
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, `${JSON.stringify({ approvers }, null, 2)}\n`, { encoding: 'utf8', mode: 0o600 });
  return file;
}

// Resolves every name of a chain to an enrolled account before anything is asked: a chain with a
// missing approver is refused at once rather than after the first person has answered.
function resolveChain(names) {
  if (names.length === 0) return [];
  const approvers = loadApprovers();
  const resolved = names.map((name) => {
    const approver = approvers[name];
    if (!approver?.channel_type || !approver?.channel_user_id) {
      throw new GateError(`The approver "${name}" is not enrolled. Run: node "${fileURLToPath(import.meta.url)}" enroll ${name}`);
    }
    return { name, ...approver };
  });
  // Two approvals from one account are one approval: the chain exists to require different people.
  const accounts = resolved.map((a) => `${a.channel_type}:${a.channel_user_id}`);
  const repeated = resolved.find((a, at) => accounts.indexOf(accounts[at]) !== at);
  if (repeated) {
    throw new GateError(`The approver "${repeated.name}" is enrolled with the same account as another approver of the chain: enroll a different person.`);
  }
  return resolved;
}

// The Skill tool has named its input field differently across Claude Code versions; both are
// read so that a rule can always say "skill".
function normalizeToolInput(toolName, toolInput) {
  const input = { ...(toolInput ?? {}) };
  if (toolName === 'Skill') {
    input.skill ??= input.command;
    input.args ??= '';
  }
  return input;
}

// Finds the first rule matching the hook event. A rule matches when its tool/prompt pattern and
// every pattern over the tool input match; the prompt's capture groups become {match.N}.
function findRule(rules, hookInput) {
  const event = hookInput.hook_event_name;
  for (const rule of rules) {
    if (rule.event !== event) continue;

    if (event === HookEvent.PreToolUse) {
      if (!new RegExp(rule.tool).test(hookInput.tool_name)) continue;
      const input = normalizeToolInput(hookInput.tool_name, hookInput.tool_input);
      const inputMatches = Object.entries(rule.input ?? {})
        .every(([field, pattern]) => new RegExp(pattern).test(stringify(input[field])));
      if (!inputMatches) continue;
      return { rule, context: { tool: hookInput.tool_name, input } };
    }

    if (event === HookEvent.UserPromptSubmit) {
      const match = new RegExp(rule.prompt).exec(hookInput.prompt ?? '');
      if (!match) continue;
      return { rule, context: { prompt: hookInput.prompt, match: { ...match } } };
    }
  }
  return null;
}

function stringify(value) {
  if (value === undefined || value === null) return '';
  return typeof value === 'string' ? value : JSON.stringify(value);
}

// Fills "{input.command}"-style placeholders from the context. An empty value is left out of the
// request, so the wording ladder falls back to a step that does not use that slot.
function resolveSlots(slotMap, context) {
  const values = {};
  for (const [slot, template] of Object.entries(slotMap ?? {})) {
    const value = String(template)
      .replace(/\{([\w.]+)\}/g, (_, path) => stringify(lookup(context, path)))
      .trim();
    if (value !== '') values[slot] = value;
  }
  return values;
}

function lookup(context, path) {
  return path.split('.').reduce((node, key) => (node == null ? undefined : node[key]), context);
}

function projectName() {
  return basename(process.env[Env.ProjectDir] || process.cwd());
}

// ---------------------------------------------------------------------------------------------
// Veriqa API
// ---------------------------------------------------------------------------------------------

function settings() {
  const secretFile = process.env[Env.ClientSecretFile];
  const secret = secretFile ? readFileSync(secretFile, 'utf8').trim() : process.env[Env.ClientSecret];
  if (!secret) {
    throw new GateError(
      `Veriqa is not configured: set ${Env.ClientSecret} or ${Env.ClientSecretFile} (see the plugin README).`);
  }
  return {
    baseUrl: (process.env[Env.Url] || Defaults.Url).replace(/\/+$/, ''),
    clientId: process.env[Env.ClientId] || Defaults.ClientId,
    clientSecret: secret,
    ttlSeconds: Math.min(
      Math.max(Number(process.env[Env.TtlSeconds]) || Defaults.TtlSeconds, Defaults.MinTtlSeconds),
      Defaults.MaxTtlSeconds),
    locale: process.env[Env.Locale] || undefined,
  };
}

async function http(url, init) {
  try {
    return await fetch(url, { ...init, signal: AbortSignal.timeout(Defaults.RequestTimeoutMs) });
  } catch (error) {
    throw new GateError(`Veriqa at ${new URL(url).origin} is unreachable (${error.cause?.code ?? error.message}).`);
  }
}

// The API answers Problem Details (code in "title"), the token endpoint answers an OAuth error
// ("error" / "error_description"); neither echoes a submitted value.
async function failure(response, what) {
  let code = '';
  try {
    const body = await response.json();
    code = [body.title ?? body.error, body.detail ?? body.error_description].filter(Boolean).join(': ');
  } catch { /* not a JSON body */ }
  code = code.replace(/\.+$/, '');
  return new GateError(`${what} failed with HTTP ${response.status}${code ? ` — ${code}` : ''}.`);
}

// RFC 6749 §2.3.1: the credentials are form-encoded before they go into the Basic header.
function basicAuth(config) {
  const credentials = Buffer
    .from(`${encodeURIComponent(config.clientId)}:${encodeURIComponent(config.clientSecret)}`)
    .toString('base64');
  return `Basic ${credentials}`;
}

async function getToken(config) {
  const response = await http(config.baseUrl + Api.Token, {
    method: 'POST',
    headers: {
      Authorization: basicAuth(config),
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: new URLSearchParams({ grant_type: 'client_credentials' }),
  });
  if (!response.ok) throw await failure(response, 'Getting a Veriqa token');
  return (await response.json()).access_token;
}

// With an approver, the transaction names the account expected to confirm and keeps to that
// account's channel, so that the comparison of accounts is made within one messenger.
async function createConfirmation(config, token, { actionType, slotValues, ttlSeconds, approver }) {
  const response = await http(config.baseUrl + Api.Create, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      action_type: actionType,
      slot_values: slotValues,
      ttl_seconds: ttlSeconds,
      locale: config.locale,
      expected_identities: approver ? { [IdentityType]: approver.channel_user_id } : undefined,
      allowed_channel_types: approver ? [approver.channel_type] : undefined,
    }),
  });
  if (!response.ok) throw await failure(response, `Creating the "${actionType}" confirmation`);
  return response.json();
}

// Polls the result until it is no longer pending or the transaction's deadline has passed.
// Returns { outcome, matchedType }.
async function waitForOutcome(config, token, transaction) {
  const deadline = Date.parse(transaction.expires_at) + Defaults.DeadlineGraceMs;
  while (Date.now() < deadline) {
    const response = await http(config.baseUrl + Api.Result(transaction.transaction_id), {
      headers: { Authorization: `Bearer ${token}` },
    });
    // An expired transaction is removed, and the result then answers 404 instead of "expired".
    if (response.status === 404 && Date.now() >= Date.parse(transaction.expires_at)) {
      return { outcome: Outcome.Expired, matchedType: null };
    }
    // The host limits result reads per client, and one client serves every Claude Code of a team:
    // several approvals waiting at once can run into the limit. That is a reason to slow down, not
    // a refusal — the deadline above still ends the wait.
    if (response.status === HttpTooManyRequests) {
      const retryAfterMs = (Number(response.headers.get('retry-after')) || 0) * 1000;
      const pauseMs = Math.min(Math.max(retryAfterMs, Defaults.ThrottledPollIntervalMs), deadline - Date.now());
      await new Promise((resolve) => setTimeout(resolve, Math.max(pauseMs, 0)));
      continue;
    }
    if (!response.ok) throw await failure(response, 'Reading the confirmation result');
    const { outcome, matched_type: matchedType } = await response.json();
    if (outcome !== Outcome.Pending) return { outcome, matchedType };
    await new Promise((resolve) => setTimeout(resolve, Defaults.PollIntervalMs));
  }
  return { outcome: Outcome.Expired, matchedType: null };
}

// Exchanges a confirmed transaction, once, for the id_token of the person who confirmed and reads
// their messenger account from it. The token comes straight from the token endpoint over TLS in
// answer to an authenticated client, so its signature is not checked here (OpenID Connect Core
// §3.1.3.7 allows TLS validation in its place for a token received this way).
async function redeemConfirmation(config, transactionId) {
  const response = await http(config.baseUrl + Api.Token, {
    method: 'POST',
    headers: {
      Authorization: basicAuth(config),
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: new URLSearchParams({
      grant_type: ConfirmationGrant.Type,
      transaction_id: transactionId,
      scope: ConfirmationGrant.Scope,
    }),
  });
  if (!response.ok) throw await failure(response, 'Learning who confirmed');
  const { id_token: idToken } = await response.json();
  const payload = JSON.parse(Buffer.from(String(idToken).split('.')[1] ?? '', 'base64url').toString('utf8'));
  if (!payload.channel_type || !payload.channel_user_id) {
    throw new GateError('The id_token names no messenger account: is the "channel" scope allowed to the client?');
  }
  return { channel_type: payload.channel_type, channel_user_id: String(payload.channel_user_id) };
}


// ---------------------------------------------------------------------------------------------
// Showing the way in
// ---------------------------------------------------------------------------------------------

function showMode() {
  const mode = process.env[Env.Show] || ShowMode.Chat;
  return Object.values(ShowMode).includes(mode) ? mode : ShowMode.Chat;
}

// Writes what the person has to open: a PNG of the QR, for the conversation, and a page with the
// QR, the link and the values of the question, for the browser or to be sent on. Both are readable
// by the current user only, because the link is a bearer one: whoever opens it first becomes the
// approver.
function writeWayIn(actionType, slotValues, transaction, step) {
  const entry = transaction.channel_entry;
  if (!entry) return null;
  const base = join(tmpdir(), `veriqa-approval-${transaction.transaction_id}`);
  const png = /^data:image\/png;base64,(.+)$/.exec(entry.qr ?? '');
  const qrFile = png ? `${base}.png` : null;
  if (qrFile) writeFileSync(qrFile, Buffer.from(png[1], 'base64'), { mode: 0o600 });
  const pageFile = `${base}.html`;
  writeFileSync(pageFile, renderPage(actionType, slotValues, transaction, step), { encoding: 'utf8', mode: 0o600 });
  return { id: transaction.transaction_id, url: entry.url, validUntil: entry.valid_until ?? transaction.expires_at, qrFile, pageFile };
}

function renderPage(actionType, slotValues, transaction, step) {
  const entry = transaction.channel_entry;
  const rows = Object.entries(slotValues)
    .map(([name, value]) => `<tr><th>${escapeHtml(name)}</th><td>${escapeHtml(value)}</td></tr>`)
    .join('');
  return `<!doctype html><html lang="en"><head><meta charset="utf-8">
<title>Approve in Veriqa</title>
<style>body{font-family:system-ui,sans-serif;max-width:32rem;margin:2rem auto;padding:0 1rem;color:#1b1b1f}
img{max-width:100%;height:auto}th{text-align:left;padding-right:1rem;color:#555}td{word-break:break-all}
a{font-size:1.1rem}</style></head><body>
${step ? stepHeading(step) : `<h1>Claude Code is waiting for your approval</h1>
<p>Scan the code with your phone, or open the link on this computer. The question arrives in the
messenger; answer it there.`} This page does not update — the result appears in Claude Code.</p>
${entry.qr ? `<img alt="QR code of the approval link" src="${escapeHtml(entry.qr)}">` : ''}
<p><a href="${escapeHtml(entry.url)}">${escapeHtml(entry.url)}</a></p>
<table><tr><th>action</th><td>${escapeHtml(actionType)}</td></tr>${rows}</table>
<p>Valid until ${escapeHtml(entry.valid_until ?? transaction.expires_at)}.</p>
</body></html>`;
}

// A step of a chain is meant for one account. The first step is usually the person at this
// computer; a later one goes to somebody else, and the page says how to get it to them.
function stepHeading({ index, total, approver }) {
  const name = escapeHtml(approver.name);
  const where = escapeHtml(approver.channel_type);
  return `<h1>Approval ${index} of ${total}: ${name}</h1>
<p>Only the ${where} account enrolled as <b>${name}</b> can approve this step; a confirmation from any
other account is refused. ${index === 1
    ? 'If that is you, scan the code with your phone or open the link on this computer.'
    : `Send the link to ${name} — in a chat, by e-mail, however you reach them — or let them scan the code.`}`;
}

// What the agent is told to show: the QR code Veriqa returned, through the plugin's own tool. A later
// step of a chain belongs to somebody else; this sample shows their code in the same conversation
// and says so, because a real setup delivers it to that person another way.
function wayInForAgent(wayIn, step) {
  if (!wayIn) return 'Veriqa returned no link for this approval; it cannot be answered.';
  const lines = [];
  if (step) {
    lines.push(`Approval ${step.index} of ${step.total} is for "${step.approver.name}": only the ${step.approver.channel_type} account enrolled under that name can approve it.`);
  }
  if (wayIn.qrFile) {
    lines.push(`Show the user the QR code Veriqa returned: call the tool show_approval_qr of this plugin with transaction_id "${wayIn.id}".`);
    lines.push(`Without that tool, send the image ${wayIn.qrFile} if you can send files to the user; reading the file yourself does not show it to them.`);
    lines.push(`Give the approval link only if the code cannot be shown: ${wayIn.url}`);
  } else {
    lines.push(`Show the user the approval link: ${wayIn.url}`);
  }
  if (step && step.index > 1) {
    lines.push(`Tell the user that ${step.approver.name} scans this code and answers in the messenger, and that the code is shown in this conversation for the demonstration only: in a real setup it reaches ${step.approver.name} another way, such as their own chat or e-mail.`);
  } else {
    lines.push('Ask the user to scan the code and answer in the messenger.');
  }
  lines.push(`The link is valid until ${wayIn.validUntil}.`);
  return lines.join('\n');
}

function openInBrowser(file) {
  const [command, args] = process.platform === 'win32'
    ? ['cmd', ['/c', 'start', '""', file]]
    : [process.platform === 'darwin' ? 'open' : 'xdg-open', [file]];
  try {
    spawn(command, args, { detached: true, stdio: 'ignore', windowsHide: true })
      .on('error', () => { /* no browser: the transaction still waits for its deadline */ })
      .unref();
  } catch { /* same as above */ }
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

// ---------------------------------------------------------------------------------------------
// Pending approvals
// ---------------------------------------------------------------------------------------------

// A hook cannot show anything in the conversation while it waits, so in chat mode an approval
// spans two calls: the first creates the transaction and hands the link to the agent, the next one
// waits for the answer. What connects them is kept in a file per approval, readable by the current
// user only; one file each keeps parallel tool calls from overwriting each other's state.
const TurnKey = 'turn';
const AskScope = 'ask';

function pendingFile(scope, key) {
  const digest = createHash('sha256').update(`${scope}\n${key}`).digest('hex').slice(0, 32);
  return join(tmpdir(), `veriqa-approval-pending-${digest}.json`);
}

function loadPending(scope, key) {
  const file = pendingFile(scope, key);
  if (!existsSync(file)) return null;
  try {
    return JSON.parse(readFileSync(file, 'utf8'));
  } catch {
    return null; // unreadable: the approval starts over, which asks the person again
  }
}

function savePending(scope, key, run) {
  const file = pendingFile(scope, key);
  if (run === null) {
    rmSync(file, { force: true });
    return;
  }
  writeFileSync(file, JSON.stringify(run), { encoding: 'utf8', mode: 0o600 });
}

// A tool call is identified by its rule, its tool and the input fields the rule matches or shows
// the approver: a repeated call with a changed command is another call and needs its own approval,
// while a changed timeout or description is the same call. A rule naming no field keys on it all.
function callKey(rule, toolName, toolInput) {
  const shown = Object.values(rule.slots ?? {})
    .flatMap((template) => [...String(template).matchAll(/\{input\.([^}]+)\}/g)].map((m) => m[1]));
  const fields = [...new Set([...Object.keys(rule.input ?? {}), ...shown])].sort();
  const keyed = fields.length ? fields.map((field) => [field, toolInput[field] ?? null]) : toolInput;
  return createHash('sha256').update(JSON.stringify([rule.id, toolName, keyed])).digest('hex');
}

// ---------------------------------------------------------------------------------------------
// The gate
// ---------------------------------------------------------------------------------------------

// Node reads extra trusted certificates only at start-up, so a local self-signed certificate is
// applied by running this script once more with NODE_EXTRA_CA_CERTS set. It is done only once
// Veriqa is about to be called, and before anything is written: every other tool call pays for one
// Node start, not two. A second run that fails exits with failureCode, so a crash still refuses.
function rerunWithCaIfNeeded(stdinText, failureCode) {
  const caFile = process.env[Env.CaFile];
  if (!caFile || process.env[Env.NodeExtraCa] === caFile) return;
  const run = spawnSync(process.execPath, process.argv.slice(1), {
    input: stdinText,
    stdio: [stdinText === undefined ? 'inherit' : 'pipe', 'inherit', 'inherit'],
    env: { ...process.env, [Env.NodeExtraCa]: caFile },
  });
  // A hook states its decision in JSON and exits 0; "ask" and "wait" exit 0, 1 or 3. Anything
  // else is a crash.
  const expected = failureCode === ExitCode.HookBlock
    ? [ExitCode.Approved]
    : [ExitCode.Approved, ExitCode.NotApproved, ExitCode.Pending];
  process.exit(expected.includes(run.status) ? run.status : failureCode);
}

// An approval in progress: without a chain one step that anybody holding the link may confirm,
// with a chain one step per approver, each in a transaction of its own that names their account.
function newRun(kind, ruleId, actionType, slotValues, approvers, ttlSeconds) {
  return { kind, ruleId, actionType, slotValues, approvers, step: 0, ttlSeconds, transaction: null };
}

function currentStep(run) {
  const approver = run.approvers[run.step];
  return approver ? { index: run.step + 1, total: run.approvers.length, approver } : null;
}

function stepLabel(step) {
  return step ? `step ${step.index} of ${step.total} (${step.approver.name})` : '';
}

// Creates the transaction of the current step and returns what the person has to open.
async function openStep(config, token, run, ttlSeconds) {
  const approver = run.approvers[run.step] ?? null;
  const values = approver ? { ...run.slotValues, [ApproverSlot]: approver.name } : run.slotValues;
  const transaction = await createConfirmation(config, token, {
    actionType: run.actionType, slotValues: values, ttlSeconds, approver,
  });
  run.transaction = { transaction_id: transaction.transaction_id, expires_at: transaction.expires_at };
  return writeWayIn(run.actionType, values, transaction, currentStep(run));
}

// Waits for the answer to the current step. Returns null when it is confirmed — by the enrolled
// account, if the step names one — or the reason of the refusal. Veriqa reports whether the
// expected account confirmed (matched_type) but does not refuse on a mismatch, so the check is here.
async function closeStep(config, token, run) {
  const step = currentStep(run);
  const label = stepLabel(step);
  const { outcome, matchedType } = await waitForOutcome(config, token, run.transaction);
  if (outcome !== Outcome.Confirmed) {
    return `Not approved in Veriqa${label ? `: ${label}` : ''}: the outcome is "${outcome}".`;
  }
  if (step && matchedType !== IdentityType) {
    return `Not approved in Veriqa: ${label} was confirmed by another account than the one enrolled as "${step.approver.name}".`;
  }
  return null;
}

function approvedReason(run) {
  return run.approvers.length === 0
    ? 'Approved by a person in Veriqa.'
    : `Approved in Veriqa by ${run.approvers.map((a) => a.name).join(', then ')}.`;
}

function failureReason(error) {
  const message = error instanceof GateError ? error.message : `Unexpected error: ${error.message}`;
  return `Not approved — the approval could not be completed. ${message}`;
}

// Asks and waits in one call (browser and none modes) and returns { approved, reason }. Throws
// nothing: every failure is a refusal. The steps of a chain run in turn, the next one only after
// the previous one is confirmed, all within one budget.
async function askPerson(actionType, slotValues, { chain, budgetSeconds }) {
  try {
    const config = settings();
    const run = newRun(PendingKind.Call, null, actionType, slotValues, resolveChain(chain), config.ttlSeconds);
    const token = await getToken(config);
    const budgetEnds = Date.now() + budgetSeconds * 1000;
    for (; run.step < Math.max(run.approvers.length, 1); run.step += 1) {
      const leftSeconds = Math.floor((budgetEnds - Date.now() - Defaults.DeadlineGraceMs) / 1000);
      if (leftSeconds < Defaults.MinTtlSeconds) {
        const label = stepLabel(currentStep(run));
        return { approved: false, reason: `Not approved in Veriqa: no time was left for ${label || 'the approval'}.` };
      }
      const wayIn = await openStep(config, token, run, Math.min(config.ttlSeconds, leftSeconds));
      if (wayIn && showMode() === ShowMode.Browser) openInBrowser(wayIn.pageFile);
      const refusal = await closeStep(config, token, run);
      if (refusal) return { approved: false, reason: refusal };
    }
    return { approved: true, reason: approvedReason(run) };
  } catch (error) {
    return { approved: false, reason: failureReason(error) };
  }
}

// Chat mode, first call: resolves the chain, creates the first transaction and returns the run and
// the text for the agent. Throws on failure.
async function beginRun(kind, ruleId, actionType, slotValues, chain, budgetSeconds) {
  const config = settings();
  const run = newRun(kind, ruleId, actionType, slotValues, resolveChain(chain),
    Math.min(config.ttlSeconds, budgetSeconds));
  const token = await getToken(config);
  const wayIn = await openStep(config, token, run, run.ttlSeconds);
  return { run, wayIn: wayInForAgent(wayIn, currentStep(run)) };
}

// Chat mode, next call: waits for the answer to the current step. Returns { state: 'approved' |
// 'refused', reason }, or { state: 'pending', wayIn } when the next approver of a chain has to be
// reached first. Throws nothing: a failure is a refusal. Each call waits for one transaction only,
// which keeps it within the timeout of its caller.
async function resumeRun(run) {
  try {
    const config = settings();
    const token = await getToken(config);
    const refusal = await closeStep(config, token, run);
    if (refusal) return { state: 'refused', reason: refusal };
    if (run.step + 1 >= run.approvers.length) return { state: 'approved', reason: approvedReason(run) };
    run.step += 1;
    const wayIn = await openStep(config, token, run, run.ttlSeconds);
    return { state: 'pending', wayIn: wayInForAgent(wayIn, currentStep(run)) };
  } catch (error) {
    return { state: 'refused', reason: failureReason(error) };
  }
}

function hookOutput(event, approved, reason) {
  if (event === HookEvent.PreToolUse) {
    return {
      hookSpecificOutput: {
        hookEventName: event,
        permissionDecision: approved ? 'allow' : 'deny',
        permissionDecisionReason: reason,
      },
    };
  }
  // UserPromptSubmit: an approved prompt simply goes on; a refused one is blocked before it reaches
  // the model. This event takes "decision": "block" — a permissionDecision here is ignored.
  return approved ? { systemMessage: reason } : { decision: 'block', reason };
}

function writeJson(value) {
  process.stdout.write(JSON.stringify(value));
}

const AgentText = {
  CallPending: (ruleId, wayIn) => [
    `[${ruleId}] Approval requested in Veriqa — the tool call has NOT run.`,
    wayIn,
    'Right after showing the code, in the same turn, repeat the same tool call with the same command — do not wait for the user to reply: that call itself waits for the answer, up to the time the link is valid. Do not change the command and do not look for another way to do it.',
  ].join('\n'),
  TurnStarted: (ruleId, wayIn) => [
    `[${ruleId}] This command needs approval in Veriqa before any work on it starts.`,
    wayIn,
    'Show the code first, before you use any other tool. Then carry on with the command: your next tool call waits for the answer. If the approval is refused, the tools stay closed for this command: stop and tell the user.',
  ].join('\n'),
  TurnPending: (ruleId, wayIn) => [
    `[${ruleId}] The previous approval is in; the next one is needed before the command may go on.`,
    wayIn,
    'Once the link is shown, carry on: your next tool call waits for the answer.',
  ].join('\n'),
  Refused: (ruleId, reason) =>
    `[${ruleId}] ${reason} Do not repeat it and do not work around the refusal; tell the user it was not approved.`,
};

async function runHook() {
  const stdinText = readFileSync(0, 'utf8');
  const hookInput = JSON.parse(stdinText);
  const event = hookInput.hook_event_name;
  const session = hookInput.session_id || 'default';

  let rules;
  let found;
  let chain;
  try {
    rules = loadRules();
    found = findRule(rules.rules, hookInput);
    chain = found && chainFor(rules.chains, found.rule.action_type);
  } catch (error) {
    // A broken rules file must not silently turn the gate off.
    writeJson(hookOutput(event, false, `Veriqa approval rules are unreadable: ${error.message}`));
    return;
  }
  const slotValuesOf = () => resolveSlots(found.rule.slots,
    { ...found.context, project: projectName(), rule: found.rule.id });

  if (showMode() !== ShowMode.Chat) {
    if (!found) return; // not ours: Claude Code proceeds as usual
    rerunWithCaIfNeeded(stdinText, ExitCode.HookBlock);
    const { approved, reason } = await askPerson(found.rule.action_type, slotValuesOf(), {
      chain, budgetSeconds: Defaults.HookBudgetSeconds,
    });
    writeJson(hookOutput(event, approved, `[${found.rule.id}] ${reason}`));
    return;
  }

  if (event === HookEvent.UserPromptSubmit) {
    await promptInChat(stdinText, session, found, chain, slotValuesOf);
  } else {
    await toolInChat(stdinText, session, hookInput, rules, found, chain, slotValuesOf);
  }
}

// A typed command in chat mode: the prompt reaches the agent together with the link, and every
// tool call of the turn waits until the approval is answered. A refused approval keeps the tools
// closed until the next prompt. Every prompt ends the approval of the one before it.
async function promptInChat(stdinText, session, found, chain, slotValuesOf) {
  if (!found) {
    savePending(session, TurnKey, null);
    return;
  }
  rerunWithCaIfNeeded(stdinText, ExitCode.HookBlock);
  savePending(session, TurnKey, null);
  try {
    const { run, wayIn } = await beginRun(PendingKind.Turn, found.rule.id, found.rule.action_type,
      slotValuesOf(), chain, Defaults.HookBudgetSeconds);
    savePending(session, TurnKey, run);
    writeJson({
      hookSpecificOutput: {
        hookEventName: HookEvent.UserPromptSubmit,
        additionalContext: AgentText.TurnStarted(found.rule.id, wayIn),
      },
    });
  } catch (error) {
    writeJson(hookOutput(HookEvent.UserPromptSubmit, false, `[${found.rule.id}] ${failureReason(error)}`));
  }
}

// A tool call in chat mode. First the approval of a typed command, if one is open; then the rules.
async function toolInChat(stdinText, session, hookInput, rules, found, chain, slotValuesOf) {
  const turn = loadPending(session, TurnKey);
  if (turn && !rules.displayTools.test(hookInput.tool_name)) {
    if (turn.refused) {
      writeJson(hookOutput(HookEvent.PreToolUse, false, AgentText.Refused(turn.ruleId, turn.refused)));
      return;
    }
    rerunWithCaIfNeeded(stdinText, ExitCode.HookBlock);
    const result = await resumeRun(turn);
    if (result.state === 'pending') {
      savePending(session, TurnKey, turn);
      writeJson(hookOutput(HookEvent.PreToolUse, false, AgentText.TurnPending(turn.ruleId, result.wayIn)));
      return;
    }
    if (result.state === 'refused') {
      savePending(session, TurnKey, { ...turn, refused: result.reason });
      writeJson(hookOutput(HookEvent.PreToolUse, false, AgentText.Refused(turn.ruleId, result.reason)));
      return;
    }
    savePending(session, TurnKey, null); // approved: this call and the rest of the turn go on
  }

  if (!found) return; // not ours: Claude Code proceeds as usual
  const key = callKey(found.rule, hookInput.tool_name, found.context.input);
  const pending = loadPending(session, key);
  rerunWithCaIfNeeded(stdinText, ExitCode.HookBlock);

  if (!pending) {
    try {
      const { run, wayIn } = await beginRun(PendingKind.Call, found.rule.id, found.rule.action_type,
        slotValuesOf(), chain, Defaults.HookBudgetSeconds);
      savePending(session, key, run);
      writeJson(hookOutput(HookEvent.PreToolUse, false, AgentText.CallPending(found.rule.id, wayIn)));
    } catch (error) {
      writeJson(hookOutput(HookEvent.PreToolUse, false, AgentText.Refused(found.rule.id, failureReason(error))));
    }
    return;
  }

  const result = await resumeRun(pending);
  if (result.state === 'pending') {
    savePending(session, key, pending);
    writeJson(hookOutput(HookEvent.PreToolUse, false, AgentText.CallPending(found.rule.id, result.wayIn)));
    return;
  }
  savePending(session, key, null);
  writeJson(result.state === 'approved'
    ? hookOutput(HookEvent.PreToolUse, true, `[${found.rule.id}] ${result.reason}`)
    : hookOutput(HookEvent.PreToolUse, false, AgentText.Refused(found.rule.id, result.reason)));
}

// Adds the estimated cost in whole USD to a token estimate, so that the person approves money and
// not only token counts. The arithmetic stays here rather than in the command's prompt.
function addEstimatedCost(slotValues) {
  const input = Number(slotValues[EstimateSlot.InputMtok]);
  const output = Number(slotValues[EstimateSlot.OutputMtok]);
  if (EstimateSlot.CostUsd in slotValues || !Number.isFinite(input) || !Number.isFinite(output)) {
    return slotValues;
  }
  const price = (name, fallback) => {
    const value = Number(process.env[name]);
    return process.env[name] && Number.isFinite(value) && value >= 0 ? value : fallback;
  };
  const cost = input * price(Env.PriceInput, Defaults.PriceInputUsdPerMtok)
    + output * price(Env.PriceOutput, Defaults.PriceOutputUsdPerMtok);
  return { ...slotValues, [EstimateSlot.CostUsd]: String(Math.round(cost)) };
}

function printAsked(slotValues) {
  process.stdout.write(`Asked: ${Object.entries(slotValues).map(([k, v]) => `${k}=${v}`).join(' ')}\n`);
}

function printFinal(approved, reason, slotValues) {
  process.stdout.write(`${approved ? 'APPROVED' : 'NOT APPROVED'}: ${reason}\n`);
  printAsked(slotValues);
  process.exit(approved ? ExitCode.Approved : ExitCode.NotApproved);
}

function printPending(wayIn, ticket) {
  process.stdout.write(`PENDING: approval requested in Veriqa — nothing has started yet.\n${wayIn}\n`);
  process.stdout.write(`Once the link is shown, run with the Bash timeout 600000: node "${fileURLToPath(import.meta.url)}" wait ${ticket}\n`);
  process.exit(ExitCode.Pending);
}

async function runAsk(args) {
  const [actionType, ...pairs] = args;
  if (!actionType) {
    process.stderr.write('Usage: veriqa-approval.mjs ask <action_type> [slot=value ...]\n');
    process.exit(ExitCode.Usage);
  }
  rerunWithCaIfNeeded(undefined, ExitCode.NotApproved);
  const slotValues = addEstimatedCost(Object.fromEntries(pairs.map((pair) => {
    const at = pair.indexOf('=');
    return at < 0 ? [pair, ''] : [pair.slice(0, at), pair.slice(at + 1)];
  }).filter(([, value]) => value !== '')));

  let chain;
  try {
    chain = chainFor(loadRules().chains, actionType);
  } catch (error) {
    printFinal(false, `Veriqa approval rules are unreadable: ${error.message}`, slotValues);
  }

  if (showMode() !== ShowMode.Chat) {
    const { approved, reason } = await askPerson(actionType, slotValues, {
      chain, budgetSeconds: Defaults.AskBudgetSeconds,
    });
    printFinal(approved, reason, slotValues);
  }

  try {
    const { run, wayIn } = await beginRun(PendingKind.Ask, null, actionType, slotValues, chain,
      Defaults.AskBudgetSeconds);
    const ticket = randomBytes(12).toString('hex');
    savePending(AskScope, ticket, run);
    printPending(wayIn, ticket);
  } catch (error) {
    printFinal(false, failureReason(error), slotValues);
  }
}

async function runWait(args) {
  const [ticket] = args;
  if (!ticket || !/^[0-9a-f]{24}$/.test(ticket)) {
    process.stderr.write('Usage: veriqa-approval.mjs wait <ticket printed by "ask">\n');
    process.exit(ExitCode.Usage);
  }
  rerunWithCaIfNeeded(undefined, ExitCode.NotApproved);
  const run = loadPending(AskScope, ticket);
  if (!run) {
    process.stdout.write('NOT APPROVED: no approval is pending under this ticket; it has ended or never began.\n');
    process.exit(ExitCode.NotApproved);
  }
  const result = await resumeRun(run);
  if (result.state === 'pending') {
    savePending(AskScope, ticket, run);
    printPending(result.wayIn, ticket);
  }
  savePending(AskScope, ticket, null);
  printFinal(result.state === 'approved', result.reason, run.slotValues);
}

// Registers an approver: the person confirms a question with the phone that should approve later,
// and the account that confirmed is stored under the given name. Whoever confirms is enrolled, so
// the link has to reach exactly that person.
async function runEnroll(args) {
  const [name] = args;
  if (!name || !/^[\w.-]{1,32}$/.test(name)) {
    process.stderr.write('Usage: veriqa-approval.mjs enroll <name>  (letters, digits, "_", "-", ".", up to 32)\n');
    process.exit(ExitCode.Usage);
  }
  rerunWithCaIfNeeded(undefined, ExitCode.NotApproved);
  try {
    const config = settings();
    const token = await getToken(config);
    const slotValues = { name, project: projectName() };
    const transaction = await createConfirmation(config, token, {
      actionType: EnrollActionType, slotValues, ttlSeconds: config.ttlSeconds,
    });
    const wayIn = writeWayIn(EnrollActionType, slotValues, transaction, null);
    if (wayIn && showMode() === ShowMode.Browser) openInBrowser(wayIn.pageFile);
    if (wayIn) process.stdout.write(`Open or scan: ${wayIn.url}\nThe same link with its QR: ${wayIn.pageFile}\n`);
    process.stdout.write(`Waiting for ${name} to confirm in the messenger…\n`);
    const { outcome } = await waitForOutcome(config, token, transaction);
    if (outcome !== Outcome.Confirmed) {
      process.stdout.write(`NOT ENROLLED: the outcome is "${outcome}".\n`);
      process.exit(ExitCode.NotApproved);
    }
    const account = await redeemConfirmation(config, transaction.transaction_id);
    const file = saveApprover(name, { ...account, enrolled_at: new Date().toISOString() });
    process.stdout.write(`ENROLLED: "${name}" is the ${account.channel_type} account that just confirmed (${file}).\n`);
    process.exit(ExitCode.Approved);
  } catch (error) {
    const message = error instanceof GateError ? error.message : `Unexpected error: ${error.message}`;
    process.stdout.write(`NOT ENROLLED: ${message}\n`);
    process.exit(ExitCode.NotApproved);
  }
}

const [mode, ...rest] = process.argv.slice(2);
if (mode === 'hook') {
  await runHook().catch((error) => {
    process.stderr.write(`Veriqa approval gate failed, the action is refused: ${error.message}\n`);
    process.exit(ExitCode.HookBlock);
  });
} else if (mode === 'ask') {
  await runAsk(rest);
} else if (mode === 'wait') {
  await runWait(rest);
} else if (mode === 'enroll') {
  await runEnroll(rest);
} else {
  process.stderr.write('Usage: veriqa-approval.mjs hook | ask <action_type> [slot=value ...] | wait <ticket> | enroll <name>\n');
  process.exit(ExitCode.Usage);
}
