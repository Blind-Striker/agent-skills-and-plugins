# S2 — Overlays Hardened and Reviewed, Skill Deep-Dive Started

Date: 2026-07-30

You are entering this repository after a wave that did three things: decided how invocation intent
is expressed across harnesses (ADR-0005, not yet implemented), built and then hardened the body-edit
overlay subsystem through two independent adversarial reviews, and began — but did not finish — the
content comparison between the two process frameworks this repo vendors. Current as of `56c8536`,
pushed, CI green. The single most important state observation: **the toolchain is now trustworthy
enough to express a curation decision, and curation still has not started** — every manifest holds
one pipeline-proof starter item, and `overlays/` does not exist.

## First Principle

> Treat every claim here as **current-as-of-authoring (2026-07-30)** and verify against the live
> repository: `git log --oneline -8`, `npm test`, `node tools/validate.ts`, and the canonical
> documents — before acting.

## What Just Happened

| Concern | What changed | Verification |
| --- | --- | --- |
| Invocation model | Researched both harnesses. OpenCode skills are model-only — a user cannot invoke one — so the same intent is a frontmatter flag in Claude Code and a *different artifact* (a command) in OpenCode. Recorded in `docs/research/skill-invocation-across-harnesses.md`; decided in ADR-0005 (`invocation: model \| user \| both`, `as: command` retired) | Doc + ADR written and reviewed with the user; **no code written** |
| OpenCode output paths | Emitter wrote `opencode/skill\|command\|agent`; OpenCode documents all three plural. Fixed, ADR-0002 corrected | RED test first; build, CI |
| Overlays | `body: patch` added alongside `body: overlay`; `eject` grew `--patch`, `--bless`, an overwrite guard | See below — the first version was broken |
| Overlay hardening | Two independent reviews (one vcpkg-informed, one context-free) found three criticals, all reproduced by hand: `eject` crashed on any skill with a subdirectory (51/96 upstream skills); npm swallows `--patch`/`--bless`/`--force` so every printed remediation was inert and `--patch` silently produced a full-file overlay; patch-cutting silently discarded added and deleted files while deleting the working copy | Each fixed with a RED test first; verified end-to-end on real upstream skills |
| Guardrail redesign | `git apply` was assumed self-checking. It is not: it matches hunk context at an unbounded offset, so a hunk relocates silently, and where a file repeats a passage it lands on the wrong region while the intended one is gone — both exit 0. **Both overlay kinds are now hash-blessed** through `overlays/overlays.lock.json`; the false claim in ADR-0001 is replaced | Wrong-site application reproduced directly; `--verbose` offset parsing was tried and does **not** cover it |
| Skill deep-dive | Structural pass over superpowers (cross-reference graph, sizes, bundled scripts, where the coercive language actually lives) and mattpocock (bucket policy, router trap, deprecations). Content comparison done for **one pair only**: `brainstorming` ↔ `grill-me`/`grilling` | Findings folded into `docs/research/`; three pairs unread |

## Current State You Should Assume Until Verified

- **HEAD** (`master`): `56c8536` — roadmap gaps from the reviews. Pushed.
- **Tests:** 57 passed. **Validate:** 0 errors, 4 warnings (2 unrewritten `superpowers:*` refs × 2
  output trees — the doubling is a Known Gap).
- **CI:** green on `56c8536` (run 30577369992), 57/57 on Linux — the first Linux run of the patch
  machinery. One unrelated annotation: `actions/checkout@v4` and `setup-node@v4` target Node 20 and
  are being forced onto Node 24.
- **Curation state:** nothing curated. Starter picks only: `systematic-debugging`,
  `csharp-coding-standards`, `aspire` (hollow router — Known Gap), `akka-best-practices`.
  `overlays/` does not exist; both overlay kinds are exercised only by tests and by throwaway
  experiments that were reverted.
- **Local-only state:** none.

## Recommended Next Step

1. **Finish the framework comparison, then curate `deniz-process`** (multi-hour, user-driven — the
   user makes every selection). Three pairs remain unread: `systematic-debugging` ↔
   `diagnosing-bugs`, `test-driven-development` ↔ `tdd`, `writing-skills` ↔ `writing-great-skills`.
   Pre-flight: `npm run inventory`, then `docs/research/skill-framework-landscape.md` and
   `upstream-repo-layouts.md` — the latter carries the superpowers cross-reference graph, which
   decides what a skip costs. Acceptance: the user says the module is theirs.
2. **Implement ADR-0005** (small, well-scoped): the `invocation` field, the two emitters, retire
   `as: command`, extend `validate` for the skill+command name collision. Needed before any item
   can say "the user starts this". Verify first that `disable-model-invocation: true` still permits
   the user's own slash invocation on a live install.
3. **Close the two structural overlay gaps** (see ROADMAP Known Gaps): `validate` has no overlay
   awareness at all — dropping a `body:` line from a manifest silently ships pristine upstream with
   every guardrail bypassed — and item resolution is duplicated between `collectProblems` and
   `emitItem`, which is how the last hole got in.

Confirm the choice with the user before starting — option 1 cannot proceed without them.

## Mandatory Grounding (read in this order)

1. `AGENTS.md`
2. `docs/ROADMAP.md` — Known Gaps grew considerably this wave
3. `docs/adr/` — 0001 (overlays; rewritten), 0002 (multi-harness), 0005 (invocation intent)
4. `docs/research/skill-invocation-across-harnesses.md` and `skill-framework-landscape.md`
5. `docs/research/upstream-repo-layouts.md` — read before judging any upstream item
6. `docs/inventory.md` — regenerate first — then `curation/*.yaml` and `tools/` as needed

## Locked Policy Recap

- `external/`, `plugins/`, `opencode/` are never hand-edited; regenerate via `npm run build`.
- No skill-by-skill curation decision without the catalog, and none without the user — including
  "obvious" picks. The why goes beside the item in the manifest.
- Run the full gates before calling anything done, and **prefer verifying on real upstream data**:
  every critical this wave was invisible to a green test suite because the fixtures were too tidy.
- `npm run eject` needs the npm separator: `npm run eject -- <plugin> <item> --patch`.
- A bundled script needs `git update-index --chmod=+x` on its built copies.

## Final Steering Note

The rhythm the user wants is conversational: they think by talking, and they have been waiting since
the project started to work through what they like and dislike in these frameworks. Two of this
wave's best decisions came from them pushing back — the vcpkg-style patch overlay, and catching that
a preference had been attributed to them that they never held. Reflect understanding back, put
options with a recommendation, and let them decide.

One habit worth carrying: when something is claimed to work, run it against a real skill from
`external/`, not only against a fixture. Three separate defects this wave passed a green suite and
died on first contact with real data.
