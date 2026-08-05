# S13 plan review — xAI / Grok

Date: 2026-08-05

Reviewer/model lineage: Grok 4.5 (xAI), independent subagent review. Author of the
spec/plans under review was a different lineage (Claude / Opus 5 per handover).

Planning scratch (AGENTS.md, Documentation Hygiene): delete when the reviewed work merges.
This report is also planning scratch under `docs/superpowers/reviews/` and should leave with
the wave close-out once findings are dispositioned.

## Verdict

**Do not execute Wave A/B as written.** Multiple High defects would leave T3's zero
excluded-name end state false, produce a self-poisoning patch recut on Task 11, and ship
description claims the bodies cannot honour. Correct findings 1–5 (and the Task 11 ceremony)
before any task runs. The curator retains the go decision.

Live baseline at review (no `npm run build` — shared tree with another reviewer):

- `git`: `master` @ `ea8b805`, dirty with uncommitted s13 docs only; submodules unchanged from
  pins recorded 2026-08-04 (`dotnet-agent-skills` `85cd1034`, `dotnet-skills` `c2ac7e9`, etc.).
- `npm run validate` → `0 error(s), 6 warning(s)` (2 devcert + elements-of-style +
  subagent-driven-development + teach + writing-great-skills parked SKILL.md links).
- `npm test` → 134/134 pass; `npm run typecheck` clean.

## Ranked findings

### 1. High — Wave A leaves excluded artifact names in kept output (T3 / Scan 1 end state fails)

The plan's Task 10 final sweep
(`docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:468-469`) and the patches in
Tasks 4–10 do **not** clear every excluded-name hit a real Scan 1 finds on kept skills.

**Missed by the patch set (live today, still present after applying the planned edits):**

| Location | Excluded name(s) | Evidence |
|---|---|---|
| `test-anti-patterns` body Over-mocking row | `exp-mock-usage-analysis` | `external/dotnet-agent-skills/plugins/dotnet-test/skills/test-anti-patterns/SKILL.md:90` — Task 6 never edits this sentence |
| `test-anti-patterns` body Duplicate-tests row | `exp-test-maintainability` | same file `:100` — Task 6 never edits this sentence |
| `test-analysis-extensions` body capability list | `test-tagging` | `…/test-analysis-extensions/SKILL.md:57` — Task 10 only rewrites the description + `extensions/dotnet.md` consumer line |
| All 11 `extensions/*.md` section headers | `test-tagging` | e.g. `extensions/dotnet.md:113` `## Tag/Trait Attributes (for \`test-tagging\`)` — none are in Task 10 |
| `mjml-email-templates` related-skills | `verify-email-snapshots` | `plugins/deniz-dotnet-general/skills/mjml-email-templates/SKILL.md:21` (`testing/verify-email-snapshots`) — no Wave A task at all |

The hand-maintained alternation at plan `:468-469` also **omits** `verify-email-snapshots`, so
Task 10 Step 6 can print "clean" while T3 and
`docs/superpowers/specs/2026-08-04-t-items.md:46-47` remain false.

**Required:** extend Task 6 (drop or reword the two exp-\* handoffs), extend Task 10 (body tag
capability + every extension header, or deliberately narrow the playbook exception and the
end-state claim), add a task or frontmatter/body edit for `mjml-email-templates` → kept
`snapshot-testing`, and derive the Scan 1 name set from the manifest rather than a partial list.

### 2. High — Task 11 patch-extension ceremony self-includes `overlay.patch`

Ceremony as written
(`wave-a-general-curation.md:488-491`, then recut at `:534`):

```text
npm run eject -- … --patch --force
git apply -p1 --directory=overlays/…/testcontainers-integration-tests overlays/…/overlay.patch
# edit…
npm run eject -- … --patch
```

Live tools behavior:

- `listFiles` ignores `overlay.patch` (`tools/lib/overlay.ts:66-68`), so a patch-only dir has
  `working.length === 0` and `--force` re-lays upstream (`tools/eject.ts:196-207`). Phase 1 is OK.
