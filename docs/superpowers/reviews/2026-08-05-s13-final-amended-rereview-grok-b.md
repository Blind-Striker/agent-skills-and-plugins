# S13 final-amended re-review — Grok B (independent)

Date: 2026-08-05

Reviewer/model lineage: Grok 4.5 (second fresh independent reviewer, "Grok B"). Live-git audit
of the **final amended** s13 planning set. Prior reports under `docs/superpowers/reviews/` were
read only as historical context after live verification; they are not authority. In particular,
earlier "final" Grok reports that still describe Task 11 as an MJML keep/patch or pin fixed
taken/excluded totals are **stale relative to this amended set**.

Planning scratch (AGENTS.md, Documentation Hygiene): delete when the reviewed work merges.
This file is also planning scratch under `docs/superpowers/reviews/`.

## Verdict

**PASS — execution-ready for the curator’s go / execution-mode decision**, with one Medium plan
defect that Task 8 must correct when it lands (four-column Inputs row). No High/blocking defects
found in the final amended spec, correction design, Wave A/B plans, playbook, or handover against
live git.

No wave was executed. No existing file or implementation I/O was modified. Sole write: this file.

## Findings

### 1. Medium — coverage-analysis Inputs row has the wrong column arity

- **Where:** `docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:449`
- **Live table:** `external/dotnet-agent-skills/plugins/dotnet-test/skills/coverage-analysis/SKILL.md:45-51`
  is a **four-column** table: `Input | Required | Default | Description`.
- **Plan text:** adds
  `| Target scope | No | Exact method name, class name, or source-file path; selects the targeted workflow |`
  — only three data cells. The long phrase lands in `Default`; `Description` is empty/misaligned.
- **Required fix at Task 8 (not a redesign):** e.g.
  `| Target scope | No | — | Exact method name, class name, or source-file path; selects the targeted workflow |`
- **Why not blocking:** gates, edges, exclusions, and the targeted CRAP branch body are otherwise
  sound; this is a one-line table correction inside an already-owned patch task.

### No other findings

All other amendment surfaces and pre-existing correction areas checked clean against live git
(details below). Residual items are execution risks, not plan defects.

## Independent live baseline

| Check | Result |
|---|---|
| HEAD | `ea8b805` |
| Working tree | Dirty **planning/docs only** (`docs/superpowers/`, playbook, s13 handover, ROADMAP, agents README). No curation/overlay/tools/generated/submodule drift attributable to this correction pass |
| Submodule pins | Unchanged (`dotnet-agent-skills` `85cd1034`, `dotnet-skills` `c2ac7e9`, …) |
| `npm run validate` | `0 error(s), 6 warning(s)` — standing identities: 2× `dotnet-devcert-trust` aspire-ns, `elements-of-style`, `subagent-driven-development` relative ref, `teach` `./SKILL.md`, `writing-great-skills` bare `SKILL.md` |
| Package scripts used by plans | Present and executable: `build`, `inventory`, `eject`, `validate`, `test`, `typecheck`, `format:check`, `lint` (`package.json`) |
| Playbook derivation smoke | Runnable from repo root via the embedded `node --input-type=module` script; `parse`/`parseDoc` imports resolve under Node ≥24 |

Hand-written planning docs carry **no canonical fixed taken/excluded/warning totals**. Close-out
derives census via named item sets + dynamic commands (`wave-a` Task 14; spec end-state).

## Amendment audit (curator final cuts)

