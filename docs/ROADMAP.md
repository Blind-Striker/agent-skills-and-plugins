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
- Four plugins built and committed (`deniz-process`, `deniz-dotnet-general`,
  `deniz-dotnet-aspire`, `deniz-dotnet-akka`), plus the matching `opencode/` output and
  `marketplace.json`. `deniz-process` carries its first real batch — sixteen items decided with
  the user (2026-07-31), invocation set per item, exercising the rewrite path on real data
  (`deniz-process:*` in the Claude tree, bare names in the OpenCode one). The three dotnet
  modules still hold one pipeline-proof starter each.
- `npm run validate` on the current build: 0 errors; remaining warnings are unrewritten
  cross-references to uncurated superpowers skills.
- Body edits come in both kinds (ADR-0001): `body: patch` applies `overlay.patch`, `body: overlay`
  replaces whole files, and both are hash-blessed through `overlays/overlays.lock.json`. One real
  overlay exists: systematic-debugging's merged SKILL.md — scoped to that one file, so the
  sibling technique files keep flowing from upstream. The gaps two independent reviews left open
  are listed below.
- `validate` covers the overlay wiring the build cannot see: an overlay directory that no item
  claims, or whose item declares no `body:`, is an error (the build would ship pristine upstream in
  silence); a lock entry without its directory, and a cut patch still sitting beside a working copy,
  are warnings. Item resolution lives once, in `tools/lib/resolve.ts`, so a per-item rule can no
  longer be added to one side of the build only.
- Items declare who pulls the trigger with `invocation: auto | manual | both` (ADR-0005). Absent
  states no intent and passes upstream frontmatter through unchanged. Claude Code gets a frontmatter
  flag; OpenCode gets a choice of artifact, and a `manual` item's bundled files are parked under
  `opencode/skills/<name>/` without a `SKILL.md` so its command body can still reach them.
- The OpenCode skill path adapts instead of mirroring (ADR-0006 axis 3): frontmatter is filtered to
  the keys OpenCode recognises with every drop reported, and each tree is rewritten with its own
  reference spelling — `<plugin>:<name>` for Claude Code, the bare name for OpenCode. The two trees
  are no longer byte-identical, which is the point.
- Items can shed upstream files with `omit:` (glob patterns, ADR-0001) instead of owning the whole
  file through an overlay. `validate` warns on a pattern that matches nothing and on `omit` under a
  conversion, and the build refuses a pattern that swallows a file the patch edits. It also errors
  when a built copy of an upstream-executable file is recorded non-executable — the Windows
  checkout failure that otherwise surfaces as an opaque CI staleness diff.

## Next Up

1. **Per-module curation sessions.** `deniz-process` is closed but for two merge passes
   (batches 1+2, 2026-07-31): every superpowers and mattpocock-promoted candidate has an answer
   in the manifest except matt's `tdd` and `code-review`, which await their mixes — a TDD mix
   (superpowers test-driven-development × matt tdd) and a review mix (requesting-code-review ×
   matt code-review), each in the systematic-debugging mold, each also repointing `implement`'s
   bare `/tdd` and `/code-review` prose. The three dotnet modules still hold one starter each;
   fill them against `docs/inventory.md`, with the user, one plugin per session.
   `docs/research/skill-framework-landscape.md` is the standing input; the why of each decision goes
   beside the item.
2. **Aspire router repair.** `deniz-dotnet-aspire` ships the upstream `aspire` skill, a router whose
   five targets are not curated. The misdirection sits in two different places, and they are worth
   separating: the frontmatter `description` ends `INVOKES: aspire-init, aspireify,
   aspire-orchestration, aspire-deployment, aspire-monitoring`, which is the part injected into the
   system prompt and therefore the part that actually misleads a model; the body's routing table
   points at upstream GitHub URLs, which resolve for a human reader, but labels three rows
   "(in-plugin)" when they are not. So the options are wider than eject-and-rewrite: curate the
   targets, or override `frontmatter.description` alone, or own the body. Bare-name references are
   invisible to `validate`.
3. **Public release decision.** The repo is private today. Going public needs a deliberate pass:
   fix or pull the aspire router, and decide whether `marketplace.json`'s embedded owner name and
   email (format-required) may go public. Until then, do not install `deniz-dotnet-aspire`.
