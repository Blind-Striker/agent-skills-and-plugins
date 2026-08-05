# S13 final-amended re-review — Grok A (independent)

Date: 2026-08-05

Reviewer/model lineage: Grok 4.5 (independent session A). Fresh pass against the **final
amended** planning set and the live repository. Prior s13 reviews were treated as historical
disposition sources only, not as authority for this verdict — especially the earlier “final”
Grok reports that still describe pre-amendment Task 11 as an MJML take/patch.

Planning scratch (AGENTS.md, Documentation Hygiene): delete when the reviewed work merges.
This file is also planning scratch under `docs/superpowers/reviews/`.

## Verdict

**PASS — execution-ready for the curator’s go / execution-mode decision.**

No blocking findings in the final amended planning set. Spec, correction design, Wave A/B
plans, reference-audit playbook, s13 handover, and ROADMAP relays reconcile on the amended
email/snapshot and version-migration boundary and on the failure surfaces the handover requires
reviewers to check.

No wave was executed. No manifest, overlay, tool, generated tree, submodule, or existing
document was modified. Sole write: this file.

## Findings

**No findings.**

Items below are residual execution risks, not plan defects.

## Live baseline

| Check | Result |
|---|---|
| HEAD | `ea8b805` |
| Working tree | Dirty planning/docs only (`docs/superpowers/`, playbook, s13 handover, ROADMAP relay, agents README); no curation/overlay/tools/generated/submodule drift from the correction pass |
| Submodule pins | Unchanged (`dotnet-agent-skills` `85cd1034`, `dotnet-skills` `c2ac7e9`, …) |
| `npm run validate` | `0 error(s), 6 warning(s)` — standing identities: 2× `dotnet-devcert-trust` aspire-ns, `elements-of-style`, `subagent-driven-development` relative ref, `teach` `./SKILL.md`, `writing-great-skills` bare `SKILL.md` |

Do **not** pin post-wave taken/excluded totals in prose. Close-out must run the Wave A Task 14
census and `npm run inventory` against the final manifest.

## Amended boundary checks (primary for this pass)

### T1 cut list includes MJML and both version migrations

Canonical cut identities (spec T1; Wave A Task 1; handover; Task 14 removed-entries gate):

- `grade-tests`
- `exp-mock-usage-analysis`
- `exp-test-maintainability`
- `find-untested-sources`
- `migrate-vstest-to-mtp`
- `migrate-xunit-to-xunit-v3`
- `mjml-email-templates`
- `migrate-dotnet8-to-dotnet9`
- `migrate-dotnet9-to-dotnet10`

Live manifest still takes all nine (pre-wave). Each has a concrete `exclude: true` + reason block
in Task 1. `verify-email-snapshots` remains already excluded; `mailpit-integration` remains
never-curated (inventory curated column `—`).

### Version-migration bundles are internal; pair only hand off to each other

At the pinned `dotnet-agent-skills` tree:

- `migrate-dotnet8-to-dotnet9`: `SKILL.md` + ten `references/*-dotnet8to9.md` files only.
- `migrate-dotnet9-to-dotnet10`: `SKILL.md` + ten `references/*-dotnet9to10.md` files only.

Artifact handoffs between the pair only:

- 8→9 body: “use the `migrate-dotnet9-to-dotnet10` skill”
- 9→10 body/description: “use the `migrate-dotnet8-to-dotnet9` skill first”

No separately curated skill dependencies. Independently curated general skills that topic-overlap
migration reference prose remain taken (live spot-check: `efcore-patterns`, `serialization`,
`database-performance`, `project-structure`, `package-management`, `csharp-coding-standards`,
`dotnet-pinvoke`). Plan text forbids cutting those merely for topic overlap — correct.

### Ceremony flips exclude the removed version migrations

Wave A Task 2 flip set (eight identities only):

`convert-to-cpm`, `generate-testability-wrappers`, `migrate-static-to-wrapper`,
`dotnet-trace-collect`, `dump-collect`, `migrate-nullable-references`,
`thread-abort-migration`, `dotnet-aot-compat`

`migrate-dotnet8-to-dotnet9` / `migrate-dotnet9-to-dotnet10` are **not** in the flip table; they
are cut in Task 1. Keep-`both`: `slopwatch`, `analyzing-dotnet-performance`,
`dotnet-devcert-trust`. Live tree: all eight flip targets are currently `both`; keep-both three
are `both`. None of the eight bundles link their own `SKILL.md` (no new dead-self-link validate
identity expected from the flips).

