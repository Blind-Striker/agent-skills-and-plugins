# OpenCode plugin packages, cached artifacts, and per-module distribution

Date: 2026-08-18

## Question and scope

At the 2026-08-18 evidence boundary, the lead question was why a Git-backed package such as
Superpowers could expose skills from OpenCode's package cache while the measured local design used
per-Module Bundles and an installer to compose selected Modules. Could one repository URL expose
skills, commands, and agents, and could a monorepo install those artifacts a module at a time?

The package, plugin, discovery, and installer paths below were rechecked against OpenCode v1.18.18,
commit [`31406cc`](https://github.com/anomalyco/opencode/tree/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d),
and the 2026-08-17 development head, commit
[`2cba7e2`](https://github.com/anomalyco/opencode/tree/2cba7e227d68a7e7e4a2aa9c85b808e8ecb14daf).
Critical behavior was unchanged between those pins. Ecosystem examples are pinned separately so this
document does not infer a convention from whichever branch happens to be current.

This is source research, not a live installation report. No global config or package cache was
mutated. Questions not established by that evidence session are identified in the snapshot matrix.

> **Dated source analysis and decision history, not current local policy.** The OpenCode, npm, and
> ecosystem findings are bounded by the pins below. For the repository's current terms and product
> mechanics, follow [`CONTEXT.md`](../../CONTEXT.md),
> [Transformation and emission](../architecture/transformation-and-emission.md), and
> [Distribution and installation](../architecture/distribution-and-installation.md). The root
> [README](../../README.md) owns the runnable consumption recipe; experiment records own observed
> installation evidence.

## Direct answer

- A non-path `plugin` value is an **npm package specifier**. OpenCode passes registry, Git, GitHub
  shorthand, and alias-style specs through `npm-package-arg`, materializes the resulting package,
  resolves a server entrypoint, imports it, and runs its hooks. Cache placement is only storage; it
  does not register artifacts. [OC-SPEC] [OC-NPM] [NPM-SPEC]
- OpenCode does **not** scan an arbitrary installed package root for `skills/`, `commands/`, or
  `agents/`. Skills can be registered through `config.skills.paths`; commands and agents have no
  corresponding path setting and must be merged into `config.command` and `config.agent` or placed
  under a normal config directory. [OC-SKILL] [OC-COMMAND] [OC-AGENT]
- Superpowers works because its root package has an importable `main`, and that plugin explicitly
  appends its package-relative `skills/` directory to `config.skills.paths`. It also injects its
  bootstrap message. The Git URL and cache directory do neither job. [SP-PACKAGE] [SP-PLUGIN]
- OpenCode v1.18.18 shipped `opencode plugin <module>` (alias `plug`). It installed the package,
  inspected its server and TUI entrypoints, and safely added the same package spec to JSON/JSONC
  config. It was a config installer for a **plugin package**, not a package-root artifact scanner or
  a per-module artifact installer. It had no external-plugin list, update, or uninstall command.
  [OC-CLI] [OC-INSTALL]
- A package adapter can expose all three artifact kinds; `opencode-froggy` proves that shape. The
  adapter still has to load and register each kind explicitly. [FROGGY]
- npm Git syntax selects a repository and optional Git ref. It does not select a package directory
  inside that repository. npm workspaces make subpackages manageable inside a checkout but do not
  turn a workspace path into part of a Git package spec. A monorepo therefore needs a root adapter,
  separately published module packages, or its own installer. [NPM-SPEC] [NPM-WORKSPACES]
- At the 2026-08-18 evidence boundary, this repository had selected **per-module composition** via a
  managed installer rather than a root runtime adapter. That is the dated decision outcome; current
  Bundle, Native-tree, ownership, and update mechanics live in the architecture linked above. A root
  package adapter remained viable for an all-in-one distribution. [LOCAL-CANON] [WSHOBSON]

## 1. What OpenCode installs and loads

### 1.1 Spec classification

OpenCode treats `file://`, dot-relative, and absolute values as local path plugins. Every other
string follows the npm-package path. `parsePluginSpecifier` and `resolvePluginTarget` use
`npm-package-arg`; a bare package becomes `@latest`, while explicit registry versions, Git URLs,
GitHub shorthand, and named aliases retain their package-spec meaning. [OC-SPEC]

For example, Superpowers documents:

```json
{
  "plugin": ["superpowers@git+https://github.com/obra/superpowers.git"]
}
```

Here `superpowers` supplies the installed package identity and the right-hand side supplies the Git
source. Adding `#v6.3.0` selects a Git ref; it does not select a directory inside the repository.
[SP-INSTALL] [NPM-SPEC]

### 1.2 Materialization and cache reuse

At the pinned OpenCode revisions, `Npm.add(spec)`:

1. chooses `<OpenCode cache>/packages/<sanitized-spec>/`;
2. uses `@npmcli/arborist` to reify the package with lifecycle scripts disabled;
3. finds the installed dependency node and returns its package directory; and
4. reuses an existing `<cache>/packages/.../node_modules/<package>` without reifying it again.

At the snapshot, the official plugin page described automatic Bun installation into
`~/.cache/opencode/node_modules/`. That prose and the pinned implementation disagreed on both the
installer and exact layout. Code that runs inside a package should resolve from `import.meta.url`,
not derive a cache pathname, and tests should assert behavior through OpenCode rather than a cache
layout. [OC-NPM] [OC-DOC]

The early reuse check also matters for updates: when the spec string is unchanged and its installed
package directory exists, a restart returns that directory without reifying the package again. A
moving branch therefore does not refresh merely because OpenCode restarts. `opencode plugin <module>
--force` replaces a matching config row; it does not clear the package cache. Immutable
version/tag/commit specs were the predictable choice in this analysis; the 2026-08-18 matrix recorded
moved tags and changed spec strings as unmeasured edge cases. [OC-NPM] [OC-INSTALL]

### 1.3 Package entrypoint and hook execution

For a server plugin package, OpenCode resolves `exports["./server"]` first and falls back to
`package.json.main`. The resolved path must remain inside the package. npm packages may declare an
`engines.opencode` semver range. Once compatibility passes, OpenCode imports the module and supports
the then-current default-export module shape as well as its legacy function-export fallback. A
package directory with no server entrypoint is skipped; it is not reinterpreted as an artifact root.
[OC-SPEC] [OC-LOADER] [OC-PLUGIN]

OpenCode loads config, initializes plugins, and runs each plugin's `config` hook before initializing
later services. The hook receives the live config object. This is the supported seam used by package
adapters to append `skills.paths` and to merge `command` or `agent` definitions. [OC-BOOTSTRAP]
[OC-PLUGIN] [OC-API]

The complete server-plugin chain is:

1. read and merge plugin specs from config;
2. classify each spec as a local path or npm-style package spec;
3. materialize or reuse the package;
4. resolve `exports["./server"]` or `main`;
5. check compatibility and import the module;
6. invoke the plugin function and collect hooks; and
7. invoke `config` hooks before later artifact services discover their state.

At no step does the package directory become a generic config directory. [OC-CONFIG] [OC-PLUGIN]

### 1.4 What `opencode plugin` adds

OpenCode v1.18.18's CLI command is:

```text
opencode plugin <module> [--global] [--force]
opencode plug <module> [--global] [--force]
```

It uses the same resolver as runtime loading, reads `package.json`, detects a server target from
`exports["./server"]` or `main`, and also recognizes TUI targets from `exports["./tui"]` or packaged
themes. It patches the corresponding project-local or global config. The patcher uses
`jsonc-parser`, preserves comments, serializes writes under a lock, and compares npm entries by
package identity. Without `--force`, an already configured package is a no-op; with it, the command
can replace the spec while preserving options from an existing tuple. [OC-CLI] [OC-INSTALL]

That removes the need for users to edit config by hand, but it does not add:

- Git-subdirectory or workspace-package selection;
- package-root artifact discovery;
- per-module selection within one package, except behavior a plugin itself implements through its
  options;
- external-plugin update or uninstall; or
- ownership of files copied into `skills/`, `commands/`, or `agents/`.

Those are distribution-design responsibilities, not consequences of the new CLI.

## 2. Artifact discovery remains explicit

| Artifact | Native discovery | Package adapter route | Missing primitive |
|---|---|---|---|
| Skills | compatibility locations, OpenCode config directories, `skills.paths`, and `skills.urls` | append a package-relative directory to `config.skills.paths` | package roots are not inferred |
| Commands | `{command,commands}/**/*.md` below each config directory | merge generated objects into `config.command` | no `commands.paths` |
| Agents | `{agent,agents}/**/*.md` below each config directory | merge generated objects into `config.agent` | no `agents.paths` |
| Local plugins | `{plugin,plugins}/*.{ts,js}` below each config directory | list an npm/Git/path spec in `plugin` | no scan below another package root |

[OC-CONFIG] [OC-SKILL] [OC-COMMAND] [OC-AGENT]

Skill discovery scans global `.claude/skills` and `.agents/skills`, project compatibility locations,
known OpenCode config directories, configured `skills.paths`, and directories downloaded from
`skills.urls`. Duplicate skill names produce a warning and one definition overwrites another; the
loader performs parsing concurrently, so an installer must not treat duplicate resolution as a
stable ownership policy. Avoid the collision instead. [OC-SKILL]

`skills.urls` is a separate, skill-only HTTP protocol. OpenCode requests `<base>/index.json` shaped
like:

```json
{
  "skills": [
    {
      "name": "example",
      "files": ["SKILL.md", "references/details.md"],
      "version": "1"
    }
  ]
}
```

It downloads only the enumerated files and returns resulting skill directories to the skill
scanner. It neither runs Git nor exposes commands, agents, plugin hooks, or arbitrary repository
contents. A GitHub page or `git+https` spec is not a `skills.urls` endpoint. [OC-REMOTE]

## 3. Why Superpowers works

Superpowers v6.3.0, commit
[`b36e082`](https://github.com/obra/superpowers/tree/b36e0829c6d0140e93cfef2ca599b1b07d4a7797),
has a root package manifest with:

```json
{
  "name": "superpowers",
  "type": "module",
  "main": ".opencode/plugins/superpowers.js"
}
```

The imported module resolves `../../skills` relative to itself and appends that absolute directory
to `config.skills.paths`. The same module reads `using-superpowers/SKILL.md`, constructs tool-mapping
text, caches it, and injects it through `experimental.chat.messages.transform`. It does not register
an OpenCode command tree or agent tree. [SP-PACKAGE] [SP-PLUGIN]

Its behavior demonstrates four separate layers:

| Layer | Superpowers responsibility |
|---|---|
| Distribution | Git package spec materializes repository-root package files |
| Storage | OpenCode retains those files in its package cache |
| Plugin runtime | `main` imports a JavaScript adapter and runs its hooks |
| Artifact discovery | the adapter adds the package-relative skill path |

The package would still install without the config hook, but its skills would remain undiscovered.
The files being visible in cache therefore proves distribution, not registration.

## 4. Git repositories, workspaces, and module boundaries

npm's documented Git package forms accept a repository and an optional `#ref`. OpenCode does not add
a path selector before passing the spec to `Npm.add`. A local path can point directly at a package
directory, but there is no corresponding remote syntax for “this directory inside that Git repo.”
[OC-SPEC] [NPM-SPEC]

Workspaces do not change this. A workspace is a package linked and managed from a root checkout by
workspace-aware npm commands. It can be published as its own registry package, but
`github:owner/repo#ref` still identifies the repository package root. A root `package.json` can
forward to one adapter which exposes multiple generated modules; it cannot make those workspace
directories separately selectable by OpenCode's package syntax. [NPM-WORKSPACES]

The viable module boundaries are therefore:

1. **One root package, one adapter.** The adapter exposes all modules and may implement its own
   selection options. OpenCode owns one plugin config entry and one cache unit.
2. **One published package per module.** A registry package gives each module its own package root,
   version, config entry, and update unit. A Git monorepo alone does not.
3. **One root installer package/CLI.** The installer understands repository subdirectories because
   it owns that grammar, then composes selected modules into native OpenCode directories. This is an
   installer convention, not OpenCode or npm Git-subdirectory support.
4. **Separate repositories.** Each module becomes a Git package root at the cost of repository and
   release fragmentation.

## 5. Ecosystem evidence

The examples establish viable mechanisms, not prevalence. None relies on automatic package-root
artifact scanning.

| Project and pin | Shape | Mechanism | Lesson |
|---|---|---|---|
| Superpowers `v6.3.0` (`b36e082`) | skills | package `main`; `config.skills.paths` | smallest package-relative skill adapter |
| `opencode-froggy` `v0.12.0` (`ca3e228`) | skills, commands, agents | explicitly loads all three directories; mutates all three config fields | exact proof that one adapter can expose all kinds |
| `micode` `v0.10.0` (`d735ecf`) | commands, agents | programmatic registries assigned in a config hook | Markdown directories are optional when generated data is ready |
| `opencode-beads` `v0.7.0` (`896f66e`) | commands, agent | explicitly parses package-relative vendored Markdown | package files are inert until adapter code reads them |
| `wshobson/agents` (`d6837ae`) | generated skills, commands, agents | per-harness generator plus clone/generate/symlink installer | useful per-module source boundary and adapter architecture; not package-subdirectory support |
| `vercel-labs/skills` (`c6f69c6`) | skills | custom source parser, deterministic hash lock, add/update/remove | useful ownership and Git-subpath grammar implemented by the installer itself |
| Homerun marketplace (`54c5a4a`) | skills and config | root `npx` installer with an ownership manifest | package-root CLI can copy owned output without being an OpenCode plugin |

[SP-PACKAGE] [SP-PLUGIN] [FROGGY] [MICODE] [BEADS] [WSHOBSON] [VERCEL]
[HOMERUN]

`wshobson/agents` was especially relevant but easy to overread. Its first-party modules lived under
`plugins/<module>/`; `tools/generate.py --harness opencode --plugin <module>` transformed one module,
and its OpenCode adapter wrote one flat generated `.opencode/` tree. Its installer then symlinked
generated entries into the global config and refused to overwrite non-symlinks. The generated
OpenCode tree was gitignored, and the documented flow was clone, generate, install. At the snapshot,
this supported the per-harness transformation and module-boundary decisions already made here. It
did **not** prove that OpenCode could install `plugins/<module>` from a Git package spec. [WSHOBSON]

The Homerun precedent has a different ownership boundary from the design below. It writes an owned
tree outside the shared config root and selects that tree through a persistent `OPENCODE_CONFIG_DIR`.
At the decision snapshot this repository had rejected that environment-variable end state, so the
chosen shared Native-tree option was evaluated against stronger path-manifest, collision, rollback,
and local-edit requirements. Homerun proves an ownership manifest can work; it did not establish
those additional local requirements. Current Destination and safety mechanics are in
[Distribution and installation](../architecture/distribution-and-installation.md). [HOMERUN]
[LOCAL-CANON]

## 6. Dated decision outcome for this repository

By the 2026-08-18 evidence session, the implemented outcome was a private root Package whose
`deniz-skills` bin used committed `dist/install-opencode.js` to compose generated
`opencode/<module>/` Bundles. It had no OpenCode `main` or `exports["./server"]`, so it was an
installer Package rather than a runtime plugin adapter. The measured remote recipe downloaded
`deniz-agent-skills-0.1.0.tgz` from private Release `installer-v0.1.0`, targeted at commit `5ab4117`,
and compared its SHA-256 with the local pack. GitHub reported `immutable: false`; the recorded pin
and digest detected replacement but could not prevent an authorized re-upload. These exact names and
pins are dated recipe and measurement provenance, not a current install policy. See section 7 and
record
[`opencode-module-installer-local-pack-2026-08-18`](../../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md).

Current documentary authority is intentionally elsewhere:

- [`CONTEXT.md`](../../CONTEXT.md) owns Module, Bundle, Package, Destination, Selection, Ownership,
  Plan, Apply, and Recovery vocabulary.
- [Transformation and emission](../architecture/transformation-and-emission.md) owns the
  compile/install/runtime boundary and generated Bundle handoff;
  [Distribution and installation](../architecture/distribution-and-installation.md) owns current
  Package, Destination, composition, and lifecycle mechanics; and
  [References and linking](../architecture/references-and-linking.md) owns the full-estate proof
  boundary.
- [ADR-0001](../adr/0001-submodule-manifest-overlay-architecture.md),
  [ADR-0002](../adr/0002-multi-harness-output.md), and
  [ADR-0004](../adr/0004-minimal-toolchain.md) retain the accepted rationale and trade-offs.
- The root [README](../../README.md) owns the runnable current recipe, while the
  [roadmap](../ROADMAP.md) owns unfinished work and current operational state.

The design comparison at the snapshot was:

| Design | Per-module selection | Skills | Commands/agents | Update and uninstall | Fit |
|---|---:|---:|---:|---:|---|
| Continue manual staging | manual file selection | native | native | manual; stale copies possible | rejected; no ownership boundary |
| Root OpenCode package adapter | only if adapter options implement it | `skills.paths` | generated config objects | OpenCode cache; no native uninstall command | good all-in-one package, weak module ownership |
| Registry package per module | yes | adapter path | adapter config objects | package versions; no OpenCode uninstall command at the snapshot | clean package boundary, high publication overhead |
| Managed config-tree installer | yes | native files | native files | exact Plan/Apply, Recovery, Update, Remove | implemented selection |
| `skills.urls` | skill-by-skill | remote protocol | unsupported | version field refreshes files | deliberately skill-only channel |

### Why the managed installer won at the snapshot

The deciding premise at the snapshot was that the product was the compile-time transformation, not
a runtime reinterpretation of upstream. A managed installer preserved that boundary: the build
emitted harness-native Module Bundles, and the installer only composed those ready artifacts into the
one flat address space OpenCode scanned. It did not need to parse upstream Markdown, infer invocation
posture, or rewrite references at install time. [LOCAL-CANON]

Current distribution guarantees live in
[Distribution and installation](../architecture/distribution-and-installation.md), and the current
compile/install/runtime boundary lives in
[Transformation and emission](../architecture/transformation-and-emission.md). This section retains
why that boundary was selected, not a second operating contract.

The root package-adapter design remained a valid alternative if the desired product changed to “one
spec always installs the whole curated set.” The minimum adapter described by this analysis would
register the generated skills path, merge pre-generated command and agent objects with an explicit
collision policy, resolve all package paths from its own module URL, expose only build-produced data,
and avoid transforming `external/` content at runtime. [FROGGY] [LOCAL-CANON]

## 7. Implementation evidence and measurement matrix at 2026-08-18

The repeatable isolation and recording method is owned by the
[harness-invocation protocol](../../experiments/harness-invocation/protocol.md), and committed
observations are owned by its [records](../../experiments/harness-invocation/records/README.md). This
section keeps the research synthesis and measurement-question matrix; it is not a substitute for a
record's fields or evidence excerpts.

The selected local-package path and the private Release transport are both recorded in
[`opencode-module-installer-local-pack-2026-08-18`](../../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md):
on Windows with OpenCode 1.18.18, the packed Package and downloaded private Release asset produced
equivalent Package, Module, Install-state, Native-tree, and discovery outcomes. Plan remained
zero-write, Apply installed the selected Modules, and OpenCode support files coexisted with Install
state. The record separately documents the authorized real-profile migration and preservation of
routing material outside Module Ownership. That session explicitly did not measure model-driven
parked-body reads, the human permission prompt, or post-initialization Update/Remove. Exact digests,
byte and file counts, and control-root details remain in the record; the root README owns the current
download and verification recipe.

| Question | Fixture and action | Required assertion | Status at 2026-08-18 |
|---|---|---|---|
| Does a root Git package load? | tagged package with `exports["./server"]` and a diagnostic hook | exact tag, package root, hook invocation | Source-established at the snapshot; no local runtime record in this snapshot. |
| Is a package root auto-scanned? | same package contains unregistered `skills/`, `commands/`, `agents/` | none appear before explicit registration | Source-established at the snapshot; no local runtime control in this snapshot. |
| Can a Git spec select a workspace subdirectory? | root plus two workspace packages; try npm/Git/GitHub spellings | either documented success or recorded rejection for each spelling | Unmeasured as of 2026-08-18; documentation showed no selector. |
| Does the all-three adapter work end to end? | generated skill path plus command and agent config objects | skill tool, slash command, and subagent each invoke the intended artifact | Ecosystem-established at the snapshot; no local end-to-end fixture in this snapshot. |
| What wins on collisions? | global, project, native config-tree, and adapter definitions share names | observed winner and warning for each artifact kind | Unmeasured as of 2026-08-18; no policy was inferred from load order. |
| How does cache refresh behave? | immutable tag, moved tag, branch, commit; restart, changed spec, and `--force` | exact installed commit after each action | Unchanged-spec restart was source-established; the edge cases were unmeasured as of 2026-08-18. |
| Is CLI patching safe? | JSONC with comments, tuples, duplicate package versions, local/global scope | comments preserved; expected add/no-op/replace; no partial write | Source-tested upstream at the snapshot; no local compatibility record in this snapshot. |
| Does Git installation work on Windows? | named Git alias and GitHub shorthand with Git on `PATH` | installation, sanitized cache path, module import | Unmeasured as of 2026-08-18; the cited Superpowers reports recorded historical failures. |
| Can the managed installer recover? | interrupted install, unowned collision, locally edited owned file | rollback succeeds; unowned and edited files survive | Implementation integration tests had passed at the snapshot; no isolated Package-interruption record existed. |
| Are support files relocatable? | command, agent, and skill references cross module and use relative files | every link resolves and runtime invocation reads the intended file | Build-linker and installed-tree introspection evidence existed; model-driven reads were unmeasured as of 2026-08-18. |
| Do parked bundles need permission configuration? | invoke a global Native-tree command that reads its parked body | literal prompt or no-prompt observation recorded by a human | Unmeasured as of 2026-08-18 for installer composition; the old config-dir mount did not answer it. |
| Does Install state coexist with OpenCode config maintenance? | initialize OpenCode after install, then update and remove | OpenCode-owned support files survive; installer prunes only paths in its Ownership | Isolated introspection had been measured; no post-initialization Update/Remove record existed in this snapshot. |
| Does the pinned private Release match local pack? | download the fixed asset with `gh` into an isolated profile | package hash, Selection, Native-tree hashes, and Install state equal the local pack | Measured in `opencode-module-installer-local-pack-2026-08-18`: local pack and Release matched across Package, Module, Install-state, Native-tree, and discovery checks. |

A passing package-adapter fixture would have proved only that the option was technically available.
It would not by itself have chosen that option over the managed installer; ownership, module
selection, and recovery were separate design criteria in the comparison.

## Primary sources

- **[OC-SPEC]** OpenCode v1.18.18
  [`plugin/shared.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/plugin/shared.ts):
  spec parsing and classification, package/path resolution, package entrypoints, and compatibility.
- **[OC-NPM]** OpenCode v1.18.18
  [`core/npm.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/core/src/npm.ts):
  Arborist reification, ignored scripts, cache layout, sanitization, and early reuse.
- **[OC-LOADER]** OpenCode v1.18.18
  [`plugin/loader.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/plugin/loader.ts):
  target resolution, entrypoint checks, compatibility, and dynamic import.
- **[OC-INSTALL]** OpenCode v1.18.18
  [`plugin/install.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/plugin/install.ts):
  manifest target detection and JSONC-safe config patching.
- **[OC-CLI]** OpenCode v1.18.18
  [`cli/cmd/plug.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/cli/cmd/plug.ts)
  and [CLI documentation](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/web/src/content/docs/cli.mdx):
  `plugin`/`plug`, `--global`, and `--force`.
- **[OC-PLUGIN]** OpenCode v1.18.18
  [`plugin/index.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/plugin/index.ts):
  then-current and legacy module loading and sequential config hooks.
- **[OC-BOOTSTRAP]** OpenCode v1.18.18
  [`project/bootstrap.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/project/bootstrap.ts):
  plugin initialization before other services.
- **[OC-CONFIG]** OpenCode v1.18.18
  [`config/config.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/config/config.ts):
  config directories, command/agent scans, and plugin origins.
- **[OC-SKILL]** OpenCode v1.18.18
  [`skill/index.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/skill/index.ts):
  compatibility roots, config roots, `skills.paths`, URLs, and duplicates.
- **[OC-REMOTE]** OpenCode v1.18.18
  [`skill/discovery.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/skill/discovery.ts):
  `index.json`, enumerated downloads, cache, and version refresh.
- **[OC-COMMAND]** OpenCode v1.18.18
  [`config/command.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/config/command.ts).
- **[OC-AGENT]** OpenCode v1.18.18
  [`config/agent.ts`](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/opencode/src/config/agent.ts).
- **[OC-API]** OpenCode v1.18.18
  [`@opencode-ai/plugin` hooks](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/plugin/src/index.ts).
- **[OC-DOC]** Pinned official
  [plugin documentation source](https://github.com/anomalyco/opencode/blob/31406ccc51b4bd2a4e1e086b2bcaa5f7f804f26d/packages/web/src/content/docs/plugins.mdx),
  rendered at <https://opencode.ai/docs/plugins/>.
- **[NPM-SPEC]** npm CLI v11
  [package-spec documentation](https://docs.npmjs.com/cli/v11/using-npm/package-spec), including
  Git URLs and `#ref`.
- **[NPM-WORKSPACES]** npm CLI v11
  [workspace documentation](https://docs.npmjs.com/cli/v11/using-npm/workspaces).
- **[SP-PACKAGE]** Superpowers v6.3.0
  [`package.json`](https://github.com/obra/superpowers/blob/b36e0829c6d0140e93cfef2ca599b1b07d4a7797/package.json).
- **[SP-PLUGIN]** Superpowers v6.3.0
  [OpenCode plugin](https://github.com/obra/superpowers/blob/b36e0829c6d0140e93cfef2ca599b1b07d4a7797/.opencode/plugins/superpowers.js).
- **[SP-INSTALL]** Superpowers v6.3.0
  [OpenCode installation guide](https://github.com/obra/superpowers/blob/b36e0829c6d0140e93cfef2ca599b1b07d4a7797/.opencode/INSTALL.md).
- **[FROGGY]** `opencode-froggy` v0.12.0
  [manifest](https://github.com/smartfrog/opencode-froggy/blob/ca3e228d53b62be2cd882c91a0eb091d94909371/package.json)
  and [three-kind adapter](https://github.com/smartfrog/opencode-froggy/blob/ca3e228d53b62be2cd882c91a0eb091d94909371/src/index.ts).
- **[MICODE]** micode v0.10.0
  [manifest](https://github.com/vtemian/micode/blob/d735ecff7c2588eb719b5942b8ed2b2d4c4dee03/package.json)
  and [config hook](https://github.com/vtemian/micode/blob/d735ecff7c2588eb719b5942b8ed2b2d4c4dee03/src/index.ts).
- **[BEADS]** opencode-beads v0.7.0
  [manifest](https://github.com/joshuadavidthomas/opencode-beads/blob/896f66ea32e77902f87eea77d286669178af920e/package.json),
  [vendored loaders](https://github.com/joshuadavidthomas/opencode-beads/blob/896f66ea32e77902f87eea77d286669178af920e/src/vendor.ts),
  and [config hook](https://github.com/joshuadavidthomas/opencode-beads/blob/896f66ea32e77902f87eea77d286669178af920e/src/plugin.ts).
- **[WSHOBSON]** wshobson/agents commit `d6837ae`:
  [generator](https://github.com/wshobson/agents/blob/d6837ae274c2cd817acad3fb98f193a4390a4c3e/tools/generate.py),
  [OpenCode adapter](https://github.com/wshobson/agents/blob/d6837ae274c2cd817acad3fb98f193a4390a4c3e/tools/adapters/opencode.py),
  [installer](https://github.com/wshobson/agents/blob/d6837ae274c2cd817acad3fb98f193a4390a4c3e/tools/install_opencode.py),
  and [harness guide](https://github.com/wshobson/agents/blob/d6837ae274c2cd817acad3fb98f193a4390a4c3e/docs/harnesses.md).
- **[VERCEL]** vercel-labs/skills commit `c6f69c6`:
  [source/subpath parser](https://github.com/vercel-labs/skills/blob/c6f69c631292444cc541ac6d91e2226b0ff247da/src/source-parser.ts)
  and [deterministic local lock](https://github.com/vercel-labs/skills/blob/c6f69c631292444cc541ac6d91e2226b0ff247da/src/local-lock.ts).
- **[HOMERUN]** Homerun marketplace commit `54c5a4a`:
  [root installer entrypoint](https://github.com/homeruntech/claude-plugin-marketplace/blob/54c5a4a046000026a9e0b359232c8d741723912b/tools/install.mjs)
  and [owned install-tree design](https://github.com/homeruntech/claude-plugin-marketplace/blob/54c5a4a046000026a9e0b359232c8d741723912b/docs/ARCHITECTURE.md).
- **[LOCAL-CANON]** Current local vocabulary and mechanics:
  [`CONTEXT.md`](../../CONTEXT.md),
  [Transformation and emission](../architecture/transformation-and-emission.md),
  [Distribution and installation](../architecture/distribution-and-installation.md), and
  [References and linking](../architecture/references-and-linking.md). Accepted rationale:
  [ADR-0001](../adr/0001-submodule-manifest-overlay-architecture.md),
  [ADR-0002](../adr/0002-multi-harness-output.md),
  [ADR-0004](../adr/0004-minimal-toolchain.md),
  [ADR-0006](../adr/0006-output-is-a-transformation.md), and
  [ADR-0008](../adr/0008-references-are-symbols.md). Consumption and operational state:
  [README](../../README.md) and [roadmap](../ROADMAP.md).

## Conclusion

An OpenCode Git plugin spec installs a repository-root package and imports its plugin entrypoint. It
does not make arbitrary package folders discoverable and does not select a workspace subdirectory.
Superpowers succeeds because its adapter registers a package-relative skill path; an all-three
adapter is likewise possible only through explicit registration.

For an all-in-one product, the evidence left a root package adapter as a small supported option. The
2026-08-18 local decision outcome instead used a root installer and already transformed Module
Bundles because per-Module selection, path Ownership, and exact update/remove boundaries were part of
the goal. OpenCode's `plugin` command improved package configuration but did not supply that ownership
design.

That outcome is decision history, not the current operating contract. Follow the section 6 relays to
architecture, `CONTEXT.md`, ADRs, the root README, and the roadmap. Exact measurement digests, byte
and file counts, control-root details, and equivalence results remain dated provenance in record
[`opencode-module-installer-local-pack-2026-08-18`](../../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md),
while the release tag and target commit remain recipe provenance in section 6, not a timeless
installation pin.