- Phase 1 does **not** delete the existing `overlay.patch`; `cpSync` leaves it beside the working
  copy.
- Phase 2 snapshots the whole directory with `cpSync(dest → tmp/b)` (`eject.ts:215-217`), so the
  retained `overlay.patch` appears as an **added file** in the newly cut patch.

**Required:** after a successful `git apply`, delete or move aside the old `overlay.patch` before
any further edit and before the second `eject --patch`. Document that as a mandatory step; dry-run
once before trusting the path. Fallback (manual re-application of three hunks) remains valid.

### 3. High — Absorbed-job description claims exceed body capability (ADR-0007 honest trigger)

Curator approved consolidation policy, not unsupported capability claims.

**`test-anti-patterns` (Task 6, plan `:262-264`):** new description text claims "shallow or
low-variety assertions" and "named test smells from the testsmells.org catalog". The body is still
the pragmatic anti-pattern tables; it does not gain `assertion-quality`'s metrics workflow or
`test-smell-detection`'s academic catalog. Task 7 then rewires
`measuring assertion variety (use assertion-quality)` → `test-anti-patterns`
(`wave-a-general-curation.md:313,322`), actively routing a job the absorber does not perform.

**`coverage-analysis` (Task 8, plan `:359-365`):** description and body claim the targeted
single-method/class CRAP job absorbed from `crap-score`. Upstream `crap-score` is explicitly
named-method / class / **single source file**
(`external/…/crap-score/SKILL.md:4-7`). `Compute-CrapScores.ps1` only accepts
`-CoberturaPath`, `-CrapThreshold`, `-TopN` (no target filter;
`plugins/…/coverage-analysis/scripts/Compute-CrapScores.ps1:17-20`) and emits top-N hotspots.
"Filter the report to the named method" fails when the target is outside TopN, and drops
single-file scope entirely.

**Required:** either (a) patch bodies/scripts so the claimed jobs are real, or (b) rewrite
descriptions to honest trigger shape ("pragmatic audit that overlaps assertion/smell concerns";
"project-wide CRAP/coverage including reading a named row out of the hotspot list when present")
without asserting unsupported workflows. Do not redirect gap-analysis to a phantom metrics home.

### 4. High — Promotion-on-contact incomplete on touched bodies (T3 / ADR-0008)

Global plan rule and T3: bodies touched by these patches promote live references to namespaced
spelling + `depends_on` (first mention per target). Several bare redirects to **kept** skills
survive in patched files and are absent from proposed `depends_on`:

| Touched body | Bare live target | Source line (upstream) | Proposed `depends_on` |
|---|---|---|---|
| `run-tests` | `mtp-hot-reload` | `run-tests/SKILL.md:37` (and description `:17-18`) | only `[filter-syntax, platform-detection]` |
| `test-anti-patterns` | `run-tests` | `test-anti-patterns/SKILL.md:42` (and description) | only `[test-analysis-extensions]` |
| `test-gap-analysis` | `test-anti-patterns` | `test-gap-analysis/SKILL.md:40` (plus rewired variety line) | only `[test-analysis-extensions]` |

These remain candidate-tier edges in bodies the plan already owns — contrary to T3 and the
promotion-on-contact rule the plan itself states (`wave-a-general-curation.md:16`).

**Required:** namespace the first mention of each kept target and extend `depends_on` in the same
change (both directions of the declaration contract).

### 5. High — Replacement header still states a false coercion-free claim (T5)

Task 2 header rewrite (`wave-a-general-curation.md:92-97`) says knowledge/`auto` descriptions are
"honest, trigger-shaped, and **free of coercion**". Kept (and mostly unpatched) descriptions still
carry coercive loaders:

- `run-tests`: "ALWAYS use when the user asks…" (`run-tests/SKILL.md:4-5`) — Task 4 does not
  remove this.
