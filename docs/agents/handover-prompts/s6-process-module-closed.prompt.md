# S6 — Both Mixes Landed; deniz-process Is Closed

Date: 2026-08-01

You are entering after the session that finished the first module. Two editorial merges landed —
the TDD mix and the review mix — and between them the machinery grew twice, both times because the
editorial work walked into a guard that could not cover it. The single most important state
observation: **`deniz-process` is closed and the next work is a different kind of curation.** The
three dotnet modules are single-vendor and overlap by *subject* rather than by job, so the merge
question that shaped this module is probably replaced by a naming-and-scope question. Do not
assume the `deniz-process` playbook transfers.

## First Principle

> Treat every claim here as **current-as-of-authoring (2026-08-01)** and verify against the live
> repository: `git log --oneline -10`, `npm test`, `node tools/validate.ts`, and the canonical
> documents — before acting.

## What Just Happened

Eight commits, `defab5f..35b6205`. Two of them are curation; the rest cleared ground for it or
recorded what the ground now is.

| Concern | What changed | Verification |
|---|---|---|
| **TDD mix** (`cc54b0b`) | superpowers `test-driven-development` × mattpocock `tdd`, `body: overlay` over both files. superpowers keeps the spine (Iron Law, watch-it-fail, rationalization tables); matt's **seam** is installed as its own section and a first node in the cycle, plus **one slice at a time / tracer bullets**. Seams gate is conditional — name it always, put it to the human when consequential — because the item is `auto` and an unconditional round-trip makes a discipline an interrupt. Refactor keeps cleanup-under-green and routes structural work out. matt `tdd` → `exclude: true — merged` | Two items already pointed at "seam" with nowhere to send a reader (`systematic-debugging` Phase 6, `implement`). Both trees rebuilt; linker green |
| **Review mix** (`35b6205`) | superpowers `requesting-code-review` × mattpocock `code-review`, `body: overlay` over `SKILL.md` + `code-reviewer.md`. superpowers brings dispatch, calibration, read-only discipline, output format; matt brings the **Standards axis** (twelve Fowler smells as labelled judgement calls, repo standard overriding), **range pinning** that fails before a subagent burns context, and **spec discovery**. Two-axis separation kept as a *reporting* rule, not a topology: **one reviewer by default, two only when the spec is a genuinely separate document** | Every SDD review has the plan AS requirements — a second agent would read one document twice per range. `code-reviewer.md` keeps its name and path (SDD reaches it from three spots incl. a flowchart) |
| **First pointer edge** | The review mix ships `/mattpocock-skills:setup-matt-pocock-skills` → `/deniz-process:…` (Claude) / `/…` (OpenCode). ADR-0008's pointer tier had **zero** shipped instances until now; it was enforced against fixtures only | Present in both trees and in the ledger |
| **`merged_from` files lists** (`a369fe0`) | An entry may name its own `files:`, replacing the same-filename rule. Written because the TDD mix drew from matt's `mocking.md` — a name our overlay does not own, so the rule guarded nothing. Two new failure modes covered: a list that grew without the source *set* changing (declared, unstamped) errors; a misspelled name warns | ADR-0001 rewritten in place. The alternative — shrinking the merge to fit the guard — is the toolchain deciding curation, which AGENTS.md forbids |
| **Relative paths become checkable** (`6476f6e`, canon in `7510428`) | ADR-0008 had put paths in the candidate tier ("never build state"); they are now a **third tier**. Two narrow rules: a `../<item>/` climb into a sibling must land, and a missing same-directory file is a finding **only when upstream still ships it**. Measured first: a naive link check reports 68 unresolved links, essentially all illustrative prose | Found real breakage immediately — `writing-skills` cited two files `using-superpowers` omits, dead in both trees since batch 2. Patched (`body: patch`) |
| **Parked-warning fix** (`defab5f`) | The build called every `both` item's live skill directory "parked". Most warnings were false; build now agrees with the ledger on the remaining names | Report and ledger cross-checked to the same names |
| **Docs** (`1e9e81c`, `8f31f12`) | README's freshness gate said four paths, CI guards five. The pointer-wording probe lived only in a handover and a local-only lab; now in the research note **with the two details the handover dropped** — the model was grok-4.5, and the bare slash was never probed | — |

## Current State You Should Assume Until Rechecked

- **HEAD** (`master`): `35b6205` — the review mix. **Eight commits ahead of `origin/master`, unpushed.**
  CI has seen none of them.