Wave B real-tree example is a **retained** bundled manual: `dotnet-aot-compat` (spot-check after
Task 2; Wave B Task 3). Also references already-manual process bundles (`writing-skills`,
`teach` / `writing-great-skills` warning retirement). No example names a cut migration skill.

### Snapshot-testing retained and patched (not cut)

- Live: taken (`curation/deniz-dotnet-general.yaml` + ledger key
  `deniz-dotnet-general/skill/snapshot-testing`).
- Task 11: `body: patch`; no `depends_on` (design + plan + Task 14 edge gate agree).
- Description becomes general Verify / rendered-and-generated output (drops “rendered emails”).
- Removes `## Email Template Testing` (incl. trailing HR before `## API Surface Approval`) and
  `## Integration with MJML Email Testing` (incl. dead `aspnetcore/transactional-emails` handoff
  before `## Resources`).
- Recasts When-to-use bullet, decision-table row, String/HTML example, and Recommended Structure
  sample tree; keeps generic HTML / `.verified.html` / `Verify(..., extension: "html")`.
- Leaves ordinary DTO data (`UserDto.Email`) untouched.
- Step 5 forbids residual email-workflow tokens while requiring API/HTTP/serialization/scrubbing/CI/lifecycle retention.

### Every snapshot old-string / section boundary is physically present upstream

Pinned `external/dotnet-skills/skills/snapshot-testing/SKILL.md` (all HIT):

| Anchor | Locus |
|---|---|
| single-line `description:` with “rendered emails” | L3 |
| `- Verifying rendered output (HTML emails, reports, generated code)` | L12 |
| `### String/HTML Verification` … `viewable in browser.` | L95–108 |
| `## Email Template Testing` … HR before `## API Surface Approval` | L112–147 |
| `### Recommended Structure` sample tree (`EmailTests/`, `WelcomeEmail`, `PasswordReset`) | L268–282 |
| `\| Rendered HTML/emails \| Yes \| Catches visual regressions \|` | L343 |
| `## Integration with MJML Email Testing` … HR before `## Resources` + `transactional-emails` | L388–403 |
| DTO `Email: "john@example.com"` (must survive) | L78 |

## Patch / description / edge set reconciliation

| Set | Named identities |
|---|---|
| New `body: patch` | `run-tests`, `mtp-hot-reload`, `test-anti-patterns`, `test-gap-analysis`, `coverage-analysis`, `dotnet-webapi`, `test-analysis-extensions`, `snapshot-testing` |
| Extended existing patch | `testcontainers-integration-tests` only |
| Description-bearing | `filter-syntax`, `platform-detection` (frontmatter overrides), plus description edits inside the eight new patches except `mtp-hot-reload` (body-only) → nine description identities in Task 14 |
| Edge-bearing `depends_on` / model facts | `run-tests` → filter-syntax, mtp-hot-reload, platform-detection; `mtp-hot-reload` → filter-syntax, platform-detection; `test-anti-patterns` → run-tests, test-analysis-extensions; `test-gap-analysis` → test-analysis-extensions, test-anti-patterns; `dotnet-webapi` → database-performance, efcore-patterns |
| Explicit non-edges | `coverage-analysis`, `test-analysis-extensions`, `snapshot-testing` are patch-without-`depends_on` |

Task 14 ledger gate, design table, and per-task manifest shapes match this partition. Pre-amendment
“six edges including MJML→snapshot” is obsolete; final amended set correctly has **five**
edge-bearing sources and **no** MJML artifact.

## Excluded-reference coverage (pre-wave map)

Grep of kept `plugins/deniz-dotnet-general` skills for the cut/already-excluded names shows true
artifact hits only in:

1. skills Task 1 removes (self-bodies / cross-talk among cuts), and
2. skills Tasks 3–11 patch (`filter-syntax`/`platform-detection` descriptions; `run-tests`;
   `mtp-hot-reload`; `test-anti-patterns` exp-\* sentences + dead redirects; `test-gap-analysis`;
   `coverage-analysis` crap-score; `dotnet-webapi` optimizing-ef-core-queries;
   `test-analysis-extensions` consumer list + 11× `test-tagging` headings; `snapshot-testing`
   transactional-emails / email sections).

