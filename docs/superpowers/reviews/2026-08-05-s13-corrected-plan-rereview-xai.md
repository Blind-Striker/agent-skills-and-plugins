# S13 corrected-plan re-review — xAI / Grok

Date: 2026-08-05

Reviewer/model lineage: Grok 4.5 (xAI). Independent re-review of the post-correction
spec, Wave A/B plans, and reference-audit playbook against the live repository.
Prior report: `docs/superpowers/reviews/2026-08-05-s13-plan-review-xai.md`.

Planning scratch (AGENTS.md, Documentation Hygiene): delete when the reviewed work merges.
This report is also planning scratch under `docs/superpowers/reviews/`.

## Verdict

**FAIL — blocking corrections remain.**

Almost every defect from the first xAI/OpenAI/GLM reviews is closed in the rewritten
documents. One High defect introduced by the MJML “fix” remains: Task 11 declares a
model edge and `depends_on` target for `mailpit-integration`, which is **not taken in any
manifest** (inventory curated column `—`, never-curated). Executing Task 11 as written
fails `npm run validate` (stale `depends_on` and/or unrewritten upstream namespace) and
poisons Scan 2 with a never-curated handoff. Fix that task, then the set is execution-ready
for the curator’s go decision.

No wave was executed. No manifests, overlays, tools, generated trees, or other docs were
modified. Sole write: this file.

## Live baseline

| Check | Result |
|---|---|
| HEAD | `ea8b805` (dirty: s13 planning docs only) |
| Submodules | Unchanged pins (`dotnet-agent-skills` `85cd1034`, `dotnet-skills` `c2ac7e9`, …) |
| `npm run validate` | `0 error(s), 6 warning(s)` |
| `npm test` | 134/134 pass |
| `npm run build` | not run (read-only review) |

Manifest arithmetic **before** Wave A (live): 71 taken (55 `auto` + 15 `both` + 1 agent),
80 `exclude: true`. After planned T1+T2: **65 taken = 51 auto / 3 both / 10 manual / 1 agent;
86 excluded** — still correct.

## Findings (severity-ranked)

### 1. High — Task 11 MJML edge to never-curated `mailpit-integration`

**Evidence**

- Inventory: `docs/inventory.md` row `mailpit-integration` curated as `—` (U+2014), source
  `dotnet-skills/skills/aspire-mailpit-integration`.
- Playbook-style derivation (live, from repo root): `taken=71`, `excluded=80`,
  `neverCurated=34`, `mailpit.taken=false`, `mailpit.never=true`. `snapshot-testing` **is**
  taken (`deniz-dotnet-general/skill/snapshot-testing` in ledger).
- Aspire manifest (`curation/deniz-dotnet-aspire.yaml`) ships only the `aspire` router — no
  mailpit item.
- Corrected plan Task 11
  (`docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:620-636`) replaces
  related-skills with namespaced model edges and declares:

  ```yaml
  depends_on: [mailpit-integration, snapshot-testing]
  ```

  Description text also says `use mailpit-integration` (`:615-617`).
- Correction design (`docs/superpowers/specs/2026-08-05-s13-plan-correction-design.md:73-75`,
  `:89`) asserts mailpit is a **kept** handoff — that claim is false on the live tree.
- Linker contract (`tools/validate.ts:569-584`): `depends_on` must match **own-namespace**
  model-edges derived from the shipped body. `dotnet-skills:mailpit-integration` is not on
  the rewrite map (source never curated) → no own-ns derived edge → **`stale depends_on:
  mailpit-integration` error**. Unrewritten upstream namespace also warns. Bare
  `mailpit-integration` in the new description is a Scan 2 never-curated artifact handoff.

**Required fix (choose one, with curator if selection changes)**

1. **Preferred (no selection change):** rewire only `verify-email-snapshots` →
   `dotnet-skills:snapshot-testing` + `depends_on: [snapshot-testing]`. For the Mailpit line,
   use non-artifact prose (e.g. “local SMTP capture with Mailpit”) **or** drop the line —
   do **not** namespacize or `depends_on` a never-curated name.
2. **If the curator wants the edge:** take `dotnet-skills/skills/aspire-mailpit-integration`
   into a manifest in the same wave (selection decision), then keep the dual edge.

