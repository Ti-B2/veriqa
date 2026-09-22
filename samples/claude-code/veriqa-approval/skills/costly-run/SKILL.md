---
name: costly-run
description: Demo of a costly run that starts only after a person approves its token estimate in Veriqa. Use when the user asks to start a costly or long agent run and wants approval of its estimated token spend first.
argument-hint: "<what to run> [input=<millions of input tokens>] [output=<millions of output tokens>]"
disable-model-invocation: true
---

# Costly run with an approved estimate

The user asked for: `$ARGUMENTS`

This run is expensive, so it may start only after a person approves its estimate in the messenger.
Follow the steps in order and do not skip the approval.

## 1. Estimate

Estimate the token spend of the whole run, in **millions of tokens**:

- if the arguments state `input=` and `output=`, use those numbers as they are;
- otherwise use the demo estimate: **1000** million input tokens (1 billion) and **50** million
  output tokens.

Give the run a short name, at most 64 characters, from what the user asked for.

## 2. Ask for approval

Run exactly one Bash command:

```bash
node "${CLAUDE_PLUGIN_ROOT}/scripts/veriqa-approval.mjs" ask agent-costly-run "run=<run name>" "input_mtok=<input>" "output_mtok=<output>" "project=$(basename "$PWD")"
```

Put the values in place of the `<…>` placeholders and keep `project=$(basename "$PWD")` as it is:
the shell fills in the project name. Numbers are plain decimals, with no spaces and
no units (`1000`, not `1 000` or `1000M`).

Do not add the cost yourself: the gate computes it from the estimate and the configured prices and puts
it into the question.

The run needs two approvers in turn, `developer` and then `manager`, each from their own enrolled
messenger account.

## 3. Act on the answer

The command answers with one of three results, on the first line of its output:

- `PENDING`, exit code `3` — nothing has started yet. Show the user what the output asks for — the
  QR code of the approver who is due — then run the
  `wait` command the output names, with the Bash `timeout` parameter set to `600000`, because the
  person may need a few minutes to answer. `wait` may answer `PENDING` again when the next approver
  is due: show what it asks for and run `wait` again.
- `APPROVED`, exit code `0` — start the run.
- `NOT APPROVED` or any other exit code — **do not start the run**, not even a smaller version of
  it. Print the reason from the output and stop. Do not ask again another way and do not work
  around the refusal.

## 4. The run

This is a demo: in place of the real work, say that the run would start now and repeat the approved
estimate, including `cost_usd` from the `Asked:` line of the command output. A real command replaces this step with its own work and keeps steps 1–3 as they are.