No kept skill outside that set retained a true excluded-artifact handoff in this sweep. Close-out
must still re-run the playbook mechanically (Task 14 Step 5) rather than trusting this map.

## Upstream body anchors (non-snapshot)

Sampled exact physical strings at the pin (body rows / unique bullets); folded descriptions use
whole-block replace as the plan requires:

| Task | Result |
|---|---|
| 2 header invocation + reference-posture paragraphs | HIT at live `curation/deniz-dotnet-general.yaml` L10–23 |
| 4 run-tests five body rows | HIT |
| 5 mtp-hot-reload three body rows | HIT |
| 6 anti-patterns exp-\* sentences; When-Not rows; No-assertions / Duplicate-tests insert points; depth-bar block “satisfy all four” | HIT |
| 7 gap-analysis one-line description + body rows incl. integration-table mention | HIT |
| 8 coverage When-Not crap-score bullet; `## Workflow`; Inputs table | HIT |
| 9 webapi two `optimizing-ef-core-queries` body sites | HIT |
| 10 extensions `>-` description; Tag support line; dotnet.md consumer list; 11/11 tagging headings | HIT |
| 12 post-apply working-copy anchors | `TestcontainersNetworkBuilder`, `using Testcontainers;`, MigrationTests `private readonly IContainer` + `InitializeAsync` assignment all present in current emitted tree; `## Common Issues and Solutions` insert point HIT |

## Contract soundness

### Testcontainers (Task 12)

- Output identity is `testcontainers-integration-tests` (frontmatter name); eject/overlay paths use
  that name — correct vs source basename `testcontainers`.
- Save → `--force` eject → `git apply --check` + apply → **remove** `overlay.patch` before edits →
  recut; verify headers do not target `overlay.patch`. Matches `listFiles` excluding `PATCH_FILE`
  and eject Phase-2 whole-dir snapshot hazard.
- Scope narrowed to named legacy API + MigrationTests readonly defect + module-packages note with
  `using Testcontainers.PostgreSql;`; disposable `dotnet build` is a real API compile gate.
- No “every example compiles” overclaim.

### Assertion-depth absorption (Task 6)

- Qualitative trivial-only + single-facet rows; depth-bar gains calibrate-assertion-depth item.
- Description DO NOT USE refuses full assertion-diversity metrics and academic catalog.
- Direct exp-\* handoff sentences deleted; practical over-mocking / duplication detectors kept.
- No `merged_from`.

### Targeted CRAP (Task 8)

- `Extract-MethodCoverage.ps1 -Filter all` exists (default `all`).
- Targeted branch filters method/class/file, applies existing CRAP formula
  `C²(1−cov)³+C` with `LineCoverage/100`, explicit no-row outcome.
- Project-wide workflow renamed and gated so full-report obligations do not leak.
- No new scripts / `merged_from`.

### Playbook

- Output-name resolution order matches build/eject.
- Own-skill frontmatter union; disjoint taken/excluded/never (live identity wins).
- Pattern escapes names; token boundaries match `candidateHits` in `tools/lib/refs.ts`
  (`[^a-z0-9-:]` both sides; colon keeps namespaced facts out of bare candidates).
- `rg` is candidate generation; classification rules require artifact-context reading.
- Wave A close-out consumes derived sets — no hand-maintained alternation.

### Wave B stub emitter

| Surface | Assessment |
|---|---|
| Bundled manual | Five-line stub; project + global prose paths; `$ARGUMENTS`; no `@` |
| Bundle-less manual | Pre-wave `serializeDoc` bytes; no parked dir; no parking report |
| `both` | Skill + command byte-identical; no `BODY.md` |
| Link spellings | bare, `./`, `../`, `../../`; fixtures placed so each link resolves to skill root |
| Regex | `(?:(?:\.\./)+|\./)?` + lookahead boundary; preserves closing `)` via lookahead |
| RED | Four named failures; preservation tests expected green before implementation |
| BODY write-before-rewrite | Parks body then rewrites all `*.md` |
| Report | `body parked at …/BODY.md`; bundle list filters `BODY.md`; drops WARN / not-rewritten |
| JSDoc | Must drop stale “references are not rewritten” / direct-bundle wording |
| Runtime smoke | Project-local + global; observe model read of `BODY.md`; no `OPENCODE_CONFIG_DIR` distribution promise |
| Validate | Retires `teach` + `writing-great-skills` dead-self-link identities only |