| Amendment | Live check | Result |
|---|---|---|
| **MJML excluded** | T1 cut list includes `mjml-email-templates` (`wave-a:51-52`; spec T1; design Email boundary). No Task keeps/patches MJML. | **OK** |
| **snapshot-testing kept; email-authoring removed by exact anchors; generic HTML remains** | Task 11 (`wave-a:630-713`): description drop of “rendered emails”; When-to-use + decision-table rows; whole `### String/HTML Verification` replace; delete `## Email Template Testing` through HR before API Surface; generic Recommended Structure; delete `## Integration with MJML…` through HR before Resources (removes `aspnetcore/transactional-emails`). `UserDto.Email` intentionally retained. Upstream anchors HIT at `external/dotnet-skills/skills/snapshot-testing/SKILL.md` (`:3`, `:12`, `:95-108`, `:112-145`, `:268-282`, `:343`, `:388-403`). `depends_on` absent. | **OK** |
| **migrate-dotnet8→9 and 9→10 excluded with internal closures + cross-pair handoffs** | Cut list (`wave-a:53-56`); design Version Migration Boundary. Upstream closures are bundled `references/*` only (not separately curated). Cross-pair handoffs HIT in both SKILL.md bodies. Not on ceremony-flip list. Close-out removed-entries names both. | **OK** |
| **Do not cut general EF/serialization/ASP.NET/MSBuild/crypto/container/interop** | Live manifest still takes `efcore-patterns`, `database-performance`, `serialization`, `dotnet-webapi`, full MSBuild estate, `dotnet-pinvoke`, `testcontainers`, etc. Plan Task 1 prose forbids cutting them for topic overlap. | **OK** |
| **Retained manual list + Wave B example** | Ceremony flip table is eight retained mutators (`wave-a:93-100`); `slopwatch` / `analyzing-dotnet-performance` / `dotnet-devcert-trust` stay `both`. Wave B real-tree example is `dotnet-aot-compat` + `grill-me` bundle-less (`wave-b:153`), not a migrate-dotnet skill. | **OK** |
| **Selection regenerates inventory** | Global constraint + Task 1 Step 3/4 (`wave-a:23-24`, `:70-78`). | **OK** |
| **No fixed taken/excluded/warning totals in prose** | Spec end-state and design derive counts; Task 14 census script records emitted numbers in the execution report only. Handover says recompute live warning identities. | **OK** |
| **Close-out named removals/invocations/patches/descriptions/edges** | `wave-a` Task 14 Step 2 (`:862-872`) lists T1 cut identities, eight manuals, eight new patch names (incl. `snapshot-testing`), description-bearing set, five edge sources. Matches design depends_on table (`design:140-149`) with snapshot gaining no edge. | **OK** |
| **Same upstream submodule → general + Akka without duplicating flat OpenCode identity** | Design Source Pools (`design:89-98`); handover P2/cross-module backlog; manifest header already states Akka/Aspire are other manifests’ candidates (`curation/deniz-dotnet-general.yaml:6-8`). | **OK** |

## Pre-existing correction areas (recheck)

### Excluded-reference closure + promotion-on-contact

Live pre-wave sweep of kept `plugins/deniz-dotnet-general` for the T1 cut set and related already-excluded names shows true artifact hits only in:

- items themselves being cut (leave with Task 1),
- bodies/descriptions Task 3–11 patch (`filter-syntax`, `platform-detection`, `run-tests`, `mtp-hot-reload`, `test-anti-patterns`, `test-gap-analysis`, `coverage-analysis`, `dotnet-webapi`, `test-analysis-extensions` + 11 extension files, `snapshot-testing`).

Spot-check of other taken skills (detect-static / testability pair / slopwatch / perf / microbenchmarking / concurrency) returned no additional true hits. Task 14 Step 5 requires a mechanical playbook Scan 1 after execution rather than a hand-maintained alternation.

Promotion-on-contact: edge-bearing manifest shapes match design; first live mentions namespaced in patched bodies; `snapshot-testing` correctly has no `depends_on`.

### Honest absorption

- `test-anti-patterns`: qualitative assertion-depth rows + depth-bar item; no diversity metrics/dashboard/academic catalog (`wave-a:303-351`; design `:36-48`).
- `coverage-analysis`: bounded targeted branch over `Extract-MethodCoverage.ps1 -Filter all`, existing CRAP formula, explicit no-row outcome; no new scripts / `merged_from` (`wave-a:454-501`). Live script property names `Class`/`Method`/`File`/`Complexity`/`LineCoverage` match the planned filter pipeline.

### Folded anchors

Whole-block description replaces for folded `description: >` / `>-` items (run-tests, test-anti-patterns, coverage-analysis, webapi, extensions). test-gap-analysis and snapshot-testing use complete single-line description replaces. Body old-strings checked as contiguous physical text at the pin for Tasks 4–7, 9–12.

### Testcontainers safe recut + compile gate

Task 12 (`wave-a:717-822`):

- Saves `overlay.patch`, `eject --patch --force`, `git apply --check` + apply with **relative** `--directory=$overlay`, then **removes** old `overlay.patch` before edits (prevents self-inclusion; `listFiles` already skips `overlay.patch` when cutting).
- Verified: `git apply -p1 --directory=<rel-under-repo>` succeeds from repo root; absolute `--directory` under Temp fails on this Windows Git — plan’s relative overlay path is the correct form.
- Completions: `TestcontainersNetworkBuilder` → `NetworkBuilder`; `using Testcontainers;` → Builders+Containers usings; MigrationTests `readonly IContainer` assigned in `InitializeAsync` → `= null!` (only that defect pattern; constructor-assigned readonly fields correctly left alone).
- Module-packages section includes `using Testcontainers.PostgreSql;`.
- Real `dotnet build` API smoke for NetworkBuilder / IContainer / PostgreSqlBuilder.
- Stale-name scan after emit.

### Wave B contract

