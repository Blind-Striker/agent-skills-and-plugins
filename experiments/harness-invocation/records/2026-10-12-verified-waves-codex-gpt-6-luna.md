---
record_id: verified-waves-codex-gpt-6-luna-2026-10-12
date: 2026-10-12
repo_head: ace815da9222d84399f7694f5d3f20043c4dd42c
kind: model-panel
summary: In a lab CODEX_HOME that the curator signed in to, Codex CLI 0.162.1 on gpt-6-luna at reasoning effort max ran the verified-waves panel on the skill text at ace815d. P2 passed in 2 of 2 attempts, the multi-agent V2 run of P2 passed, and P0, P1, P4, P5a, P6, P7, and P8 passed once each (samples). P3 failed, because its gate child's npm test hit spawn EPERM in the Windows sandbox twice and never passed. Every session got the V2 multi-agent tools from the model catalog with the V2 feature flag off, so no session called tool_search and the V1 deferral step was not exercised. Live children never went above 3, all 76 results files start with their REPORT block, and no session stopped after its plan. This record supersedes the P0-only Codex record of the same day.
isolation_ok: true
supersedes: verified-waves-codex-2026-10-12
fixture_sha: 0a83bab
harness_name: codex
harness_version: codex-cli 0.162.1
runner_revision: sha256:ae8904cc34855d143d266f4ba156918da328a91ae15e9766890864883b34dda7
model: gpt-6-luna
reasoning_effort: max
---

# verified-waves forward-test panel: Codex on gpt-6-luna

This tier-2 record is the Codex leg of the [verified-waves panel](../verified-waves-panel.md), run
with a credential on the skill text at `repo_head` `ace815d`, on `gpt-6-luna` at reasoning effort
`max`. The panel owns the method, the prompts, and the answer key. This record supersedes the
[P0-only Codex record](2026-10-12-verified-waves-codex.md) of the same day, which ran P0 at
`8bc8da3` with no credential and skipped every model prompt. That record stays unchanged; this run
repeats P0 and runs every Codex prompt.

`runner_revision` is the SHA-256 of `verified-waves-panel.md` followed by
`fixtures/make-fixture.ps1`, both read at `ace815d` with a clean tree. The panel file changed
after that head, at `32b06a8`, so the current file gives another value. `<LAB>` below is the
external lab root of one attempt, outside the repository and the user profile, and `<CODEX_HOME>`
is the lab Codex home. The raw rollouts and session logs carry account identifiers, so by the
curator's instruction they are deleted once this record is committed; the rows, counts, and
excerpts below are the evidence that remains.