- `test-anti-patterns`: "INVOKE whenever asked…" (`test-anti-patterns/SKILL.md:6`) — Task 6 keeps
  it while rewriting other description clauses.

T5 only required dropping the false "none carries disable-model-invocation" claim and the
"honour the upstream fire-design" rationale
(`docs/superpowers/specs/2026-08-04-t-items.md:24-25`). The stronger "free of coercion" sentence
is author wording and is false on the live set.

**Required:** delete "free of coercion", or obtain a curator ruling to strip ALWAYS/INVOKE
loaders from every `auto` description in the same wave.

### 6. Medium — Many description-level old-string anchors are not contiguous at the pin

Plan global rule: "Anchor edits by exact strings"
(`wave-a-general-curation.md:17`). Body-level anchors checked at the current pin mostly **HIT**.
Several **description** old-strings **MISS** as contiguous substrings because upstream wraps with
`description: >` indentation:

| Task | Planned old-string (prefix) | Result |
|---|---|---|
| 6 | `duplicated tests, or magic values` | MISS (`magic` / `values` split across lines) |
| 6 | `writing new tests (use code-testing-agent, or writing-mstest-tests for MSTest); running tests` | MISS |
| 6 | long `assertion-diversity…writing-mstest-tests).` clause | MISS |
| 8 | `where to add tests, coverage analysis, coverage report. DO NOT USE FOR:…` | MISS |
| 9 | `EF Core data access or query optimization (use optimizing-ef-core-queries), frontend/Blazor work` | MISS |
| 10 | `for the test ANALYSIS skills (assertion-quality, … test-tagging)` | MISS |
| 10 | `Do not use directly — invoked by the test-quality-auditor…` | MISS (wrap) |

Body anchors for Tasks 4–5, 6 body table, 7, 8 body, 9 body, 11 API names, and header paragraphs
**HIT**. Implementers who paste the description old-strings into a literal replace will no-op
silently.

**Required:** quote old-strings with the real newlines/indent, or instruct "match across folded
description lines / edit the YAML folded block as a whole".

### 7. Medium — Playbook Scan 1 is noisy; Wave A then hand-maintains a partial name list

A real current-tree walkthrough of Scan 1 (excluded names × `plugins/deniz-dotnet-general/**/*.md`)
confirms the playbook's threat model but also its operational weakness:

- Excluded set size today: **80** `exclude: true` rows (→ 86 after T1).
- Naive alternation matches product words and substrings (e.g. excluded agent stem `msbuild`
  against MSBuild skill prose; `build-perf` against taken `build-perf-*` names). False-positive
  discipline in the playbook (`reference-audit-playbook.md:73-83`) is necessary, not optional —
  but the procedure gives no mechanical filter, only judgment.
- Name derivation text says "keys of `docs/ledger.json`" (`:24`) — ledger keys are composite
  (`deniz-dotnet-general/skill/run-tests`), not output names. Item-level `name:` precedence is
  stated nowhere (SCHEMA requires it; this module currently has none, but the playbook is durable).
- Wave A Task 10 Step 6 then supplies a **hand-maintained** pattern and tells the agent that is the
  gate — contradicting "Never hand-maintain these lists" (`:21-22`).

**Required:** fix derivation (frontmatter `name` / item `name:` / basename fallback), require
token boundaries on the alternation, and make Wave A's close-out scan consume the derived set (or
delete the playbook's "never hand-maintain" line if a frozen allowlist is intentional).

### 8. Medium — Wave B tests under-specify the contract; RED count is wrong

`docs/superpowers/plans/2026-08-04-wave-b-opencode-stub-commands.md`:

- Tests cover bare `SKILL.md` and `../SKILL.md` via one fixture write (`:79`), not the live
  `./SKILL.md` spelling (`opencode/skills/teach/RESOURCES-FORMAT.md:29`).
- Regex `(\]\((?:\.\.\/|\.\/)?)SKILL\.md` (`:129`) allows at most one `../`; no test for deeper
  climbs (none live today — residual risk only).
