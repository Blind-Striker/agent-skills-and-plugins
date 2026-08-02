# S1 — Toolchain Complete, Docs Canonicalized, Curation Phase Opens

Date: 2026-07-30

You are an agent entering this repository after its founding arc: the curation toolchain (designed,
built across 14 reviewed tasks, hardened through a final whole-branch review, pushed to the private
GitHub repo `Blind-Striker/agent-skills-and-plugins` with green CI), followed by a
documentation-canonicalization wave — docs restructured around a single-canonical-home principle,
durable knowledge rescued from the planning scratch into ADRs, ROADMAP, research and manifest
comments, and the scratch (`.superpowers/`, `docs/superpowers/`) deleted. Current as of `b6c9a54`
plus the docs-canonicalization commit carrying this file, 2026-07-30. The single most important
state observation: **the machine and its paperwork are finished, but curation has not started** —
every manifest holds one pipeline-proof starter skill that is NOT a curation decision.

## First Principle

> Treat every claim here as **current-as-of-authoring (2026-07-30)** and verify against the live
> repository: `git log --oneline -5`, `npm test`, `node tools/validate.ts`, and the canonical
> documents — before acting.

## What Just Happened

| Concern | What landed | Verification |
| --- | --- | --- |
| Toolchain | `tools/`: build, inventory, eject, sync, validate + libs (frontmatter, manifest, scan, rewrite, preflight). Node 24 native TS, ESM, single runtime dep `yaml`, TS7 typecheck, Biome | test suite and all gates green |
| Submodules | 5 upstream repos under `external/`; scanner hardened for symlink mirrors, bare `plugin.json` namespaces, malformed manifests | scanned against real data |
| First build | 4 `deniz-*` plugins (1 starter skill each) + `opencode/` mirror + `marketplace.json`, committed; deterministic (double-build clean) | run, plus CI rebuild |
| Safety fixes | fail-fast pre-pass before output deletion; token-boundary rewrites keyed on upstream dir names; symlinks never copied into output; submodule preflight; test-discovery guard | RED-first tests for each |
| Repo/CI | private GitHub repo, default branch `master`, `validate.yml` green incl. freshness gate; exec-bit fix for `find-polluter.sh` | CI run 30517929663 success |
| Docs structure | Single-canonical-home + relay principle (ADR-0003 reworked); README a pure front door; no CONTRIBUTING.md by decision; ADR-0004 (minimal toolchain) + `docs/adr/template.md`; third routing table removed | build + validate clean; reviewed with the user |
| Knowledge rescue | Planning scratch mined before deletion: curation-authoring contracts → ADR-0001/0002 consequences; 8 new ROADMAP Known Gaps; intended module sources → manifest header comments; upstream layout traps → `docs/research/upstream-repo-layouts.md` | user-driven session 2026-07-30 |

## Current State You Should Assume Until Rechecked

- **HEAD** (`master`): `b6c9a54` + the docs-canonicalization commit containing this file.
- **Tests:** 40 passed in the founding wave; not run in the docs session (`tools/` untouched).
  **Validate:** 0 errors, 4 warnings (2 unrewritten `superpowers:*` refs × 2 output trees — the
  doubling is itself a Known Gap).
- **CI:** green as of `b6c9a54`; verify after the docs-canonicalization commit lands.
- **Curation state:** nothing curated. Starter picks only: `systematic-debugging`,
  `csharp-coding-standards`, `aspire` (a hollow router — known gap), `akka-best-practices`.
  Each manifest's header comment records its intended upstream sources; every future
  take/skip/modify needs a why-comment beside the item (AGENTS.md hard rule). No curation session
  has happened; the user explicitly wants to make every selection decision in conversation,
  module by module.
- **Local-only state:** none required; everything travels with the repo.

## Recommended Next Step

1. **deniz-process curation session** (multi-hour, user-driven). Pre-flight: `npm run inventory`,
   open the superpowers and mattpocock-skills sections of `docs/inventory.md`, and read
   `docs/research/upstream-repo-layouts.md` first — it names the traps (mattpocock's
   `deprecated`/`in-progress` categories, inventory's truncated descriptions: open the upstream
   SKILL.md when judging an item). Walk skill by skill with the user; record take/skip/modify with
   its why in `curation/deniz-process.yaml`; build, validate, review the diff together.
   Acceptance: user says the module is theirs.
2. **Pre-curation tooling fix** (small, well-scoped — do before any agent/command curation):
   `.agent.md` double-extension address bug in `tools/lib/rewrite.ts` `addressOf` — see ROADMAP
   Known Gaps, which also lists small tool fixes worth batching with it (eject overwrite guard,
   Biome exclude for `.claude-plugin/`).
3. **OpenCode wiring** (optional, independent): load `opencode/` into a real OpenCode config,
   write findings to `docs/research/` + `docs/agents/README.md`.

Confirm the choice with the user before starting — option 1 cannot proceed without them.

## Mandatory Grounding (read in this order)

1. `AGENTS.md`
2. `docs/ROADMAP.md`
3. `docs/adr/` (0001 architecture, 0002 multi-harness, 0003 docs structure, 0004 minimal toolchain)
4. `docs/agents/README.md`
5. `docs/inventory.md` — regenerate first — plus `docs/research/upstream-repo-layouts.md` before
   judging any upstream item
6. `curation/*.yaml`, then `tools/` as needed

## Locked Policy Recap

- `external/`, `plugins/`, `opencode/` are never hand-edited; regenerate via `npm run build`.
- No skill-by-skill curation decision without the catalog, and none without the user — this
  includes "obvious" picks. Record the why beside every item in the manifest.
- Every fact has one canonical home; relays link, they don't restate.
- Verify before claiming: full gates before calling anything done.
- Docs in the same change as code; planning scratch dies at merge.
- Bundled scripts need `git update-index --chmod=+x` on built copies.

## Final Steering Note

The natural rhythm now is conversational, not mechanical: the user has been waiting to talk
through what they like and dislike in superpowers since the project started — the deniz-process
session is that conversation, with the manifest as its transcript. Keep builds and validates
frequent so every decision is immediately visible in the diff, and let the user drive.
