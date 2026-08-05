# S13 final re-review — Grok independent (OpenCode-Go route)

Date: 2026-08-05

Reviewer/model lineage: Grok 4.5 (independent second reviewer). Fresh pass against the live
repository. Did **not** treat the prior Grok/xAI final verdict as authority; prior reports were
read only after live verification, and only for historical-finding disposition coverage.

Planning scratch (AGENTS.md, Documentation Hygiene): delete when the reviewed work merges.
This file is also planning scratch under `docs/superpowers/reviews/`.

## Verdict

**PASS — execution-ready for the curator’s go / execution-mode decision.**

No blocking findings remain in the corrected planning set. Wave A and Wave B documents, the
correction design, the T-items spec, the reference-audit playbook, the s13 handover, and ROADMAP
relays are mutually consistent on the surfaces that matter for execution.

No wave was executed. No manifest, overlay, tool, generated tree, submodule, or existing document
was modified. Sole write: this file.

## Findings

**No findings.**

The corrected documents are coherent enough to execute as written. Items below are residual
execution risks, not plan defects.

## Independent checks (live tree)

### Baseline

| Check | Result |
|---|---|
| HEAD | `ea8b805` |
| Working tree | Dirty planning docs only (`docs/superpowers/`, playbook, s13 handover, ROADMAP relay, agents README); no curation/overlay/tools/generated/submodule drift from this correction pass |
| Submodule pins | Unchanged (`dotnet-agent-skills` `85cd1034`, `dotnet-skills` `c2ac7e9`, …) |
| `npm run validate` | `0 error(s), 6 warning(s)` — standing set: 2× `dotnet-devcert-trust` aspire-ns, `elements-of-style`, `subagent-driven-development` relative ref, `teach` `./SKILL.md`, `writing-great-skills` bare `SKILL.md` |

### Invocation arithmetic (final 65 / 86)

Live manifest before Wave A:

- 71 taken = 55 `auto` + 15 `both` + 1 agent (`roslyn-incremental-generator-specialist`, no
  `invocation:` field) + 0 `manual`
- 80 `exclude: true`

Planned cuts (Task 1): six items, all currently taken — four `auto`
(`grade-tests`, `exp-mock-usage-analysis`, `exp-test-maintainability`, `find-untested-sources`)
and two `both` (`migrate-vstest-to-mtp`, `migrate-xunit-to-xunit-v3`).

After cuts: 51 `auto` / 13 `both` / 1 agent. After ten ceremony flips (Task 2), three stay `both`
(`slopwatch`, `analyzing-dotnet-performance`, `dotnet-devcert-trust`).

**Final: 65 taken = 51 auto / 3 both / 10 manual / 1 agent; 86 excluded.** Matches spec end-state
(`2026-08-04-t-items.md:49`), correction design (`:118-119`), Wave A Task 14 (`:807-809`), and
handover checklist.

### Wave A body / description anchors (pinned upstream)

All body old-strings checked as contiguous physical text at the pin unless the task uses a whole
folded-block replace:

| Task | Target | Result |
|---|---|---|
| 2 | Header invocation + reference-posture paragraphs | HIT at `curation/deniz-dotnet-general.yaml:10-23` |
| 3 | `filter-syntax` / `platform-detection` descriptions name `migrate-vstest-to-mtp` | HIT; override replaces whole description |
| 4 | `run-tests` five body rows + whole `description: >` block | HIT (`external/.../run-tests/SKILL.md`) |
| 5 | `mtp-hot-reload` three body rows | HIT |
| 6 | anti-patterns description block; exp-\* sentences; When-Not rows; No-assertions / Duplicate-tests insert points; depth bar `**Depth bar`…item 4 | HIT (`:26`, `:38-45`, `:75`, `:100`, `:133-138`, `:90`, `:100`) |
| 7 | gap-analysis one-line description + six body rows | HIT |
| 8 | coverage whole description block; When-Not crap-score bullet; `## Workflow`; Inputs table | HIT (delete instruction is content-level for the unique When-Not bullet; description fully replaced) |
| 9 | webapi description block + two `optimizing-ef-core-queries` body sites | HIT (`:35`, `:420`) |
| 10 | extensions description `>-` block; Tag support line; dotnet.md consumer list; 11× `## Tag/Trait Attributes (for \`test-tagging\`)` | HIT (11/11 extension files) |
| 11 | MJML single-line description; related-skills mailpit + verify-email lines | HIT (`external/dotnet-skills/skills/mjml-email-templates/SKILL.md:3`, `:17-18`) |
| 12 | post-apply working-copy anchors | `TestcontainersNetworkBuilder`, `using Testcontainers;`, MigrationTests `private readonly IContainer _container` with `InitializeAsync` assignment all present in current emitted/overlay state; `## Common Issues and Solutions` insert point HIT |

