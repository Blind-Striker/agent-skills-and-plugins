# S13 final re-review — Grok / xAI

Date: 2026-08-05

Reviewer/model lineage: Grok 4.5 (xAI). Final independent re-review after the
mailpit / body-mode / depth-anchor corrections. Prior reports in this lineage:

- `docs/superpowers/reviews/2026-08-05-s13-plan-review-xai.md` (initial FAIL)
- `docs/superpowers/reviews/2026-08-05-s13-corrected-plan-rereview-xai.md` (post-correction FAIL on mailpit)

This report does **not** modify those files.

Planning scratch (AGENTS.md, Documentation Hygiene): delete when the reviewed work merges.
This file is also planning scratch under `docs/superpowers/reviews/`.

## Verdict

**PASS — execution-ready for the curator’s go / execution-mode decision.**

No blocking findings remain. The immediately prior High (never-curated
`mailpit-integration` edge) and both prior Lows are fixed in the live corrected
set. Wave A/B plans, correction design, spec end-state, handover arithmetic,
playbook, and ROADMAP relays are mutually consistent on the surfaces that matter
for execution.

No wave was executed. No existing file was modified except the creation of this
report.

## Findings

**No findings.**

The corrected documents are coherent enough to execute as written. Residual
items below are execution risks, not plan defects.

## Disposition of immediately prior findings

From `2026-08-05-s13-corrected-plan-rereview-xai.md`:

| Prior | Status | Evidence |
|---|---|---|
| **High — MJML `depends_on` / namespaced edge to never-curated `mailpit-integration`** | **Fixed** | Task 11 now: related-skills mailpit line → product prose only (`wave-a-general-curation.md:633`); verify-email → `dotnet-skills:snapshot-testing` (`:634`); `depends_on: [snapshot-testing]` sole edge (`:644`); description drops any `mailpit-integration` token (`:622-626`); Step 5 requires prose classification (`:647`). Design (`2026-08-05-s13-plan-correction-design.md:73-75`, `:89`), spec end-state (`2026-08-04-t-items.md:53-56`), and ledger gate (`wave-a:805`) all list **six** edge-bearing sources with MJML → **snapshot-testing only**. Live tree: `snapshot-testing` taken (`docs/ledger.json` key `deniz-dotnet-general/skill/snapshot-testing`); no mailpit ledger key; inventory still `mailpit-integration` curated `—`. |
| **Low — Task 6 depth bar lacked exact anchor** | **Fixed** | Whole-block replace from `**Depth bar` through item 4 (`wave-a:322-332`). Upstream physical start is contiguous at `external/…/test-anti-patterns/SKILL.md:133-138` (“satisfy all four”); replacement expands to five items including “Calibrate assertion depth.” |
| **Low — `body: full` wording** | **Fixed** | Task 11 Step 4 now: “retain the existing invocation and add the patch mode and edge” (`wave-a:638-645`); no `body: full`. |
| Residual risks (coverage pipe, Wave B smoke, post-apply testcontainers anchor) | **Still residual, non-blocking** | See below. |

Broader first-round defects (excluded-ref closure, promotion-on-contact, header
coercion claim, playbook mechanics, Wave B contract/RED count, patch arithmetic,
testcontainers self-include, absorption honesty) remain closed as previously
verified; spot-checks on this pass did not reopen them.

## Independent checks (not string-only)

### Live baseline

| Check | Result |
|---|---|
| HEAD | `ea8b805` (dirty: s13 planning docs only) |
| Submodule pins | Unchanged (`dotnet-agent-skills` `85cd1034`, `dotnet-skills` `c2ac7e9`, …) |
| `npm run validate` | `0 error(s), 6 warning(s)` (standing set unchanged) |
| Manifest pre-wave | 55 `auto` / 15 `both` / 80 `exclude` / 1 agent → planned post-wave 51/3/10/1 + 86 exclude |

### MJML / mailpit / snapshot (primary gate for this pass)

- Upstream still ships path-like handoffs at
  `external/dotnet-skills/skills/mjml-email-templates/SKILL.md:17-18`
  (`` `aspire/mailpit-integration` ``, `` `testing/verify-email-snapshots` ``) — Task 11 old
  strings **HIT**.
- Planned new body: one namespaced model edge to kept `snapshot-testing`; Mailpit is
  “Local SMTP capture with Mailpit …” — product word only, no `mailpit-integration`
  artifact identity (`wave-a:633-634`, `:644`).