- **Tests:** 111. **Validate:** 0 errors, **3 warnings**.
- **Curation:** `deniz-process` **closed** — 36 sources, 4 excluded, 10 items declare edges, 35
  ledger entries. Three dotnet manifests still hold one starter each.
- **The 3 warnings are one cause wearing three faces**, and this is a standing decision, not rot: a
  converted OpenCode command is a single file in `commands/`, so nothing written for a skill
  directory reaches from it — not a sibling item's file (`subagent-driven-development` →
  `code-reviewer.md`), not its own parked bundle's `SKILL.md` (`teach`, `writing-great-skills`).
  All three close when the mount-point/installer decision closes. **Tripwire:** if that decision
  keeps slipping, they should be explicitly accepted and suppressed with a recorded reason. A
  warning that outlives its conversation is wallpaper — the same reasoning that killed the false
  parked ones this session.
- **Local-only:** the OpenCode probe lab (`E:\opencode-probe-lab`); the machine still carries the
  global skill installs (ROADMAP migration item).

## Recommended Next Step

**Push first.** Eight commits, zero CI. The module boundary is the natural place.

Then one of three, all open — **the choice is the user's**:

1. **TUI round** (small, ~10 min of his time). Newly worth doing because there is finally something
   real to measure: the first shipped **pointer** on the Claude side, `brainstorming`'s `both`
   behaviour, and how the two merged bodies actually steer a live session. Method:
   `docs/agents/harness-probing.md`.
2. **Curation sanity panel design** (medium, ROADMAP item 6). Its input is now real rather than
   hypothetical — two merged bodies to read against their upstreams and their recorded intent.
   Advisory subagents returning *judgement*, never a gate. Needs a design pass on the panel prompt
   and the presentation shape before anything runs.
3. **First dotnet module** (large). Read the caution in the opening paragraph first.

## Mandatory Grounding (read in this order)

1. `AGENTS.md` — the contract.
2. `docs/ROADMAP.md` — state, next steps, Known Gaps (three of them were rewritten this session).
3. `docs/adr/0008-references-are-symbols.md` — the three tiers, and why some true findings are
   warnings. Then `0001`'s `merged_from` section, then `0005`, `0006`, `0007`.
4. `docs/ledger.json` — the measured map. Read edges before proposing anything.
5. `curation/deniz-process.yaml` — the decision record, now complete. The two merge comments are
   the worked examples of how a mix is justified.
6. `docs/research/skill-framework-landscape.md` and `skill-invocation-across-harnesses.md`.

## Locked Policy Recap

- `external/`, `plugins/`, `opencode/` never hand-edited; output — including `docs/ledger.json` —
  regenerates and is committed; `npm run inventory` in the same commit as any curation change.
- Curation decisions are the user's. Both failure modes are rejected: no silent pre-decisions, no
  item-by-item question loops. One holistic, opinionated proposal with the genuine debates flagged;
  he reacts to the whole, and when he pushes back the push contains the design.
- **Never shrink the work to fit a guard.** When a need and a rule disagree the rule is rewritten
  in place — that happened twice this session and both times it was correct.
- References: neutral-space upstream addresses; `ns:name` model-edge (target `auto`/`both`),
  `/ns:name` pointer (target `manual`/`both`); a body edit that adds one carries its `depends_on`
  edit in the same commit. Paths are checked too now, but only where the build could have broken
  them.
- Merges: `merged_from` on day one, with `files:` when the merge drew from names the overlay does
  not own. A rule and the data satisfying it land as one commit; gates green on every commit.
- Provenance: no curator names, no dates in the curation layer — `validate` enforces it.
- Subagent dispatch: Opus 5 implementers; commit messages via plain bash heredoc (a PowerShell
  here-string mangled one subject in an earlier wave).

## Final Steering Note

Two things this session proved that are worth carrying rather than rediscovering. First, the
editorial work is what finds the machinery's holes — both tooling changes here were provoked by a
merge walking into a guard that could not cover it, and neither would have been found by reading
the code. Second, **measure before building a rule**: the relative-path check looked unfixable
because a naive version reports 68 false positives, and it became a small change the moment the
noise was characterised instead of assumed.

A caution for the dotnet sessions. `deniz-process` had two upstreams competing for the same jobs,
which is why merging was the central act. The dotnet upstreams are single-vendor and overlap by
subject — both `aspire-skills` and `dotnet-skills` cover Aspire under different names — so expect
naming, scope and the aspire router repair (ROADMAP item 2) to dominate instead. The user thinks by
talking, in prose; reflect understanding back before executing.