### MJML / mailpit / snapshot (reference closure)

- `snapshot-testing` is taken (`docs/ledger.json` → `deniz-dotnet-general/skill/snapshot-testing`).
- `mailpit-integration` is never-curated (inventory curated column U+2014 `—`; no ledger key).
- `verify-email-snapshots` is already excluded in the general manifest and inventory.
- Task 11 (`wave-a-general-curation.md:619-647`): Mailpit → product prose only; verify-email →
  `dotnet-skills:snapshot-testing`; `depends_on: [snapshot-testing]` sole edge; Step 5 requires
  prose classification and absence of both path-like artifact tokens.
- Design table (`correction-design.md:82-89`) and Task 14 ledger gate (`wave-a:805`) list **six**
  edge-bearing sources with MJML → snapshot only. `coverage-analysis` and
  `test-analysis-extensions` remain patch-without-edges.

### Excluded-reference coverage (pre-wave map)

Grep of kept `plugins/deniz-dotnet-general` skills for the cut/already-excluded names shows true
artifact hits only in:

- skills Task 1 will remove (self-bodies and cross-talk among cuts), and
- skills Tasks 3–11 patch (`run-tests`, `mtp-hot-reload`, `test-anti-patterns`, `test-gap-analysis`,
  `coverage-analysis`, `dotnet-webapi`, `test-analysis-extensions` + 11 extension tables,
  `filter-syntax`/`platform-detection` descriptions, `mjml-email-templates`).

No kept skill outside that set retained a true excluded-artifact handoff in this sweep. Close-out
still must re-run the playbook mechanically (Task 14 Step 5).

### Patch / description / edge arithmetic

| Quantity | Documented | Live-reconciled |
|---|---|---|
| New `body: patch` | 8 | Tasks 4–10 + Task 11 mjml |
| Extended existing patch | 1 | Task 12 testcontainers |
| Edge-bearing sources | 6 | run-tests, mtp-hot-reload, test-anti-patterns, test-gap-analysis, dotnet-webapi, mjml-email-templates |
| Description changes | 9 | filter-syntax, platform-detection, run-tests, test-anti-patterns, test-gap-analysis, coverage-analysis, dotnet-webapi, test-analysis-extensions, mjml-email-templates |
| Removals / flips | 6 / 10 | As above |
| Bundled flips gaining build park WARNs | 7 | convert-to-cpm, dotnet-trace-collect, dump-collect, migrate-dotnet8/9-to-*, migrate-nullable-references, dotnet-aot-compat — none link own `SKILL.md` |

### Testcontainers ceremony (Task 12)

- `listFiles` excludes `overlay.patch` (`tools/lib/overlay.ts:66`), so `--patch --force` on a
  patch-only overlay directory re-enters Phase 1 and lays upstream via `cpSync` merge.
- Plan saves the old patch, force-ejects, `git apply --check` + apply, **removes**
  `overlay.patch` before edits/recut, and verifies regenerated headers do not target
  `overlay.patch` — closes the self-include hazard (`tools/eject.ts:215-217` whole-dir snapshot).
- Module sample includes `using Testcontainers.PostgreSql;`; disposable `dotnet build` is a real
  API compile gate, not a textual scan.
- Scope is the named legacy API + MigrationTests readonly defect + module note — no
  “every example compiles” overclaim.

### Absorption honesty

- `test-anti-patterns`: qualitative trivial-only + single-facet rows and depth-bar item 2; DO NOT
  USE still refuses full assertion-diversity metrics and academic catalog.
- `coverage-analysis`: targeted branch uses `Extract-MethodCoverage.ps1 -Filter all` (param exists,
  default `all`), existing CRAP formula `C²(1−cov)³+C` (`SKILL.md` spot-check language), explicit
  no-row outcome; no new scripts / `merged_from`.
- Header rewrite states honest descriptions + manual ceremonies; no “free of coercion” /
  “coercion-free” claim.
- ROADMAP TUnit / `expects` deferrals are trigger-only (Task 13); no pre-decided Verify/Testcontainers/xUnit scope or field schema.