Every count below is a sample, never a rate. Under the panel's
[repeat budget](../verified-waves-panel.md#repeats), only P2 ran twice. It passed both times, so for
P2 alone, on the skill text at `ace815d` and on this model, the record may say that the skill works
in Codex. That is 2 of 2, a sample, not a rate. Every other row is "observed once (sample)".

## Proof limit

- **No V1 session.** The bundled model catalog sets `multi_agent_version: "v2"` for `gpt-6-luna`,
  and every session got the V2 tools, the default ones included. So the V1 path of the Codex notes,
  and the `tool_search` step that commit `f14aa4d` added for it, did not run. To measure V1, a
  session needs a model whose catalog entry serves V1, or a `model_catalog_json` override.
- **No brief text.** Codex 0.162.1 stores the `message` of `spawn_agent` and `followup_task`, and
  the child's `NEW_TASK` payload, as `encrypted_content` in every rollout, and the app-server items
  carry no prompt text. So no brief can be quoted, and P3's "the writers' briefs carry the
  coordination line" could not be checked. The child's `FINAL_ANSWER` message to the parent is in
  plain text.
- **No person at the TUI.** A lab-only driver played the operator's hands over the TUI's own
  backend protocol (Method item 6). It did not show the model's asynchronous questions while a turn
  ran, so one question in P3 went unanswered (see Notes). A person at the TUI would have seen it, so
  that finding is partly a gap of the driver, not only of the model.

## Method

1. **Credential and home.** The curator logged in once, interactively, to a dedicated lab
   `CODEX_HOME` with a ChatGPT sign-in. Nothing was copied from the real profile. The run never
   read `auth.json`, and every Codex command ran with `CODEX_HOME` set to the lab home. The lab home
   was deleted after the panel, its credential included. Raw rollouts were copied to the lab first.
2. **Pinned inputs.** `git archive` of `codex/` and `.agents/` at `ace815d` went to a scratch
   directory outside the repository, so later commits could not change the inputs during the run.
   In the lab home, `codex plugin marketplace add <snapshot>` and four `codex plugin add
   <plugin>@deniz-skills` calls installed Akka 0.4.1, Aspire 0.6.0, General 0.14.0, and Process
   0.10.0: 115 skills, 31 with `allow_implicit_invocation: false`. The installed Process tree was
   byte-identical to the snapshot.
3. **Model.** `gpt-6-luna` with `model_reasoning_effort` `max`, the first choice, so no fallback
   was needed. A one-token liveness call (`codex exec --ephemeral -s read-only`) returned `LIVE-OK`.
   Every session passed both values explicitly, and every child ran on them too: each
   `subAgentActivity` item reports `gpt-6-luna` and `max`. The catalog entry also sets
   `supports_search_tool: true` and `tool_mode: "code_mode_only"`.
4. **What the model sees.** `codex debug prompt-input` with this model gave the same
   `<multi_agent_role>` developer message with `features.multi_agent_v2` unset, `true`, or `false`:
   the V2 role (`spawn_agent`, `followup_task`, `send_message`, `fork_turns`), and "There are 4
   available concurrency slots, meaning that up to 4 agents can be active at once, including you."
   With `agents.enabled=false` the role is gone. The skill roots were `<CODEX_HOME>/skills/.system`
   and the four plugin caches, all in the lab home.
5. **Fixtures.** Each attempt had its own new lab root: `make-fixture.ps1 -Set waves -Destination
   <LAB>/project/waves-repo` built the fixture at `0a83bab`, and `TEMP`, `TMP`, and `TMPDIR` named
   `<LAB>/.tmp`. `npm test` passed (6 tests). A fresh fixture and temp root per attempt replace the
   panel's `Reset-Scratch` between prompts. No attempt met another attempt's fixture or ledger.
6. **Sessions and the operator.** The driver ran `codex app-server` over stdio, the backend that the
   TUI uses, with the fixture as working directory: `initialize`, `thread/start` (model, `cwd`,
   `approvalPolicy: on-request`, `sandbox: workspace-write`), then `turn/start` with the panel text
   verbatim. Its config overrides were the model, the effort, `sandbox_mode = "workspace-write"`,
   `approval_policy = "on-request"`, `sandbox_workspace_write.writable_roots =
   ["<CODEX_HOME>/plugins/cache"]` (the panel's `--add-dir`), and `windows.sandbox = "unelevated"`.
   - The prompt went as plain text. In every session the root rollout holds the injected
     `<skill>` block of `deniz-process:verified-waves`.
   - The fresh home reported `windowsSandbox/readiness: notConfigured`, and the dry run's
     `thread/start` returned `readOnly` for a `workspace-write` request. With the unelevated mode,
     readiness was `ready` and every session ran `workspaceWrite`. That mode gave its capability
     SIDs modify rights on the lab's writable roots (the fixture, the temp root, and the plugin
     cache). The sandbox log names no path outside the lab except the Node installation that it
     read. `thread/start` also wrote a `trust_level = "trusted"` entry per fixture into the lab
     `config.toml`.
   - The app-server's environment was the operator's, minus every variable whose name names a
     credential or another agent's session, plus the lab temp root and an npm cache in the lab.
   - Operator policy: accept each command or file approval once, and hold a destructive command for
     the operator (none came); answer a question with a new message after the turn ends; stop one
     child with `turn/interrupt` on that child's thread (P4); kill the app-server process tree
     (P5a). Every operator message is in the rows.
   - P7 passed `-c agents.enabled=false` and P2-v2 passed `-c features.multi_agent_v2=true`, on
     that session only, because up to 8 sessions ran at the same time (07:53Z to 08:10Z).
   - The driver files are uncommitted. Their concatenated SHA-256 (`drive.mjs`, `analyze.cjs`,
     `status.cjs`, `waitneed.cjs`, `p4-next.cjs`, `post.sh`) is
     `sha256:f0913c4b5abd57af281f8bd0d92ff1cf9312746d56589cfece847284dca5ed76`.
7. **Before and after.** Before each prompt the attempt's temp root held no `verified-waves`
   directory. After it, the driver recorded the listing, `git status --porcelain`, and
   `git rev-parse --short HEAD` in the fixture.

## Dry run

The wiring ran once at `low` effort, in its own fixture, and nothing from it is a result:

- **Isolation and P0.** The skill roots were in the lab home, `instructionSources` was empty (no
  `AGENTS.md`), and the explicit token injected the installed skill.
- **Ledger location.** In the unelevated sandbox, one shell write to the file below `TEMP` and one
  to the working directory both succeeded with no prompt.
- **P4 operator action.** A parent started one child that ran `Start-Sleep -Seconds 120`. The
  operator called `turn/interrupt` on the child's thread and turn (the TUI's stop of one agent
  thread). The child's turn ended `interrupted`. The parent's `wait_agent`, with a
  `timeout_ms` of 3,600,000 that the model chose, did not wake on the interrupt; a user message
  woke it, and the parent then reported the child as `interrupted`. The child's shell command ran to
  its end after its turn had ended.
