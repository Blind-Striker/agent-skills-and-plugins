# Roadmap

Date: 2026-07-30

Operational document: what is done, what is next. It shrinks as work lands. How things work and why
lives in [AGENTS.md](../AGENTS.md) and [docs/adr/](adr/).

## Current State

- Toolchain complete: `build`, `inventory`, `eject`, `sync`, `validate` in `tools/`; all gates green
  (`npm test`, typecheck, Biome lint + format).
- Repo lives on GitHub as `Blind-Striker/agent-skills-and-plugins`, private, default branch `master`.
  CI (`.github/workflows/validate.yml`) runs the gates plus build, inventory and validate, then fails
  if committed build output is stale.
- Upstream repos vendored in `external/`; `docs/inventory.md` (regenerate via `npm run inventory`)
  catalogs what they offer.
- Four starter plugins built and committed (`deniz-process`, `deniz-dotnet-general`,
  `deniz-dotnet-aspire`, `deniz-dotnet-akka`), one pipeline-proof skill each, plus the matching
  `opencode/skills/` output and `marketplace.json`.
- `npm run validate` on the current build: 0 errors; remaining warnings are unrewritten
  cross-references to uncurated superpowers skills.
- The cross-reference rewrite path has never run on real data — no starter pick references another
  pick, so it is proven by unit tests only. The first real curation batch exercises it.
- Body edits come in both kinds (ADR-0001): `body: patch` applies `overlay.patch`, `body: overlay`
  replaces whole files, and both are hash-blessed through `overlays/overlays.lock.json`. Exercised
  end to end against real upstream skills, but no curated item uses either yet — `overlays/` does
  not exist. The gaps two independent reviews left open are listed below.
- `validate` covers the overlay wiring the build cannot see: an overlay directory that no item
  claims, or whose item declares no `body:`, is an error (the build would ship pristine upstream in
  silence); a lock entry without its directory, and a cut patch still sitting beside a working copy,
  are warnings. Item resolution lives once, in `tools/lib/resolve.ts`, so a per-item rule can no
  longer be added to one side of the build only.

## Next Up

1. **Implement invocation intent** (ADR-0005). Add the item-level `invocation: model | user | both`
   field, derive `disable-model-invocation` / `user-invocable` in the Claude emitter and the
   skill-vs-command choice in the OpenCode emitter, drop `as: command`, and extend `validate` for the
   new collision class. Needed before or alongside the first curation session, since "the user starts
   this" is otherwise inexpressible. Verify against a live install first that
   `disable-model-invocation: true` still permits the user's own slash invocation.
2. **Per-module curation sessions.** Each manifest currently holds a single starter item. Fill them
   module by module against `docs/inventory.md`, with the user, one plugin per session. The
   intended upstream sources of each module are noted at the top of its manifest.
   `docs/research/skill-framework-landscape.md` is the standing input; the why of each decision goes
   beside the item.
3. **Aspire router repair.** `deniz-dotnet-aspire` ships the upstream `aspire` skill, a router whose
   five targets are not curated. The misdirection sits in two different places, and they are worth
   separating: the frontmatter `description` ends `INVOKES: aspire-init, aspireify,
   aspire-orchestration, aspire-deployment, aspire-monitoring`, which is the part injected into the
   system prompt and therefore the part that actually misleads a model; the body's routing table
   points at upstream GitHub URLs, which resolve for a human reader, but labels three rows
   "(in-plugin)" when they are not. So the options are wider than eject-and-rewrite: curate the
   targets, or override `frontmatter.description` alone, or own the body. Bare-name references are
   invisible to `validate`.
4. **Public release decision.** The repo is private today. Going public needs a deliberate pass:
   fix or pull the aspire router, and decide whether `marketplace.json`'s embedded owner name and
   email (format-required) may go public. Until then, do not install `deniz-dotnet-aspire`.
5. **Wire OpenCode on a real machine.** `opencode/` is emitted but has never been loaded by OpenCode.
   Link the tree into an OpenCode config and confirm skills, commands and agents resolve; research
   notes go to `docs/research/`, agent-facing operational findings to `docs/agents/README.md`.

## Known Gaps

- **`.agent.md` double-extension addresses.** `addressOf` in `tools/lib/rewrite.ts` strips only
  `.md`, so an upstream agent file named `<name>.agent.md` gets a rewrite-map key ending in
  `.agent` while harness references spell the bare name — such a reference would not be rewritten.
  Affected files: `find external -name "*.agent.md"`. No impact on what is built today; fix before
  any agent curation.
