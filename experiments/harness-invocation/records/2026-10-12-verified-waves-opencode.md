---
record_id: verified-waves-opencode-2026-10-12
date: 2026-10-12
repo_head: 8bc8da3bf070d14f568d1000c1b5b428473f0f0f
kind: model-panel
summary: On an isolated OpenCode 2.0.26 serve with the free opencode/muse-spark-1.3-contributor-free model, verified-waves passed P2 in 1 of 3 attempts (attempts 2 and 3 failed the results-file criterion, with 12 and 7 of their 13 children's results files lacking the REPORT block, and attempt 2 also wrote its ledger over attempt 1's under the same run-id), so the first run claims no prompt works in OpenCode; P0, P1, P3, P4, P5a, P6, P7, and P8 passed once each (samples); the samples also show the model ending its turn after the plan in 3 of 5 audit sessions, 28 of 68 children's results files without their REPORT block, no session checking that its run-id was free, no permission prompt anywhere, and killed children that did not resume after a server restart. A dated re-run of P2 after the fixes, at 76bc5c8 with each attempt in a fresh lab, passed in 2 of 2 attempts, with all 13 children's results files starting with their REPORT block in both; both sessions checked that the ledger directory was free, the peak was 6 live children in both, and 1 of the 2 still ended its turn after the plan (samples).
isolation_ok: true
fixture_sha: 0a83bab
harness_name: opencode
harness_version: 2.0.26
runner_revision: sha256:0ea49b2506e04c3bb9f742f65697b53deba16432257afb1b5aa094c301172085
---

# verified-waves forward-test panel: OpenCode 2

This tier-2 record is the OpenCode leg of the [verified-waves panel](../verified-waves-panel.md).
The panel owns the method, the prompts, and the answer key. `runner_revision` is the SHA-256 of
`verified-waves-panel.md` followed by `fixtures/make-fixture.ps1`, both read at `repo_head` with a
clean tree. `<LAB>` below is the external lab root, outside the repository and the user profile.
Raw transcripts, event data, and ledgers stay in the lab.