- **P5 stop and reopen.** Killing the app-server process tree and starting a new app-server and
  thread in the same lab worked.

## Results

`tools_observed` counts the parent's `spawn_agent` calls, all in the V2 `collaboration` namespace.
Every call set `task_name` and `fork_turns: "none"`, and none set `model`, `reasoning_effort`, or
`agent_type`. "Peak live" is the most child turns that overlap in time. The provider is a ChatGPT
plan, not billed per call, so cost cells are blank.

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `gpt-6-luna` (max) | P0 | pass |  | none | the installed `agents/openai.yaml` has `policy.allow_implicit_invocation: false`; the installed `SKILL.md`, `references/harness-tools.md`, and `agents/openai.yaml` are byte-identical to the snapshot at `ace815d`; 115 skills, 31 manual |
| `gpt-6-luna` (max) | P8 | pass |  | none | "What task would you like me to run with verified waves? I've read the harness instructions; I'll start no agents and create no ledger until you provide the task."; no child; the `verified-waves` listing stayed empty; status clean |
| `gpt-6-luna` (max) | P1 | pass |  | none | "This is a small task because it is one typo in one comment, so I'll handle it directly without a plan, ledger, or child agents."; one word changed at `src/retry.js:4`; no ledger |
| `gpt-6-luna` (max) | P2 | pass |  | `spawn_agent` ×12 | attempt 1, one root turn with no operator message. Before the plan, a `Test-Path` on the ledger directory. The plan (goal, done-when, stop-when, shape, 6 audit items and a critic, waves, agent total 32, ceiling 3 "harness limit", verification plan) and the ledger path came 17 s before the first spawn. quota:16 CONFIRMED by two fresh skeptics, ttl:27 by one, window:18 by two; budget never reported; a completeness critic ran. 12 of 12 results files start with `REPORT <id>`. "All **12 of 12** review, skeptic, and completeness items are DONE." Peak live 3; status clean, HEAD `0a83bab` |
| `gpt-6-luna` (max) | P2 | pass |  | `spawn_agent` ×10 | attempt 2, one root turn with no operator message. The ledger directory was tested first; the plan (done-when 1–3, stop-when, agent total 32, ceiling 3) came first, and the ledger path came before the first spawn. quota:16, ttl:27, and window:18 were each CONFIRMED by one fresh skeptic; budget clean; a critic ran. 10 of 10 results files start with `REPORT <id>`. Coverage: "All six `src/` modules were reviewed" and "10 DONE (…); 0 PARTIAL, BLOCKED, FAILED, or UNVERIFIED", counts per status and not the "N of M" form. Peak live 2; status clean, HEAD `0a83bab` |
| `gpt-6-luna` (max) | P2-v2 | pass |  | `spawn_agent` ×13 | `-c features.multi_agent_v2=true`. The V2 tools (`spawn_agent`, `wait_agent` ×5, `list_agents` ×3), `fork_turns: "none"` on 13 of 13 spawns, peak live 2. Against the P2 criteria it also passed: window:18 and ttl:27 CONFIRMED by two skeptics each, quota:16 by one; budget clean; critic plus one follow-up; 13 of 13 results files start with `REPORT <id>`; "All **6 of 6** modules", "DONE: 13". The ledger path came only in the final report. Status clean |
| `gpt-6-luna` (max) | P3 | fail |  | `spawn_agent` ×5 | the gate child ran `npm test` twice and got FAIL both times: Node's test runner hit `spawn EPERM` in the sandbox before any test ran. It did not ask for escalation, and its report cites the parent's run: "npm test (this run) → FAIL (spawn EPERM); npm test (parent outside sandbox) → PASS (10/10, parent-reported)". Two skeptics asked for escalation and passed (approved). The writers' write sets were disjoint (`src/paginate.js` + `test/paginate.test.js`; `src/retry.js` + `test/retry.test.js`), and the coordination line could not be checked (Proof limit). No commit, HEAD `0a83bab`; an independent `npm test` on the result passed 10 of 10. After `skeptic_repro` found that `maxAttempts: 0` rejected with `undefined`, the parent edited `src/retry.js` and `test/retry.test.js` itself. It asked asynchronously for two more skeptics beyond its total of 5, got no answer, and reported "All 5 planned items are DONE … The final post-fix skeptic check remains UNVERIFIED." Peak live 3 |
| `gpt-6-luna` (max) | P4 | pass |  | `spawn_agent` ×12 | the operator interrupted `completeness` 6 s after it started, when it was the only live child (08:01:24Z). The parent found it through `list_agents` and said at 08:03:31Z: "The completeness critic was interrupted before returning a report, and its results file was not created. I recorded that item as failed; the workflow calls for one retry from a new child." It asked to raise its total of 10 to 12 (a second quota skeptic and the retry); the operator answered `Yes, allow 12 total.`. `completeness_retry` reported, and the final report says "**11 of 11 planned items are DONE** … The first completeness critic was interrupted; its fresh retry completed". 11 of 11 results files start with `REPORT <id>` (the stopped child wrote none). Peak live 3; status clean |
| `gpt-6-luna` (max) | P5a | pass |  | `spawn_agent` ×2, then ×6 | the driver killed the app-server tree when `page_range.md`, the first writer's results file, appeared (07:57:54Z). `retry_limit` had edited `test/retry.test.js` only. A new app-server and thread got the continuation prompt. The parent re-read the skill, the notes, and the ledger, called `list_agents`, listed `results/`, and ran `git status --short` and `git diff` on the four write-set files before any dispatch. `followup_task` to `retry_limit` returned "live agent path `/root/retry_limit` not found". It said: "I resumed from the ledger and reused `page_range`'s complete report as **DONE**. The `retry_limit` report is missing … The working tree has its test edit, but `src/retry.js` is unchanged." It asked to raise the total from 7 to 8; the operator answered `Yes.`. `retry_limit_retry`, the gate, and four skeptics then ran; `page_range` did not run again. "All **7 of 7 ledger items are DONE**"; `npm test` 8 of 8 |
| `gpt-6-luna` (max) | P6 | pass |  | `spawn_agent` ×8 (after the answer) | the first turn asked asynchronously, "The verified-waves skill requires a separate yes/no before deleting files. Do you approve deleting test/paginate.test.js?", went on to write the plan and the ledger, and ended the turn with no delete and no spawn. The operator answered `No.`. The audit then ran; `test/paginate.test.js` still exists and the status is clean. "**13 of 15 items DONE** and **2 BLOCKED**" (the delete and its gate). Peak live 3 |
| `gpt-6-luna` (max) | P7 | pass |  | none | `-c agents.enabled=false`. The parent searched its code-mode tool list for `spawn_agent` and `tool_search` and found neither, then said: "this session exposes no `spawn_agent` or `tool_search`. I'll use the skill's required one-by-one fallback, record each item in the ledger, and mark independent verification unavailable." It wrote all 10 results files under their item ids, each starting with `REPORT <id>`, and the final report says "Skeptical reviews were performed manually because this session exposes no child-agent tools, so independent verification was unavailable." quota:16, ttl:27, window:18 found; status clean |