- **Upstream noise in output.** Author-facing and test files travel with curated skills (creation
  logs and pressure-test files in `systematic-debugging`, for instance). Per-item exclude patterns
  in the manifest are the candidate fix.
- **Doubled validate warnings.** `validate` scans both `plugins/` and its byte-identical
  `opencode/` mirror, so every real unrewritten reference is reported twice. Superpowers skills
  cross-reference heavily; a partial curation batch could produce dozens of lines. Candidate fix:
  scan `plugins/` only, or group per reference.
- **Case-sensitive reference scan.** The unrewritten-reference pattern is lowercase-only by design
  (avoids prose false positives), so a `Superpowers:Foo` spelling slips through — alongside the
  bare-name and relative-path blindness catalogued in
  [upstream-repo-layouts.md](research/upstream-repo-layouts.md#superpowers), which is the larger
  hole: only the namespaced spelling is visible to `validate` at all.
- **Scanner blind spots.** Commands and agents are discovered only directly under a
  `commands/`/`agents/` directory, so upstream subdirectory grouping is silently missing from the
  inventory; conversely such a directory nested inside a skill is double-counted as a standalone
  component.
- **Own-skill collision false negative.** An original skill in `skills/` silently overwrites a
  curated skill of the same name in the same plugin — own skills are emitted last, and `validate`'s
  duplicate check only errors across plugins.
- **`--bless` shows nothing before it stamps.** ADR-0001 calls re-blessing deliberate, but the
  command re-records whatever is on disk without displaying what changed — and the previous blob
  SHA is right there in the lock. Print the diff, and require confirmation.
- **File modes are invisible to both overlay guards.** `git hash-object` hashes content, and
  patches are cut with `core.fileMode=false`, so upstream flipping a bundled script's executable
  bit produces no signal. Related to the executable-bit item below; the same `git ls-files` read
  fixes both.
- **A patch cannot touch anything at or beyond a symlink.** `git apply` refuses such paths outright;
  `aspire-skills` carries symlinks inside curated skills. Undocumented in ADR-0001.
- **`sync` still mislabels a deleted or renamed source** as "auto-updated on next build", while the
  next build fails with `source not found in external/` — the same class as the patch mislabel
  already fixed.
- **Manifest overrides have no staleness guard.** Overlays are guarded — a patch stops applying, a
  full-file overlay's recorded hash stops matching — but a `frontmatter.description` override
  written for one upstream body keeps being applied after upstream rewrites that body, with nothing
  to notice. Same class as overlay drift, rarer, and unsolved.
- **Inventory truncates descriptions at 140 characters** with no ellipsis marker — many rows cut
  mid-sentence, so curation sessions must open the upstream file to judge an item.
- **Biome checks generated `marketplace.json`.** `biome.json` excludes the other build output but
  not `.claude-plugin/`; `format:check` passes only because the emitter happens to write
  Biome-compatible JSON. Candidate fix: add `!.claude-plugin` to the excludes.
- **`dotnet-agent-skills` is pinned at a nightly-adjacent tag** (`skill-validator-nightly-*`),
  unlike the other four submodules, which sit on releases. Hold or move is an open decision for the
  next `npm run sync`.
- **Dead `invocable:` metadata in output.** Upstream `dotnet-skills` sets `invocable: true|false`,
  which is not a frontmatter field in either target harness, and it travels into our output
  untouched. Strip it during curation, or leave it as harmless noise — a decision, not a bug.
- **`hooks.include` unimplemented.** The build throws on a non-empty list. Deliberate: no upstream
  hook is wanted yet (this is how the superpowers session-start behaviour stays off). Implement only
  when a specific hook is.
- **Re-serialization drift.** Frontmatter is parsed and re-emitted, so a passthrough skill is never
  byte-identical to upstream. Harmless, but it adds noise to `npm run sync` diffs.
- **Executable-bit parity.** Windows checkouts cannot see the exec bit; any newly curated skill
  bundling a script needs `git update-index --chmod=+x` on its built copies once, or CI's freshness
  gate fails on the mode diff. Candidate automation: build reads upstream modes via git ls-files.

## Deferred

Out of scope until there is a concrete need: OpenCode agent permission mapping; Codex, Cursor and
Gemini outputs; automated or scheduled upstream sync (`npm run sync` stays manual).

- **Docs-structure template skill.** Package this repo's documentation structure (single canonical
  home + relay principle, evergreen/operational split, audience-based placement, ADR and roadmap
  skeletons) as an original skill under `skills/` so any repo can adopt it. The skill form looks
  right, but the idea needs its own brainstorming session before any work starts.