- Tests first; RED names bundled-manual contract, link-repoint, existing invocation BODY.md assert, updated parking report (`wave-b:78-80`); bundle-less and `both` must stay green.
- Branches: bundled manual / bundle-less manual / `both`.
- Regex `(\]\((?:(?:\.\.\/)+|\.\/)?)SKILL\.md(?=[)#?\s]))` covers bare, `./`, `../`, repeated `../` **inside markdown links**; tests place links where each spelling resolves to the skill root.
- `BODY.md` written before rewrite loop; parked report names BODY.md as event and excludes it from parenthetical bundle list (`wave-b:128-132`).
- JSDoc rewrite required (`wave-b:89-90`).
- Ledger gate: BODY.md only on previously non-empty parked manuals; no Claude/invocation/edge/description delta.
- Runtime smoke: both project-local and global mounts; markers + observed read of BODY.md; `OPENCODE_CONFIG_DIR` not a distribution promise; dry-run + `selftest -SkipLab` first.
- Docs replace complete ROADMAP Known Gap and research “typed command pastes…” paragraph after smoke evidence.

### Playbook derivation

- Output-name order: ledger key / own-skill frontmatter / exclude `name:` → inventory → basename (`playbook:26-28`, `:83-86`).
- Disjoint sets: taken wins; excluded beats never-curated (`:90-97`).
- Never-curated marker is U+2014 `—`, matching live `tools/inventory.ts` fallback and current `docs/inventory.md`.
- Candidate regex escapes names; token boundaries match `candidateHits` in `tools/lib/refs.ts` (`(^|[^a-z0-9-:])…($|[^a-z0-9-:])`); `rg -w` rejected.
- Grep is candidates-only; classification ladder for true artifact refs vs product/CLI/API/framework rows.

## Commands validated for executability

| Command / fragment | Status |
|---|---|
| `npm run build` / `validate` / `inventory` / `eject` / `test` / `typecheck` / `format:check` / `lint` | Scripts exist; validate run green (0 errors) |
| Playbook derivation `node --input-type=module` block | Executes; imports resolve |
| Task 14 census `node --input-type=module` block | Syntactically aligned with manifest/ledger shape |
| `git apply -p1 --directory=<repo-relative overlay>` | Works on this host for the saved testcontainers patch format |
| `pwsh -File experiments/harness-invocation/selftest.ps1 -SkipLab` | `-SkipLab` switch exists |
| Wave B smoke runner (to be created) | Plan binds to existing `common.ps1` / `Get-LabRoot` / `$script:LegTable` / protocol patterns |

## Residual execution risks (not findings)

1. **Task 8 Inputs row** — apply the four-column fix above before cutting the coverage patch.
2. **Task 12 post-apply working copy** — new anchors are edited only after the saved baseline patch reapplies cleanly; if a future pin moves upstream, re-verify before force-eject.
3. **Wave B runtime smoke** — depends on lab isolation, provider keys, and model read-tool observation; failure correctly blocks Known Gap retirement (plan already says so).
4. **Inter-wave ROADMAP wording** — after Wave A alone, the pre-Wave-B Known Gap still mentions long-body `both` migrate-\* ceremonies that Wave A excluded; Wave B Task 5 replaces that paragraph. Execute B promptly after A or accept brief operational staleness.
5. **Census agent row** — Task 14 counts agents by `/agents/` path separately from invocation buckets; correct today because the kept agent has no `invocation:`. If an agent later gains one, avoid double-counting in human readouts.
6. **Warning baseline** — recompute identities after Task 2 (new parked-bundle **build** reports for bundled manuals are expected; validate identities must not gain new ones). Wave B retires only the two named dead-self-link validate identities.
7. **Historical reviews** — do not re-use fixed totals or MJML-Task-11 dispositions from pre-amendment reports; this amended set is the contract.

## Disposition vs historical defect themes

| Theme | Final amended set |
|---|---|
| Excluded refs / test-tagging ×11 / exp-\* handoffs | Covered by Tasks 3–10 + close-out playbook Scan 1 |
| MJML / mailpit edge | **Superseded:** MJML excluded; snapshot patched without mailpit edge |
| migrate-dotnet as ceremony flip | **Superseded:** excluded with closures |
| Fixed 65/86 (or similar) prose totals | **Removed** from spec/plans; derive at close-out |
| Coercion-free auto claim | Header rewrite + design forbid it |
| Full metrics / 19-smell absorption | Explicitly not absorbed |
| Testcontainers self-include / NetworkBuilder / compile | Task 12 |
| Wave B under-tested / BODY.md in bundle list / JSDoc | Task 1–2 contract |
| Playbook hand alternation / `rg -w` | Mechanical derivation + candidateHits boundaries |

## Summary

The final amended s13 documents are mutually consistent with live git and with each other on
selection boundary, reference closure, absorption honesty, testcontainers recut, Wave B stub
contract, playbook mechanics, and close-out structure. One Medium defect remains: the Task 8
coverage Inputs markdown row must gain a Default column before that patch is cut. After that
one-line correction, the set is ready for the curator’s go and execution-mode choice.

**PASS**