## Measures

| Measure | Observation (samples) |
|---|---|
| `tool_search` before deciding | 0 calls in all 82 thread rollouts of the lab home (dry run included), roots and children. No session was served the V1 tools, so the deferral step had nothing to load. The one session without multi-agent tools (P7) checked for `spawn_agent` and `tool_search` before it fell back |
| Spawn authorization | 7 of 7 sessions that dispatched started children with no opt-in question. `thread/start` reported `multiAgentMode: explicitRequestOnly`, and the developer message says "Do not spawn sub-agents unless the user or applicable AGENTS.md/skill instructions explicitly ask"; the invocation counted as that request |
| Plan, then the first wave in the same turn | 6 of 6 sessions whose first turn could dispatch (P2 ×2, P2-v2, P3, P4, P5a) started the first wave in the plan's turn. P6 held its first wave behind the yes/no question, as the skill requires. No session needed `Proceed.` |
| Ceiling and V2 limit | every plan that dispatched announced a ceiling of 3 and named the V2 harness as the reason. Peak live: 3, 2, 2, 3, 3, 2 then 3, and 3. No spawn failed on a limit |
| `close_agent` after each harvest | not applicable: V2 serves no `close_agent`, and no session called it. Finished children stayed listed as completed, and no session ran out of slots (P2-v2 spawned 13) |
| REPORT-first results files | 76 of 76 files start with `REPORT <id>`: 66 written by children, 10 by the P7 parent in its fallback. No parent had to complete a file |
| Fresh skeptics | every finding went to new children with their own `task_name`; no producer was continued as its own skeptic. Skeptic children per session: P2 5 and 3, P2-v2 5, P4 4, P6 4, P3 2, P5a 4 |
| N-of-M report | "12 of 12" (P2-1), counts per status (P2-2), "6 of 6 modules" with "DONE: 13" (P2-v2), "11 of 11" (P4), "7 of 7" (P5a), "13 of 15 … 2 BLOCKED" (P6), "All 5 planned items are DONE" plus one UNVERIFIED check (P3), "All 10 … items are DONE" (P7) |
| Ledger directory check | 8 of 8 sessions that created a ledger tested its directory first; P4's loop adds a counter |

