# S3 — The Harnesses Were Measured, and the Machinery Finished

Date: 2026-07-31

You are entering after a wave that stopped guessing. Both target harnesses were measured in a
throwaway lab — invocation flags, discovery paths, frontmatter tolerance, name collisions, file
reference resolution — and the two features those measurements were blocking were then implemented:
the `invocation` field (ADR-0005) and a real OpenCode skill adapter (ADR-0006 axis 3).

The single most important state observation: **there is no longer a measurement or a decision
standing between this repo and its first curation session.** Every manifest still holds exactly one
pipeline-proof starter item, and `overlays/` still does not exist. The machinery is finished; the
product has not been started.

## First Principle

> Treat every claim here as **current-as-of-authoring (2026-07-31)** and verify against the live
> repository — `git log --oneline -12`, `npm test`, `node tools/validate.ts` — before acting. Three
> claims in `docs/research/` were wrong this wave, each written from inference and killed by a
> measurement that cost less than the sentence had.

## The machinery, end to end

This is the part worth reading slowly, because most wrong proposals come from an incomplete picture
of it.

### The pipeline

```
external/<upstream>/                     git submodule, read-only, never hand-edited
     │   cpSync   − symlinks   − omit globs
     ▼
plugins/<plugin>/skills/<name>/          ← overlays apply HERE, to the emitted copy
     │   body: overlay  → overlay files copied over the top
     │   body: patch    → git apply overlay.patch
     │   frontmatter    → upstream ∪ item.frontmatter ∪ invocation keys ∪ forced name
     ▼
emitOpenCode()                           ← reads the PRISTINE plugin tree, before any rewrite
     │   skills          → frontmatter filtered to OpenCode's keys, every drop reported
     │   invocation:manual → command emitted; bundle parked at skills/<name>/ with no SKILL.md
     │   commands/agents → frontmatter reduced to what OpenCode understands
     ▼
rewriteTree(plugins/,  claude map)       superpowers:tdd → deniz-process:tdd
rewriteTree(opencode/, opencode map)     superpowers:tdd → tdd
```

Three things about this shape are easy to get wrong:

- **An overlay is not a stage.** It is applied *on top of the emitted copy*, after `omit` and before
  frontmatter. There is no separate "overlay tree" that gets merged later.
- **OpenCode is emitted before the Claude rewrite, deliberately.** It used to be copied afterwards,
  which is how OpenCode ended up carrying `<plugin>:<name>` references it cannot resolve. Each tree
  is now rewritten with its own map. Do not move that call back.
- **The two output trees are no longer byte-identical, and that is the goal**, not a bug. If a change
  makes them identical again, axis 3 has stopped running.

### Which mechanism, when

The full ladder with costs is in [ADR-0001](../../adr/0001-submodule-manifest-overlay-architecture.md).
The rule in one line: **take the lowest rung that says what you mean**, because the two body rungs are
the only ones that cost anything when upstream moves.

| Intent | Reach for | Why not an overlay |
|---|---|---|
| Do not take it | `exclude: true` | — |
| Drop some files | `omit:` globs | an overlay to delete a file is a full-file copy plus a hash blessing |
| Reword a description | `frontmatter:` override | an overlay of the **body cannot reach the description** — and the description is what steers auto-invocation |
| Rename | `name:` | — |
| Change who triggers it | `invocation:` | — |
| Change what artifact it is | `as:` | — |
| Surgical body edit | `body: patch` | this *is* the right rung |
| Rewrite most of a file | `body: overlay` | the escape hatch; you now own the file and forgo upstream improvements |

Both body rungs are hash-blessed through `overlays/overlays.lock.json`. Any upstream change to a
file the overlay touches **fails the build** until `npm run eject -- <plugin> <item> --bless`. That
friction is the design, not an oversight — `git apply` is not a staleness guard, because it matches
hunk context at an unbounded offset and will silently relocate a hunk or land it on the wrong
identical passage, exiting 0 both times.

### What each command is for

| Command | Purpose |
|---|---|
| `npm run build` | manifests + overlays + own skills → `plugins/` and `opencode/` |
| `npm run inventory` | regenerate `docs/inventory.md` — the catalog no curation decision may skip |
| `npm run eject -- <plugin> <item> [--patch] [--bless] [--force]` | create or re-bless an overlay. **npm eats the flags without the `--` separator** |
| `npm run sync [submodule]` | move submodule pins, report the impact on curated items |
| `npm run validate` | sources, frontmatter, collisions, dangling refs, overlay wiring, exec bits, marketplace |

## What Just Happened