Also fix the Task 11 wording `change the item from body: full` (`:629`) — there is no
`body: full` grammar; current item is invocation-only (`curation/deniz-dotnet-general.yaml:62-63`).

Until this is fixed, Wave A close-out Scan 1/2 and ledger edge gates cannot pass as specified
(`wave-a-general-curation.md:796`, `:806-808`; spec end state
`docs/superpowers/specs/2026-08-04-t-items.md:53-56`).

### 2. Low — Task 6 “Step 5 depth bar” edit lacks exact anchor text

`wave-a-general-curation.md:322` instructs: add assertion depth to the Step 5 depth bar
without quoting the four existing bullets
(`external/…/test-anti-patterns/SKILL.md:133-138`). Implementable by judgment, but weaker
than the plan’s own “exact physical strings” rule (`:20`). Prefer an old/new bullet list.

### 3. Low — Residual execution risks (non-blocking if Task 11 fixed)

- **Coverage targeted branch** pipes `Extract-MethodCoverage.ps1` to `ConvertFrom-Json`. The
  script `Write-Output`s JSON and `Write-Host`s summaries
  (`plugins/…/coverage-analysis/scripts/Extract-MethodCoverage.ps1:182-192`) — host stream
  should not pollute the pipeline; still worth a one-shot dry run during Task 8.
- **Wave B Task 4 runtime smoke** is non-deterministic (model + isolation). Plan correctly
  refuses to narrow Known Gaps until both mount legs pass; budget time and a pinned model.
- **Task 12 `private readonly IContainer _container`** matches the **post-apply** working copy
  (current patched output `database-patterns.md:154`), not pristine upstream
  (`TestcontainersContainer`). Plan context is adequate; do not search only upstream.

No other High/Medium defects found in Wave B, playbook mechanics, counts, or remaining Wave A
tasks.

## Disposition of prior findings

### Original xAI report (`2026-08-05-s13-plan-review-xai.md`)

| # | Prior finding | Status | Evidence in corrected docs |
|---|---|---|---|
| 1 | Excluded refs survive (exp-\*, test-tagging×11, mjml verify-email); hand list | **Partial** | exp-\* deleted Task 6 `:307-308`; test-tagging body+11 headings Task 10 `:570-578`; verify-email Task 11. **Mailpit mishandled** (new High). Close-out uses playbook derivation Task 14 `:806-808` — fixed hand-list issue |
| 2 | Task 11 (old) overlay.patch self-include | **Fixed** | Now Task 12: save patch → force eject → apply → **Remove-Item overlay.patch** before recut (`:655-682`); verify headers (`:731`) |
| 3 | Description-only absorption / phantom metrics | **Fixed** | Minimal honest absorption: Task 6 description + trivial-only/single-facet rows (`:284-320`); Task 7 routes qualitative only, not metrics (`:370-371`); Task 8 targeted branch via `Extract-MethodCoverage -Filter all` (`:424-472`). Spec T3/non-goals updated |
| 4 | Incomplete promotion-on-contact | **Fixed** | `depends_on` tables: run-tests +mtp-hot-reload (`:219`); anti-patterns +run-tests (`:335`); gap +test-anti-patterns (`:382`); global rule `:19` |
| 5 | Header “free of coercion” | **Fixed** | Header `:95-101` — “honestly name the work”; no coercion-free claim |
| 6 | Description old-string wrap MISS | **Fixed** | Folded blocks replaced whole (`:20`, Tasks 4/6/8/9/10); body tables remain contiguous HIT at pin |
| 7 | Playbook Scan 1 unreliable | **Fixed** | Mechanical derivation, disjoint sets, escaped boundaries, candidate classification (`reference-audit-playbook.md:24-186`) |
| 8 | Wave B under-tested / RED count | **Fixed** | Four named red tests (`wave-b:78-80`); branches bundled/bundle-less/both; regex multi-`../`; exact stub; BODY.md before rewrite; report filters BODY.md |
| 9 | Patch/edge arithmetic wrong | **Fixed** | Eight new patches, six edge sources, nine descriptions (spec `:53-56`, Task 14 `:796-804`) |
| 10 | T2 ungated compile claim | **Fixed** | `using Testcontainers.PostgreSql` (`:712`); disposable `dotnet build` smoke (`:733-745`); T2 wording narrowed in spec |
| 11 | Docs follow-through / TUnit overscope | **Fixed** | Full-paragraph research/ROADMAP replacements Wave B Task 5; TUnit bullet session-only (`wave-a:768-771`); JSDoc rewrite Wave B Task 2 |
| 12 | Report lists BODY.md in bundle clause | **Fixed** | `filter((f) => f !== "BODY.md")` (`wave-b:129-131`) |