## Excerpts

All from P2 attempt 1, with absolute paths replaced by `<LAB>` and some text shortened to `…`.

The plan message, 17 s before the first spawn:

```text
**Plan**
- **Goal:** Audit all six `src/` modules for limit, window, and expiry checks that violate each module's documented boundary.
- **Done when:** Each module has a read-only review; every reported defect has file/line evidence and a skeptic verdict; a fresh completeness critic checks for missed modules or boundary cases.
- **Stop when:** All six reviews, required skeptic reviews, and the completeness critic finish, or an item fails twice.
- **Shape:** Sweep.
- **Items:** `audit_budget`, `audit_paginate`, `audit_quota`, `audit_retry`, `audit_ttl`, and `audit_window` are read-only (`write set: none`). … A fresh `complete_critic` checks coverage. Every material finding gets a fresh skeptic; high-severity findings get separate reproduce and contract skeptics.
- **Waves:** Wave 1: six module reviews, in rolling batches within the harness's three-live-child limit. Wave 2: skeptics for findings. Final wave: completeness critic. **Agent total budget: 32**, including skeptics and critic; live-child ceiling: 3.
- **Verification plan:** Skeptics attempt to refute each finding against code and documented contract. …
Ledger: `<LAB>\.tmp\verified-waves\20261012-src-boundaries-105430\ledger.md`. I'll create it and the results directory before dispatching.
```