### Wave B contract

| Surface | Plan locus | Assessment |
|---|---|---|
| Bundled manual stub | `:43-55`, `:113-120` | Exact five-line stub; project + global paths; `$ARGUMENTS`; no `@` |
| Bundle-less manual | `:59` | Pre-wave bytes via `serializeDoc`; no parked dir; no parking report |
| `both` | `:61` | Skill + command byte-identical; no `BODY.md` |
| Link spellings | `:63-71`, regex `:100` | bare, `./`, `../`, `../../`; fixtures placed so each link resolves to skill root |
| RED failures | `:78-80` | Four named: new contract, new link-repoint, invocation `BODY.md` assert, parking-report assert; preservation tests expected green |
| BODY write-before-rewrite | `:91-108` | `BODY.md` written then all `*.md` rewritten |
| Report semantics | `:129-131` | `body parked at …/BODY.md`; bundle list filters `BODY.md`; no `WARN` / `not rewritten` |
| JSDoc | Task 2 Step 1 | Must drop stale “references are not rewritten” / direct-bundle wording |
| Real-tree ledger | Task 3 | Every bundled manual gains `BODY.md` in `opencode.parked` only |
| Runtime smoke before docs | Tasks 4–5 | Both mounts; observed read of `BODY.md` + markers; `OPENCODE_CONFIG_DIR` not a distribution promise |
| Warning arithmetic | Task 3 | `0/4` after teach + writing-great-skills self-link retirement (live links confirmed) |

Regex spot-check against sample links: bare / `./` / `../` / `../../` / fragment HIT; non-link
`SKILL.md` and `foo/SKILL.md` correctly MISS.

### Playbook

Live derivation for `deniz-dotnet-general` (playbook script):

- `taken=71`, `excluded=80`, `neverCurated=34`, paths=87, all paths exist
- Disjoint: taken∩excluded, taken∩never, excluded∩never all empty
- Output-name segment of ledger keys; own-skill frontmatter union present in script
- `mailpit-integration` never-curated; `snapshot-testing` taken; `verify-email-snapshots` excluded
- Agent names resolve via inventory map (not broken `.agent.md` basename)
- Candidate pattern escapes names and uses `candidateHits`-equivalent boundaries
  (`(^|[^a-z0-9-:])…(?=$|[^a-z0-9-:])` ↔ `tools/lib/refs.ts:77`)
- Grep is candidates only; classification rules cover product words, CLI, framework API rows,
  longer taken names; ambiguous → report, not silent promote
- Wave A Task 14 consumes mechanical derivation — no hand-maintained alternation

### Warning expectations

- Through Wave A validate: `0/6` (teach + writing-great-skills remain until Wave B emitter).
- After Wave B: `0/4` (those two retire; standing four unchanged).
- Task 2 correctly notes seven additional **build** park WARNs for flipped bundled manuals — not
  validate warnings.

## Disposition of historical findings

### OpenAI (`2026-08-05-s13-plan-review-openai.md`) 1–12

| # | Status | Evidence in corrected set |
|---|---|---|
| 1 Excluded refs / hand list | **Fixed** | Tasks 6, 10, 11; close-out playbook derivation Task 14 `:815-817` |
| 2 overlay.patch self-include | **Fixed** | Task 12 save/apply/Remove-Item/recut + header check |
| 3 Description-only absorption | **Fixed** | Minimal honest body work Tasks 6–8; design absorption section |
| 4 Incomplete promotion-on-contact | **Fixed** | Six `depends_on` tables include live edges (mtp-hot-reload, run-tests, test-anti-patterns, …) |
| 5 Header free of coercion | **Fixed** | Header `:95-101` — honestly name the work; no coercion-free claim |
| 6 Playbook unreliable | **Fixed** | Mechanical sets, escape, boundaries, classification |
| 7 Wave B under-tested / RED | **Fixed** | Four RED; branches; multi-`../`; mounts; smoke |
| 8 Patch arithmetic | **Fixed** | Eight new patches, six edges, nine descriptions |
| 9 T2 compile claim | **Fixed** | Namespace + `dotnet build` gate; T2 narrowed |
| 10 Docs / JSDoc / TUnit overscope | **Fixed** | Full-paragraph replacements; JSDoc rewrite; session-only TUnit bullet |
| 11 Description wrap anchors | **Fixed** | Whole folded-block replaces |
| 12 Wave B ledger gate | **Fixed** | Task 3 Step 3 real-tree ledger diff |