- No assertion that the stub names **both** mount paths, contains no `@`, that bundle-less
  `manual` remains byte-identical inline, or that `both` is unchanged.
- RED expectation (`:106-109`) under-counts: two new tests fail, the parked-report assertion
  fails, **and** the added `BODY.md` assert on the existing invocation test
  (`:104-105`) fails → **four** red tests, not "two new + one updated".
- No mount smoke (project `.opencode/` vs global / `OPENCODE_CONFIG_DIR`) for the prose stub —
  research already showed `@` is project-root-only
  (`docs/research/skill-invocation-across-harnesses.md:144-154,239-243`).

Implementation sketch order must write `BODY.md` **before** emitting the stub and before
`listFiles` for the report; otherwise L6
(`tools/validate.ts:612-618`) errors on `skills/<name>/BODY.md`. Say that explicitly.

### 9. Medium — Ledger / spec arithmetic for patches is wrong

Recount from live manifest + plan tasks:

| Quantity | Plan/spec claim | Live arithmetic |
|---|---|---|
| Taken after T1+T2 | 65 = 51 auto / 3 both / 10 manual / 1 agent | **Correct** (now 71 taken: 55 auto + 15 both + 1 agent; cut 4 auto + 2 both → 51/13/1; flip 10 both → 51/3/10/1; exclude 80+6=86) |
| Bundled among 10 flips | 7 | **Correct** (convert-to-cpm, dotnet-trace-collect, dump-collect, migrate-dotnet8/9/10 pair, migrate-nullable-references, dotnet-aot-compat; not generate-testability-wrappers, migrate-static-to-wrapper, thread-abort-migration) |
| New `body: patch` in Wave A | "six items" (spec `:48`, Task 13 `:591`) | **Seven**: Tasks 4–10 (coverage-analysis and test-analysis-extensions gain `body: patch` without `depends_on`) |
| New `depends_on` | "six patched items" / five named | **Five** as written (run-tests, mtp-hot-reload, test-anti-patterns, test-gap-analysis, dotnet-webapi); more if finding 4 is fixed |

Task 13's "exactly the expected delta" gate cannot pass against the written expectation.

### 10. Medium — T2 "no non-compiling example survives" is ungated

Task 11 correctly targets live defects:

- `TestcontainersNetworkBuilder` still shipped
  (`plugins/…/testcontainers-integration-tests/infrastructure-patterns.md:159`)
- `using Testcontainers;` still shipped (`database-patterns.md:14`)
- `MigrationTests` assigns `private readonly IContainer _container` in `InitializeAsync`
  (`database-patterns.md:154-159`) — non-compiling

But the new module-packages snippet (`wave-a-general-curation.md:521-526`) uses
`PostgreSqlBuilder` / `GetConnectionString()` with **no** `using Testcontainers.PostgreSql;`,
and verification is only a negative grep for legacy names (`:539`). That weakens T2's
"no non-compiling example survives" claim
(`docs/superpowers/specs/2026-08-04-t-items.md:20-21`).

**Required:** add the using (or fully qualify), or narrow T2's verification sentence to the three
named defects plus "legacy API names absent".

### 11. Low — Documentation follow-through leaves contradictions; TUnit bullet over-scopes

- Wave B Task 4 replaces only the final pointer clause of the research bullet
  (`wave-b-opencode-stub-commands.md:214`), leaving
  "long-body `manual` conversions are noisy" immediately before the new stub sentence
  (`skill-invocation-across-harnesses.md:239-243`). Rewrite the whole bullet.
- Stale `emitOpenCodeSkill` JSDoc still says parked references are "not rewritten"
  (`tools/build.ts:462-466`); not in the plan.
- Proposed TUnit ROADMAP bullet pre-decides Verify, Testcontainers, and occasional-xUnit scope
  (`wave-a-general-curation.md:560-565`) beyond T6's ruling to defer an own skill to a separate
  authoring session (`specs/2026-08-04-t-items.md:25-26`).

### 12. Low — Wave B report lists `BODY.md` inside the "bundle:" file list