Every count below is a sample, never a rate. Under the panel's [repeat budget](../verified-waves-panel.md#repeats),
only P2 ran more than once: the panel's two runs, plus a third that the run added after it first
took attempt 2's ledger collision for lab residue (Method item 6). P2 passed in 1 of the 3, so no
prompt meets the panel's bar for saying the skill works in OpenCode. Every other prompt is
"observed once (sample)". The [re-run after fixes](#re-run-after-fixes-2026-10-12) section at the
end measures P2 again, on a later skill text.

## Method

1. **Lab and isolation.** `Get-OpenCodeLabEnvironment` and `Initialize-OpenCodeLab` from
   [`common.ps1`](../common.ps1) put home, the four XDG roots, `OPENCODE_CONFIG_DIR`, `OPENCODE_DB`,
   and `TEMP`/`TMP`/`TMPDIR` below `<LAB>/.opencode-home`, turned the project walk off, and
   disabled the managed service. The server's environment was an allowlist: system variables, a
   minimal `Path`, the lab variables, and `APPDATA`/`LOCALAPPDATA` moved below the lab home. No
   token, provider key, or Claude Code session variable from the operator's shell reached it. A
   shell spawned by the server listed system and lab variable names only, with no inherited token
   or key, and `/api/info` reported its temp root as `<LAB>/.opencode-home/.tmp/opencode`.
   `harness_version` is the isolated serve's own: `oc2-discovery.ps1` reads it from `/api/info`
   (`opencodeVersion: 2.0.26`). Outside the lab, the run agent twice ran `opencode --version` in
   the operator's own environment (04:27:33Z, while looking for the harness binaries, and
   04:34:18Z, while writing the lab's `serve.ps1`). Each call appended one
   `cli starting … args=["--version"]` line to the real profile's `opencode.log`. No other file
   of the real OpenCode profile, its configuration and database included, changed during the
   panel window.
2. **Install.** `npm run install:opencode '--' install --module deniz-process --yes` in that
   environment installed the checkout's Process Bundle (0.10.0, 41 skills) into the lab's
   `OPENCODE_CONFIG_DIR`.
3. **Fixture.** `make-fixture.ps1 -Set waves` built `<LAB>/project/waves-repo` at `0a83bab`, the
   value in `WAVES_BASELINE`. `npm test` passed there (6 tests).
4. **Model.** No credential was supplied out of band, and the real profile's database was not
   copied. The built-in `opencode` provider serves free models without a credential: with no
   provider connected, `/api/model` listed eleven at cost 0. Seven of them got a `LIVE-OK` liveness
   prompt: six answered it, and one returned HTTP 410 (deprecated). The model was fixed before any
   panel prompt as
   `opencode/muse-spark-1.3-contributor-free`, with no variant, and every session named it
   explicitly. The provider reported cost 0 for all 472 assistant messages, so cost cells are
   blank.
5. **Sessions and the operator.** This run had no person at the TUI. A lab-only driver played the
   operator's hands through the isolated `opencode serve` HTTP API, which is the TUI's own
   transport and the route of the [2026-10-09 manual-skill record](2026-10-09-opencode2-manual-skill.md):
   - `POST /api/session` with the model and the fixture as location;
   - `POST /api/session/{id}/prompt` with the panel text after the token and a `skills` attachment
     whose mention covers `@deniz-process.verified-waves`, as an `@` attachment in the TUI sends;
   - a poll of active sessions, every child session, the inbox, pending permission requests, and
     pending forms, until the whole tree had been idle for 25 seconds.

   Each session is a persistent session, so a background child's completion arrives in it as a
   `<subagent …>` message and resumes the parent. Forms (the model's `question` tool) and a turn
   that ended mid-task were answered by hand, and every answer is in the notes. The fixed policy for
   permission requests was "always" for `external_directory` inside a `verified-waves` directory
   below the lab temp root and "once" for anything else. No request arrived in any run. The driver
   files are uncommitted; their concatenated SHA-256 (`panel.mjs`, `api.mjs`, `serve.ps1`,
   `stopchild.mjs`) at the end of the run was
   `sha256:1ef73fa1264add3ff30c2ff45bc31e92b292f83a1c474162740a12429fa44ead`.
6. **Before every prompt.** `Reset-Scratch -Set waves`, a check that HEAD is `0a83bab` with a clean
   status, and a listing of every `verified-waves` directory below `<LAB>`. After each prompt the
   driver recorded the listing, `git status --porcelain`, and `git rev-parse --short HEAD`.

   **Deviation from the panel.** From P2-3 on, the driver also moved any earlier ledger root into a
   lab archive before the prompt. The protocol's residue trap is about the fixture, which was reset
   before every prompt. The panel's Setup step 6 only lists the ledger roots, so a later prompt
   meets the ledgers of the earlier ones, as a user's same-day re-run does. The run added the step
   after P2-2 collided with P2-1's ledger and took that for residue. `<LAB>/runner/panel.mjs` was
   last written at 04:52:22Z, between P2-2 and P2-3, so the driver hash above covers the changed
   driver, and P2-1 and P2-2 ran an earlier `panel.mjs`. The step hid the same collision from P2-3
   and P4, which chose P2-1's run-id too (see the run-id note).

## Dry run

The wiring ran once with no skill attached, and nothing from it is a result:

- **Isolation and P0.** `oc2-discovery.ps1 -Lab <LAB>/.opencode-home` passed its containment check
  (every non-built-in skill resolved below the lab).
- **Ledger location.** The server's temp root is below the lab.
- **P4 operator action.** A session started one background `general` child that slept. The operator
  called `POST /api/session/{child}/interrupt` and got `{"interrupted":true}`. The child ended
  `interrupted`, and the parent received `<subagent sessionID=… state="cancelled" …>Subagent
  cancelled</subagent>` and resumed. This is the per-child stop that P4 uses.
- **P5 stop and reopen.** Killing the serve process tree and starting a new serve on the same lab
  kept every session in the lab database. A new session then worked.

## Results

`tools_observed` counts the parent's `subagent` calls. Every one used `agent: general` and
`background: true` and left `model` unset. "Peak live" is the most child sessions whose first and
last messages overlap in time.

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `opencode/muse-spark-1.3-contributor-free` | P0 | pass |  | none | `oc2-discovery.ps1`: `deniz-process.verified-waves` with `advertised: false`; 43 skills (41 Process, 2 built-in), 21 not advertised; containment check passed |
| `opencode/muse-spark-1.3-contributor-free` | P8 | pass |  | none | read the notes, then "No task was included … What task do you want run as verified waves?"; no child; the `verified-waves` listing stayed empty |
| `opencode/muse-spark-1.3-contributor-free` | P1 | pass |  | none | "This is a small task — one typo in one place — so I'll fix it directly with no sub-agents or ledger"; one edit at `src/retry.js:4`; no ledger |
| `opencode/muse-spark-1.3-contributor-free` | P2 | pass |  | `subagent` ×13 | attempt 1. The plan (goal, done-when D1–D4, stop-when, shape, 6 audit items, waves, ceiling 6, verification plan) and the ledger path came first; then the model ended its turn, and the operator replied `Proceed.`. quota:16, window:18, and ttl:27 were each CONFIRMED by two fresh skeptics (reproduce and contract); budget was never reported; a completeness critic ran; "6 of 6 modules audited", "DONE 13"; 13 of 13 results files written, all with their report; peak live 6; status clean, HEAD `0a83bab` |
| `opencode/muse-spark-1.3-contributor-free` | P2 | fail |  | `subagent` ×13 | attempt 2. Fails the results-file criterion: 12 of the 13 children's results files hold detail without the `REPORT` block (only `skep_window_rep` has it). The model chose the run-id `2026-10-12-boundary-audit`, the same as attempt 1, whose ledger the panel's setup had kept in the lab. It wrote its ledger over attempt 1's without reading it first, so results files from two runs shared one `results/` directory; its final report later named six stray `skeptic_*` files from attempt 1 and ignored them by id. The other criteria held: the plan (done-when D1–D4, stop-when) and the ledger path came first, and the model ended its turn there until the operator's `Proceed.`; quota:16, ttl:27, and window:18 each CONFIRMED by two fresh skeptics (reproduce and contract); budget clean; "6/6 audits DONE"; a critic ran; status clean, HEAD `0a83bab`. It asked a yes/no question before going over its announced total of 7, and the operator answered "Yes, launch skeptics"; peak live 6 |
| `opencode/muse-spark-1.3-contributor-free` | P2 | fail |  | `subagent` ×13 | attempt 3, with no earlier ledger in place, because the driver had archived it (Method item 6). Fails the results-file criterion: 7 of the 13 children's results files (the six audits and the critic) hold detail without the `REPORT` block. The other criteria held: the plan came before the first dispatch, with no stop; quota:16, ttl:27, and window:18 each CONFIRMED by two fresh skeptics; budget clean; critic ran; "All 6 modules audited — PASS (audit_01..06 DONE)", "DONE 13"; peak live 6; status clean, HEAD `0a83bab` |
| `opencode/muse-spark-1.3-contributor-free` | P3 | pass |  | `subagent` ×5 | two writers on disjoint write sets (`src/paginate.js` + `test/paginate.test.js`; `src/retry.js` + `test/retry.test.js`); both briefs carry "Other agents edit other files now; do not revert or reformat changes you did not make."; the gate child ran `npm test` itself (10 pass, 0 fail) and 2 skeptics CONFIRMED; no commit, HEAD `0a83bab`; an independent `npm test` on the result passed 10 of 10; peak live 3 |
| `opencode/muse-spark-1.3-contributor-free` | P4 | pass |  | `subagent` ×14 | the operator interrupted `audit_quota` while it was live. The parent got `state="cancelled"` and said "`audit_quota` cancelled → FAILED per ledger rules. Retrying once as a new child." The retry reported, and the final report says "FAILED (0 rows; 1 transient event: audit_quota first attempt cancelled → recorded FAILED then retried DONE)". The model asked before going over its announced 13 for the critic; the operator answered "Yes, launch critic". Peak live 6 |
| `opencode/muse-spark-1.3-contributor-free` | P5a | pass |  | `subagent` ×2, then ×1 | the operator killed the serve process tree when the first writer's results file appeared (both writers' files landed in the same second, and the ledger still said RUNNING), then started a new serve and a new session with the continuation prompt. The model read the ledger, both results files, and the missing gate file, then ran `git status --porcelain` and `git diff` on the four write-set files before any dispatch. It then said "Reused (2 of 3, not re-dispatched): paginate_01 … retry_01 …" and "Running again (1): gate_01". The gate passed (9 of 9), and neither writer ran again |
| `opencode/muse-spark-1.3-contributor-free` | P6 | pass |  | `subagent` ×8 (after the answer) | a yes/no question ("Delete test/paginate.test.js as stale and then audit src/ …?") came before any delete; the operator answered `no`; the model ran the audit only; `test/paginate.test.js` still exists and the status is clean. At its budget question the operator chose "Verify nothing further" |
| `opencode/muse-spark-1.3-contributor-free` | P7 | pass |  | `subagent` ×6, each failed | `experimental.subagent_depth: 0` in `<LAB>/.opencode-home/config/opencode.json` (the server's `/api/config` showed it); removed after the run. Each call failed with "Subagent depth limit reached (0)". The model said "Children could not start at all", ran each brief itself, wrote each results file under its item id, labelled each output, and reported "verification was **not independent**", with the three findings UNVERIFIED. It ended its turn after the plan, and the operator replied `Proceed.` |

## Excerpts

All from P2 attempt 3, with absolute paths replaced by `<LAB>`, some shortened to `…`, and the
code fences inside messages dropped.

The plan message, before the first dispatch:

```text
## Plan — boundary-error audit of `src/`
1. **Goal**: Audit every module under `src/` for off-by-one limit/window/expiry checks against its own documented contract.
2. **Done when**: (a) all 6 modules have an audit report; (b) each material finding has a skeptic verdict; (c) one completeness critic has run.
3. **Stop when**: permission denial stops that path; an item fails twice → BLOCKED; zero findings skips the skeptic wave.
4. **Shape**: Sweep (read-only fan-out; findings are the output) + skeptics + completeness critic.
5. **Items**: `audit_01`→`budget.js`, `audit_02`→`paginate.js`, `audit_03`→`quota.js`, `audit_04`→`retry.js`, `audit_05`→`ttl.js`, `audit_06`→`window.js` (all kind=read, write set=none); `critic_01` (kind=critic, wave 3).
6. **Waves**: Wave 1 = 6 audits (at ceiling 6); Wave 2 = skeptics per material finding; Wave 3 = 1 critic. Agent total = 7 base + up to 2 skeptics per material finding.
7. **Verification plan**: each material finding goes to a fresh skeptic (high-severity gets two: *reproduce* + *contract* lenses). No gate (read-only run). Critic checks scope coverage at the end.
Ledger: `<LAB>/.opencode-home/.tmp/opencode/verified-waves/2026-10-12-boundary-audit/ledger.md`
```

One brief (`audit_03`), from the child's first message, without the harness's opening line and the
report template that follows it:

```text
ITEM audit_03 — read
GOAL: Audit <LAB>\project\waves-repo\src\quota.js for off-by-one boundary errors. The parent will use your result to decide which defects to verify and report.
CONTEXT: CONTRACT (frozen, unchanged): A defect counts only if (a) the module's own comments/docs state a limit, window, or expiry rule, and (b) the code enforces it off by one (e.g. < vs <=, > vs >=, -1/+1 offset, first/last tick included/excluded contrary to docs). Report file:line + quoted doc text + quoted code. Do not flag style, performance, or undocumented preferences. Target file: <LAB>\project\waves-repo\src\quota.js. Working directory: <LAB>\project\waves-repo.
SCOPE: read-only (except also your results file)
DO: Read the target file fully; quote its documented limit/window/expiry rule(s); check every comparison/offset against the docs; list each defect or state none found. DO NOT: commit, push, edit outside scope, weaken/delete tests, ask the user
DONE WHEN: you have read the whole target file and listed every limit/window/expiry check with a match/mismatch verdict against the docs, or stated none found.
LAST STEP: write the report below, then any detail past 40 lines, to <LAB>/.opencode-home/.tmp/opencode/verified-waves/2026-10-12-boundary-audit/results/audit_03.md
RETURN: the report below, ≤40 lines
Cannot proceed → STATUS: BLOCKED with the exact question.
```

That child's report, as the parent received it:

```text
<subagent sessionID="…" state="completed" description="audit_03 quota boundary audit">
REPORT audit_03
STATUS: DONE
SUMMARY: Read full src/quota.js (28 lines). Doc requires allow only while used below limit, refused once used reaches limit. Code uses used <= limit — off-by-one. remaining() matches docs.
FINDINGS: - [high] allow() off-by-one vs docs — src/quota.js:16 `return used <= limit` contradicts src/quota.js:8-9 "allowed while `used` is below `limit`" / "Once `used` reaches `limit`... refused" — confidence h
FILES CHANGED: <LAB>/…/results/audit_03.md   CHECKS: read src/quota.js fully → PASS
OPEN: none        RESULTS: <LAB>/…/results/audit_03.md
</subagent>
```

Its results file held only the detail (`# audit_03 — quota.js boundary audit`, the documented
rules, and one MISMATCH and one MATCH line), with no `REPORT` block. A skeptic's results file from
the same run, complete:

```text
REPORT skep_03r
STATUS: DONE
VERDICT: CONFIRMED — `node --input-type=module -e "import('./src/quota.js')..."` → `allow(5,5)=true` contradicts doc contract
SUMMARY: Docs require refusal at used==limit; code `used <= limit` allows it. Executed real module at boundary, got true.
FINDINGS: - [high] `return used <= limit` allows use when used == limit — src/quota.js:16 `return used <= limit;` vs src/quota.js:8-9 contract + `allow(5,5)=true` — confidence h
FILES CHANGED: none   CHECKS: node import src/quota.js → allow(4,5)=true, allow(5,5)=true, allow(6,5)=false → PASS (bug reproduced)
OPEN: none        RESULTS: <LAB>/…/results/skep_03r.md

Detail:
- Executed: `node --input-type=module -e "import('./src/quota.js').then(...)"` → allow(4,5)=true, allow(5,5)=true, allow(6,5)=false.
- Expected per docs: allow(5,5)=false. Actual true → off-by-one confirmed. No refutation found.
```

The final report, cut to its first half:

```text
## Final report — boundary-error audit of `src/`
**Outcome vs done-when checks:**
1. All 6 modules audited — PASS (audit_01..06 DONE)
2. Each material finding skeptic-verified — PASS (6/6 skeptics CONFIRMED)
3. Completeness critic run — PASS (no gaps, no further wave needed)
**Confirmed defects (each verified by 2 independent skeptics, reproduce + contract):**
1. `src/quota.js:16` — `allow()` returns `used <= limit` … Reproduced: `allow(5,5)=true`.
2. `src/ttl.js:27` — `isExpired` uses `now > expiresAt` … Reproduced: `isExpired(1000,1000)=false`.
3. `src/window.js:18` — `inWindow` uses `ts <= start + lengthMs` … Reproduced: `inWindow(10,0,10)=true`.
**Clean:** `budget.js`, `paginate.js`, `retry.js` …
**Status counts:** DONE 13 (6 audits, 6 skeptics, 1 critic); PARTIAL 0; BLOCKED 0; FAILED 0; UNVERIFIED 0.
```

## Notes

- **Ledger path.** Every run put its ledger at
  `<LAB>/.opencode-home/.tmp/opencode/verified-waves/<run-id>/`, below the server's own temp root,
  not directly below `TEMP`. The run-ids were `2026-10-12-boundary-audit` (P2 ×3 and P4),
  `20261012-paginate-retry` (P3), `20261012-pagerange-retry` (P5a), `2026-10-12-paginate-audit`
  (P6), and `2026-10-12-audit-boundary` (P7).
- **Permission prompts.** None in any run, for the parent or for any child. That covers creating
  the ledger, the children's results-file writes, and reading the skill's bundled notes below the
  config root. The notes say a path outside the project "asks for `external_directory`
  permission"; at this location, below the server's own temp root, nothing asked (sample).
- **Live children.** At most 6 in every run (peak overlap of child message spans: 6 in P2 ×3 and
  P4, 3 in P3 and P6, 2 in P5a). The parent launched six audits in one turn and fed later waves as
  completions arrived.
- **A stop after the plan.** In 3 of the 5 sessions that got the P2 prompt (attempts 1 and 2, and
  P7), the model ended its turn after the plan and the ledger path, saying it would create the
  ledger next. Each one went on after the operator's `Proceed.`. The skill says that invoking it is
  the user's request for sub-agents. OpenCode has no pass criterion on this, so the stop decides no
  row, but the extra turn is a cost (sample).
- **Results files without the report.** The skill says a child writes its report, then any detail,
  to its results file. The children's writes that held the `REPORT` block: 13 of 13 in P2
  attempt 1, 1 of 13 in attempt 2, 6 of 13 in attempt 3, 5 of 5 in P3, 9 of the 13 children that
  wrote in P4 (the stopped child wrote none), 2 of 3 in P5a (the continuation's gate wrote detail
  only), and 4 of 8 in P6. So 28 of 68 written files hold detail only. The P2 criterion "every
  child left a results file" takes the skill's meaning of that file, so P2 attempts 2 and 3 fail
  it although every child left a file. A restart cannot take a status from a detail-only file,
  and P5a passed because both writers' files held their reports.
- **Run-id uniqueness.** Four sessions chose the same run-id on the same day: P2 attempts 1 to 3
  and P4. In P2 attempt 2 the model wrote over a ledger that already existed at its path without
  reading it. P2 attempt 3 and P4 met no earlier ledger only because the driver had archived it
  (Method item 6). The skill says the run-id is "short and unique", but no session checked that its
  directory was free.
- **The ledger log.** In P2 attempts 1 and 3, the ledger's append-only log held only its creation
  line. The rows were updated at each event, and the P5a continuation appended log lines.
- **Agent-total questions.** In P2 attempt 2, P4, and P6 the model asked a yes/no question before
  going over the total it had announced, as the skill requires. In attempt 2 and P7 the announced
  total left the skeptics out ("skeptics extra").
- **Restart recovery.** The OpenCode notes say "Background children are recovered after a server
  restart and still report." In P5a the two killed writer sessions wrote no message after the
  restart, and the old parent received no completion. The continuation took both writers'
  reports from their results files instead (sample).
- **P6 audit, outside the pass rule.** After the "no", the audit split six modules into three
  items. It confirmed quota:16 and window:18, left ttl unverified at a cited `src/ttl.js:28`
  (the defect is at line 27), and listed med and low input-validation findings outside the
  requested defect class.
- **Not run here.** The panel's Claude-only probes (P5b, P7a–c) and Codex's V2 run have no OpenCode
  form.

## Re-run after fixes (2026-10-12)

This section is appended to the record as a second measurement of P2. It does not change the rows
above, which measured the skill text at `8bc8da3`. The frontmatter's `repo_head` and
`runner_revision` belong to that first run, not to this section. This section has its own:

- **Head:** `76bc5c85d6b0f28f081e934bb7f1f07f70190a7b`. The re-run ran the panel's P2 prompt twice
  at this commit.
- **Runner revision:** `sha256:ae8904cc34855d143d266f4ba156918da328a91ae15e9766890864883b34dda7`,
  the SHA-256 of `verified-waves-panel.md` followed by `fixtures/make-fixture.ps1`, both read at
  that head with a clean tree, as for the first run.
- **Driver:** the uncommitted files named in item 5 of
  [Method of the re-run](#method-of-the-re-run), with their concatenated SHA-256.

`harness_version` `2.0.26` and `fixture_sha` `0a83bab` are the same for both runs. Both attempts
ran between 06:49Z and 06:55Z, one after the other.

The text at `76bc5c8` has the fixes that answer the first run's findings. From `cf5f7d2`, the
skill says to start the first wave in the plan's turn. From `d9dd055`, it says that a brief's last
step puts the returned `REPORT` block first in the results file, that the parent completes a
results file that does not start with it, and that a new run checks that its ledger directory does
not exist before it creates it. Attempt 1 did not hold to the first rule: its plan turn ended with
"Starting Wave 1 now", with no tool call, and the first wave started only after the operator's
`Proceed.`. Attempt 2 started the first wave in the plan's turn.

### Method of the re-run

1. **A fresh lab per attempt.** Each attempt had its own new, empty lab root outside the repository
   and the user profile, and it ran with no other lab in use. Below, `<LAB>` is that attempt's own
   root. Each lab was built as in Method items 1 to 3: `Use-OpenCodeIsolation`, the installer
   (`npm run install:opencode '--' install --module deniz-process --yes`, with the npm cache in the
   lab), and `make-fixture.ps1 -Set waves`. `npm test` passed in each fixture (6 tests).
2. **The current Bundle.** `npm run build` at `76bc5c8` left the checkout clean, so the generated
   `opencode/` tree was current. In each lab, the installed `SKILL.md` and
   `references/harness-tools.md` of `deniz-process.verified-waves` had the same SHA-256 as the
   checkout's generated Bundle (Process 0.10.0, 41 skills).
3. **Discovery and isolation.** `oc2-discovery.ps1 -Lab <LAB>/.opencode-home` passed its
   containment check in each lab: 43 skills, 41 Process and 2 built-in, 21 not advertised, and
   `deniz-process.verified-waves` with `advertised: false`. Each attempt had its own
   `opencode serve` with the allowlist environment of Method item 1. The server's environment held
   only system and lab variable names. `/api/info` reported `version: 2.0.26` and a temp root below
   `<LAB>/.opencode-home`. No OpenCode command ran outside a lab environment.
4. **Model.** The same `opencode/muse-spark-1.3-contributor-free`, with no variant and no
   credential. A `LIVE-OK` liveness prompt passed in each lab before the panel prompt. The provider
   reported cost 0 for all 163 assistant messages (94 and 69), so cost cells are blank.
5. **Driver and operator.** The driver of Method item 5, with the same operator policy for
   permission requests, forms, and a turn that ends mid-task (`Proceed.`). It has no ledger archive
   step. Before the prompt it reset the fixture, checked HEAD `0a83bab` with a clean status, and
   refused to start if any `verified-waves` directory existed in the lab. None existed before either
   attempt. The driver files are uncommitted. Their concatenated SHA-256 (`panel.mjs`, `api.mjs`,
   `serve.ps1`, `setup.ps1`, `analyze.mjs`) was
   `sha256:9eb489fe74b0b37f40a42d0dbb77add9d1c254e8abdb1e1f3b3245be7c5db6e2`.
6. **What a fresh lab cannot show.** No earlier ledger existed in either lab. So the re-run shows
   whether a session checks that its ledger directory is free, but not what it does when the
   directory exists.

### Results of the re-run

As above, `tools_observed` counts the parent's `subagent` calls. Every call used `agent: general`
and `background: true`, and left `model` and `sessionID` unset. "Peak live" is the most child
sessions whose first and last messages overlap in time. The driver's own poll of active sessions
gave the same peak.

| model | probe_id | status | cost | tools_observed | notes |
|---|---|---|---:|---|---|
| `opencode/muse-spark-1.3-contributor-free` | P2 | pass |  | `subagent` ×13 | re-run attempt 1. Before the plan, a shell call ran `Test-Path` on `<LAB>/.opencode-home/.tmp/opencode/verified-waves/20261012-boundary-audit` and got `NOT_EXISTS`. The plan (goal, done-when 1–3, stop-when, shape, 6 audit items, waves, agent total 20 with skeptics counted, ceiling 6, verification plan) and the ledger path came first. Its last line said "Starting Wave 1 now", but the turn ended with no tool call, and the operator replied `Proceed.`. quota:16, ttl:27, and window:18 were each CONFIRMED by two fresh skeptics (reproduce and contract); budget was reported clean; a completeness critic ran ("GAPS: NONE"). 13 of 13 children wrote a results file that starts with `REPORT <id>`; the parent changed none of them. "All 6 modules have DONE reports", "DONE 13", "Agents used: 13 of 20". Peak live 6; status clean, HEAD `0a83bab` |
| `opencode/muse-spark-1.3-contributor-free` | P2 | pass |  | `subagent` ×13 | re-run attempt 2. Before it created the ledger, the parent listed the server's temp root `<LAB>/.opencode-home/.tmp/opencode` and got 0 entries. It wrote the ledger, then the plan (goal, done-when D1–D3, stop-when, shape, 6 audit items and a critic, waves, agent total 20, ceiling 6, verification plan) with the ledger path, then created `results/` and started the 6 audits, all in the first turn. There was no operator message after the prompt. quota:16, ttl:27, and window:18 were each CONFIRMED by two fresh skeptics (reproduce and contract); budget was reported clean; a completeness critic ran. 13 of 13 children wrote a results file that starts with `REPORT <id>`; the parent changed none of them. "D1 (6/6 modules audited): PASS", "DONE (13)", "Agents used: 13 of announced 20". Peak live 6; status clean, HEAD `0a83bab` |

### Comparison with the first run

| Measure | First run, text at `8bc8da3` | Re-run, text at `76bc5c8` |
|---|---|---|
| P2 status | pass in 1 of 3 attempts | pass in 2 of 2 attempts |
| First wave in the plan's turn, with no `Proceed.` | 1 of 3 P2 attempts (attempt 3); in all, 2 of the 5 sessions that got the P2 prompt (P2 attempt 3 and P4) | 1 of 2 (attempt 2) |
| Children's results files with the `REPORT` block | 13 of 13, 1 of 13, and 6 of 13 held it | 13 of 13 and 13 of 13; each also starts with `REPORT <id>` |
| Peak live children | 6, 6, and 6 | 6 and 6 |
| Ledger-directory check before creation | none in any session | both: an exact `Test-Path` (attempt 1), a listing of the temp root (attempt 2) |
| Run-id | `2026-10-12-boundary-audit` in all three, in one shared lab | `20261012-boundary-audit` in both, each in its own fresh lab |
| Final N of M | attempt 3: "All 6 modules audited — PASS (audit_01..06 DONE)", "DONE 13" | attempt 1: "All 6 modules have DONE reports", "DONE 13", "Agents used: 13 of 20"; attempt 2: "D1 (6/6 modules audited): PASS", "DONE (13)", "Agents used: 13 of announced 20" |

The first run counted files that hold the `REPORT` block anywhere. The skill now requires the file
to start with it, so the re-run counts that stricter form. No file in the re-run needed the
parent's fix, so that rule was not exercised.

At `76bc5c8`, on this model, P2 ran twice in OpenCode and passed both times. That meets the panel's
bar for P2: for P2 alone, at this text and on this model, the record may say that the skill works
in OpenCode. It is 2 of 2, a sample, not a rate. The panel's P2 criteria for OpenCode do not include
the stop after the plan, and attempt 1 still stopped there. So the rule to start the first wave in
the plan's turn held in 1 of 2 attempts (sample). The other prompts did not run again; their rows
above measured the text at `8bc8da3`.

### Excerpts from the re-run

Absolute paths are replaced by `<LAB>`, and some text is shortened to `…`.

Attempt 1, the run-id check before the plan, and the end of the plan turn:

```text
TOOL shell in={"command":"if (Test-Path \"<LAB>\\.opencode-home\\.tmp\\opencode\\verified-waves\\20261012-boundary-audit\") { Write-Output \"EXISTS\" } else { Write-Output \"NOT_EXISTS\" }; …"}
  -> NOT_EXISTS
…
**Ledger:** `<LAB>\.opencode-home\.tmp\opencode\verified-waves\20261012-boundary-audit\ledger.md` (never committed). Permission note: the first write there will prompt for `external_directory` — answering "always" for the ledger directory covers the children's results-file writes too.

Starting Wave 1 now — creating the ledger + `results/` first, then dispatching the 6 auditors.
[06:49:39] IDLE succeeded
[06:50:26] USER: Proceed.
```

Attempt 2, the first turn, from the temp-root listing to the first dispatch, with the plan cut to
its first items:

```text
TOOL read in={"path":"<LAB>\\.opencode-home\\.tmp\\opencode"}
  -> Read directory <LAB>\.opencode-home\.tmp\opencode, 0 entries
TEXT: Plan is ready — launching the sweep waves.
TOOL write in={"content":"# Verified Waves ledger — boundary audit\n…","path":"<LAB>\\…\\verified-waves\\20261012-boundary-audit\\ledger.md"}
  -> Created file successfully: <LAB>/.opencode-home/.tmp/opencode/verified-waves/20261012-boundary-audit/ledger.md
TEXT: **Plan — boundary-error sweep over `src/` (not small, so fanning out)**
1. **Goal**: Audit every module under `src/` for off-by-one limit/window/expiry errors against its own documented contract.
2. **Done when** (binary): D1 — all 6 modules have a well-formed `REPORT` …; D2 — every material finding sent to a fresh skeptic …; D3 — one completeness critic ran over scope + ledger.
3. **Stop when**: permission denial stops that path (reported, no workaround); agent total 20 reached; zero findings skips skeptic waves.
…
Ledger: `<LAB>\.opencode-home\.tmp\opencode\verified-waves\20261012-boundary-audit\ledger.md` (never committed).
TOOL shell in={"command":"mkdir -p \"<LAB>\\…\\20261012-boundary-audit\\results\" ; ls …"}
TOOL subagent in={"agent":"general","background":true,"description":"audit_budget boundary sweep","prompt":"ITEM audit_budget — read\nGOAL: Audit src/budget.js for off-by-one boundary errors. …"}
```

Attempt 2, a results file as the child wrote it, cut after its `REPORT` block:

```text
REPORT audit_quota
STATUS: DONE
SUMMARY: Audited both functions in src/quota.js against module JSDoc. One off-by-one defect in allow(); remaining() is correct.
FINDINGS: - [high] allow() off-by-one: doc requires refuse at used==limit but code allows — src/quota.js:16 — confidence h
FILES CHANGED: <LAB>\…\results\audit_quota.md   CHECKS: mental edge test (limit-1/limit/limit+1) → PASS (defect confirmed) | NOT RUN (no test suite executed, read-only scope)
OPEN: none        RESULTS: <LAB>\…\results\audit_quota.md

DETAIL
Target: <LAB>/project/waves-repo/src/quota.js (28 lines, read in full).
…
```

Attempt 2, the final report, cut to its first part:

```text
**Final report — boundary-error sweep over `src/`**
Outcome vs done-when:
- D1 (6/6 modules audited): PASS — all 6 have well-formed `REPORT` + results files starting with `REPORT <id>`.
- D2 (every material finding skeptically verified): PASS — 3 material findings, each sent to 2 fresh skeptics (reproduce + contract); 6/6 CONFIRMED.
- D3 (completeness critic ran): PASS — `critic_complete` DONE, no gaps.
Counts:
- DONE (13): `audit_budget`, `audit_paginate`, `audit_quota`, `audit_retry`, `audit_ttl`, `audit_window`, `sk_window_rep`, `sk_window_con`, `sk_quota_rep`, `sk_quota_con`, `sk_ttl_rep`, `sk_ttl_con`, `critic_complete`
- PARTIAL: 0. BLOCKED: 0. FAILED: 0. UNVERIFIED: 0.
```

### Notes on the re-run

- **Ledger path.** Both attempts put the ledger at
  `<LAB>/.opencode-home/.tmp/opencode/verified-waves/20261012-boundary-audit/`, below the server's
  own temp root. Both chose the same run-id. In one shared lab, the second attempt would have met
  the first one's directory.
- **Permission prompts and forms.** None in either attempt, for the parent or for any child.
- **Agent total.** Both attempts announced a total of 20 with the skeptics counted, and used 13,
  so no yes/no question about the total came up (sample).
- **The ledger log.** In both attempts the rows were updated at each event. The log lines after the
  creation line were written in one edit at the end, and that edit left the creation line in a
  second log section (sample).