| Concern | What changed | Verification |
| --- | --- | --- |
| Purpose was invisible | Every agent formed a "pick and copy" model, because `AGENTS.md` opened by calling the manifests a thing that "selects and customizes". **ADR-0006** now states the contract — output is a *transformation* along three axes, each resolved per harness — and `AGENTS.md` leads with it in six lines | Read `AGENTS.md` first; if your model of this repo is subsetting, re-read it |
| Harness behaviour | Both harnesses measured in a lab: Claude's three frontmatter states, OpenCode's three mount points, precedence, frontmatter tolerance, `@file` resolution, name collisions, tool vocabulary | All in `docs/research/skill-invocation-across-harnesses.md`, with versions |
| `invocation` | Implemented. `auto \| manual \| both`; absent means passthrough. Claude gets a frontmatter flag, OpenCode a choice of artifact | RED tests first; exercised on real upstream — with no field set the output is byte-identical to before |
| OpenCode adapter | The skill path adapts instead of mirroring: frontmatter filtered and drops reported, references spelled per harness | Immediately caught `invocable:` leaking into two curated skills |
| `omit:` and the exec bit | Items can shed upstream files; `validate` errors when a built copy of an upstream-executable file is recorded non-executable | Exercised on real upstream, both directions |
| Overlay wiring | `validate` errors on an overlay no item claims or whose item declares no `body:` — the silent bypass that shipped pristine upstream | Reproduced on a real skill: build said "complete", the edit reached nothing |
| Naming | Decided **no plugin prefix** on OpenCode names, and recorded why in ADR-0002 with the measurement (zero name collisions across the five vendored repos) and what it forfeits | — |

## Current State You Should Assume Until Rechecked

- **HEAD** (`master`): the `docs: the mechanism ladder…` commit. Pushed, CI green.
- **Tests:** 70 passed. **Validate:** 0 errors, 4 warnings (2 unrewritten `superpowers:*` refs × 2
  output trees — those two skills are uncurated, so there is nothing to rewrite them to).
- **Curation state:** nothing curated. Starter picks only: `systematic-debugging`,
  `csharp-coding-standards`, `aspire` (hollow router — see roadmap), `akka-best-practices`.
  `overlays/` does not exist; both overlay kinds are exercised only by tests and reverted experiments.
- **The lab** lives outside this repo and is disposable. `docs/agents/harness-probing.md` describes
  how to rebuild one anywhere; it does not depend on the old location.

## Recommended Next Step

**The `deniz-process` curation session.** It is the only thing left that the machinery was built for,
and it cannot proceed without the user — every take, skip and modify is theirs.

Pre-flight, in this order:

1. `npm run inventory`
2. `docs/research/skill-framework-landscape.md` — what the two frameworks are each good at
3. `docs/research/upstream-repo-layouts.md` — the superpowers coupling graph. **Regenerate it**; the
   recorded version was wrong once already because it counted only namespaced references
4. `docs/research/skill-invocation-across-harnesses.md` — the harness matrix and the composition
   pattern mattpocock documents

All 36 candidate skills (superpowers' 14, mattpocock's 22 promoted) were read in full this wave. The
findings that survived are in the research notes; the reading itself is not repeated there, so expect
to open files during the session.

## Locked Policy Recap

- `external/`, `plugins/`, `opencode/` are never hand-edited; regenerate via `npm run build`.
- No skill-by-skill curation decision without the catalog, and **none without the user** — including
  "obvious" picks. The why goes beside the item in the manifest, at the moment it is decided.
- Run the full gates before calling anything done, and **prefer verifying on real upstream data**.
- Intended behaviour changes get an ADR. ADRs are editable in place — the user prefers a direct
  rewrite to an amendment note, and has said so explicitly. Three were rewritten this wave.

## What I Would Tell You If We Overlapped

**Measure. Do not infer.** I put three wrong claims into `docs/research/` this session — that
OpenCode does not read `.claude/skills/` (it does; my shell had a disabling variable set), that
upstream bodies are "full of Claude tool names" (four hits across 206 files, all of them C#'s
`Task`), and that installing our plugins leaks them into OpenCode (it does not; plugins land under
`plugins/cache/`, which is not on OpenCode's path). Each took minutes to measure and each had already
been committed. `docs/agents/harness-probing.md` exists because of them; its Traps section is a list
of my own mistakes.

**Prefer the harness's own introspection.** `opencode debug skill|config|paths` answers discovery
questions for free and deterministically. Every early round asked a model what it could see, which is
slower, costs tokens, and depends on the model reporting honestly.

**Do not ask the user which experiment to run.** They pushed back on exactly that, and they were
right: the experiments are determined by what the build needs next. Deriving them from the design is
the work, not a question to delegate.

**The user's own transcripts are a source.** A curation leaning everyone assumed was lost turned out
to be recorded verbatim in a past session. They live under `$CLAUDE_CONFIG_DIR/projects/<slug>/*.jsonl`
— *not* `~/.claude/`, which is a different account. Search them before concluding something is gone.

**One insight worth carrying into curation.** `manual` makes an item structurally unreachable by the
model — Claude filters it out of the list the model can name at all, so its description never gets
the chance to persuade anything. That means stripping superpowers' coercive trigger prose is a matter
of **taste, not safety**. A large part of the earlier curation anxiety dissolves once that is clear:
the "1% chance you MUST invoke" text cannot fire on an item the user holds the trigger for.

**The rhythm.** The user thinks by talking and wants prose, not forms. Two of this wave's best
decisions came from them pushing back — "don't bend the need to fit the ADR, bend the ADR" (which
produced the passthrough default, and prevented every curated skill from silently becoming
model-only), and a passing remark about the CLI's home directory that solved an isolation problem two
rounds of probing had not. Reflect understanding back, put options with a recommendation, and let
them decide.
