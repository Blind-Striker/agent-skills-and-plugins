# deniz-skills

Personal, opinionated multi-harness skill collection. It exists for my own workflow rather than as a
general-purpose framework or supported product. I keep it public because the curation engine, the .NET
selections, or the recorded transformation decisions may still be useful to someone else.

Upstream skill repositories live as submodules in `external/`; curation manifests in
`curation/*.yaml` select and transform what gets packaged into the `deniz-*` Claude Code Plugins in
`plugins/` and same-named OpenCode Module Bundles in `opencode/`.

Working in this repo, human or agent, starts at [AGENTS.md](AGENTS.md): the contract and the map of
where everything else lives.

For a quick human-facing route through the mature process and general .NET sets, see the
[skills cheatsheet](docs/cheatsheet.md). Its item-level source of truth remains the curation manifests.

## Why this mix

`deniz-process` is a transformed personal mix, not either upstream framework wholesale. At the
current pins it takes all 14 Superpowers skills and 22 of Matt Pocock's 35 scanned skills directly;
Matt's TDD, debugging, and code-review material is folded into three corresponding
Superpowers-based skills, while ten other Matt items are excluded. Superpowers supplies most of the
plan, build, and verify spine; Matt supplies mostly human-started ceremonies plus routing,
domain/design, research, and teaching tools. The Superpowers SessionStart bootstrap is not shipped,
and ASD-STE100 contributes one additional controlled-English skill. Exact item decisions and reasons
remain beside the items in [`curation/deniz-process.yaml`](curation/deniz-process.yaml).

## How it works

Authored transformation inputs live in `curation/*.yaml` (what to take and how to customize it),
`curation/attribution.json` (where redistributed sources and licenses come from), `overlays/`
(patches or owned replacement files), and `skills/` (original skills). `npm run build` regenerates
committed `plugins/`, `opencode/`, and `dist/` output; never edit those generated trees directly.

The generated `docs/inventory.md` is the scanner-visible catalog for the initialized upstream pins.
It is evidence and a starting index for curation, not an absolute substitute for the upstream source
or scanner behavior. Current product mechanics live in
[transformation and emission](docs/architecture/transformation-and-emission.md),
[references and linking](docs/architecture/references-and-linking.md), and
[distribution and installation](docs/architecture/distribution-and-installation.md).

## What the curation compiler does

This repository is more than a directory of copied skills. Its authored inputs and generated outputs
form a small compiler and installer pipeline:

```text
pinned upstream repos + original skills
  -> inventory + curator intent + patches/overlays
  -> preflight + transformation + reference localization/linking
  -> Claude Code Plugins + OpenCode Module Bundles
  -> transactional OpenCode Native-tree installation
```

- Every scanner-visible upstream item receives an explicit take, merge, transform, or exclude
  decision with the reason beside it.
- A curated item can be renamed, stripped of runtime-irrelevant files, given target-fit metadata and
  invocation, or emitted in another supported artifact shape. Native commands and agents are kept;
  source skills can also become commands or agents.
- `auto`, `manual`, and `both` are harness-neutral intent. Claude Code receives native invocation
  flags; OpenCode receives a skill, a command, or both. Bundled manual skills retain their assets in a
  non-discoverable parked body rather than losing them during command conversion.
- Surgical patches, owned overlays, and declared multi-source merges are stamped against their
  upstream inputs. Reviewed upstream drift blocks generation until it is deliberately reconciled or
  re-blessed.
- Neutral namespaced references are localized independently for each harness. The linker checks
  target existence, model/user reachability, two-way `depends_on` symmetry, and transformation-caused
  path breakage.
- Preflight aggregates source, identity, collision, conversion, and attribution failures before old
  generated output is deleted. The deterministic ledger makes invocation, shape, dependencies,
  dropped metadata, and emitted artifacts reviewable as data.
- Every OpenCode Bundle carries a manifest of final paths, SHA-256 hashes, executable-mode claims,
  source-specific notices, and exact upstream license texts.
- The OpenCode installer verifies those Bundles and composes a selected Native tree through a
  zero-write Plan followed by explicit Apply. Ownership, collisions, local modifications, locking,
  crash recovery, rollback, and post-commit finalization fail closed rather than taking over files.
- `npm run sync` is a deliberate pin-move workflow that reports source deletion/rename, posture and
  frontmatter movement, merge-source drift, and candidate reference changes for human review.