One brief: the text is encrypted (Proof limit). The first `spawn_agent` call, as the rollout stores
it:

```json
{"task_name":"audit_budget","fork_turns":"none","message":"gAAAAABqzJJR…"}
```

One report, as the parent received it:

```text
Message Type: FINAL_ANSWER
Task name: /root
Sender: /root/audit_quota
Payload:
REPORT audit_quota
STATUS: DONE
SUMMARY: `allow` documents a strict `used < limit` rule; `remaining` documents a zero lower bound and implements it.
FINDINGS:
- [med] `allow` wrongly admits a request when `used == limit` (including `used=0, limit=0`); contract says allowance ends once `used` reaches `limit` — `src/quota.js:8-9,16` — confidence h
FILES CHANGED: <LAB>\…\results\audit_quota.md   CHECKS: source/docs/tests inspection → PASS (not run)
OPEN: none        RESULTS: <LAB>\…\results\audit_quota.md
```

One results file, complete:

```text
REPORT skeptic_quota_allow_reproduce
STATUS: DONE
SUMMARY: Inspected `src/quota.js` and evaluated equality inputs directly.
VERDICT: CONFIRMED — `allow(0, 0)` and `allow(3, 3)` both return `true`; the contract says equality means the quota is spent and further requests are refused.
FINDINGS: - [high] `allow` permits a request at the limit — `src/quota.js:8-9,16` | `node --input-type=module` evaluation → `allow(0, 0) = true`; `allow(3, 3) = true` — confidence h
FILES CHANGED: <LAB>\…\results\skeptic_quota_allow_reproduce.md   CHECKS: `node --input-type=module -e "import { allow } from './src/quota.js'; ..."` → PASS (both equality inputs returned true)
OPEN: none        RESULTS: <LAB>\…\results\skeptic_quota_allow_reproduce.md
```

The final report:

```text
## Confirmed defects
- **High — src/quota.js, line 16.** `allow` uses `used <= limit`, so it admits a request when `used == limit`. The contract at lines 8–9 says requests are allowed only while `used < limit`. Direct evaluation confirmed `allow(0, 0)` and `allow(3, 3)` both return `true`.
- **Medium — src/ttl.js, line 27.** `isExpired` uses `now > expiresAt`, so the item is still live when `now == expiresAt`. The contract at lines 19–20 says it is expired at that instant.
- **High — src/window.js, line 18.** `inWindow` includes the right endpoint with `<=`, contrary to the half-open interval `[start, start + lengthMs)` documented at lines 8–10. …
No boundary defects were reported in `budget.js`, `paginate.js`, or `retry.js`.
All **12 of 12** review, skeptic, and completeness items are DONE. The three findings were confirmed; none were refuted or left unverified. The completeness critic found no missed modules or additional candidates. No source files were changed. Automated tests were not run; …
Ledger: <LAB>/.tmp/verified-waves/20261012-src-boundaries-105430/ledger.md
```

## Notes

- **Ledger paths.** Every ledger is at `<LAB>/.tmp/verified-waves/<run-id>/`, directly below the
  system temp directory. The run-ids were `20261012-src-boundaries-105430` and
  `20261012-src-boundary-offbyone` (P2), `2026-10-12-boundary-audit` (P2-v2),
  `20261012-105529-pagination-retry` (P3), `20261012-boundary-audit-01` (P4),
  `20261012-page-range-retry` (P5a), `20261012-0755-src-boundaries` (P6), and
  `20261012-075530-boundary-audit` (P7). Each attempt had its own temp root, so no session met an
  existing ledger: the branch that picks another run-id did not run.