### OpenAI numbered findings 1–10

| # | Status | Notes |
|---|---|---|
| 1 | **Partial** | Coverage of listed excluded refs fixed; mailpit/never-curated regression remains |
| 2 | **Fixed** | Safe recut ceremony |
| 3 | **Fixed** | Honest absorption |
| 4 | **Fixed** | Promotion lists complete for planned edges (except broken mailpit target) |
| 5 | **Fixed** | Header |
| 6 | **Fixed** | Playbook |
| 7 | **Fixed** | Wave B contract |
| 8 | **Fixed** | Arithmetic |
| 9 | **Fixed** | Compile smoke + usings |
| 10 | **Fixed** | Docs/JSDoc/TUnit |

### GLM High F1–F4

Same dispositions as xAI 1–4 / OpenAI 1–2, 4: **F1/F4 partial** only insofar as Task 11’s
mailpit target is wrong; exp-\*, tagging, and verify-email rewires are present.

## Independent verification notes

### (2) Playbook derivation + candidate sweep

Ran the playbook inventory regex + ledger/manifest derivation against the live tree:

- `inventoryParsed=223`, `inventoryFailed=0`
- `taken=71`, `excluded=80`, `neverCurated=34` (disjoint after taken wins)
- Excluded sources all resolve via inventory map (`missingInv=[]`)
- Own-skill union: no `skills/deniz-dotnet-general/` today — path is dormant but coded
- Pattern builder matches `candidateHits` boundaries (`tools/lib/refs.ts:74-77` ↔ playbook
  `:114-122`); hyphenated names need the custom class, not `rg -w` alone — documented

**True excluded-artifact hits on kept skills today** (pre-wave; each has a Task after correction
except the mailpit mistake):

| Kept skill | Hits | Covering task |
|---|---|---|
| test-anti-patterns | exp-mock, exp-test-maintain, assertion-quality, test-smell, writing-mstest, code-testing-agent | Task 6 |
| test-analysis-extensions | assertion-quality, test-smell, test-tagging (body+11 files), test-quality-auditor | Task 10 |
| mjml-email-templates | verify-email-snapshots; path-like aspire/mailpit | Task 11 (verify-email OK; mailpit **wrong**) |
| coverage-analysis | crap-score | Task 8 |
| run-tests / mtp-hot-reload / filter / platform | migrate-vstest, test-writing agents | Tasks 3–5 |
| test-gap-analysis | assertion-quality, code-testing-*, writing-mstest | Task 7 |
| dotnet-webapi | optimizing-ef-core-queries | Task 9 |

Wave A close-out correctly forbids a hand-maintained alternation (`:806-808`).

### (3) Anchors vs pinned upstream

All planned **body** old-strings probed as contiguous physical text: **HIT** (run-tests,
mtp-hot-reload, anti-patterns including exp-\* sentences and DynamicData pair, gap-analysis,
webapi, extensions/dotnet consumer line + tagging heading, mjml related-skills lines,
testcontainers NetworkBuilder / `using Testcontainers;`).

**Description** edits use full folded-block or full one-line replacement (not normalized
sentences): run-tests, anti-patterns, coverage, webapi, extensions (`description: >-`),
gap one-line, mjml single-line → folded. Header paragraphs match live
`curation/deniz-dotnet-general.yaml:10-23`.

### (4) Final Wave A arithmetic (derived)

| Quantity | Value |
|---|---|
| Taken after T1+T2 | 65 = 51 auto / 3 both / 10 manual / 1 agent |
| Excluded after T1 | 86 |
| New `body: patch` | **8**: run-tests, mtp-hot-reload, test-anti-patterns, test-gap-analysis, coverage-analysis, dotnet-webapi, test-analysis-extensions, mjml-email-templates |
| Extended existing patch | testcontainers-integration-tests |
| Description changes | **9**: filter-syntax, platform-detection, run-tests, test-anti-patterns, test-gap-analysis, coverage-analysis, dotnet-webapi, test-analysis-extensions, mjml-email-templates |
| Edge-bearing sources (as written) | 6 — **but mailpit target invalid** |
| Intended valid edges after mailpit fix | 5 sources if mailpit dropped; 6 if mailpit is actually taken |