After writing `BODY.md`, `parked = listFiles(destSkill)` includes it, so the report becomes
`body parked at skills/X/BODY.md (bundle: BODY.md, references/…)`. Harmless but slightly
misleading; filter `BODY.md` out of the bundle clause if polish matters.

## OpenAI Claim Verification

Independent review was completed **before** reading
`docs/superpowers/reviews/2026-08-05-s13-plan-review-openai.md`. Every numbered claim was then
re-checked on live files.

| # | OpenAI claim | Verdict | Evidence |
|---|---|---|---|
| 1 | Wave A cannot hit zero excluded refs; exp-\* rows, test-tagging×11+body, mjml→verify-email-snapshots; hand list omits verify-email-snapshots | **Confirmed** | `test-anti-patterns/SKILL.md:90,100`; `test-analysis-extensions/SKILL.md:57` + 11 extension headers; `mjml-email-templates/SKILL.md:21`; plan `:468-469` pattern omits `verify-email-snapshots` |
| 2 | Task 11 ceremony self-includes old `overlay.patch` on recut | **Confirmed** | `eject.ts:196-207` force re-lay keeps patch file; `:215-217` phase-2 `cpSync` of whole dest; `listFiles` excludes patch from emptiness check only (`overlay.ts:66-68`) |
| 3 | Capability absorptions are description-only; gap-analysis redirected to phantom assertion home; coverage lacks target filter / drops single-file scope | **Confirmed** | Task 6/7/8 wording vs bodies; `Compute-CrapScores.ps1:17-20` params; `crap-score/SKILL.md:4-7` single-file scope |
| 4 | Promotion-on-contact incomplete for mtp-hot-reload, run-tests, test-anti-patterns bare edges | **Confirmed** | Upstream `run-tests:37`, `test-anti-patterns:42`, `test-gap-analysis:40`; proposed `depends_on` lists omit them; T3 + plan `:16` require promotion on touched bodies |
| 5 | Header "free of coercion" false (ALWAYS use / INVOKE whenever) | **Confirmed** | `run-tests/SKILL.md:4`; `test-anti-patterns/SKILL.md:6`; header text plan `:92-97` |
| 6 | Playbook Scan 1 unreliable (FP noise, derivation bugs, hand list contradiction) | **Confirmed** | Walkthrough: 80 excluded names, product-word FPs; ledger keys composite; playbook `:21-24` vs plan `:468-469` |
| 7 | Wave B tests miss contract; regex one `../`; RED count understates; no mount smoke | **Confirmed** | plan tests `:32-109` vs teach `./SKILL.md`; regex `:129`; fourth fail from invocation-test BODY assert `:104-105` |
| 8 | Seven new body:patch not six; depends_on five; ledger gate wrong | **Confirmed** | Tasks 4–10 = 7; depends_on on five items; spec `:48` and Task 13 `:591` |
| 9 | T2 compiling claim ungated; PostgreSql snippet missing using | **Confirmed** | plan snippet `:521-526`; verify only negative grep `:539` |
| 10 | Docs follow-through contradictions; JSDoc stale; TUnit bullet over-scopes | **Confirmed** | research `:239-243` + plan `:214`; `build.ts:462-466`; TUnit bullet `:560-565` vs T6 |

**OpenAI "Verified Claims" row "All quoted old-string anchors exist":** **Disputed** as overstated.
Body-level anchors largely exist; multiple **description** old-strings do **not** exist as
contiguous substrings at the current pin (see finding 6). Other verified-sound rows (counts, seven
bundles, two parked-link spellings, 0/6 and 0/4 validate arithmetic, baseline green) **Confirmed**.

## Additional findings not in the OpenAI report

1. **Description-level exact-string anchors break on `>` folding** (finding 6) — OpenAI's verified
   claim said all anchors exist; this is the counter-evidence and an execution hazard.
2. **Wave B `BODY.md` write order vs validate L6** (finding 8 tail) — must create `BODY.md` before
   the stub references it; not spelled as a hard ordering constraint in the sketch.
