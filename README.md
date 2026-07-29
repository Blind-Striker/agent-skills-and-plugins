# deniz-skills

Personal multi-harness skill/plugin marketplace. Upstream skill repos live as
submodules in `external/`; curation manifests in `curation/*.yaml` select and
customize what gets packaged into the `deniz-*` Claude Code plugins in
`plugins/` and the OpenCode output in `opencode/`.

## Docs

| Document | Contents |
|---|---|
| [AGENTS.md](AGENTS.md) | Canonical contract for agents working in this repo (Claude Code reads it via [CLAUDE.md](CLAUDE.md)) |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Current state, next steps, known gaps |
| [docs/adr/](docs/adr/) | Architecture decisions and why |
| [docs/research/](docs/research/) | Harness and integration notes |
| [docs/inventory.md](docs/inventory.md) | Generated catalog of everything upstream offers |
| [design spec](docs/superpowers/specs/2026-07-29-skills-plugin-repo-design.md) | Original design (Turkish) |

## Rules

- Never hand-edit `external/`, `plugins/`, or `opencode/`.
- Your world: `curation/` (what to take, how to tweak), `overlays/` (full-file
  body edits), `skills/` (original skills).
- No curation decision without the catalog: `npm run inventory` →
  `docs/inventory.md`.

## Setup

Requires Node.js >= 24 (the tooling runs TypeScript directly via `node`).

```bash
git clone --recurse-submodules <this repo>
npm install
```

Already cloned without submodules? `git submodule update --init --recursive`.
The build, validate and inventory commands all read `external/`, so the
submodules must be checked out.

## Commands

| Command | Purpose |
|---|---|
| `npm run build` | Compile manifests + overlays + own skills into `plugins/` and `opencode/` |
| `npm run inventory` | Regenerate `docs/inventory.md` catalog |
| `npm run eject <plugin> <name>` | Copy an item to `overlays/` for body editing |
| `npm run sync [submodule]` | Update submodule(s), report impact on curated items |
| `npm run validate` | Check sources, frontmatter, collisions, dangling refs, marketplace |
| `npm test` | Run the tooling test suite |
| `npm run typecheck` | Type-check `tools/` with `tsc --noEmit` |
| `npm run lint` / `npm run format` | Biome lint / format (submodules and build output excluded) |

`npm run format:check` is the non-writing variant of `format`, used by CI.

Build output is committed. CI rebuilds and fails if `plugins/`, `opencode/`,
`.claude-plugin/` or `docs/inventory.md` differ from what is checked in, so run
`npm run build && npm run inventory` and commit the result with any curation
change.

## Consuming

Claude Code: `/plugin marketplace add <this repo>` then install `deniz-*`
plugins. Once a `deniz-*` plugin covers an upstream source, uninstall the
upstream plugin (avoid duplicate similar skills).

OpenCode: point OpenCode at the `opencode/` tree (skills are the open
SKILL.md standard; commands/agents are OpenCode markdown).