4. **Machine migration to single source of truth.** The end state (user, 2026-07-31): every
   globally installed skill set on this machine — Claude-side plugins, OpenCode's superpowers
   package, `~/.config/opencode/skills/`, `~/.agents/skills/` — is uninstalled as its `deniz-*`
   replacement lands, leaving this repo as both harnesses' only skill source. Staged, not
   big-bang. First in line: the OpenCode superpowers package (`plugin:` entry in
   `~/.config/opencode/opencode.json`), because measured precedence (package cache >
   `OPENCODE_CONFIG_DIR` mount > global `.config` skills) means it shadows curated output for
   every colliding name — the one global our tree cannot shadow away.
5. **OpenCode installer — parked (2026-07-31), deliberately not a side quest yet.** The user's
   model is an installer, symmetric to the Claude marketplace; a permanent `OPENCODE_CONFIG_DIR`
   is rejected (env vars are for throwaway tests, consumption goes through a native mechanism).
   Candidates, in current order: (a) OpenCode's own package mechanism (`opencode.json` `plugin:`
   list — measured today: package cache tops discovery precedence; feasibility of a git-subdir
   ref for `opencode/` unmeasured), (b) a switchboard-style installer — prior art in the user's
   `opencode-switchboard` repo (`tools/install.py`): hash-compared sync, manifest-owned prune,
   and the known hard part, surgical JSONC merge into the user's existing config behind
   validation gates with backup restore ("config concat" concern), (c) a sync command in
   `tools/` (a deliberate ADR-0004 growth decision). wshobson/agents is the third reference:
   generate harness-native trees, commit only registries. The behavioural half is settled: the
   real tree was mounted three ways and exercised in a TUI on 2026-07-31 — discovery, commands,
   model-mediated composition, parked-bundle reachability and all three invocation surfaces
   verified on real output (`docs/research/skill-invocation-across-harnesses.md`). Only the
   install mechanism remains open.

## Known Gaps

- **Long-body `manual` conversions paste their whole body into the OpenCode chat.** A command is
  a template, and the TUI renders the entire body as the user's message: seven-line grill-me is
  clean, 150-line brainstorming is a wall on every invocation. Cosmetic, not correctness — the
  model follows the pasted body either way. Candidate emitter change: a stub command body that
  points at the body parked beside the bundle, which needs the parked directory to carry the body
  under a non-discoverable name. A decision, with the installer conversation the natural place.
- **Out-of-project bundle reads prompt for folder access** under a config-dir mount — parked
  files live outside the project tree, so the first read asks permission. Whether to
  pre-authorize is an installer-decision detail (config `permission` block).
- **`.agent.md` double-extension addresses.** `addressOf` in `tools/lib/rewrite.ts` strips only
  `.md`, so an upstream agent file named `<name>.agent.md` gets a rewrite-map key ending in
  `.agent` while harness references spell the bare name — such a reference would not be rewritten.
  Affected files: `find external -name "*.agent.md"`. No impact on what is built today; fix before
  any agent curation.
- **Doubled validate warnings.** `validate` scans both `plugins/` and `opencode/`, so every real
  unrewritten reference is reported once per tree. Superpowers skills
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
  bit produces no signal. `validate` now reads `git ls-files` to compare built output against
  upstream, but the overlay *lock* still records content only, so a mode-only upstream change does
  not force a re-bless.
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
- **Dead `invocable:` metadata in the Claude tree.** Upstream `dotnet-skills` sets
  `invocable: true|false`, which is a field in neither target harness. The OpenCode adapter now
  drops and reports it; the Claude tree still carries it, because that tree passes upstream
  frontmatter through and filtering it would need a recognised-key list for Claude Code too. Strip
  it per item during curation, or leave it as harmless noise — a decision, not a bug.
- **`hooks.include` unimplemented.** The build throws on a non-empty list. Deliberate: no upstream
  hook is wanted yet (this is how the superpowers session-start behaviour stays off). Implement only
  when a specific hook is.
- **Re-serialization drift.** Frontmatter is parsed and re-emitted, so a passthrough skill is never
  byte-identical to upstream. Harmless, but it adds noise to `npm run sync` diffs.

## Deferred

Out of scope until there is a concrete need: OpenCode agent permission mapping; Codex, Cursor and
Gemini outputs; automated or scheduled upstream sync (`npm run sync` stays manual).

- **Docs-structure template skill.** Package this repo's documentation structure (single canonical
  home + relay principle, evergreen/operational split, audience-based placement, ADR and roadmap
  skeletons) as an original skill under `skills/` so any repo can adopt it. The skill form looks
  right, but the idea needs its own brainstorming session before any work starts.