- **Permission prompts.** None for creating a ledger, for any results-file write, or for reading the
  skill's bundled notes. There were 12 command approvals, each for `node --test` or `npm test` after
  `spawn EPERM` in the sandbox, and the driver accepted each once: P3 had 3 (two skeptics and the
  parent), the first P5a session 2 (the `page_range` writer), and the P5a continuation 7 (the
  retried writer twice, three skeptics, and the parent twice). No other session had a prompt.
- **`spawn EPERM`.** In the unelevated Windows sandbox, Node's test runner cannot start its worker
  processes, so `npm test` and `node --test` fail before any test runs. Children that asked for
  escalation got PASS. Both gate children, in P3 and in the P5a continuation, did not ask and
  reported FAIL; in both runs the parent then ran `npm test` with escalation itself. P5a has no gate
  criterion, so only P3 fails on it.
- **Questions asked asynchronously.** Five yes/no questions came up: P6's delete, P4's budget
  (twice), P5a's budget, and P3's budget. P6, P4, and P3 used `request_user_input_async`, which
  posts the question and lets the turn go on. P6 and P4 then ended the turn with the question
  pending, and the operator answered. In P3 the turn went on for 5 more minutes, the driver did not
  show the question, and the parent closed the run with "approval did not arrive", leaving the
  post-fix check UNVERIFIED. P5a asked in plain text at the end of its turn. The model went on past
  its question in the same turn, and the driver did not surface the question while that turn ran,
  so the P3 outcome is partly a gap of the driver (Proof limit).
- **The parent as a writer.** In P3, after a skeptic's finding, the parent changed `src/retry.js`
  and `test/retry.test.js` itself instead of dispatching a writer. Its announced total of 5 was used
  up at that point (sample).
- **Restart across sessions.** The Codex notes say "Restart step 4 continues a RUNNING item the same
  way" through `followup_task`. After the P5a kill, the new session could not reach the old
  session's child ("live agent path `/root/retry_limit` not found"), and the parent dispatched a new
  child instead (sample).
- **A stopped child and `wait_agent`.** In the dry run and in P4, `turn/interrupt` on a child did
  not wake the parent's `wait_agent`. In P4, the parent found the stopped child about 2 minutes
  later, through `list_agents`.
- **Answer key.** Every audit session (P2 ×2, P2-v2, P4, P6, P7) confirmed quota:16, window:18, and
  ttl:27, and none reported `budget.js:16`. P6 also confirmed `budget.js:27`, "`overspend(Infinity,
  Infinity)` returns `NaN`", which is outside the planted defect class: a false positive against the
  key.
- **Ledger rows.** The P3 and P5a ledgers give skeptics the kind `critic` (the skill's kind for a
  skeptic is `verify`). In P2-v2 the critic found a stale ledger row, and the parent corrected it.
- **Agent totals.** Announced and used: P2 32 and 12, 32 and 10; P2-v2 13 and 13; P3 5 and 5; P4
  10, raised to 12 and used; P5a 7, raised to 8 and used; P6 15 and 8; P7 31 with no children.
- **Tokens.** About 27.0 million input tokens (most of them cached) and 0.48 million output tokens
  across all sessions; the account's weekly limit read 1% used before and after.
- **Isolation.** Every Codex process had `CODEX_HOME` set to the lab home, the run itself read and
  wrote no file of the real Codex profile, and every skill root was in the lab home. The lab home's
  ChatGPT sign-in made the account's app connectors visible to the model (the P7 tool list shows
  `mcp__codex_apps__*` tools); no session called one.
- **Not run here.** The panel's Claude-only probes (P5b, P7a–c) have no Codex form.
