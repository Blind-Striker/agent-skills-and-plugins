# deniz-skills

Personal multi-harness skill/plugin marketplace. Upstream skill repos live as
submodules in `external/`; curation manifests in `curation/*.yaml` select and
customize what gets packaged into the `deniz-*` Claude Code plugins in
`plugins/` and same-named OpenCode Module Bundles in `opencode/`.

Working in this repo — human or agent — start at [AGENTS.md](AGENTS.md): the
contract, and the map of where everything else lives.

For a quick human-facing route through the mature process and general .NET sets, see the
[skills cheatsheet](docs/cheatsheet.md). Its item-level source of truth remains the curation manifests.

## How it works

Authored transformation inputs live in `curation/*.yaml` (what to take and how to customize it),
`overlays/` (patches or owned replacement files), and `skills/` (original skills). `npm run build` regenerates committed `plugins/`, `opencode/`, and `dist/` output; never edit those generated trees
directly.

The generated `docs/inventory.md` is the scanner-visible catalog for the initialized upstream pins.
It is evidence and a starting index for curation, not an absolute substitute for the upstream source
or scanner behavior. Current product mechanics live in
[transformation and emission](docs/architecture/transformation-and-emission.md),
[references and linking](docs/architecture/references-and-linking.md), and
[distribution and installation](docs/architecture/distribution-and-installation.md).

## Setup

Requires Node.js >= 24 (the tooling runs TypeScript directly via `node`).

```bash
git clone --recurse-submodules <this repo>
npm install
```

Already cloned without submodules? `git submodule update --init --recursive`.
Authoring commands that inspect or regenerate from upstream (`build`, `inventory`, `eject`, `sync`,
and `validate`) require initialized submodules and refuse an empty submodule directory. The OpenCode
installer consumes already built Bundles from the checkout or Package; it does not read upstream
worktrees.

## Commands

| Command | Purpose |
|---|---|
| `npm run build` | Compile manifests + overlays + own skills into committed `plugins/`, `opencode/`, and installer `dist/` output |
| `npm run inventory` | Regenerate `docs/inventory.md` catalog |
| `npm run eject -- <plugin> <name>` | Copy an item to `overlays/` for body editing |
| `npm run sync [submodule]` | Update submodule(s), report impact on curated items |
| `npm run validate` | Link the built trees: references resolve and are reachable, `depends_on` matches the bodies, plus sources, frontmatter, collisions, overlay wiring, file modes, marketplace |
| `npm run install:opencode -- <action>` | Plan or Apply global OpenCode Module installation from this checkout |
| `npm test` | Run the tooling test suite (a `pretest` guard fails if the glob stops finding it) |
| `npm run typecheck` | Type-check `tools/` with `tsc --noEmit` |
| `npm run lint` / `npm run format` | Biome lint / general formatting; see `biome.json` for the exact exclusions |

`npm run format:check` is the non-writing variant of `format`, used by CI.
Generated harness output is excluded from the general Biome pass, while `npm run build` formats the
committed installer JavaScript in `dist/`; `biome.json` is authoritative for the exact exclusions.

Build output is committed. CI rebuilds and fails if `plugins/`, `opencode/`, `dist/`,
`.claude-plugin/`, `docs/inventory.md`, or `docs/ledger.json` differ from what is
checked in, so run `npm run build && npm run inventory` and commit the result
with any curation change or submodule bump. The build compiles the installer to
`dist/` and formats that committed JavaScript; consumers do not compile it.
`docs/ledger.json` is the resolved state of every curated item per harness
(invocation, artifacts, references, dropped keys); its diff is how a posture
change — yours or upstream's — shows up in review.

## Consuming

Claude Code: `/plugin marketplace add <this repo>` then install `deniz-*`
plugins. Once a `deniz-*` plugin covers an upstream source, uninstall the
upstream plugin (avoid duplicate similar skills).

### OpenCode from this checkout

The installer composes selected Modules into OpenCode's global Native tree. A mutating action is a
zero-write Plan unless `--yes` is present; `--yes` acquires the lock and recomputes the Plan before
Apply.

```bash
# Plan, then Apply one or more Modules
npm run install:opencode -- install --module deniz-process
npm run install:opencode -- install --module deniz-process --yes

# Or select every Module
npm run install:opencode -- install --all
npm run install:opencode -- install --all --yes

# Inspect, reconcile the persisted Selection, or remove from it
npm run install:opencode -- status
npm run install:opencode -- update
npm run install:opencode -- update --yes
npm run install:opencode -- remove --module deniz-process
npm run install:opencode -- remove --module deniz-process --yes
```

The first installer-owned Apply requires a manual clean start. Uninstall package adapters that
shadow the same names, then move or remove old manually staged copies named by the Plan. Existing
files are Unowned Collisions: there is no force, reset, legacy migration, project-local target, or
JSON-config mutation. Preserve unrelated global configuration and artifacts. Once Install state
exists, do not delete it; restore or resolve any reported Local modification or State drift before
retrying.

### OpenCode from the private Release package

The Release is versioned, not immutable: tag `installer-v0.1.0` pins a target commit, and the recipe
below verifies each download's SHA-256 against the recorded digest before running anything.
Verification detects replacement but does not prevent an authorized re-upload. Authenticated `gh`
downloads the exact npm-format tarball; the repository itself is not installed as a Git package:

```powershell
$download = Join-Path $env:TEMP "deniz-skills-installer-v0.1.0"
New-Item -ItemType Directory -Path $download -Force | Out-Null
gh release download installer-v0.1.0 --repo Blind-Striker/agent-skills-and-plugins `
  --pattern "deniz-agent-skills-0.1.0.tgz" --dir $download
$package = Join-Path $download "deniz-agent-skills-0.1.0.tgz"
$expected = "69532caf101f5626ea652cdb7e1046783b3d64fe79613e4d59f21e95eccb9460"
$actual = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "downloaded package SHA-256 mismatch: $actual" }

npm exec --yes --package $package -- deniz-skills install --all
npm exec --yes --package $package -- deniz-skills install --all --yes
npm exec --yes --package $package -- deniz-skills status
```

Use the same `update`, `remove`, module-selection, Plan, and Apply grammar shown for the checkout.
The installer targets only OpenCode's normal global config root and refuses alternate config-dir
mounts. Current boundaries and lifecycle mechanics are in
[distribution and installation](docs/architecture/distribution-and-installation.md); the dated
[adapter research](docs/research/harness-adapters.md) and
[experiment protocol](experiments/harness-invocation/protocol.md) retain measured evidence and its
verification method.
