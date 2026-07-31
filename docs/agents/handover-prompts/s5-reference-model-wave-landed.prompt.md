# S5 — The Reference-Model Wave Landed; the Mixes Are Born Protected

Date: 2026-07-31

You are entering after the repo's first full machinery wave: in one day, ADR-0008 went from
conversation to canon to running code — one reference grammar, a linker, a committed ledger,
merge-source blessing, semantic sync, and a provenance ban — executed subagent-driven (Opus 5
implementers, controller-reviewed), closed by a whole-wave adversarial review whose three
Important findings were fixed the same evening. The single most important state observation:
**the next work is editorial, not machinery — two skill merges the user rules on, now landing on
ground that guards them from birth. Do not reopen the machinery to make a mix convenient; a red
linker during a mix is the system working — fix the data, not the rules.**

## First Principle

> Treat every claim here as **current-as-of-authoring (2026-07-31)** and verify against the live
> repository: `git log --oneline -20`, `npm test`, `node tools/validate.ts`, and the canonical
> documents — before acting.

## What Just Happened

| Concern | What changed | Verification |
|---|---|---|
| Reference model | **ADR-0008 is code.** `tools/lib/refs.ts` is the one grammar (`scanRefs` carries positions; `extractRefs` wraps it); the rewrite consumes it (single pass, exact map lookup), the linker links against it, sync diffs with it. `ns:name` = model-edge (target must be `auto`/`both`), `/ns:name` = user-pointer (target must be `manual`/`both`), chains (`a:b:c`) rejected, prose colons kept | The linker found 19 real errors the day it landed; data zeroed them in the same commit. Rebuild is byte-identical |
| Ledger | `docs/ledger.json`, generated + committed, CI freshness gate covers it. Per item × harness: invocation, artifacts, edges, dropped keys, parked files, body/mergedFrom/dependsOn. `git diff` on it is the posture-change notification channel | Deterministic across rebuilds (tested); entry count = non-excluded manifest items |
| depends_on | Declared model-edge map, enforced **both directions as errors**; drafts transcribe from the ledger, the user reviews | 7 items declare edges today |
| merged_from | Lock gains `mergeSources` (same-filename rule; absent recorded `null`; appearance is drift; **all-null source = error** — it guards nothing). Build fails naming the moved source; `--bless` shows the drift it accepts, `--yes` confirms. systematic-debugging retrofitted — its "glance by hand" comment retired | Drift/absent/appeared/all-null each RED-tested; retrofit blessed, lock carries both sources |
| Semantic sync | Pin moves report meaning: POSTURE drift on passthrough items, MERGE SOURCE tags (across submodules — the real merge's source lives in a different submodule than its primary), CANDIDATE edge diffs vs the ledger | Pure-function tests; wiring smoke-checked against the real submodules read-only |
| Provenance | **User's ruling: the curation layer stamps no curator names, no dates** — comments scrubbed, `validate` errors on both (yaml comment segments, overlay bodies, patch added-lines, own skills; `description:` values keep deliberate branding). AGENTS.md Hard Rules carries it | Rule tests incl. the patch-context negative; real repo 0/0 |
| Curation rulings | brainstorming `manual`→`both` (measured first: all jump-pressure lived in the already-overridden description; the body's one "You MUST" is in-ceremony discipline). tdd: writing-good-tests.md's citation of writing-skills **deleted** (upstream authoring culture, no audience in our set) — a one-line `body: patch` | Both trees rebuilt; linker green; ledger shows the flip |
| Pointer wording | Measured (OpenCode 1.18.7, non-Claude model): "suggest opening /X — do not run it yourself" relays without any invocation attempt; the control model-edge invoked correctly | Lab RESULTS at `E:\opencode-probe-lab` (local-only); Claude-side wording check rides the next TUI round |
| Final review | Whole-wave adversarial pass: 0 Critical / 3 Important (one-grammar unification, all-null merge guard, one stale research paragraph — all fixed) / 12 Minor (6 recorded as Known Gaps, 2 fine-as-designed) | `npm test` 104/104 after fixes |

## Current State You Should Assume Until Verified

- **HEAD** (`master`): the commit carrying this prompt. Pushed; CI green through the wave (one
  red mid-wave: Biome style-checked the generated ledger on Linux only — fixed by excluding
  generated output, which also retired the `.claude-plugin` luck-gap).
- **Tests:** 104. **Validate:** 0 errors, 0 warnings — and it now checks far more than before.
- **Curation:** `deniz-process` complete except the two merge passes; three dotnet manifests hold
  one starter each. Ledger has one entry per non-excluded item.
- **Local-only:** the OpenCode probe lab (`E:\opencode-probe-lab`, RESULTS.md); the machine still
  carries the global skill installs (ROADMAP migration item); the session's SDD progress ledger
  lives in session scratch and does not travel.

## Recommended Next Step: the two mixes (editorial, user rules on everything)

Both follow the systematic-debugging mold — draft → user reviews → eject/bless — and both now
land with machinery that did not exist when they were scoped: **declare `merged_from` on day
one** (bless stamps every source; an all-null source errors), spell every reference in neutral
space (`superpowers:x` / `/superpowers:x`), and add `depends_on` in the same change — the linker
enforces the pair, and gates stay green per commit (rule-and-data in one commit is the
established pattern).

**TDD mix** — superpowers `test-driven-development` × matt `tdd`. Host: the sp item (`auto`,
model-edged from systematic-debugging Phase 6). matt `tdd` enters `exclude: true — merged`.
Complementary content: sp brings the behavioural armor (Iron Law, delete-code-written-first,
watch-it-fail, rationalization tables, writing-good-tests.md); matt brings design concepts
(seams — "test only at pre-agreed seams, confirmed with the user", tracer bullets, the
tautological-test and implementation-coupling anti-patterns). **Genuine contradictions the user
must rule on** (syntheses were floated, never accepted): (1) sp keeps refactor inside
RED-GREEN-REFACTOR, matt moves refactoring to review — floated: small cleanups in-loop,
structural at review; (2) the seams-gate — floated: keep it, matt's best idea, `implement`
already says "at pre-agreed seams"; (3) mocking stance needs harmonizing. Matt's sibling files
(`tests.md`, `mocking.md`) do **not** ride along — fold what matters into the body. One
mechanical note: the item currently carries a one-line `body: patch` (the citation removal);
`body:` is one enum, so the mix's overlay replaces it — either own an edited
writing-good-tests.md in the overlay, or fold the intent into the merged SKILL.md and let the
sibling revert to pristine flow. Decide in-session, with the user.

**Review mix** — superpowers `requesting-code-review` × matt `code-review`. Shape: sp's dispatch
process and its `code-reviewer.md` template (a cross-skill file dependency SDD also uses) stay
the vehicle; matt's two-axis structure (Standards vs Spec, reported separately, never reranked)
and Fowler smell baseline become the rubric inside it; the standalone "review since a fixed
point" use folds in. `receiving-code-review` stays untouched. Open editorial question: matt runs
the two axes as parallel sub-agents — keep the split or fold into sp's single reviewer dispatch?

Both passes also repoint `implement`'s bare `/tdd` and `/code-review` prose — now as
neutral-space model-edges plus `depends_on` entries on `implement`; the linker verifies the
result mechanically.

**Also pending, smaller:** a ten-minute TUI round with the user (Claude-side pointer wording;
brainstorming's new `both` behaviour in a live session — method: `docs/agents/harness-probing.md`),
and a short design talk for the curation sanity panel (ROADMAP item 6 — advisory judgement
subagents, never a gate).

## Mandatory Grounding (read in this order)

1. `AGENTS.md` — hard rules now include the provenance ban and the runtime-integrity promise.
2. `docs/ROADMAP.md` — item 1 is the mixes; Known Gaps carries the review's fix-later list.
3. `docs/adr/0008-references-are-symbols.md` and the merged_from section of `0001` — the
   machinery every edit now lives on; `0005`, `0006`, `0007` behind them.
4. `docs/ledger.json` — read the real edges before proposing anything; it is the measured map.
5. `curation/deniz-process.yaml` — the decision record; note the reference-spelling and
   depends_on patterns on existing items.
6. `docs/research/skill-framework-landscape.md` (the fused-vs-split and overlap tables feed the
   mixes) and `skill-invocation-across-harnesses.md`.

## Locked Policy Recap

- `external/`, `plugins/`, `opencode/` never hand-edited; output (including `docs/ledger.json`)
  regenerates and is committed; `npm run inventory` in the same commit as any curation change.
- Curation decisions are the user's, and both failure modes are rejected: no silent
  pre-decisions, no item-by-item question loops — one holistic, opinionated proposal with the
  genuine debates flagged; he reacts to the whole, and when he pushes back the push contains the
  design.
- References: neutral-space upstream addresses only; `ns:name` model-edge (target `auto`/`both`),
  `/ns:name` pointer (target `manual`/`both`); a body edit that adds a reference carries its
  `depends_on` edit in the same commit. Never write output-space (`deniz-*:`) or bare names into
  authored bodies.
- Merges: `merged_from` + bless on day one; a rule and the data satisfying it land as one commit;
  gates green on every commit.
- Provenance: no curator names, no dates in the curation layer — `validate` enforces it.
- Compile-time boundary stands, with its sharpened second half: what a skill *does* is
  upstream's design; what it *needs* is machine-checked — do not hand-wave a reference, the
  linker will catch it.
- Subagent dispatch: Opus 5 implementers; commit messages via plain bash heredoc (a PowerShell
  here-string through the Bash tool mangled one subject this wave); exec bits via
  `git update-index --chmod=+x` on built script copies.

## Final Steering Note

The natural rhythm: one editorial session per mix — read both upstream bodies and the recorded
intent, bring a complete merged draft with the contradictions framed as decisions, let the user
rule, then eject → bless → declare (`merged_from`, `depends_on`) → gates → one commit. The TUI
round can open or close either session. After the mixes, `deniz-process` is fully closed and the
dotnet modules follow, one per session, batch-1-style. The user thinks by talking, in prose;
reflect understanding back before executing. The machinery is done — treat its errors as
information, never as obstacles.