Planned edge table (design/plan) — **snapshot half OK; mailpit half not**:

| Source | Targets |
|---|---|
| run-tests | filter-syntax, mtp-hot-reload, platform-detection |
| mtp-hot-reload | filter-syntax, platform-detection |
| test-anti-patterns | run-tests, test-analysis-extensions |
| test-gap-analysis | test-analysis-extensions, test-anti-patterns |
| dotnet-webapi | database-performance, efcore-patterns |
| mjml-email-templates | ~~mailpit-integration~~, snapshot-testing |

Bundled among ten ceremony flips: still **7** (unchanged; verified earlier session).

Warnings: Wave A `0/6`, Wave B `0/4` — still consistent with live parked-link pair
(teach `./SKILL.md`, writing-great-skills bare `SKILL.md`).

### (5) Testcontainers re-cut

Ceremony correctly: temp-copy old patch → `--force` eject → `git apply --check` + apply →
**delete** `overlay.patch` → edit → recut → assert no `overlay.patch` in new headers → API
compile smoke with `NetworkBuilder`, `IContainer`, `PostgreSqlBuilder` + required usings.
Matches `tools/eject.ts` phase-2 whole-dir snapshot hazard. **Fixed.**

### (6) Wave B

| Contract element | Status |
|---|---|
| RED: 4 failing tests named | **OK** (`wave-b:78-80`) |
| Bundled / bundle-less / both | **OK** (Tasks 1–2) |
| Exact stub text (5 lines, both mounts, `$ARGUMENTS`, no `@`) | **OK** (`:43-51`, `:113-120`) |
| Regex bare / `./` / one+ `../` + link boundary | **OK** (`:99-101`); live forms covered |
| BODY.md written before rewrite loop | **OK** (`:96-104`) |
| Report: parked body named; BODY.md excluded from bundle list | **OK** (`:129-131`) |
| Real-tree ledger gate | **OK** (Task 3 `:155`) |
| JSDoc follow-through | **OK** (Task 2 `:89`) |
| Two-mount recorded runtime smoke | **OK** (Task 4); gates Known Gap retirement |

### (7) Playbook

| Element | Status |
|---|---|
| Output-name precedence (item `name:` / frontmatter / basename) | **OK** (`:26-28`, `:86`) |
| Ledger key → name segment | **OK** (`:58-61`) |
| Own-skill frontmatter union | **OK** (`:72-80`) |
| Disjoint taken/excluded/never | **OK** (`:90-97`) |
| Escape + token boundaries | **OK** (`:114-122`) |
| Module paths from ledger + parked | **OK** (`:56-69`) |
| FP classification | **OK** (`:173-186`) |
| Live parse of inventory | **OK** (223/223 rows) |

### (8) New bugs beyond prior reviews

Only **Finding 1** (mailpit) is new/residual blocking. Lows 2–3 are polish/residual risk.

## Commands / checks run

- `git status` / `log` / `submodule status` / `rev-parse`
- `npm run validate` → 0/6; `npm test` → 134 pass
- Manifest invocation/exclude counts; ledger keys for snapshot/mailpit/agent
- Contiguous old-string probes on pinned `external/` sources (body HIT matrix)
- Playbook inventory+ledger derivation script (223 parsed, mailpit never-curated)
- Grep kept-skill excluded-name map vs Task coverage
- Read `emitOpenCodeSkill`, eject patch phases, validate depends_on linker,
  `Extract-MethodCoverage.ps1`, Wave B regex against sample spellings
- Read correction design, both plans, updated spec, playbook, ROADMAP, handover, prior three
  reviews

## Residual uncertainty

- Whether the curator prefers taking mailpit into general/aspire versus prose-only related-skills
  (selection; not re-litigated here).
- Post-edit Scan 1 cleanliness cannot be proven without applying patches; coverage of known true
  hits is complete **except** the mailpit mistake Task 11 would introduce.

## Bottom line

Correct the MJML task’s mailpit handling (and the `body: full` wording), align design/spec/plan
edge tables and ledger gate text, then re-spot-check Task 11 only. **Do not start Wave A until
that lands.** After that fix: **PASS-eligible** for curator go / execution-mode choice.
