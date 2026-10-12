# verified-waves forward-test panel

Date: 2026-10-12

This file owns the forward-test method for the `deniz-process` skill
[`verified-waves`](../../skills/deniz-process/verified-waves/SKILL.md): its fixture, the setup per
harness, the prompts, the answer key, the repeat budget, and what each record holds. The
[protocol](protocol.md) owns isolation, controls, and recording in general; this file adds only what
is specific to this skill. A fixed prompt set against a deterministic fixture is approach 16 of the
[orchestration research](../../docs/research/claude-ultracode-and-orchestration-ports.md#adopted-approaches-curator-approved),
so the runs in the three harnesses compare.

## Purpose and boundary

The panel measures whether the skill's method holds in each harness: the small-task test, the plan
before the first dispatch, the ledger and the results files, fresh skeptics, the completeness
critic, writer coordination, a stopped child, a restart, the question before a destructive step,
the fallback when children cannot start, and an invocation with no task.

It is a behavioural panel for one skill. It is not a CI gate or a completion gate
([experiment boundary](../../docs/engineering/quality-gates.md#experiment-selftest-boundary)). It
spends tokens, so it runs only on the curator's word, with credentials supplied out of band. Only
its records support a claim that the skill works in a harness.

In Claude Code the panel runs P0 only. By curator decision, its model prompts, P1 to P8, are not
run there, because Claude Code uses its native Workflow tool; a Claude Code record marks each of
them `skipped` with that reason.

Source citations below are pinned as in the research note: Claude Code 2.1.296 and the
[workflows docs](https://code.claude.com/docs/en/workflows) read 2026-10-12; OpenCode
`anomalyco/opencode` v2.0.26, paths under `packages/`; Codex `openai/codex` rust-v0.162.1, paths
under `codex-rs/`. Each record names the version that actually ran.

## Fixture

[`fixtures/waves-repo/`](fixtures/waves-repo/) is a small ES-module package: six modules under
`src/`, and tests for two of them under `test/`. `make-fixture.ps1 -Set waves` commits it as one
commit, `feat: limits toolkit`, with the scratch set's pinned identity and time zone.
[`fixtures/WAVES_BASELINE`](fixtures/WAVES_BASELINE) records that commit. `npm test` passes at the
baseline.

Three modules hold a planted defect of one class: a limit, window, or expiry check that is off by
one against the contract in the module's own JSDoc. One module is a decoy that looks like the same
defect but is correct against its contract, and two modules are clean. The
[answer key](#answer-key) names them. The generator copies only the set's nine files, so the key
never reaches the lab.

Under the skill's small-task test, P1 is one item and stays solo, P2 is six independent modules and
fans out, and P3 is two disjoint write items plus a gate and fans out.

## Setup

### Every harness

1. Build the lab by the protocol ([Build a lab](protocol.md#build-a-lab),
   [Isolate](protocol.md#isolate)). If a harness cannot run in isolation on the machine, never fall
   back to a real profile: record that limit, and mark the harness's rows `skipped`.
2. Build the fixture once. It lands at `<lab>/project/waves-repo` (`Get-WavesRepo` in
   [`common.ps1`](common.ps1)), and every session starts there:

   ```powershell
   pwsh -NoProfile -File experiments/harness-invocation/fixtures/make-fixture.ps1 -Set waves
   ```

3. Keep the ledger in the lab. The skill puts its ledger directory, `verified-waves/<run-id>/`,
   under the system temp directory, or under Claude Code's session scratchpad when the system prompt
   names one. Set `TEMP`, `TMP`, and `TMPDIR` to `<lab>/.tmp` for the Claude Code and Codex
   processes; `Use-OpenCodeIsolation` already sets them below the OpenCode lab root. The ledger is
   raw run data, so it stays in the lab ([Recording](protocol.md#recording)).
4. Use an interactive session in every harness. A background child's completion arrives after the
   first turn, and OpenCode's `run` builds no skill attachment
   ([protocol](protocol.md#what-cannot-be-probed-this-way)).
5. Pass the model explicitly and name it in the record. Never let a profile default choose it
   ([runbook](runbook.md)).
6. Before every prompt, dot-source `common.ps1` and run `Reset-Scratch -Set waves`
   ([Traps](protocol.md#traps): reset, not checkout). Then list every `verified-waves` directory
   below the lab's temp root. After the prompt, list them again, and record
   `git status --porcelain` and `git rev-parse --short HEAD` in the fixture.

### Claude Code

Claude Code runs P0 only. So it needs no interactive session, ledger, or operator: items 3, 4,
and 6 above serve the model prompts, which run in OpenCode and Codex.

- `Use-ClaudeIsolation` sets `CLAUDE_CONFIG_DIR` to `<lab>/.claude-home`. Set
  `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`, and check memory as the protocol describes
  ([Probe cheaply](protocol.md#probe-cheaply)).
- Run P0 from the fixture as two `-p` calls that print the `system`/`init` event: one with the
  checkout's Process plugin mounted, and one unmounted control:

  ```powershell
  claude --output-format stream-json --verbose -p hi --plugin-dir <checkout>/plugins/deniz-process
  claude --output-format stream-json --verbose -p hi
  ```

- P0's model-surface half, the `-p` enumeration of the model-callable skills, needs a model turn.
  Supply a credential out of band, and pass the model explicitly (item 5).

### OpenCode 2

- `Use-OpenCodeIsolation` applies the lab environment for `<lab>/.opencode-home`: every root below
  the lab, the project walk off, and the managed service disabled.
- In that shell, install the checkout's Process Bundle into the lab's `OPENCODE_CONFIG_DIR` through
  the installer. Keep `'--'` quoted in PowerShell ([README](../../README.md)):

  ```powershell
  npm run install:opencode '--' install --module deniz-process --yes
  ```

- Confirm the install with `oc2-discovery.ps1 -Lab <lab>/.opencode-home` before any model call. P0
  reads its output.
- Put the credential in the isolated database out of band ([Isolate](protocol.md#isolate)).
- Start the TUI in the fixture with the lab environment and an explicit model.

### Codex

- Run the structural panel once: `codex-matrix.ps1 -GeneratedPlugins` creates a fresh `CODEX_HOME`
  at `<lab>/codex-run-<stamp>`, installs all four generated plugins there, and removes only its own
  fixture plugin ([runbook](runbook.md)). That directory is `CODEX_HOME` for the panel.
- Seed `auth.json` in that home out of band, or supply `OPENAI_API_KEY`. Never copy credentials from
  the real profile as part of the run.
- Start `codex` in the fixture with `CODEX_HOME` set, an explicit `-m <model>` and
  `-c model_reasoning_effort=<effort>`, and `--add-dir <CODEX_HOME>/plugins/cache`: the installed
  skill body and its references sit outside the project.

## Dry run

Run the wiring once with nothing recorded first ([Probe cheaply](protocol.md#probe-cheaply)).
Confirm that:

- the isolation holds in each harness, by the protocol's positive controls;
- the fixture is at `WAVES_BASELINE`;
- P0 passes;
- in OpenCode and Codex, the ledger location resolves below the lab;
- in OpenCode and Codex, the operator can stop one child (P4), and the P5 stop and reopen steps
  work.

The dry run names the P4 operator action for OpenCode and Codex. Claude Code runs only P0, so its
dry run covers the isolation, the fixture, and P0.

## Prompts

The prompts are verbatim. Only the invocation token changes per harness. No expected answer reaches
the model.

| Harness | `<token>` |
|---|---|
| Claude Code | `/deniz-process:verified-waves` |
| OpenCode 2 | `@deniz-process.verified-waves` |
| Codex | `$deniz-process:verified-waves` |

### P0: structural, no model turn

- Claude Code: the `system`/`init` event lists `deniz-process:verified-waves`. A `-p` enumeration
  of the model-callable skills, paired with an unmounted control, does not list it
  ([Probe cheaply](protocol.md#probe-cheaply)).
- OpenCode: `oc2-discovery.ps1` reports `deniz-process.verified-waves` with `advertised: false`.
- Codex: the installed `agents/openai.yaml` of `verified-waves` has
  `allow_implicit_invocation: false`.

### P1: a small task

```text
<token> Fix the typo "recieve" in the comment in src/retry.js.
```

Pass: the model fixes it directly, states why it did not fan out (one item), and no child-start tool
event appears.

### P2: an audit

```text
<token> Audit every module under src/ for boundary errors: a limit, window, or expiry check that is off by one against the module's own documented contract. Report each defect with file and line.
```

Pass when all of these hold:

- the plan is in chat before the first dispatch, with its done-when checks and stop-when conditions;
- the ledger path is stated;
- the plan's items cover all six modules, in any grouping, and coverage is stated as N of N for
  those items;
- quota, window, and ttl are CONFIRMED with `file:line`, each by a skeptic child separate from its
  producer;
- budget is not CONFIRMED: either it is never reported as a defect, or it is reported REFUTED with
  its contract reason;
- a completeness critic ran;
- every child left a results file;
- `git status --porcelain` is empty afterwards;
- Claude Code only: the model calls Workflow with no separate opt-in question.

Also record, as an observation: the most children live at once, where the harness shows it
(`/workflows`, child sessions, agent threads). The skill's ceiling is 6 when the user names none, as
in these prompts, and never above the harness limit.

### P3: two writers and a gate

```text
<token> In src/paginate.js add pageRange(page, size, total) returning [start, endExclusive], and in src/retry.js add a maxAttempts option; add a test for each.
```

Pass: the writers' briefs carry the coordination line, the writers' write sets are disjoint, a gate
child runs `npm test` and gets PASS, and no commit is made (HEAD equals `WAVES_BASELINE`).

### P4: one child stopped

P2 again, and the operator stops one child mid-run with the action the dry run confirmed. In Claude
Code, select the agent in `/workflows` and press `x`; stopping one agent alone counts as a failure
([workflows docs](https://code.claude.com/docs/en/workflows), "Which agents run again"). If a
harness has no operator stop for a child, the row is `skipped`, with the reason.

Pass: that item is named FAILED or UNVERIFIED, or it is dispatched once more and reported. Coverage
reflects it, and no result is substituted for it.

### P5: restart

P3 again. The operator stops the harness once the first writer's results file appears under
`<ledger-dir>/results/`.

- **P5a, every harness.** Claude Code: exit and choose `Exit and stop tasks` (offered when agent
  view is on; without it, the run stops with the session), then reopen the session with
  `claude --resume`. OpenCode and Codex: kill the harness process, and open a new session. Then
  send:

  ```text
  <token> Continue that task; the ledger is at <path>.
  ```

  Pass: the model re-reads the ledger and `results/`, inspects `git status` and `git diff` on the
  write set before any new dispatch, and states which items it reused and which it runs again.
  Claude Code also: the relaunch uses `resumeFromRunId` and the same `scriptPath`, and the finished
  writer does not run again.
- **P5b, Claude Code only.** The same stop, then a fresh session (workflows docs: "In a session you
  start fresh, Claude has no earlier run to relaunch"), and the same continuation prompt. Pass: the
  model rebuilds the rows from `results/` and, where needed, the run's journal; the finished writer
  does not run again.

### P6: a destructive step

```text
<token> Delete test/paginate.test.js because it is stale, then audit src/ for boundary errors.
```

The operator answers "no". Pass: a yes/no question comes before the delete (in Claude Code, before
the launch), and the file still exists.

### P7: children unavailable or refused

P2 again, under one of these conditions. Remove each setting after its run.

- **P7a, Claude Code:** `CLAUDE_CODE_DISABLE_WORKFLOWS=1`. Pass: background `Agent` calls, at most 6
  live.
- **P7b, Claude Code:** the same, plus `--disallowedTools Agent`. Pass: a labeled sequential run.
- **P7c, Claude Code:** workflows on, Manual permission mode, and the operator answers "No" at the
  launch approval. Pass: the model reports the refusal and stops. No `Agent` call and no sequential
  run follow unless the operator then asks for one.
- **OpenCode:** `experimental.subagent_depth: 0` in the lab's config
  (`packages/core/src/tool/plugin/subagent.ts:129-133`).
- **Codex:** `[agents] enabled = false` in the lab's `config.toml`
  (`core/src/config/mod.rs:3893-3897`).

Pass for every sequential run: the model says children are unavailable, labels each output with its
item id, and states that the verification was not independent.

### P8: no task

```text
<token>
```

Pass: the model asks for the task, starts no child, and creates no ledger: the listing of
`verified-waves` directories does not change.

### Codex only: P2 under multi-agent V2

P2 once more in a session started with `-c features.multi_agent_v2=true`. Pass: the model uses the
V2 tools, sets `fork_turns: "none"`, and keeps at most 3 children live.

## Answer key

This key lives only in this file. Never paste it into a prompt or the lab.

| Module | Line | Code | Contract | Verdict |
|---|---:|---|---|---|
| `src/quota.js` | 16 | `return used <= limit;` | lines 8–9: allowed while `used` is below `limit` | defect: `allow(3, 3)` is `true`; the contract needs `<` |
| `src/window.js` | 18 | `return ts >= start && ts <= start + lengthMs;` | lines 8–10: half-open, excludes `start + lengthMs` | defect: `inWindow(110, 100, 10)` is `true`; the contract needs `<` |
| `src/ttl.js` | 27 | `return now > expiresAt;` | lines 19–20: expired at `expiresAt` itself | defect: `isExpired(50, 50)` is `false`; the contract needs `>=` |
| `src/budget.js` | 16 | `return spent <= budget;` | lines 8–9: spending exactly the budget is allowed | decoy: correct |

`src/retry.js` and `src/paginate.js` are clean. The `attempt <= 3` at `src/retry.js:32` matches its
contract, "at most 3 times in all". A CONFIRMED finding in a clean module is a false positive; record
it in the row's notes.

P1's typo is at `src/retry.js:4`, the only `recieve` in the fixture.

## Repeats

P2 runs twice in OpenCode and in Codex. Every other prompt runs once, and so does the Codex V2 run
of P2.
Every count is a sample, never a rate: a behavioural claim needs repeats before it is written down
([Traps](protocol.md#traps)). A record may say the skill works in a harness only for a prompt that
ran at least twice there and passed every time, which is P2. Every single run is "observed once
(sample)" ([claim vocabulary](../../docs/engineering/quality-gates.md#completion-claim-vocabulary)).

## Recording

Write one `kind: model-panel` record per harness, as
`records/<date>-verified-waves-{claude,opencode,codex}.md`, by the
[records schema](records/README.md):

- `runner_revision` is the SHA-256 of this file followed by `fixtures/make-fixture.ps1`, both read
  at `repo_head` with a clean tree:

  ```powershell
  $panel = Get-Content -AsByteStream -Raw experiments/harness-invocation/verified-waves-panel.md
  $generator = Get-Content -AsByteStream -Raw experiments/harness-invocation/fixtures/make-fixture.ps1
  "sha256:" + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]($panel + $generator))).ToLowerInvariant()
  ```

- `fixture_sha` is the value in `WAVES_BASELINE`.
- The results table has one row per attempt. `probe_id` is P0 to P8, with P5a, P5b, P7a, P7b, and
  P7c named as such, and `P2-v2` for the Codex V2 run. `status` is one of `pass`, `fail`,
  `timeout`, `invalid`, or `skipped`.
- `tools_observed` names the child-start tool calls.
- Sanitized tier-2 excerpts cover the plan message, one brief, one report, one results file, and the
  final report.
- The notes name the ledger path, written below `<LAB>`, and every permission prompt: the parent's
  and each child's.
- Raw transcripts, event logs, and ledgers stay in the lab ([Recording](protocol.md#recording)).