### xAI initial (`2026-08-05-s13-plan-review-xai.md`)

Same closures as OpenAI 1–12 above (excluded-ref, self-include, absorption, promotion, header,
anchors, playbook, Wave B, arithmetic, compile, docs/BODY list). Independent spot-checks did not
reopen any.

### GLM (`2026-08-05-s13-plan-review-glm-go.md`) F1–F10+

| Finding | Status |
|---|---|
| F1 exp-\* table sentences | **Fixed** — Task 6 `:307-308` |
| F2 promotion live edges | **Fixed** — depends_on lists complete |
| F3 overlay self-include | **Fixed** — Task 12 |
| F4 scan list / test-tagging / mjml | **Fixed** — Tasks 10–11 + playbook close-out |
| F5 patch/edge miscount | **Fixed** — 8 / 6 / 9 |
| F6 coercion-free header | **Fixed** |
| F7–F10 absorption / Wave B / docs | **Fixed** as in OpenAI/xAI rows |

### Post-correction xAI (`2026-08-05-s13-corrected-plan-rereview-xai.md`)

| Prior | Status |
|---|---|
| High — MJML edge to never-curated mailpit | **Fixed** — prose + snapshot-only depends_on |
| Low — depth-bar anchor | **Fixed** — whole-block replace from `**Depth bar` |
| Low — `body: full` wording | **Fixed** — “retain existing invocation and add patch mode and edge” |

Mailpit regression is closed in the live Task 11 / design / Task 14 texts verified this pass.

## Residual execution risks (not findings)

1. **Task 8 targeted CRAP pipeline** — `Extract-MethodCoverage.ps1` mixes `Write-Output` JSON with
   `Write-Host` summaries; PS 7+ should keep host out of the pipeline, but dry-run once during
   implementation.
2. **Task 12 post-apply anchors** — `private readonly IContainer _container` is working-copy text
   after re-applying the saved patch, not pristine upstream (`TestcontainersContainer`). Follow the
   save/apply/remove-`overlay.patch`/recut order exactly.
3. **Wave B Task 4 smoke** — model and isolation non-determinism; both mount legs must pass before
   Known Gap / research claims change; budget a pinned model and lab time.
4. **Task 8 When-Not delete** — instruction names the bullet by content rather than quoting the full
   markdown line (`- **Targeted…** — use the \`crap-score\` skill…`). Unique in file; still prefer
   reading the laid working copy rather than pasting the shortened plan phrase as a blind
   exact-string search.
5. **Scan 1 after Wave A** — pre-wave map looks complete; Task 14 must still derive sets and classify
   candidates (e.g. excluded agent name `msbuild` as product word) rather than trusting this review.
6. **Handover P3** still mentions upstream `mailpit-integration` samples as aspire-session context —
   not Wave A work; do not reintroduce a general-module edge.

## Commands / checks performed

- `git rev-parse HEAD`, `git log --oneline -8`, `git submodule status`, `git status --short`
- `npm run validate` → 0/6
- Manifest parse: invocation/exclude/agent counts; cut and both-item classification
- Ledger: snapshot-testing present; no mailpit keys; agent key present
- Inventory curated-column codepoints (U+2014 never-curated); mailpit / verify-email rows
- Playbook derivation script end-to-end (taken/excluded/neverCurated/paths/disjoint)
- Upstream/emitted anchor HIT/MISS matrix for Tasks 2–12
- Excluded-name grep over kept general skills outside the planned patch/cut set → 0 unexpected lines
- Bundled-flip inventory (7) and absence of own-`SKILL.md` links in those bundles
- teach / writing-great-skills live self-link spellings (`./SKILL.md`, bare `SKILL.md`)
- Wave B regex sample matrix; `Extract-MethodCoverage -Filter` param; eject `listFiles` vs
  `overlay.patch`; `emitOpenCodeSkill` current park WARN shape
- Read: AGENTS.md, T-items spec, correction design, both plans, playbook, s13 handover, ROADMAP,
  all five prior review reports (disposition only after live checks)

## Bottom line

**PASS.** Curator may choose go/no-go and subagent-driven versus inline execution. Execute Wave A
then Wave B exactly per the corrected plans; do not reopen selection or absorption policy during
implementation. Delete this planning scratch (specs, plans, and `docs/superpowers/reviews/`) when
the waves merge.