- Description DO NOT USE names bare `snapshot-testing` (candidate-tier) and no never-curated
  skill token (`:625-626`); body supplies the declared fact edge.
- Exactly **six** `depends_on` blocks in Wave A plan: run-tests, mtp-hot-reload,
  test-anti-patterns, test-gap-analysis, dotnet-webapi, mjml-email-templates
  (`:219`, `:260`, `:345`, `:392`, `:546`, `:644`). coverage-analysis and
  test-analysis-extensions remain patch-without-edges. Matches design table
  (`correction-design.md:82-89`) and handover (`s13-…prompt.md:90-92`, `:113-114`).

### Edge / patch / description arithmetic

| Quantity | Documented | Live-reconciled |
|---|---|---|
| New `body: patch` | 8 | Tasks 4–10 + Task 11 mjml |
| Edge-bearing sources | 6 | Same six; MJML → snapshot only |
| Description changes | 9 | filter/platform + seven patched bodies’ descriptions including mjml |
| Removals / flips | 6 / 10 | Unchanged from prior recount |
| Taken / excluded final | 65 / 86 | 51 auto / 3 both / 10 manual / 1 agent |

### Task 6 absorption + depth bar

- Description remains minimal/honest (no metrics dashboard, no academic catalog)
  (`wave-a:284-295`).
- exp-\* sentence deletes still anchored (`:307-308`).
- Promotion: `depends_on: [run-tests, test-analysis-extensions]` (`:345`).
- Depth bar is a full physical block swap (`:322-332`) matching upstream `:133`.

### Wave B (contract still sound)

- Bundled / bundle-less / both branches; exact five-line stub; no `@`
  (`wave-b:15-20`, `:43-51`, `:113-120`).
- Link regex multi-`../` + `./` + bare; BODY.md written before rewrite; BODY.md
  filtered from bundle list (`:96-104`, `:129-131`).
- RED names four failures; preservation tests expected green (`:78-80`).
- Real-tree ledger gate + two-mount recorded smoke before Known Gap retirement
  (`Task 3–5`).
- JSDoc rewrite required (`:89`).

### Playbook

Mechanical derivation, disjoint sets, escaped `candidateHits`-equivalent
boundaries, candidate classification unchanged and still the Wave A close-out
instrument (`reference-audit-playbook.md:24-186`; `wave-a:815-817`).

### Header / ROADMAP deferrals

- Header still lacks “free of coercion” (`wave-a:95-101`).
- TUnit / `expects` deferrals remain trigger-only without pre-decided skill scope
  (Task 13; design `:99-102`).
- ROADMAP Next Up correctly points at final dual-Grok gate before execution
  (`docs/ROADMAP.md:25-31`).

## Residual execution risks (not findings)

1. **Task 8 targeted CRAP** — `Extract-MethodCoverage.ps1` mixes `Write-Output` JSON with
   `Write-Host` summaries; pipeline-to-`ConvertFrom-Json` should be clean on PS 7+, but
   dry-run once during implementation.
2. **Wave B Task 4 smoke** — model/isolation non-determinism; both mount legs must pass
   before docs claim retirement; budget a pinned model and lab time.
3. **Task 12 testcontainers** — `private readonly IContainer _container` old-string is
   post-`git apply` working-copy text; follow the save/apply/remove-`overlay.patch`/recut
   ceremony exactly (`wave-a:655+`).
4. **Scan 1 after Wave A** — true-hit coverage is planned complete; still run the playbook
   mechanically at close-out rather than trusting this review’s pre-edit map.
5. **Handover P3 backlog** still mentions upstream `mailpit-integration` samples
   (`s13-…prompt.md:204`) as aspire-session context — not Wave A work; do not confuse with
   the fixed MJML edge.

## Commands / checks run

- `git rev-parse` / `status` / `submodule status`
- `npm run validate` → 0/6
- Manifest invocation/exclude counts
- Ledger presence of `snapshot-testing`; absence of mailpit keys
- Inventory/grep: mailpit never-curated; MJML upstream related-skills HIT
- Contiguous depth-bar start HIT on pinned anti-patterns source
- Grep of all Wave A `depends_on` lines and planning-set mailpit tokens
- Read: corrected Task 6/11/14, design reference-closure table, spec end-state, handover
  checklist, Wave B Tasks 1–2, playbook derivation, ROADMAP Next Up

## Bottom line

**PASS.** Curator may choose go/no-go and subagent-driven vs inline execution. Execute
Wave A then Wave B exactly per the corrected plans; do not reopen selection or absorption
policy during implementation.