3. **Report bundle list double-counts `BODY.md`** (finding 12).
4. **Task 11 old-string `private readonly IContainer _container`** is correct only on the
   *post-`git apply`* working copy (current patched output), not on pristine upstream (which still
   has `TestcontainersContainer`). Worth one clarifying note so implementers do not search upstream
   and bounce.
5. **Playbook / Scan 1 false-positive judgment is load-bearing** even after Wave A: framework API
   tables are correctly carved out, but short excluded basenames will keep generating noise unless
   the alternation is token-bounded — OpenAI noted FPs; this review adds that the playbook's own
   false-positive section is the only guard and is non-mechanical.

No disagreement with OpenAI on severity ordering of findings 1–5.

## Verified-sound claims and residual uncertainty

**Sound without change:**

- Final module arithmetic after Task 1+2: 65 taken = 51 `auto` / 3 `both` / 10 `manual` / 1 agent;
  86 excluded (recounted from `curation/deniz-dotnet-general.yaml`).
- Exactly seven of the ten ceremony flips carry bundles; none of those seven links its own
  `SKILL.md` today → Wave A validate stays `0/6` for the dead-link class.
- Only two live parked markdown links to withheld `SKILL.md`: `teach` `./SKILL.md`,
  `writing-great-skills` bare `SKILL.md` — Wave B regex covers both spellings present today.
- Bundle-less `manual` path (`!wantsSkill` + empty dest → husk removal at `build.ts:493-494`) stays
  inline if `bundledManual` is gated on `existsSync(destSkill)` after husk removal — design OK.
- `depends_on` both-directions linker already enforces declaration ↔ body facts
  (`tools/validate.ts` linker tests); the gap is under-declaration in the plan, not missing
  machinery.
- Warning arithmetic Wave A `0/6` → Wave B `0/4` is consistent **if** the two parked SKILL.md
  links are the only members of that class (verified today).
- Submodule pins match the authoring-time expectation; no pin move invalidates anchors via drift
  of file identity (wrapping issues are separate).

**Residual uncertainty:**

- Whether the curator wants finding-3 absorptions upgraded to real body work or toned-down
  descriptions — policy call, not a tools fact.
- Whether any third `SKILL.md` link spelling appears after Wave A parks seven more general bundles
  (spot-check said clean today; re-grep after flips still required).
- Full false-positive rate of a token-bounded Scan 1 after Wave A was not re-simulated on a
  post-edit tree (edits not applied in this read-only review).

## Commands / checks actually run

| Command / check | Result |
|---|---|
| `git status` / `git log -15` / `git submodule status` / `git rev-parse HEAD` | Dirty s13 docs only; HEAD `ea8b805`; pins listed above |
| `npm run validate` | `0 error(s), 6 warning(s)` |
| `npm test` | 134 pass, 0 fail |
| `npm run typecheck` | clean |
| `npm run build` | **not run** (handover: shared generated output with another reviewer) |
| Manifest recount (`invocation` / `exclude` / `as: agent`) | 55 auto, 15 both, 0 manual, 1 agent, 80 exclude |
| Bundle presence on ten flip targets | 7 bundled / 3 skill-only |
| Contiguous old-string probes on upstream pins | Body mostly HIT; description several MISS (finding 6) |
| Grep excluded names under `plugins/deniz-dotnet-general/` | Findings 1 baseline map |
| Grep `](...SKILL.md)` under `opencode/skills/` | teach + writing-great-skills only for parked manuals |
| Read `tools/build.ts:468-518`, `tools/eject.ts` patch phases, `tools/lib/overlay.ts` listFiles | Ceremony + emitter contract |
| Read `Compute-CrapScores.ps1` params; crap-score upstream description | Finding 3 |
| Read OpenAI report **after** independent findings locked | Verification table above |

No manifests, overlays, generated output, plans, specs, playbook, roadmap, or other reports were
modified. Sole write: this file.