### Inventory / census / no fixed totals

- Global constraint + Task 1 Step 3: `npm run inventory` in the selection-changing task; commit
  `docs/inventory.md` with the cut.
- Task 14 Step 3: dynamic Node census over final manifest + ledger; record counts in the
  **execution report**, not planning prose.
- Canonical planning docs (T-items, correction design, both plans, playbook, handover, ROADMAP)
  carry **no** fixed taken/excluded/warning totals. Historical review files still contain stale
  pre-amendment arithmetic — ignore for execution.

### Source-pool / category clarification

Correction design states: a submodule is a source pool; different manifests may take different
items; duplicate **output identities** remain unsafe because OpenCode is one flat namespace.
Live evidence: general manifest header already omits akka/aspire candidates; akka manifest is the
home for `akka-*`. Matches the amended design. No plan step duplicates an identity across plugins.

### Header / ROADMAP honesty

- Header rewrite: honest descriptions → auto; mutating ceremonies → manual; three both exceptions.
  No “coercion-free” / “none carries disable-model-invocation” claim.
- Task 13 deferrals: own TUnit skill + `expects` trigger only — no pre-decided Verify/Testcontainers
  scope or field schema.

## Historical finding disposition (amended set)

| Earlier blocking theme | Final-amended disposition |
|---|---|
| Excluded refs survive (exp-\*, test-tagging×11, mjml→verify-email) | Tasks 6, 10, 11 (mjml **cut**; snapshot patched) |
| Overlay recut self-includes `overlay.patch` | Task 12 remove-before-recut |
| Incomplete promotion-on-contact | depends_on tables for five edge sources |
| Playbook hand list / FP noise | Mechanical derivation + classification |
| MJML edge to never-curated mailpit | Superseded: mjml excluded; no mailpit depends_on |
| Version migrations as manual ceremonies | Superseded: both excluded; flip list has eight non-migration items |
| Fixed inventory totals in prose | Removed from canonical docs; dynamic census |
| Wave B under-tested | Full branch + link-spelling + RED contract |

## Residual execution risks (non-blocking)

1. **Coverage targeted branch pipeline.** `Extract-MethodCoverage.ps1` writes JSON on the success
   stream and summaries via `Write-Host`. Host stream should not pollute `ConvertFrom-Json`; still
   worth a one-shot dry run during Task 8.
2. **Testcontainers `git apply --directory=...` on Windows.** Plan paths are repo-relative; absolute
   drive-letter directories can make git reject applies. Stay at repo root with the plan’s relative
   `$overlay`.
3. **Wave B Task 4 runtime smoke** is model/isolation non-deterministic by nature. Plan correctly
   refuses to retire the Known Gap without observed `BODY.md` reads on both mounts.
4. **Close-out Scan 1 false positives.** Short excluded basenames and product words will still
   appear as candidates; playbook classification is mandatory — do not treat raw `rg` as findings.
5. **Vague “migration skills” prose** in untouched `test-anti-patterns` When-Not (not a named
   artifact). Acceptable residual after platform/version migration cuts; not a Scan-1 artifact hit.
6. **Stale historical reviews** under `docs/superpowers/reviews/` still describe pre-amendment MJML
   take and 10-flip arithmetic. Execution follows the plans/spec only.

## Checks performed (read-only)

- Read AGENTS.md contract, correction design, T-items, both wave plans, playbook, s13 handover,
  ROADMAP, current `curation/deniz-dotnet-general.yaml`, `tools/build.ts` `emitOpenCodeSkill`,
  eject/overlay/`candidateHits`, and historical s13 reports.
- `git rev-parse` / status / submodule status / log; `npm run validate`.
- Node manifest census of live invocation modes and cut/flip/keep-both membership.
- Upstream and emitted greps for cut names, snapshot section boundaries, migration bundle trees and
  cross-handoffs, extension tagging headings (11/11), ceremony-flip self-links, Wave B regex
  behavior, inventory mailpit never-curated row.
- Confirmed general skills retained despite migration topic overlap; akka source-pool separation.

## End

**PASS**

Path: `docs/superpowers/reviews/2026-08-05-s13-final-amended-rereview-grok-a.md`