Current limitations are stated rather than hidden: reverse command/agent-to-skill conversion,
per-harness body overlays, arbitrary-subset Module dependency closure, and runtime model behavior are
not claimed. See the linked architecture documents and [roadmap](docs/ROADMAP.md) for the exact proof
boundaries.

## Sources and credits

The collection currently draws from [Superpowers](https://github.com/obra/superpowers) by Jesse
Vincent, [Matt Pocock's skills](https://github.com/mattpocock/skills),
[dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) by Aaron Stannard,
[Microsoft's Aspire skills](https://github.com/microsoft/aspire-skills),
[the .NET agent skills](https://github.com/dotnet/skills), and
[ASD-STE100 skill](https://github.com/danyuchn/asd-ste100-skill) by Dustin Yuchen Teng.

This repository's original work is MIT licensed. Each generated Plugin and Bundle carries its exact
source-specific license texts. See [LICENSE](LICENSE) and
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Setup

Requires Node.js >= 24 (the tooling runs TypeScript directly via `node`).

```bash
git clone --recurse-submodules https://github.com/Blind-Striker/agent-skills-and-plugins.git
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
| `npm run verify:package -- <package.tgz>` | Verify an exact Package's payload, Bundle hashes, and tar executable modes before publication |
| `npm run check:public-safety` | Reject Gmail identities and user/workspace machine paths in current authored and generated files |
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
change, yours or upstream's, shows up in review.

## Consuming

Claude Code: `/plugin marketplace add Blind-Striker/agent-skills-and-plugins` then install `deniz-*`
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

Until dependency-aware Selection planning lands, `--all` is the complete path. A partial Selection
that uses the current guarded cross-Module handoffs must include General, Akka, and Aspire together.

The first installer-owned Apply requires a manual clean start. Uninstall package adapters that
shadow the same names, then move or remove old manually staged copies named by the Plan. Existing
files are Unowned Collisions: there is no force, reset, legacy migration, project-local target, or
JSON-config mutation. Preserve unrelated global configuration and artifacts. Once Install state
exists, do not delete it; restore or resolve any reported Local modification or State drift before
retrying.

### OpenCode from a Release Package

The current Package is attached to GitHub Release `installer-v0.2.0`, targeting commit `8867fc4`.
It was built on Linux and verified through manifest-backed tar-mode checks, zero-write Plan, Apply,
status, and a remote re-download. Verify its repository-recorded SHA-256 before first execution. The
digest detects replacement or corruption but cannot prevent an authorized re-upload. The Package is
an npm-format transport artifact, not an npm publication or Git package install:

```powershell
$download = Join-Path $env:TEMP "deniz-skills-installer-v0.2.0"
New-Item -ItemType Directory -Path $download -Force | Out-Null
gh release download installer-v0.2.0 --repo Blind-Striker/agent-skills-and-plugins `
  --pattern "deniz-agent-skills-0.2.0.tgz" --dir $download
$package = Join-Path $download "deniz-agent-skills-0.2.0.tgz"
$expected = "4ce23817052317b80926a6cd0aed7063364e9625c012f22080bfb887727286be"
$actual = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw "downloaded Package SHA-256 mismatch: $actual" }

# Plan, then Apply
npm exec --yes --package $package -- deniz-skills install --all
npm exec --yes --package $package -- deniz-skills install --all --yes
npm exec --yes --package $package -- deniz-skills status
```

Release Packages use the same `install`, `update`, `remove`, module-selection, Plan, and Apply grammar
shown for the checkout. The installer targets only OpenCode's normal global config root and refuses
alternate config-dir mounts. Current boundaries and lifecycle mechanics are in
[distribution and installation](docs/architecture/distribution-and-installation.md); the dated
[adapter research](docs/research/harness-adapters.md) and
[experiment protocol](experiments/harness-invocation/protocol.md) retain measured evidence and its
verification method. The exact corrected-asset evidence is in the
[v0.2.0 POSIX correction record](experiments/harness-invocation/records/2026-08-25-opencode-installer-v0.2.0-posix-correction.md).

## Limits and support

This repository has no compatibility or response-time SLA. Reproducible repository bugs may be
reported through GitHub Issues. Do not post suspected vulnerabilities, credentials, or secrets in a
public issue; follow [SECURITY.md](SECURITY.md).

Generation and linking prove emitted bytes, declared references, and the checks described in the
engineering documentation. They do not prove that every upstream Aspire CLI, TypeScript, testing, or
package example works in every consumer environment. Those examples remain intentionally
upstream-owned rather than locally forked to manufacture a stronger claim.
