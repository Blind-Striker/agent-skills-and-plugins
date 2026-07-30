# Roadmap

Date: 2026-07-30

Operational document: what is done, what is next. It shrinks as work lands. How things work and why
lives in [AGENTS.md](../AGENTS.md) and [docs/adr/](adr/).

## Current State

- Toolchain complete: `build`, `inventory`, `eject`, `sync`, `validate` in `tools/`, 32 tests green,
  TypeScript 7 typecheck, Biome lint + format.
- CI (`.github/workflows/validate.yml`) runs tests, typecheck, lint, format, build, inventory and
  validate, then fails if committed build output is stale.
- Five upstream repos vendored in `external/`; `docs/inventory.md` catalogs 223 components.
- Four starter plugins built and committed (`deniz-process`, `deniz-dotnet-general`,
  `deniz-dotnet-aspire`, `deniz-dotnet-akka`), one curated skill each, plus the matching
  `opencode/skill/` output and `marketplace.json`.
- `npm run validate` on the current build: 0 errors, 4 warnings (unrewritten cross-references to
  uncurated superpowers skills, counted once per output tree).

## Next Up

1. **Per-module curation sessions.** Each manifest currently holds a single starter item. Fill them
   module by module against `docs/inventory.md`, with the user, one plugin per session.
2. **Aspire router repair.** `deniz-dotnet-aspire` ships the upstream `aspire` skill, which routes by
   bare name to five skills that are not curated yet — a hollow router. Either curate the targets or
   eject and rewrite the body. Bare-name references are invisible to `validate`.
3. **Publish decision.** Public vs private on GitHub is undecided; nothing is pushed. The repo works
   fully locally. Pushing and `claude marketplace add` are user actions.
4. **Wire OpenCode on a real machine.** `opencode/` is emitted but has never been loaded by OpenCode.
   Link the tree into an OpenCode config, confirm skills/commands/agents resolve, write it up in
   `docs/research/`.

## Known Gaps

- **Rewrite-map key mismatch.** Claude Code addresses plugin skills by directory name; the
  cross-reference rewrite map keys on frontmatter `name`. 32 of 223 upstream components have
  divergent names, so a reference to one of them would not be rewritten. No impact on what is built
  today. Re-key on the upstream directory basename before large curation batches.
- **Upstream noise in output.** Author-facing and test files travel with curated skills (5 of 11
  files in `systematic-debugging`). Per-item exclude patterns in the manifest are the candidate fix.
- **`invocable: false` passthrough.** Two emitted skills carry this upstream frontmatter key; confirm
  Claude Code's semantics for it before real curation rather than assuming it is harmless.
- **`hooks.include` unimplemented.** The build throws on a non-empty list. Deliberate: no upstream
  hook is wanted yet (this is how the superpowers session-start behaviour stays off). Implement only
  when a specific hook is.
- **Re-serialization drift.** Frontmatter is parsed and re-emitted, so a passthrough skill is never
  byte-identical to upstream. Harmless, but it adds noise to `npm run sync` diffs.

## Deferred

Out of scope until there is a concrete need: OpenCode agent permission mapping; Codex, Cursor and
Gemini outputs; automated or scheduled upstream sync (`npm run sync` stays manual).

- **Executable-bit parity.** Windows checkouts cannot see the exec bit; any newly curated skill
  bundling a script needs `git update-index --chmod=+x` on its built copies once, or CI's freshness
  gate fails on the mode diff. Candidate automation: build reads upstream modes via git ls-files.
