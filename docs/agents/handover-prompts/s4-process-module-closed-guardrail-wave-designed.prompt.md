# S4 — The Process Module Closed; the Guardrail Wave Is Designed, Not Built

Date: 2026-07-31

You are entering after the repo's first real curation day. In one session: the curator's intent
became canon (ADR-0007), `deniz-process` went from one starter skill to a complete module (two
batches, 32 items decided with the user), the first overlay (a two-source skill merge) and the
first two patches landed, and the built `opencode/` tree was verified end to end in a live
OpenCode TUI. The single most important state observation: **the next wave is machinery, not
curation — two manifest guardrails designed with the user, which must land BEFORE the two
pending skill merges so those merges are born protected. Do not reorder this.**

## First Principle

> Treat every claim here as **current-as-of-authoring (2026-07-31)** and verify against the live
> repository: `git log --oneline -12`, `npm test`, `node tools/validate.ts`, and the canonical
> documents — before acting.

## What Just Happened

| Concern | What changed | Verification |
|---|---|---|
| Curator's intent | **ADR-0007**: control beats fidelity, trigger per item, heavy modification unfeared, frameworks are quarries, decisions see the dependency closure, both harnesses every time. `AGENTS.md` relays it | Read it before proposing anything |
| Layer boundary | **Compile-time vs runtime** (AGENTS.md, user's convention): this repo renders artifacts; what a skill does when invoked is upstream's design. Coined after an agent misfiled a runtime concern (where matt's skills persist tickets) as a compile-time gate and started pre-framing eliminations | The incident and rule are in the commit trail |
| Batch 1 (16 items) | The daily-flow spine. The rule that shaped it: **`manual` is unreachable even from another skill's body** — every body-invoked link must be `both`/`auto`; only true entry points are `manual` | Measured (research doc); enforced so far only by manifest comments — the guardrail wave turns it into a validator |
| First merge | systematic-debugging = superpowers' root-cause armor × matt diagnosing-bugs' loop engineering, 7 phases, `body: overlay` scoped to SKILL.md alone (sibling files keep flowing). diagnosing-bugs is `exclude: true — merged` | Live in a TUI probe: fired on a free-form bug sentence and visibly ran loop-first |
| using-superpowers | `body: patch` (level ii, user's choice): 1% block, Red Flags table, platform section cut; ordering rule, announce habit, subagent guard stay. `manual` + human-facing description | Output greps clean; refs rewrote per tree |
| Batch 2 (16 items) | Matt's whole production chain incl. the setup wizard (runtime taken as shipped), design/standalone set, superpowers' last two. `research`/`prototype` are `both` (user's call); first cross-framework stitch: research's dispatch → superpowers:dispatching-parallel-agents, spelled upstream so each tree localizes it. ask-matt `exclude` (router lies about our set) | `validate` 0 errors 0 warnings — writing-skills' take retired the last two |
| OpenCode verified | Real tree mounted three ways + TUI 5/5 (commands listed, model-mediated composition works, parked bundles reachable, auto+both live on a non-Claude model). Measured precedence: **package cache > OPENCODE_CONFIG_DIR > global .config**; global mount follows XDG_CONFIG_HOME; `debug paths` lies about discovery | `docs/research/skill-invocation-across-harnesses.md` + harness-probing traps |
| Machine migration | User's end state on the roadmap: uninstall every globally installed skill set (both harnesses) as `deniz-*` replacements land; the OpenCode superpowers package is first (it shadows curated output). Installer decision parked with references | ROADMAP items 5–6 |
| CI lesson | Two red runs, same cause: `docs/inventory.md` regenerates **in the same commit** as any curation change (the Curated column moves) | Third change caught it pre-commit |

## Current State You Should Assume Until Verified

- **HEAD** (`master`): the commit carrying this prompt. Pushed; verify CI yourself.
- **Tests:** 70 passed at session start; `tools/` untouched since. **Validate:** 0 errors, 0 warnings.
- **Curation state:** `deniz-process` — all 14 superpowers and 20 of 22 matt-promoted candidates
  answered in the manifest (29 taken, incl. 1 merge-overlay + 2 patches; 2 reasoned excludes).
  The 2 unanswered — matt `tdd` and `code-review` — are deliberately absent, awaiting their
  merge passes. Three dotnet manifests still hold one starter each.
- **Local-only:** the user's real machine still carries the global installs (migration pending);
  the OpenCode lab's RESULTS.md lives in session scratchpad — durable findings are already in
  `docs/research/`.

## Recommended Next Step: the guardrail wave (ROADMAP item 2)

Size M. Three legs designed with the user — his requirements were explicit: **guardrail, not
metadata** ("hata almamız gerekiyor"), and **sustainable, not hand-maintained** (the sync
re-check leg). Read ROADMAP item 2 for the full design; in brief:

1. **`merged_from:`** — hash-bless every merge source like the primary (same-filename rule, lock
   `mergeSources`, hard build failure naming the moved source, `--bless` stamps all). Retrofit
   systematic-debugging; retire its "glance by hand" comment.
2. **`depends_on:`** — declared dependency map, full scope, seeded from the two measured coupling
   graphs (transcribe invocation edges only — the greps over-report; **the transcription is a
   draft the user reviews, not a decision you make**). Validate: target must exist and must not
   be `manual`. No content hashing — targets are passthroughs whose updates should flow.
3. **sync re-check** — on a pin move, re-derive candidate edges for touched taken items, diff
   against declarations, report appeared/vanished candidates.

Process: extend ADR-0001 in place, RED tests first, all gates, then retrofit. Pre-flight:
`tools/lib/overlay.ts`, `tools/lib/manifest.ts`, `tools/validate.ts`, `tools/build.ts`,
`tools/eject.ts`, `tools/sync.ts`, and their tests.

## The Two Pending Mixes — Full Context (the user asked that this not be lost)

Both follow the systematic-debugging mold: draft → user reviews → eject/bless. Both were chosen
by the user **over** the alternative of demoting matt's item to `manual` — merging resolves the
trigger competition instead of dodging it. Both passes also repoint `implement`'s bare `/tdd`
and `/code-review` prose (invisible to validate — seed `depends_on` entries for them).

**TDD mix** — superpowers `test-driven-development` × matt `tdd`. Host: the sp item (already
curated, `auto`, referenced by systematic-debugging's body). matt `tdd` will enter as
`exclude — merged`. Complementary content: sp brings the behavioural armor (Iron Law,
delete-code-written-first, watch-it-fail, rationalization tables, `writing-good-tests.md`); matt
brings design concepts (seams — "test only at pre-agreed seams, confirmed with the user",
tracer bullets / vertical slices, the tautological-test and implementation-coupling
anti-patterns). **Genuine contradictions the user must rule on** (proposed syntheses were
floated, not accepted): (1) sp keeps refactor inside RED-GREEN-REFACTOR, matt says refactoring
belongs to the review stage — proposal: small cleanups in-loop, structural refactoring at
review; (2) the seams-gate — proposal: keep it (matt's best idea; `implement` already says "at
pre-agreed seams"); (3) mocking stance needs harmonizing. Matt's sibling files
(`tests.md`, `mocking.md`) do **not** ride along — fold what matters into the body
(overlay-adds-new-file mechanics are unproven; same call as the debugging merge's HITL script).

**Review mix** — superpowers `requesting-code-review` × matt `code-review`. Shape: sp's dispatch
process and `code-reviewer.md` template become the vehicle; matt's two-axis structure
(Standards vs Spec, reported separately, never reranked) and Fowler smell baseline become the
rubric inside it; the standalone "review since a fixed point" use folds in. `receiving-code-review`
stays untouched. matt `code-review` will enter as `exclude — merged`. Open editorial question:
matt runs the two axes as parallel sub-agents — keep that split or fold into sp's single
reviewer dispatch?

## Mandatory Grounding (read in this order)

1. `AGENTS.md` — note the two blocks added this wave: the ADR-0007 relay and the
   compile-time/runtime boundary.
2. `docs/ROADMAP.md` — item 2 is your work order; items 5–6 are context.
3. `docs/adr/0001` (overlay/blessing design you will extend), `0005`, `0006`, `0007`.
4. `docs/research/skill-invocation-across-harnesses.md` — incl. the new "verified on real
   output" section; `docs/agents/harness-probing.md` for the isolation traps.
5. `curation/deniz-process.yaml` — the decision record; its header carries the open items.

## Locked Policy Recap

- `external/`, `plugins/`, `opencode/` never hand-edited; output regenerates and is committed.
- **`npm run inventory` in the same commit as any curation change** — two CI failures this
  session prove the gate works.
- Curation decisions are the user's, **and he rejects both failure modes**: no silent
  pre-decisions, and no item-by-item question loops. Bring one holistic, opinionated proposal
  with reasoning per item and the genuine debates flagged; he reacts to the whole. (Calibrated
  twice this session — it is in auto-memory too.)
- Compile-time vs runtime: do not re-solve upstream's runtime problems.
- Verify before claiming; exec bits via `git update-index --chmod=+x` on built script copies.
- Measure, don't infer — and a lab under `%TEMP%` sits inside the real home (see probing traps).

## Final Steering Note

The natural rhythm: one machinery session (the guardrail wave — TDD on `tools/`, no user
decisions needed beyond the map-draft review), then two editorial sessions with the user (the
mixes — he rules on the contradictions, you draft and wire). After that `deniz-process` is fully
closed and the dotnet modules follow, one per session, batch-1-style: holistic table, debates
flagged, his call. He thinks by talking, in prose; reflect understanding back before executing,
and when he pushes back, the push usually contains the design.
