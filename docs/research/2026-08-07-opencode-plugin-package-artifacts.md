# OpenCode plugin packages and cached artifacts

Date: 2026-08-07

## Question and scope

Why does a configured Git package such as
`superpowers@git+https://github.com/obra/superpowers.git#v6.1.0` expose skills from
OpenCode's package cache, while this repository's generated `opencode/` tree is staged into an
OpenCode configuration directory? Can one Git repository URL expose a complete `skills/`,
`commands/`, and `agents/` tree without an adapter?

The behavior below was checked against the pinned OpenCode v1.18.7 checkout and the official
v1.18.14 source at commit
[`65cf14d`](https://github.com/anomalyco/opencode/tree/65cf14df16c191f3e9684f0d9a8bae69103ced6d).
The relevant install, entrypoint, config-hook, skill, command, and agent paths have the same behavior
in those two versions. Superpowers was checked at v6.1.0 commit
[`f268f7c`](https://github.com/obra/superpowers/tree/f268f7c953744036f0fa7e9d4b73535c04e57cb8);
the vendored later version changes only the version field in the files relevant here.

## Direct answer

- The Git spec is first an **npm-style package spec**. OpenCode materializes that package in its
  package cache, finds the package's JavaScript entrypoint, and imports it. Cache placement by
  itself discovers no skills, commands, or agents. [OC-PACKAGE] [NPM-SPEC]
- Superpowers works because its `package.json` points `main` at an OpenCode plugin, and that plugin's
  `config` hook adds the package-relative `skills/` directory to `config.skills.paths`.
  OpenCode then performs ordinary skill discovery on that explicit path. [SP-PACKAGE] [SP-PLUGIN]
- OpenCode does **not** automatically scan arbitrary plugin package roots for `skills/`, `commands/`,
  or `agents/`. Package resolution searches for a module entrypoint; artifact scanners search
  configuration directories and explicitly configured skill paths. [OC-ENTRY] [OC-CONFIG]
- `skills.urls` is not a general Git clone or repository URL feature. It is a skill-only HTTP index
  protocol: OpenCode requests `index.json`, downloads enumerated skill files, and scans only the
  resulting skill directories. It cannot deliver command or agent definitions. [OC-REMOTE-SKILLS]
- A Git package can carry all three trees as **package files**, but commands and agents in that cache
  remain inert unless a plugin adapter registers them in `config.command` and `config.agent` (or
  installs them into a configuration directory). Skills likewise need `skills.paths` or the separate
  remote-skill protocol. [OC-HOOK-API] [OC-COMMAND] [OC-AGENT]
- For this repository, the appropriate one-spec design is a small generated OpenCode adapter at the
  package root. It should expose already transformed build output, not transform upstream content at
  runtime. Keep the current staging workflow until package loading, collision precedence, relocation,
  updates, and Windows Git installation are measured. [LOCAL-CONTRACT] [LOCAL-OUTPUT]

## 1. From a `plugin` Git/npm spec to cache and module load

### 1.1 Configuration and spec classification

The schema accepts each `plugin` item as either a string or a `[string, options]` tuple; it does not
define a separate Git-plugin type. OpenCode treats only `file://`, relative, and absolute path-like
specs as local. Every other string, including the named Git form above, follows the npm-package path.
OpenCode parses that string with `npm-package-arg`, whose supported package specs include Git URLs
and a `#ref`; npm's official package-spec documentation likewise defines a Git URL as a package
source. [OC-PLUGIN-SCHEMA] [OC-ENTRY] [NPM-SPEC]

For the example, the leading `superpowers@` supplies the expected installed package name and the
remainder supplies the Git source plus tag. OpenCode passes the original spec through to its package
installer rather than cloning a repository as an OpenCode artifact tree. [OC-PACKAGE]

### 1.2 Package cache materialization

In v1.18.7 and v1.18.14, `Npm.add(spec)` chooses
`<OpenCode cache>/packages/<sanitized-spec>/`, uses Arborist to reify the spec with install scripts
disabled, and obtains the installed package directory from the resulting dependency tree. If the
expected package already exists below that cache's `node_modules`, it is reused without another
reify. The package bytes and cache location are distribution/storage concerns, not artifact
registration. [OC-PACKAGE]

The official v1.18.14 plugin documentation summarizes this more broadly as automatic Bun installation
into `~/.cache/opencode/node_modules/`. That prose does not match the v1.18.14 implementation's
per-spec `packages/.../node_modules` layout or its use of Arborist. For exact 1.18.14 behavior, the
versioned source is the stronger authority; callers should not depend on the prose cache path.
[OC-PLUGIN-DOC] [OC-PACKAGE]

### 1.3 Entrypoint resolution and import

After installation, OpenCode reads the installed package's root `package.json`. For a server plugin
it first checks `exports["./server"]`, then falls back to `main`. It resolves that file inside the
package boundary, checks any `engines.opencode` range, dynamically imports the module, and invokes
the exported plugin function(s). A package directory without a usable server entrypoint is not
converted into an artifact search root. [OC-ENTRY] [OC-LOADER]

The plugin function receives OpenCode context and returns hooks. During bootstrap OpenCode loads the
cached config, initializes plugins before other services, and invokes each returned `config` hook
against that cached config object. This ordering is what lets a plugin add discovery or config state
before skills, commands, and agents materialize their own state. [OC-BOOTSTRAP] [OC-PLUGIN-RUNTIME]

The complete chain is therefore:

1. parse `plugin` config;
2. classify the non-path string as an npm-style package spec;
3. materialize/reuse the package in the package cache;
4. read its package manifest and resolve `exports["./server"]` or `main`;
5. dynamically import the module;
6. invoke its plugin export and collect hooks;
7. invoke its `config` hook on the live config;
8. let later services discover whatever the hook explicitly registered. [OC-PACKAGE] [OC-ENTRY]
   [OC-LOADER] [OC-PLUGIN-RUNTIME]

## 2. OpenCode does not scan arbitrary package roots

OpenCode's configuration loader scans commands and agents only under the known configuration
directories. `ConfigCommand.load(dir)` scans `{command,commands}/**/*.md`,
`ConfigAgent.load(dir)` scans `{agent,agents}/**/*.md`, and local plugin discovery scans
`{plugin,plugins}/*.{ts,js}` in those same directories. The installed package target returned by the
plugin loader is not appended to that directory list. [OC-CONFIG] [OC-COMMAND] [OC-AGENT]

Skill discovery has one additional explicit mechanism: after scanning standard config/compatibility
locations, it scans every configured `skills.paths` entry with `**/SKILL.md`, then scans directories
returned by `skills.urls`. It does not infer either path from a loaded plugin's package root.
[OC-SKILL]

One nuance should not be confused with package-root scanning: after skills are discovered, the
v1.18.7/v1.18.14 command service creates an internal command entry with `source: "skill"` for each
skill name not already occupied by an explicit command. That entry is synthesized from loaded skill
content; it is not a scan of a cached `commands/` directory and cannot represent this repository's
distinct curated command files. [OC-SKILL-COMMAND]

## 3. What Superpowers v6.1.0 actually does

### Package distribution

Superpowers' root `package.json` declares `type: "module"` and
`main: ".opencode/plugins/superpowers.js"`. It has no install/postinstall script. OpenCode performs
the Git package installation; Superpowers supplies the importable runtime adapter and the bundled
files beside it. [SP-PACKAGE]

Because the entrypoint lives two levels below the package root, it computes
`path.resolve(__dirname, "../../skills")`. In a cached install that resolves to the `skills/`
directory inside the same installed package; no copy or symlink into the OpenCode config directory
is required. [SP-PLUGIN]

### Skill registration

The returned `config` hook creates `config.skills.paths` when necessary and appends that absolute,
package-relative skills path once. OpenCode's later skill service reads the mutated config and scans
that directory for `SKILL.md`. This adapter code—not the Git URL and not cache placement—is the
reason the skills become visible. [SP-PLUGIN] [OC-SKILL] [OC-BOOTSTRAP]

### Bootstrap/message behavior

The same plugin reads `skills/using-superpowers/SKILL.md`, strips its frontmatter, caches the body,
adds an OpenCode tool mapping, and uses `experimental.chat.messages.transform` to prepend that
bootstrap to the first user message. It guards against reinserting text containing its marker.
Superpowers does not register a cached `commands/` tree, an `agents/` tree, or a command execution
hook. Its OpenCode behavior is the skills-path registration plus this message transform.
[SP-PLUGIN]

Superpowers' own OpenCode guide describes the same two responsibilities: register the skills path
and inject bootstrap context. Its installation guide's phrase "plugin manager ... registers all
skills" is therefore shorthand for package install followed by adapter runtime behavior, not a
general package-artifact convention. [SP-DOC]

## 4. `skills.urls` is a skill-only remote protocol

The schema describes `skills.urls` as URLs from which to fetch skills and gives a
`/.well-known/skills/`-style base URL as its example. For each configured base, OpenCode appends
`index.json` and expects JSON shaped as an array of skill entries containing `name`, `files`, and an
optional `version`. Entries without literal `SKILL.md` in `files` are rejected. [OC-SKILLS-SCHEMA]
[OC-REMOTE-SKILLS]

For each accepted entry, OpenCode downloads only the enumerated files from
`<base>/<skill-name>/<file>` into `<OpenCode cache>/skills/<skill-name>/` and returns that skill
directory to the skill scanner. It neither runs Git nor interprets a repository tree. A GitHub
repository page or `git+https` package spec is therefore not a valid `skills.urls` endpoint unless
some HTTP host separately serves the required `index.json` and files at this protocol's layout.
[OC-REMOTE-SKILLS]

Only the skill service consumes the downloaded directories. There is no corresponding remote URL
field for commands or agents in the official schema, and the remote discovery implementation
returns only skill roots. Thus this mechanism can distribute skills and their enumerated supporting
files, but not commands, agents, or plugin hooks. [SCHEMA] [OC-REMOTE-SKILLS]

## 5. What this repository lacks and the minimum one-spec adapter

This repository currently has a root tooling package and a generated flat OpenCode output containing
`opencode/skills/`, `opencode/commands/`, and `opencode/agents/`. The root `package.json` has no
`main` or `exports["./server"]`, and the generated output has no JavaScript/TypeScript plugin
entrypoint. The consumption documentation accordingly says there is no durable OpenCode installer
and instructs users to stage the generated tree under a project or global OpenCode config root.
[`package.json:1-28`](../../package.json) [`README.md:57-67`](../../README.md) [LOCAL-OUTPUT]

The minimum robust package shape for one root Git spec is:

1. **A package server entrypoint.** Add a root `main` (the widest-compatible choice for these two
   OpenCode versions) or `exports["./server"]` pointing to a shipped ESM plugin module. A registry
   publication would also require changing the current private-publication posture, but `private`
   is not what supplies artifact discovery for a Git install. [OC-ENTRY]
2. **A small plugin function returning a `config` hook.** The hook adds the package's generated
   `opencode/skills` directory to `config.skills.paths`. [OC-HOOK-API] [OC-SKILL]
3. **Generated command config.** Because there is no `commands.paths`, the build should emit the
   command Markdown as ready-to-register `{template, description, agent, model, variant, subtask}`
   data, and the hook should merge that data into `config.command` with an explicit collision rule.
   Runtime parsing of arbitrary upstream Markdown would move transformation work out of the build
   and is not recommended. [OC-COMMAND-SCHEMA] [OC-COMMAND-RUNTIME] [LOCAL-CONTRACT]
4. **Generated agent config.** Likewise, emit ready-to-register agent objects and merge them into
   `config.agent`; there is no `agents.paths`. [OC-AGENT-SCHEMA] [OC-AGENT-RUNTIME]
5. **Package-aware reference linking and validation.** Some generated manual commands currently tell
   the runtime to read `skills/<name>/BODY.md` from the active project/global config root. That is
   correct for staging but wrong when the body remains in a package cache. The build must emit a
   relocatable package form (or a generated registry whose templates contain adapter-resolved package
   paths) and validate those references before claiming package installability. [`tools/build.ts:458-532`](../../tools/build.ts)
   [LOCAL-CONTRACT]
6. **An included package payload.** The adapter, generated config data, `opencode/` artifacts, and all
   linked supporting files must be included by the Git/npm package materialization. A `files` allowlist
   is advisable if the repository later publishes the same package to a registry. [NPM-SPEC]

The adapter should be boring runtime glue: locate its own package root and register build-produced
artifacts. Invocation posture, artifact shape, frontmatter filtering, body edits, cross-reference
rewriting, and dependency closure remain compile-time responsibilities, as required by this
repository's transformation and reference contracts. [`AGENTS.md:12-34`](../../AGENTS.md)
[`docs/adr/0006-output-is-a-transformation.md:13-59`](../adr/0006-output-is-a-transformation.md)

## 6. Viable options

### A. Continue staging/copying the generated tree

This is the current consumption model, not a durable managed installer. It uses OpenCode's native
config-directory scanners for all three artifact kinds, needs no runtime plugin, and matches the
paths currently emitted into manual command stubs. Its costs are manual synchronization, no
one-line package update, and the risk of stale or partial copies. [`README.md:57-67`](../../README.md)
[`experiments/harness-invocation/lab.ps1:61-73`](../../experiments/harness-invocation/lab.ps1)

### B. Add an OpenCode package adapter

This gives the desired one-spec installation and lets the transformed output remain in the managed
package cache. It requires a real package entrypoint, generated command/agent registration data,
package-aware references, collision/precedence policy, and compatibility tests. It also introduces
a small amount of runtime behavior, but that behavior can remain registration-only; the build still
owns transformation and linking. [OC-HOOK-API] [LOCAL-CONTRACT]

### C. Publish through `skills.urls`

This avoids a plugin only for skills and can serve supporting skill files from static HTTP storage.
It cannot expose this repository's curated commands or agents, cannot inject hooks, is not a Git URL
installer, and would require generating and hosting the remote skill index. It is therefore useful
only for a deliberately skill-only distribution channel. [OC-REMOTE-SKILLS] [SCHEMA]

### Recommendation

Keep staging as the supported path until an adapter experiment passes, then prefer a **root package
adapter generated from the existing `opencode/` output** if one-spec installation is worth the added
surface. Do not point runtime code back at `external/` or reinterpret upstream trees. The package
must expose the already curated, per-harness output and preserve the compile-time linker promise.
[LOCAL-CONTRACT] [LOCAL-OUTPUT]

## 7. Distribution, runtime behavior, and storage are different layers

| Layer | Responsibility | Superpowers | This repository today |
|---|---|---|---|
| Package distribution | Turn an npm/Git spec into package files on disk | Named Git package with a root manifest | Root tooling package can carry files but declares no OpenCode entrypoint |
| Cache storage | Retain the installed package for reuse | Package and `skills/` live below OpenCode's package cache | Not used for OpenCode consumption |
| Plugin runtime | Import an entrypoint and run hooks | Adds `skills.paths`; injects bootstrap | No plugin module |
| Artifact discovery | Scan configured roots/protocol outputs | Skill service scans the path added by the plugin | Native scanners see files only after staging under a config root |

[OC-PACKAGE] [OC-ENTRY] [SP-PACKAGE] [SP-PLUGIN] [LOCAL-OUTPUT]

The fact that package files are visible in a cache proves only distribution and storage. The fact
that a module imports proves only plugin loading. Artifacts become usable only when native discovery
already knows their location or adapter code registers them. [OC-CONFIG] [OC-SKILL]

## Concerns and measurements still required

- **Git subdirectory packages:** neither OpenCode's loader nor npm's documented Git package syntax
  adds a repository-subdirectory selector; the documented fragment is a Git ref. A package located
  below the repository root is therefore not an established one-spec route here. Treat it as
  unsupported unless an isolated v1.18.7/v1.18.14 measurement proves a specific package-manager
  syntax. Prefer a root entrypoint or a separately published package. [OC-ENTRY] [NPM-SPEC]
- **Windows Git-backed install:** Superpowers documents past Windows failures involving Git lookup
  and Git-spec cache paths. No live install or global cache mutation was performed for this report,
  so a clean isolated Windows test remains necessary. [SP-DOC]
- **Cache refresh:** `Npm.add` reuses an existing package directory for the same spec. Immutable tag
  pins are predictable; moving branches or unpinned refs need an explicit update/cache experiment.
  [OC-PACKAGE]
- **Precedence and collisions:** the adapter must define whether an existing user/project command or
  agent wins over package data. Skill duplicate ordering and the synthesized `source: "skill"`
  command entries also need an end-to-end invocation measurement rather than an assumption.
  [OC-SKILL] [OC-SKILL-COMMAND]
- **Documentation/source mismatch:** official plugin prose and 1.18.14 implementation disagree on
  the exact package installer/cache layout. Tests should assert behavior through OpenCode, not a
  hard-coded cache pathname. [OC-PLUGIN-DOC] [OC-PACKAGE]

## 8. Ecosystem evidence: supported adapter, not an automatic package convention

### 8.1 The three claims are different

1. **Officially supported plugin API pattern — yes.** Official documentation supports loading npm
   packages as plugins, and the typed plugin API includes a `config` hook that OpenCode invokes on
   the live configuration. Using a package entrypoint plus that hook to set schema-backed fields such
   as `skills.paths`, `command`, and `agent` is therefore supported API usage. The official docs do
   not, however, prescribe an “artifact adapter” recipe or recommend combining all three fields in
   one package. [OC-PLUGIN-DOC] [OC-ENTRY] [OC-HOOK-API] [OC-PLUGIN-RUNTIME]
2. **Automatic package-layout convention — no.** None of the verified packages below relies on
   OpenCode noticing a cached `skill(s)/`, `command(s)/`, or `agent(s)/` folder by name. Every working
   package either adds a skill path, constructs command/agent config objects, or runs its own loader.
   The counterexamples likewise show that merely publishing or checking in those folders does not
   turn an npm package into an OpenCode artifact package. [OC-CONFIG] [OC-SKILL] [OC-COMMAND]
   [OC-AGENT]
3. **Ecosystem prevalence — established but heterogeneous, not a standard layout.** A bounded source
   audit of seven package-installed repositories found five exposing bundled skills in some form
   (four through native package-relative `skills.paths`, one through a custom loader), five mutating
   `config.command`, and four mutating `config.agent`. One of the seven, `opencode-froggy`, performs
   all three schema-backed registrations in one hook. `oh-my-openagent` also exposes all three
   user-facing kinds, but its bundled skills are converted into command/tool data rather than
   registered through `skills.paths`. These are verified examples, not an ecosystem census;
   repository search cannot establish a denominator or usage share.

The defensible classification is therefore **supported-but-custom composition**. Package plugins and
`config` hooks are the standard extension mechanism; package-root artifact discovery is not a
standard; and a single skills-plus-commands-plus-agents adapter is a real ecosystem design, but not
an OpenCode-documented recommendation or a dominant convention in this sample.

### 8.2 Verified package-installed repositories

#### Superpowers

At v6.1.0 commit [`f268f7c`](https://github.com/obra/superpowers/tree/f268f7c953744036f0fa7e9d4b73535c04e57cb8),
the root `main` is `.opencode/plugins/superpowers.js`. Its `config` hook explicitly adds the
package-relative `skills/` directory to `config.skills.paths`; it does not register commands or
agents. The documented OpenCode installation uses the named Git package spec
`superpowers@git+https://github.com/obra/superpowers.git#v6.1.0`. The detailed trace remains in
section 3. [SP-PACKAGE] [SP-PLUGIN] [SP-DOC]

#### Mem0 OpenCode plugin

At tag `opencode-v0.2.2`, commit
[`5e7adc4`](https://github.com/mem0ai/mem0/tree/5e7adc4d1264bb49ab20cf8c70e4807295d77ae2),
`@mem0/opencode-plugin` publishes `main: "dist/index.js"` and includes `opencode-skills` in its
package files. The `config` hook resolves that directory relative to the imported module, appends it
to `config.skills.paths`, then reads each `SKILL.md` and writes a corresponding entry to
`config.command`. It does not register an agent. The project documents
`opencode plugin @mem0/opencode-plugin`. The folder is bundled, but both native skill discovery and
slash-command exposure are explicit adapter behavior. [MEM0-PACKAGE] [MEM0-PLUGIN] [MEM0-DOC]

#### You.com agent skills

At commit
[`2ed8355`](https://github.com/youdotcom-oss/agent-skills/tree/2ed83558991da7d09e5880fe2d119002bbcf060b),
`@youdotcom-oss/opencode` declares `main: "./plugin.ts"`. Its build script copies the repository's
shared skills into the package's `skills/` directory, and its `config` hook explicitly pushes that
package-relative directory into `config.skills.paths`. It also configures MCP servers, but does not
mutate `config.command` or `config.agent`. Installation is documented as
`opencode plugin @youdotcom-oss/opencode` or the equivalent package name in the `plugin` array. This
is generated package content plus explicit registration, not cache-layout discovery. [YOU-PACKAGE]
[YOU-BUILD] [YOU-PLUGIN] [YOU-DOC]

#### oh-my-openagent / oh-my-opencode

At tag `v4.19.4`, commit
[`b072d27`](https://github.com/code-yeongyu/oh-my-openagent/tree/b072d279110bdda2c6ac2525d0d24dc54d16148a),
the published root package retains the name `oh-my-opencode`; both `main` and
`exports["./server"]` resolve to `dist/index.js`, with the server export taking precedence in
OpenCode's loader. Its `config` handler calls separate agent and command assemblers. The agent path
builds package-owned TypeScript definitions and assigns `config.agent`; the command path assigns
`config.command` from built-in commands, built-in skills, discovered compatibility content, and
plugin components. [OMO-PACKAGE] [OMO-CONFIG]

Its bundled skills are an important qualification: the package includes skill directories, but the
OpenCode adapter does not expose the ordinary bundle by appending a package path to
`config.skills.paths`. Instead, its own loader reads the shared-skills package, merges the resulting
definitions for its custom skill tool, and converts built-in/loaded skills into command definitions.
An optional runtime-security feature separately uses `skills.urls`; that does not make the package
folders automatic. The documented installer is `bunx oh-my-openagent install`, which registers the
package in OpenCode configuration. This is a proven all-kinds plugin suite, but a custom composition,
not evidence for package-root scanning. [OMO-SKILLS] [OMO-COMMANDS] [OMO-DOC]

#### micode

At commit
[`1cf531b`](https://github.com/vtemian/micode/tree/1cf531b7f0e5470a720d39997c6703d3af19de24),
`micode` declares `main: "dist/index.js"`. The plugin imports TypeScript agent definitions, merges
user overrides, assigns the resulting objects to `config.agent`, and merges a built-in command map
into `config.command`. It has no bundled skill-path registration. The README installs it with
`{"plugin":["micode"]}`. Agents and commands are data constructed by the imported package module;
no cached Markdown folder is auto-scanned. [MICODE-PACKAGE] [MICODE-PLUGIN] [MICODE-DOC]

#### opencode-beads

At commit
[`1622668`](https://github.com/joshuadavidthomas/opencode-beads/tree/1622668e80fceba01cff30755b787e198f53f7e0),
`opencode-beads` declares `main: "src/plugin.ts"` and publishes `src/` plus `vendor/`. Its loader
explicitly reads vendored Markdown under `vendor/commands/` and `vendor/agents/task-agent.md`, parses
the frontmatter and bodies into config objects, and the `config` hook merges those objects into
`config.command` and `config.agent`. It registers no skills path. The documented spec is
`opencode-beads`, optionally pinned as `opencode-beads@0.7.0`. This is the clearest generated/indirect
case in the sample: runtime parsing is traced through to the actual two config assignments.
[BEADS-PACKAGE] [BEADS-VENDOR] [BEADS-PLUGIN] [BEADS-DOC]

#### opencode-froggy

At tag `v0.12.0`, commit
[`ca3e228`](https://github.com/smartfrog/opencode-froggy/tree/ca3e228d53b62be2cd882c91a0eb091d94909371),
`opencode-froggy` declares `main: "dist/index.js"` and publishes package-relative `agent/`,
`command/`, and `skill/` directories. Its module explicitly loads all three directories. In the
`config` hook it merges parsed agents into `config.agent`, parsed commands into `config.command`, and
adds `skill/` to `config.skills.paths`. The project documents `{"plugin":["opencode-froggy"]}`.
This is a verified single-adapter implementation of the proposed three-kind shape—and also direct
evidence that folder names alone are insufficient, because the plugin contains explicit loading and
registration code for every kind. [FROGGY-PACKAGE] [FROGGY-PLUGIN] [FROGGY-DOC]

### 8.3 Counterexamples and non-examples

- **Alibaba OpenCodeReview** at commit
  [`adbd4fd`](https://github.com/alibaba/open-code-review/tree/adbd4fd65ce4f9e976a158504333df1ff41ff717)
  has an OpenCode TypeScript plugin that mutates `config.command`, but its nearby package manifest is
  private development metadata with no `main` or `exports["./server"]`. Its installation guide uses
  `curl` to copy the TypeScript file into a global or project OpenCode plugin directory. The sibling
  `skills/` tree is not registered by that OpenCode plugin. It proves local-plugin config mutation,
  not package-cache artifact discovery. [OCR-PACKAGE] [OCR-PLUGIN] [OCR-DOC]
- **`@wbern/agent-instructions`** at commit
  [`0743a53`](https://github.com/wbern/agent-instructions/tree/0743a5330a66fb3e73fe72b9bc37e0b8dbfde84f)
  publishes a CLI `bin` and no plugin `main` or server export. Although its repository contains
  generated `.opencode/commands` and `.opencode/skills`, its documented OpenCode path runs the CLI to
  write project- or user-scope artifacts. An npm package is involved, but the package is an installer,
  not an OpenCode package adapter. [WBERN-PACKAGE] [WBERN-DOC]
- **`wildwasser/opencode-agents`** at commit
  [`c850b15`](https://github.com/wildwasser/opencode-agents/tree/c850b153fe02c8f30b863dd515960012f5e145fe)
  checks in `.opencode/agent` and `.opencode/skills`, but documents cloning the repository and running
  `install.sh`. That script copies agent Markdown into the global OpenCode config; the example config
  is copied separately, and the skills are project-local to a checkout unless separately staged.
  There is no package entrypoint or `config` hook. [OC-AGENTS-INSTALL] [OC-AGENTS-DOC]

The search also found many project repositories with `.opencode/*` content and many plugin modules
that mutate unrelated config fields. They are intentionally excluded from the prevalence count when
the first-party manifest, installation path, or runtime mutation could not prove package-cache
registration. Consequently, the counts above answer “does the pattern exist, and in what verified
forms?” rather than “what percentage of all OpenCode plugins use it?”

### 8.4 Compact result

| Repo (pin) | Skills | Commands | Agents | Mechanism | Verdict |
|---|---|---|---|---|---|
| `obra/superpowers` (`v6.1.0`) | `skills.paths` | No | No | `main` + config hook | Package adapter, skill-only |
| `mem0ai/mem0` (`opencode-v0.2.2`) | `skills.paths` | `config.command` from skills | No | `main` + package-relative loader | Package adapter, skills + commands |
| `youdotcom-oss/agent-skills` (`2ed8355`) | `skills.paths` | No | No | build-copy + `main` + config hook | Package adapter, skill-only |
| `code-yeongyu/oh-my-openagent` (`v4.19.4`) | Custom loader/tool | `config.command` | `config.agent` | `exports["./server"]` + assemblers | All kinds, custom skill composition |
| `vtemian/micode` (`1cf531b`) | No | `config.command` | `config.agent` | `main` + TypeScript registries | Package adapter, commands + agents |
| `joshuadavidthomas/opencode-beads` (`1622668`) | No | `config.command` | `config.agent` | `main` + vendored Markdown parser | Package adapter, commands + agent |
| `smartfrog/opencode-froggy` (`v0.12.0`) | `skills.paths` | `config.command` | `config.agent` | `main` + three explicit loaders | Exact all-three package adapter |
| `alibaba/open-code-review` (`adbd4fd`) | Not registered | Local plugin mutation | No | Copy `.ts` into config | Non-example: no package entrypoint |
| `wbern/agent-instructions` (`0743a53`) | Generated/copied | Generated/copied | No | CLI installer | Non-example: npm package, not plugin |
| `wildwasser/opencode-agents` (`c850b15`) | Project-local | No | Copied | Clone + shell installer | Non-example: config-tree staging |

## Primary sources

- **[OC-PACKAGE]** Pinned OpenCode v1.18.7,
  `packages/core/src/npm.ts:43-60,72-137`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/core/src/npm.ts#L43-L137).
- **[OC-ENTRY]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/plugin/shared.ts:22-34,54-59,81-114,136-169,171-235`;
  official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/plugin/shared.ts#L22-L235).
- **[OC-LOADER]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/plugin/loader.ts:76-145,203-236`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/plugin/loader.ts#L76-L236).
- **[OC-PLUGIN-RUNTIME]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/plugin/index.ts:95-121,177-249`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/plugin/index.ts#L97-L251).
- **[OC-BOOTSTRAP]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/project/bootstrap.ts:32-45`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/project/bootstrap.ts#L32-L45).
- **[OC-CONFIG]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/config/config.ts:416-466`, plus
  `packages/opencode/src/config/plugin.ts:18-30`; official v1.18.14
  [config permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/config/config.ts#L416-L466)
  and [plugin-directory permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/config/plugin.ts#L18-L30).
- **[OC-SKILL]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/skill/index.ts:21-25,173-233`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/skill/index.ts#L21-L233).
- **[OC-REMOTE-SKILLS]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/skill/discovery.ts:13-21,35-131`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/skill/discovery.ts#L13-L131).
- **[OC-COMMAND]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/config/command.ts:13-39`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/config/command.ts#L13-L39).
- **[OC-AGENT]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/config/agent.ts:11-31`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/config/agent.ts#L11-L31).
- **[OC-SKILL-COMMAND]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/command/index.ts:88-152`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/command/index.ts#L88-L152).
- **[OC-HOOK-API]** Pinned OpenCode v1.18.7,
  `packages/plugin/src/index.ts:222-228,282-296`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/plugin/src/index.ts#L222-L296).
- **[OC-PLUGIN-SCHEMA]** Pinned OpenCode v1.18.7,
  `packages/core/src/v1/config/plugin.ts:5-9`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/core/src/v1/config/plugin.ts#L5-L9).
- **[OC-SKILLS-SCHEMA]** Pinned OpenCode v1.18.7,
  `packages/core/src/v1/config/skills.ts:5-12`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/core/src/v1/config/skills.ts#L5-L12).
- **[OC-COMMAND-SCHEMA]** Pinned OpenCode v1.18.7,
  `packages/core/src/v1/config/command.ts:5-13`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/core/src/v1/config/command.ts#L5-L13).
- **[OC-AGENT-SCHEMA]** Pinned OpenCode v1.18.7,
  `packages/core/src/v1/config/agent.ts:12-40,83-89`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/core/src/v1/config/agent.ts#L12-L89).
- **[OC-COMMAND-RUNTIME]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/command/index.ts:55-103`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/command/index.ts#L55-L103).
- **[OC-AGENT-RUNTIME]** Pinned OpenCode v1.18.7,
  `packages/opencode/src/agent/agent.ts:267-294`; official v1.18.14
  [permalink](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/opencode/src/agent/agent.ts#L269-L296).
- **[OC-PLUGIN-DOC]** Official v1.18.14 plugin docs source,
  [`packages/web/src/content/docs/plugins.mdx:29-70`](https://github.com/anomalyco/opencode/blob/65cf14df16c191f3e9684f0d9a8bae69103ced6d/packages/web/src/content/docs/plugins.mdx#L29-L70),
  rendered at <https://opencode.ai/docs/plugins/>.
- **[SCHEMA]** Official current schema: <https://opencode.ai/config.json> (inspected
  2026-08-07; `plugin`, `skills.paths`, `skills.urls`, `command`, and `agent`).
- **[SP-PACKAGE]** Vendored `external/superpowers/package.json:1-22`; official v6.1.0
  [permalink](https://github.com/obra/superpowers/blob/f268f7c953744036f0fa7e9d4b73535c04e57cb8/package.json#L1-L22).
- **[SP-PLUGIN]** Vendored
  `external/superpowers/.opencode/plugins/superpowers.js:13-15,49-57,61-113,115-138`;
  official v6.1.0
  [permalink](https://github.com/obra/superpowers/blob/f268f7c953744036f0fa7e9d4b73535c04e57cb8/.opencode/plugins/superpowers.js#L13-L138).
- **[SP-DOC]** Vendored `external/superpowers/docs/README.opencode.md:5-21,83-103,120-157`;
  official v6.1.0
  [installation guide](https://github.com/obra/superpowers/blob/f268f7c953744036f0fa7e9d4b73535c04e57cb8/.opencode/INSTALL.md)
  and [OpenCode guide](https://github.com/obra/superpowers/blob/f268f7c953744036f0fa7e9d4b73535c04e57cb8/docs/README.opencode.md).
- **[MEM0-PACKAGE]** Mem0 `opencode-v0.2.2`,
  [OpenCode package manifest](https://github.com/mem0ai/mem0/blob/5e7adc4d1264bb49ab20cf8c70e4807295d77ae2/integrations/mem0-plugin/.opencode-plugin/package.json).
- **[MEM0-PLUGIN]** Mem0 `opencode-v0.2.2`,
  [plugin source](https://github.com/mem0ai/mem0/blob/5e7adc4d1264bb49ab20cf8c70e4807295d77ae2/integrations/mem0-plugin/.opencode-plugin/opencode-mem0.ts)
  (`registerCommands` and the returned `config` hook).
- **[MEM0-DOC]** Mem0 `opencode-v0.2.2`,
  [OpenCode install and hook guide](https://github.com/mem0ai/mem0/blob/5e7adc4d1264bb49ab20cf8c70e4807295d77ae2/integrations/mem0-plugin/.opencode-plugin/README.md).
- **[YOU-PACKAGE]** You.com agent skills commit `2ed8355`,
  [OpenCode package manifest](https://github.com/youdotcom-oss/agent-skills/blob/2ed83558991da7d09e5880fe2d119002bbcf060b/packages/opencode/package.json).
- **[YOU-BUILD]** You.com agent skills commit `2ed8355`,
  [skill-copy build script](https://github.com/youdotcom-oss/agent-skills/blob/2ed83558991da7d09e5880fe2d119002bbcf060b/packages/opencode/scripts/build.ts).
- **[YOU-PLUGIN]** You.com agent skills commit `2ed8355`,
  [OpenCode plugin](https://github.com/youdotcom-oss/agent-skills/blob/2ed83558991da7d09e5880fe2d119002bbcf060b/packages/opencode/plugin.ts).
- **[YOU-DOC]** You.com agent skills commit `2ed8355`,
  [OpenCode package guide](https://github.com/youdotcom-oss/agent-skills/blob/2ed83558991da7d09e5880fe2d119002bbcf060b/packages/opencode/README.md).
- **[OMO-PACKAGE]** oh-my-openagent v4.19.4,
  [root package manifest](https://github.com/code-yeongyu/oh-my-openagent/blob/b072d279110bdda2c6ac2525d0d24dc54d16148a/package.json).
- **[OMO-CONFIG]** oh-my-openagent v4.19.4,
  [plugin module](https://github.com/code-yeongyu/oh-my-openagent/blob/b072d279110bdda2c6ac2525d0d24dc54d16148a/packages/omo-opencode/src/testing/create-plugin-module.ts),
  [config handler](https://github.com/code-yeongyu/oh-my-openagent/blob/b072d279110bdda2c6ac2525d0d24dc54d16148a/packages/omo-opencode/src/plugin-handlers/config-handler.ts),
  and [agent assembly](https://github.com/code-yeongyu/oh-my-openagent/blob/b072d279110bdda2c6ac2525d0d24dc54d16148a/packages/omo-opencode/src/plugin-handlers/agent-config-assembly.ts).
- **[OMO-COMMANDS]** oh-my-openagent v4.19.4,
  [command config handler](https://github.com/code-yeongyu/oh-my-openagent/blob/b072d279110bdda2c6ac2525d0d24dc54d16148a/packages/omo-opencode/src/plugin-handlers/command-config-handler.ts)
  and [skill-to-command conversion](https://github.com/code-yeongyu/oh-my-openagent/blob/b072d279110bdda2c6ac2525d0d24dc54d16148a/packages/skills-loader-core/src/features/opencode-skill-loader/skill-definition-record.ts).
- **[OMO-SKILLS]** oh-my-openagent v4.19.4,
  [skill context](https://github.com/code-yeongyu/oh-my-openagent/blob/b072d279110bdda2c6ac2525d0d24dc54d16148a/packages/omo-opencode/src/plugin/skill-context.ts)
  and [custom shared-skill loader](https://github.com/code-yeongyu/oh-my-openagent/blob/b072d279110bdda2c6ac2525d0d24dc54d16148a/packages/skills-loader-core/src/features/opencode-skill-loader/loader.ts).
- **[OMO-DOC]** oh-my-openagent v4.19.4,
  [installation guide](https://github.com/code-yeongyu/oh-my-openagent/blob/b072d279110bdda2c6ac2525d0d24dc54d16148a/docs/guide/installation.md).
- **[MICODE-PACKAGE]** micode commit `1cf531b`,
  [package manifest](https://github.com/vtemian/micode/blob/1cf531b7f0e5470a720d39997c6703d3af19de24/package.json).
- **[MICODE-PLUGIN]** micode commit `1cf531b`,
  [plugin config hook](https://github.com/vtemian/micode/blob/1cf531b7f0e5470a720d39997c6703d3af19de24/src/index.ts)
  and [agent registry](https://github.com/vtemian/micode/blob/1cf531b7f0e5470a720d39997c6703d3af19de24/src/agents/index.ts).
- **[MICODE-DOC]** micode commit `1cf531b`,
  [installation and artifact guide](https://github.com/vtemian/micode/blob/1cf531b7f0e5470a720d39997c6703d3af19de24/README.md).
- **[BEADS-PACKAGE]** opencode-beads commit `1622668`,
  [package manifest](https://github.com/joshuadavidthomas/opencode-beads/blob/1622668e80fceba01cff30755b787e198f53f7e0/package.json).
- **[BEADS-VENDOR]** opencode-beads commit `1622668`,
  [vendored Markdown loaders](https://github.com/joshuadavidthomas/opencode-beads/blob/1622668e80fceba01cff30755b787e198f53f7e0/src/vendor.ts).
- **[BEADS-PLUGIN]** opencode-beads commit `1622668`,
  [plugin config hook](https://github.com/joshuadavidthomas/opencode-beads/blob/1622668e80fceba01cff30755b787e198f53f7e0/src/plugin.ts).
- **[BEADS-DOC]** opencode-beads commit `1622668`,
  [installation guide](https://github.com/joshuadavidthomas/opencode-beads/blob/1622668e80fceba01cff30755b787e198f53f7e0/README.md).
- **[FROGGY-PACKAGE]** opencode-froggy v0.12.0,
  [package manifest](https://github.com/smartfrog/opencode-froggy/blob/ca3e228d53b62be2cd882c91a0eb091d94909371/package.json).
- **[FROGGY-PLUGIN]** opencode-froggy v0.12.0,
  [three-kind loader and config hook](https://github.com/smartfrog/opencode-froggy/blob/ca3e228d53b62be2cd882c91a0eb091d94909371/src/index.ts).
- **[FROGGY-DOC]** opencode-froggy v0.12.0,
  [installation and artifact guide](https://github.com/smartfrog/opencode-froggy/blob/ca3e228d53b62be2cd882c91a0eb091d94909371/README.md).
- **[OCR-PACKAGE]** Alibaba OpenCodeReview commit `adbd4fd`,
  [private development manifest](https://github.com/alibaba/open-code-review/blob/adbd4fd65ce4f9e976a158504333df1ff41ff717/plugins/open-code-review/opencode/package.json).
- **[OCR-PLUGIN]** Alibaba OpenCodeReview commit `adbd4fd`,
  [local OpenCode plugin](https://github.com/alibaba/open-code-review/blob/adbd4fd65ce4f9e976a158504333df1ff41ff717/plugins/open-code-review/opencode/open-code-review.ts).
- **[OCR-DOC]** Alibaba OpenCodeReview commit `adbd4fd`,
  [copy-based OpenCode installation](https://github.com/alibaba/open-code-review/blob/adbd4fd65ce4f9e976a158504333df1ff41ff717/plugins/open-code-review/opencode/README.md).
- **[WBERN-PACKAGE]** agent-instructions commit `0743a53`,
  [CLI-only package manifest](https://github.com/wbern/agent-instructions/blob/0743a5330a66fb3e73fe72b9bc37e0b8dbfde84f/package.json).
- **[WBERN-DOC]** agent-instructions commit `0743a53`,
  [copy/generation installation guide](https://github.com/wbern/agent-instructions/blob/0743a5330a66fb3e73fe72b9bc37e0b8dbfde84f/README.md).
- **[OC-AGENTS-INSTALL]** opencode-agents commit `c850b15`,
  [copy installer](https://github.com/wildwasser/opencode-agents/blob/c850b153fe02c8f30b863dd515960012f5e145fe/install.sh).
- **[OC-AGENTS-DOC]** opencode-agents commit `c850b15`,
  [clone-and-copy guide](https://github.com/wildwasser/opencode-agents/blob/c850b153fe02c8f30b863dd515960012f5e145fe/README.md).
- **[NPM-SPEC]** Official npm package-spec documentation,
  <https://docs.npmjs.com/cli/v11/using-npm/package-spec#git-urls>, and the first-party
  [`npm-package-arg` contract](https://github.com/npm/npm-package-arg#using).
- **[LOCAL-CONTRACT]** `AGENTS.md:12-34,36-50`; `docs/adr/0006-output-is-a-transformation.md:13-59`.
- **[LOCAL-OUTPUT]** `README.md:3-17,57-67`; `package.json:1-28`;
  `docs/adr/0002-multi-harness-output.md:13-20`; generated examples
  `opencode/commands/brainstorming.md:1-9` and
  `opencode/agents/roslyn-incremental-generator-specialist.md:1-10`.

## Concise conclusion

The Git URL can deliver a package containing arbitrary files into OpenCode's package cache, but it
cannot natively turn an arbitrary package-root `skills/`, `commands/`, and `agents/` tree into
OpenCode artifacts. Superpowers succeeds because its imported plugin explicitly registers its
package-relative skills path and injects its bootstrap. A complete cached distribution for this
repository needs a package adapter that exposes the already transformed skills and registers
generated command/agent config; `skills.urls` is only a separate skill-file protocol.

| Option | Skills | Commands | Agents | One Git package spec | Recommendation |
|---|---:|---:|---:|---:|---|
| Stage `opencode/` into config | Yes | Yes | Yes | No | Keep until adapter is proven |
| Root OpenCode adapter package | Yes | Yes, via config registration | Yes, via config registration | Yes | Preferred target |
| `skills.urls` protocol | Yes | No | No | No | Skill-only channels only |
