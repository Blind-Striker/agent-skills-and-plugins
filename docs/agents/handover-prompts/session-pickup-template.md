# Session Pickup Prompt — Template

Date: 2026-07-30

> **How to use:** at the end of a session that closed a non-trivial wave, copy this file to
> `s<N>-<short-summary>.prompt.md` (e.g. `s1-toolchain-complete.prompt.md`). Replace every
> `{{placeholder}}`. Drop any section that has no content. Useful pickups are 60-150 lines.
>
> **When NOT to write one:** documentation-only edits, single-bug fixes, routine submodule syncs.
> Pickups are for toolchain waves, curation batches, multi-commit migrations, or genuinely
> stateful in-flight work the next session needs primed context to continue.

---

## Body skeleton

### Opening paragraph

One paragraph: what just landed (wave + sha + date) and the single most important state
observation. No fluff.

### `## First Principle`

> Treat every claim here as **current-as-of-authoring (`{{date}}`)** and verify against the live
> repository: `git log --oneline -5`, `npm test`, `node tools/validate.ts`, and the canonical
> documents — before acting.

Stays nearly verbatim across pickups — the standing reminder that pickup prompts go stale.

### `## What Just Happened`

The change ledger. For each distinct concern: **what changed** (paths, commands, interfaces),
**why** (one line, anchored to an ADR or ROADMAP item), **verification** (what was actually run,
and what was not). Prefer tables for mechanical waves. Longest section; no padding.

### `## Current State You Should Assume Until Verified`

Concrete, verifiable values only:

- **HEAD** (`{{branch}}`): `{{sha}}` — `{{commit-summary}}`
- **Tests:** `{{N passed | "not run this session"}}` — **Validate:** `{{errors/warnings}}`
- **CI:** `{{green | failing on X}}`
- **Curation state:** `{{which manifests hold what; which sessions are done/pending}}`
- **Local-only state:** `{{anything that does not travel with the repo}}`

### `## Recommended Next Step`

One to three numbered options, each with: name + size classification, pre-flight reads, concrete
files to touch, acceptance criteria. End by asking the reader to confirm the choice. **Curation
decisions belong to the user — never pre-decide skill selections in a pickup.**

### `## Mandatory Grounding (read in this order)`

1. `AGENTS.md` — the contract (hard rules, documentation hygiene).
2. `docs/ROADMAP.md` — status, next steps, known gaps.
3. `docs/adr/` — the decision behind whatever you are about to change.
4. `docs/research/harness-adapters.md` — harness adapter guide.
5. `docs/inventory.md` — regenerate before any curation talk (`npm run inventory`).
6. The relevant `curation/*.yaml` and `tools/` files for the scope.

Skip entries the next session does not need.

### `## Locked Policy Recap`

The invariants most tempting to violate in the upcoming work — not a mirror of `AGENTS.md`:

- `external/`, `plugins/`, `opencode/` are never hand-edited; output regenerates via
  `npm run build` and is committed.
- No skill-by-skill curation decision without `docs/inventory.md`, and none without the user.
- Verify before claiming: run the gates (`npm test`, `npm run typecheck`, `npm run lint`,
  `npm run format:check`, `npm run build`, `npm run validate`) before calling work done.
- Docs update in the same change as the code; planning scratch is deleted at merge.
- A bundled script needs its executable bit set via `git update-index --chmod=+x` on built copies
  (Windows checkouts cannot see the bit; CI's freshness gate will catch you).

### `## Final Steering Note`

One or two closing paragraphs: the natural rhythm for the next session, not a mandate. Short and
specific.

---

## Authoring discipline

- **Verify before claiming; anchor to commit SHAs.** Future agents treat your claims as current.
- **Separate "built" from "ran".** State which is which, every time.
- **Drop sections that do not apply.** No "TBD" lines.
- **Do not re-derive `AGENTS.md`.** Point at it.
- **Sign off with one concrete recommendation**, plus alternatives — not five parallel futures.
- **Update `docs/ROADMAP.md` in the same change.** The pickup is the deep handover; the roadmap is
  the permanent index.
