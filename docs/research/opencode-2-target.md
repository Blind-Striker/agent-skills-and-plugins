# OpenCode 2 as the OpenCode target

Date: 2026-10-08

> **Dated evidence and decision history, not current local policy.** This note records the OpenCode
> 2 evidence behind the 2026-10-08 decision to target OpenCode 2 only, to give manual skills a native
> skill shape, and to namespace every OpenCode ID. The accepted decisions live in
> [ADR-0002](../adr/0002-multi-harness-output.md) (harness-native output and OpenCode namespacing)
> and [ADR-0005](../adr/0005-invocation-intent-in-the-manifest.md) (invocation intent). Current
> emission lives in [Transformation and emission](../architecture/transformation-and-emission.md),
> reference spelling in [References and linking](../architecture/references-and-linking.md),
> Destination and installer mechanics in
> [Distribution and installation](../architecture/distribution-and-installation.md), the per-harness
> invocation table in [`curation/SCHEMA.md`](../../curation/SCHEMA.md), vocabulary in
> [`CONTEXT.md`](../../CONTEXT.md), and the implementation gaps and follow-up measurements in
> [`docs/ROADMAP.md`](../ROADMAP.md). The earlier OpenCode 1.18.x evidence stays in
> [Skill invocation across harnesses](skill-invocation-across-harnesses.md),
> [Harness adapters](harness-adapters.md), and
> [OpenCode plugin packages](2026-08-07-opencode-plugin-package-artifacts.md).

## Question and scope

Every OpenCode claim in this repository up to commit `9442efa` rested on OpenCode 1.18.x: 1.18.7 for
discovery and invocation, 1.18.11 for the stub-command records, and 1.18.18 for the installer record
and the plugin-package source analysis. Both curator machines had meanwhile moved to OpenCode 2. The
questions were:

- What OpenCode 2 discovers, how it names what it discovers, and how a user or the model reaches it.
- Whether the OpenCode 1 design still holds. That design was: manual items become command stubs
  with a parked `skills/<name>/BODY.md`, `both` items get a duplicate command, references use bare
  names, and the installer refuses `OPENCODE_CONFIG_DIR`.
- Whether OpenCode 2 can carry a per-Module namespace, and with which separator.
- Which distribution routes OpenCode 2 opens, and which risks come with dropping OpenCode 1.

## Evidence boundary

- **Source pin.** [anomalyco/opencode](https://github.com/anomalyco/opencode) tag `v2.0.23`, commit
  `0fd7e2829449b052abf0078666669302923d77af` (2026-10-05). This matches the measured binary
  (`opencode --version` printed `opencode v2.0.23`). Unless another commit is named, every
  `packages/...:line` citation below is a path in anomalyco/opencode at `0fd7e28`. The newest tag at
  the evidence date was `v2.0.25`, commit `b44eea9e204024db2b480a136ece29d790d5f593` (2026-10-08).
  The OpenCode 1 comparison tag was `v1.18.35`, commit `53d1eabb61e21162157817bf677da0a4ad3332e3`,
  whose runtime lives under `packages/opencode/`.
- **v2.0.23 to v2.0.25.** `git diff --stat v2.0.23 v2.0.25` showed no change to the skill, command,
  agent, or compatibility config plugins, the skill tool, `session/prompt.ts`, the TUI prompt and
  autocomplete code, or `util/src/global*`. Two related changes did land. The shared frontmatter
  parser `packages/core/src/config/markdown.ts` gained a gray-matter cache fix (`matter(content, {})`),
  so malformed YAML can behave differently on 2.0.23. The default `external_directory` ask rule was
  removed from `packages/schema/src/agent.ts`. Nothing was re-run on 2.0.25.
- **Documentation.** The production OpenCode 2 docs at <https://opencode.ai/v2/docs/> (63 pages,
  index <https://opencode.ai/v2/llms.txt>) are built from `services/www/src/docs/content`; the
  skills page matches source word for word. `packages/web/src/content/docs` holds the OpenCode 1
  docs and still describes `opencode/slash`. <https://dev.opencode.ai/v2/docs/> is a stale beta copy:
  it names the binary `opencode2`, installs `@opencode-ai/cli@next`, and documents a skill `slash`
  field. This note ignores it except where it conflicts with production.
- **Measurements.** All measurements ran on 2026-10-08 against isolated `opencode serve` instances.
  Each instance had relocated home, XDG, and database variables, a service config of
  `{"disabled":true}`, and, in later probes, `OPENCODE_DISABLE_PROJECT_CONFIG=1`. Nothing was
  written to a real OpenCode profile. A before-and-after comparison of 384 real config files showed
  no difference. Introspection used the HTTP API, for example
  `curl -u opencode:<password> -G http://127.0.0.1:<port>/api/skill --data-urlencode "location[directory]=<project>"`.
  Most measurements are Windows-only. One small model sample used the free provider model
  `opencode/fledge-alpha-free`. None of these measurements is a committed experiment record yet.
  They are prose evidence (tier 0 in the
  [record tiers](../../experiments/harness-invocation/records/README.md#evidence-tiers)). The
  committed discovery check and model record are follow-ups tracked in the roadmap.
- **Labels.** Each claim is marked *documented* (production docs or upstream issues), *source*
  (read at the pin), *measured* (observed in a probe), or *inferred* (reasoned from the others).

## 1. Release status and channels

- *Measured.* npm `@opencode/cli` dist-tag `latest` was `2.0.25` (published 2026-10-08T09:16Z).
  `2.0.0` was published 2026-09-11 and `2.0.23` on 2026-10-05. Tags `v2.0.0` to `v2.0.25` exist,
  but GitHub Releases has no OpenCode 2 entry: `Latest` was `v1.18.35` (2026-10-06), and
  `gh release view v2.0.23 -R anomalyco/opencode` returned `release not found`. Commands:
  `npm view @opencode/cli dist-tags time --json`, `gh release list -R anomalyco/opencode`,
  `gh api repos/anomalyco/opencode/tags`.
- *Measured.* Every default channel still installs OpenCode 1. npm `opencode-ai` `latest` was
  `1.18.35` (`npm view opencode-ai dist-tags --json`). The `opencode.ai/install` script downloads
  the GitHub `releases/latest` asset. <https://opencode.ai/docs/> is the OpenCode 1 documentation,
  with a banner announcing OpenCode 2. <https://opencode.ai/changelog> lists only 1.18.x releases,
  and OpenCode 1 still ships about weekly.
- *Documented.* OpenCode 2 ships through parallel opt-in channels:
  `curl https://opencode.ai/v2/install`, `brew install anomalyco/tap/opencode-v2`,
  `npm install -g @opencode/cli`, and the AUR package `opencode-beta`. OpenCode 1 and OpenCode 2
  both use the `opencode` command and are no longer installed side by side by default
  (<https://opencode.ai/v2/docs/migrate-v1/>). The migration page gives no OpenCode 1 support
  timeline. Its only rollback advice is to keep a copy of the OpenCode 1 setup.
- *Measured.* The npm package `@opencode/cli` maps both `opencode` and `opencode2` to the same
  executable, so the dev-docs name is stale wording, not a broken command.
- *Documented.* Production docs no longer call the plugin API, the client, or the SDK beta. The
  surfaces still marked unstable are the `experimental.*` hooks and config, the shell scanner, and
  `cli.json` `experimental`. Agent `request` is kept but not yet sent, and `share` is accepted but
  unsupported.
- *Measured.* OpenCode 2 is the line under active churn: 348 open issues carried the `2.0` label.
  Skill-related ones include #41030, #43742, #44915, #46568, #47285, #47505, #49891, #43872, and
  #51753.
- *Measured.* Both curator machines, a Windows workstation and a Linux host, ran `opencode v2.0.23`.
  On Windows, the managed background service binary and OpenCode Desktop also reported 2.0.23. Both
  real profiles held schema-1 Install state with four Modules and 102 skill folders owned by this
  repository, of which 14 were parked `BODY.md` folders.
- *Measured.* Public exposure of the OpenCode installer was small. The repository had 0 stars and
  0 forks, and the Release assets had 6 downloads for `installer-v0.3.0` and 2 each for `v0.2.0` and
  `v0.1.0` (`gh api repos/{owner}/{repo}/releases`).
- *Documented.* The intentional OpenCode 2 breaks are the plugin API, the server API and client
  contracts, and the move from layered `tui.json(c)` to one global `cli.json`. Supported OpenCode 1
  config fields, agent and command definitions, and skills are meant to keep working. If one breaks,
  the guide calls that a compatibility bug. OpenCode 1 plugin implementations do not run on
  OpenCode 2.

## 2. Configuration roots, project walk, and the background service

- *Source, measured.* The global config root is
  `OPENCODE_CONFIG_DIR ?? ($XDG_CONFIG_HOME || <home>/.config)/opencode`, with no AppData branch on
  Windows (`packages/util/src/global.ts:79`, `packages/util/src/global-roots.ts:4-18`). Home is
  `OPENCODE_TEST_HOME ?? os.homedir()` (`packages/util/src/global.ts:16-18`). `opencode debug paths`
  under the probe environment printed the relocated root.
- *Source, measured.* In OpenCode 2, `OPENCODE_CONFIG_DIR` **replaces** the global root. Setting
  it moved the printed config root. In OpenCode 1 the variable added an extra config directory beside
  the global root (`v1.18.35:packages/opencode/src/config/paths.ts:23-40`).
- *Inferred.* The `??` operator keeps an empty-but-set `OPENCODE_CONFIG_DIR`, so on POSIX an empty
  value may not fall back to the XDG root. On Windows, `OPENCODE_CONFIG_DIR= opencode debug paths`
  still printed the XDG root (measured). The repository installer already treats an empty value as
  unset.
- *Source.* The server also reads `OPENCODE_CONFIG` (an extra config file), `OPENCODE_CONFIG_CONTENT`
  (inline config, highest priority), and `OPENCODE_DISABLE_PROJECT_CONFIG` or
  `OPENCODE_CONFIG_PROJECT_DISABLE`, which skips the project walk
  (`packages/cli/src/server-process.ts:113-118`, `packages/core/src/config/discovery.ts:34`). Config
  priority from low to high is: wellknown, global root, `OPENCODE_CONFIG`, direct project
  `opencode.json(c)` files, project `.opencode` directories, then `OPENCODE_CONFIG_CONTENT`
  (`packages/core/src/config.ts:185-237`).
- *Source, measured.* The project walk visits every ancestor of the working directory up to the
  **filesystem root**, collecting `.claude`, `.agents`, `.opencode`, and `opencode.json(c)` at each
  level (`packages/core/src/config/discovery.ts:33-46`; `packages/util/src/fs-util.ts:164-186`
  stops only at `stop` or the root). The skills docs say "up to the project root"
  (`services/www/src/docs/content/skills.mdx:50`). OpenCode 1 stopped at the worktree.
- *Source.* `~/.claude` and `~/.agents` are always-on **global** compatibility roots, whatever the
  working directory (`packages/core/src/config/discovery.ts:28-29,55-60,71-76`). Every session on a
  machine therefore loads `~/.claude/skills/**` at compatibility precedence. A probe that relocated
  home but sat inside the real home tree also picked up the real `~/.claude/skills` through the
  ancestor walk, because that directory then no longer matched a global root (measured).
- *Source.* OpenCode 2 has no `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS` or external-skills flag. The
  OpenCode 1 flags (`v1.18.35:packages/opencode/src/skill/index.ts:178-188`) are gone. The only
  switch is a `plugins` entry `-opencode.config.compatibility`, which removes `.claude` and
  `.agents` skill scanning together (`packages/core/src/plugin/supervisor.ts:38-45`). This is
  source-only and unmeasured.
- *Measured, source.* By default the CLI talks to a long-lived managed background service. Its
  registration file is `$XDG_STATE_HOME/opencode/service.json`, its CLI config is
  `<config root>/service.json`, and its default port is 49374 (`0xc0de`)
  (`packages/cli/src/services/server-connection.ts:41-43`,
  `packages/cli/src/services/service-config.ts:30-39,95-109`,
  `packages/client/src/effect/service.ts:181-182`). `--standalone` starts a private server instead.
  A running service reads its own environment (`packages/cli/src/server-process.ts:113` to
  `packages/server/src/routes.ts:123`), so `OPENCODE_CONFIG_DIR` or `XDG_*` in the caller's shell
  does not reach it. A probe `opencode debug config` tried to start the managed service and failed
  because the port was in use.
- *Inferred.* A long-lived service can lag a CLI upgrade until it restarts. On the measured
  Windows machine, the service and the PATH binary were the same install, reached through an nvm
  symlink, so no version skew existed at the evidence date.
- *Measured.* Isolating a lab needs relocated `HOME`, `USERPROFILE`, `OPENCODE_TEST_HOME`, the four
  `XDG_*` variables, and `OPENCODE_DB`. This is the same set the upstream
  `packages/cli/script/service-smoke.ts:21-31` uses. The lab also needs a service config of
  `{"disabled":true}` or `--standalone`, and either a lab directory outside the real home or
  `OPENCODE_DISABLE_PROJECT_CONFIG=1`. `tmp` stays at the OS temp directory.
- *Measured.* The OpenCode 2 CLI has no skill or command subcommand. `debug` has only `agents`,
  `config`, and `paths`, and `opencode run` has no `--command` or `--variant` (`opencode --help`,
  `opencode debug --help`, `opencode run --help`). One-shot introspection returned empty lists,
  because the first request races the asynchronous location load: `opencode debug agents` printed
  `[]`, and `opencode api --standalone skill.list` returned no data. A persistent
  `opencode serve --hostname 127.0.0.1 --port <port> --print-logs` with Basic auth returned the full
  lists after about 2 seconds. The skill routes are annotated "Experimental skill routes"
  (`packages/protocol/src/groups/skill.ts:22-27`).

## 3. Skill discovery and precedence

- *Source, measured.* The global root and each project `.opencode` directory are scanned in both
  `skill/` and `skills/` (`packages/core/src/config/plugin/skill.ts:85-88`). The file glob is
  `{*.md,**/SKILL.md}`, symlinks followed (`packages/core/src/config/plugin/skill.ts:121`). The
  compatibility plugin scans `<root>/skills` for `~/.claude`, `~/.agents`, and every project `.claude`
  and `.agents` (`packages/core/src/config/plugin/compatibility.ts:39-47`).
- *Measured.* The glob makes any root-level Markdown file a skill. A `skills/README.md` registered as
  skill `README`, and a root `flat-one.md` as `flat-one`. OpenCode 1 used `{skill,skills}/**/SKILL.md`
  and never picked up flat files (`v1.18.35:packages/opencode/src/skill/index.ts:23-25`).
- *Measured.* A nested `SKILL.md` below a skill directory registers as a separate skill under its
  own parent-folder name.
- *Measured.* A parked `skills/<name>/BODY.md` with no `SKILL.md` is **not** discovered. At the
  evidence date, the 14 parked folders in each real profile were reachable only through their
  command stubs.
- *Source, measured.* Skills live in a map keyed by ID, and a later add overwrites an earlier one
  (`packages/core/src/skill.ts:100-102`). The effective precedence from low to high is:
  1. built-in skills `opencode` and `report` (`packages/core/src/plugin/skill.ts:23-46`);
  2. plugin-contributed skills;
  3. `~/.claude/skills`, then project `.claude/skills` (farthest first);
  4. `.agents/skills` in the same order;
  5. the global config root's `skill/` and `skills/`;
  6. project `.opencode` skills (root toward the working directory);
  7. entries in the `skills` config array.

  This follows from `packages/core/src/plugin/supervisor.ts:91-95,143-152`,
  `packages/core/src/plugin/internal.ts:213-267`, and
  `packages/core/src/config/plugin/skill.ts:77-103,149-157`. On a cold start, a project `.opencode`
  copy of `dup` won over the global root, `~/.claude`, and a plugin copy (measured). A plugin skill
  that was hot-added later won until the next restart (measured). A project skill named `report`
  replaced the built-in (measured).
- *Source, documented.* The `skills` config key is a string array. Relative entries resolve from the
  working directory, not from the config file. `http(s)` entries are skill catalogs: an `index.json`
  lists `{name, version, files}`, files are downloaded same-origin into a versioned cache, and a
  root-level `SKILL.md` in a catalog gets the literal ID `SKILL`
  (`packages/core/src/config/plugin/skill.ts:89-101`,
  `packages/core/src/skill/discovery.ts:48-56,92-199`;
  <https://opencode.ai/v2/docs/skills/>, section Catalogs).

## 4. Skill ID and frontmatter

- *Source, measured.* The skill ID comes from the path. A `SKILL.md` takes its **parent folder's**
  name; a root-level `.md` takes its basename (`packages/core/src/config/plugin/skill-file.ts:45-48`).
  `group/deep/SKILL.md` became `deep`, not `group/deep`.
- *Source, measured.* Frontmatter `name` is only a display label that defaults to the ID
  (`packages/core/src/config/plugin/skill-file.ts:56`). A skill whose folder and `name` differed was
  listed by folder ID with the `name` as label.
- *Source, measured.* IDs are unconstrained branded strings (`packages/schema/src/skill.ts:8-12`).
  The docs recommend `^[a-z0-9]+(-[a-z0-9]+)*$` and 1 to 64 characters, but OpenCode 2 enforces
  neither the pattern, the length, nor a match between name and folder. `Bad_Name` registered.
- *Source, measured.* The parser decodes only `name`, `description`, `metadata`, and
  `disable-model-invocation` (`packages/core/src/config/plugin/skill-file.ts:9-14`). Other keys,
  including `license`, `compatibility`, `user-invocable`, and any `slash` key, are ignored. If
  `name` or `description` is not a string, the file is skipped and the skip is logged only at debug
  level (`name: 123` was skipped). A file with no frontmatter, or with malformed YAML, still
  registers under its path ID with no description, and its raw text, frontmatter included, becomes
  the content.
- *Source.* OpenCode 1 keyed skills by frontmatter `name`, skipped files without a string name, and
  warned on duplicates (`v1.18.35:packages/opencode/src/skill/index.ts:53-59,123-139`). The
  OpenCode 1 rule "the `name:` field wins over the directory name" recorded in
  [Skill invocation across harnesses](skill-invocation-across-harnesses.md#opencode) is reversed in
  OpenCode 2.

## 5. Model advertisement, the autoinvoke key, and version floors

- *Source.* At each model step, OpenCode lists the skills that the `skill` permission allows, that
  have a description, and that are not `autoinvoke: false`. Each entry renders `<id>`, `<name>`,
  and `<description>`, sorted by ID (`packages/core/src/skill/instructions.ts:16-33,73-80`). OpenCode
  1 rendered name, description, and location instead.
- *Source, measured.* `autoinvoke = metadata["opencode/autoinvoke"] ?? (disable-model-invocation ?
  false : undefined)` (`packages/core/src/config/plugin/skill-file.ts:49-51`). The flat key
  `opencode/autoinvoke` wins when both keys are set. Booleans accept true/yes/on/1 and
  false/no/off/0. The nested YAML form `metadata: {opencode: {autoinvoke: false}}` is not
  recognized. Fixtures with the flat key and with `disable-model-invocation: true` both listed
  `autoinvoke: false`, and the nested form listed nothing.
- *Source, measured.* Skills are **not** slash commands in OpenCode 2. The Command service holds
  the built-in commands, MCP prompts, file commands, and plugin commands
  (`packages/core/src/plugin/command.ts:28-81`). `/api/command` listed no skill, including a fixture
  that set `metadata.opencode/slash: "true"`. In OpenCode 1.18.35, every skill was automatically a
  `/<name>` command unless a command of that name existed
  (`v1.18.35:packages/opencode/src/command/index.ts:134-152`).
- *Source.* Version history, each found with `git log -S` and `git tag --contains` in a full clone:
  - `opencode/autoinvoke` arrived in commit `716f6658` (2026-07-01) and is in `v2.0.0`.
  - The skill `slash` field was removed in commit `199aabe9e` (2026-09-13, "refactor(protocol):
    simplify skill and reference contracts"). The first tag that contains it is `v2.0.4`. At
    `v2.0.0`, `skill-file.ts` still parsed `slash`. Docs PR #52752 (merged 2026-10-02) removed it
    from the docs.
  - `disable-model-invocation` support arrived in commit `f3a23e52b` (2026-10-02, "feat(core):
    support disable-model-invocation in skill frontmatter (#52747)"). The first tag that contains it
    is `v2.0.23`. `git show v2.0.22:packages/core/src/config/plugin/skill-file.ts` has no reference
    to the key.
- *Inferred.* The floor therefore depends on the emitted key. The flat
  `metadata: {"opencode/autoinvoke": false}` hides a skill from `v2.0.4`, the first release where
  skills are not slash commands. `disable-model-invocation: true` hides one only from `v2.0.23`;
  `v2.0.4` to `v2.0.22` would advertise such a skill to the model.
- *Source.* OpenCode 1.18.35 has no `disable-model-invocation` or autoinvoke handling
  (`git grep -i 'disable-model-invocation|autoinvoke' v1.18.35 -- packages/opencode/src` finds
  nothing). OpenCode 2-shaped output installed under OpenCode 1 therefore degrades: manual skills
  become model-visible, and every skill also becomes a `/name` command.

## 6. User attachment: unadvertised, not forbidden

- *Documented, source.* A user loads a skill by mentioning `@skill-id` or by picking it in the
  `/skills` dialog (<https://opencode.ai/v2/docs/skills/>). The TUI `@` autocomplete offers every
  registered skill with no filter (`packages/tui/src/component/prompt/autocomplete.tsx:427-441`).
  The `/skills` dialog lists skills by `name` and inserts `@<id>`
  (`packages/tui/src/component/dialog-skill.tsx:41-50`,
  `packages/tui/src/component/prompt/index.tsx:579-611`). Both read the unfiltered server list
  (`packages/server/src/handlers/skill.ts:7`, `packages/client/src/solid/data.ts:1919`). The web
  composer lists skills as `@` plus the ID (`packages/app/src/composer/model.ts:188-205`).
- *Source.* An attached skill arrives as a structured part `skills: [{id, mention}]`. It is resolved
  by exact ID and expanded through `Skill.prepare` with **no permission check**
  (`packages/core/src/session/prompt.ts:56-75`). The experimental route
  `POST /api/experimental/session/:sid/skill` also has no permission check
  (`packages/core/src/session/skill.ts:13`). On load, the model receives the body, the base
  directory, and up to 10 sibling file paths. The file list exists only for directory-form
  `SKILL.md` skills (`packages/core/src/skill.ts:36-68`).
- *Source.* The model's `skill` tool takes `{id}`, described as "The ID of an available skill or a
  skill explicitly referenced by the user". It runs an exact lookup plus a `skill` permission check,
  and it has **no autoinvoke check** (`packages/core/src/tool/plugin/skill.ts:12-14,46-60`).
- *Measured.* An `autoinvoke: false` skill stayed registered. Explicit attachment returned 204 and
  added its body to the session context. In a small model sample, the model's advertised list
  omitted the hidden skill, while visible namespaced skills were called by ID and completed.
- *Inferred.* `autoinvoke: false` therefore means **unadvertised, not forbidden**. The model is not
  offered the skill, but the skill tool loads any registered ID the model learns, for example from
  text in another skill's body.
- *Inferred.* Outside the TUI, an explicit path is weaker still. `opencode run` builds no
  `prompt.skills` attachment (`packages/cli/src/run/run.ts:83-140`), so a hidden skill is reachable
  there only when the model honours a textual request. This is plausible but unmeasured.
- *Source, documented.* Permissions are one ordered array of `{action, resource, effect}`. The last
  match wins, and an action with no matching rule defaults to `ask`
  (`packages/core/src/permission.ts:87-97`). For skills, the action is `skill` and the resource is the
  ID. The docs say `deny` "hides matching skills from the model and rejects loading", while `ask`
  advertises a skill but asks before loading.
- *Measured.* A rule `zzdeny.*: deny` removed `zzdeny.one` from the model's list. An explicit
  attachment of `zzdeny.one` still returned 204 and added its body.
- *Documented.* That bypass is disputed upstream:
  - #49891 (open, reported on 2.0.8): "skills: inline @mention injects a denied skill's body
    without a permission check".
  - #43872 (open, against a beta build): "skills: explicit user references cannot bypass denied
    model permission". In that build, an `@` mention of a denied skill was rejected, so the
    behaviour has already flipped once.
  - #41030 and #41288 (`next` builds) report that deny hides a skill from the model but not from the
    `/skills` selector.
  - Commit `728b2b605` (2026-09-19, "honor session permissions in skill and MCP discovery") may
    have changed the picker. Source at `0fd7e28` still shows an unfiltered picker.

  Behaviour under `deny` for `@` mentions is therefore unstable, and nothing should rely on it in
  either direction.
- *Source, measured.* A file-defined agent's default rules allow `external_directory` for the
  global config root, tmp, and tool output, and ask for every other external directory
  (`packages/core/src/agent.ts:59-64`). Files under the global config root are readable without a
  prompt.
- *Inferred.* Supporting files under `~/.claude/skills` or `~/.agents/skills` would prompt. The
  2.0.25 change that removed the default `external_directory` ask rule may alter this. It was not
  re-measured.

## 7. Commands

- *Source, measured.* File commands load from `{command,commands}/**/*.md` under the global root and
  project `.opencode` only. Nested paths become slash-separated names, and the legacy singular
  `command/` still works (`packages/core/src/config/plugin/command.ts:144,156-187`). Project
  `.claude/commands` was not discovered.
- *Source, documented.* Frontmatter is `description`, `agent`, `model` (`provider/model` or
  `provider/model#variant`), and `subagent`, which runs a background child session. `subtask` is a
  deprecated alias, and the body is the template (`packages/schema/src/config/command.ts:7-14`).
  The template expands `$1..$N`, `$ARGUMENTS`, and shell blocks. If neither placeholder is present,
  the input is appended. There is **no** `@file` expansion and no way to attach a skill
  (`packages/core/src/config/plugin/command.ts:189-250`).
- *Source.* A command whose `agent` names a subagent-mode agent runs in a background child session
  even without `subagent: true` (`packages/core/src/config/plugin/command.ts:98`). `agent` and
  `model` switch the session persistently (`:124-128`). A command whose frontmatter fails to decode
  is silently dropped (`:177-178`).
- *Source.* The built-in commands are `init` and `review`
  (`packages/core/src/plugin/command.ts:30,43`). In the TUI, slash autocomplete lists keymap
  commands plus server commands only (`packages/tui/src/component/prompt/autocomplete.tsx:473-494`),
  and local keymap commands such as `/skills` take precedence.

## 8. Agents

- *Source, measured.* Agents load from `{agent,agents}/**/*.md` and from `{mode,modes}/*.md`, which
  are forced to `primary`. The ID is the relative path without prefix or `.md`
  (`packages/core/src/config/plugin/agent.ts:21-26,177-184,206`). Project `.claude/agents` was not
  discovered. The built-in agents are `build` and `plan` (primary), `general` and `explore`
  (subagent), and hidden `compaction`, `title`, and `summary`. OpenCode 2 has no `scout` agent.
- *Source, documented.* The native frontmatter keys are `description`, `mode`, `model`, `variant`,
  `system` (the body), `permissions`, `steps`, `hidden`, `color`, `disabled`, and `request`
  (`packages/schema/src/config/agent.ts:9-22`, `packages/core/src/config/plugin/agent.ts:32`).
  **Any** other key sends the whole file through the OpenCode 1 decoder and migrator
  (`packages/core/src/config/plugin/agent.ts:185-203`,
  `packages/core/src/v1/config/migrate.ts:36-58`). Such keys include `name`, `permission`, `tools`,
  and `temperature`.
- *Source, measured.* A native `color` must match `^#[0-9a-fA-F]{6}$`. A native-only agent with
  `color: info` vanished from `/api/agent` with nothing logged. The same color plus a legacy `tools:`
  key loaded through migration as `#aaaaaa`. A legacy file with `name:` and
  `permission: {edit: deny, bash: ask}` loaded with converted rules.
- *Source.* The OpenCode 1 `task` tool is now `subagent`
  (`packages/core/src/tool/plugin/subagent.ts:29-33`). Its `agent` input is a plain string.
- *Source.* The TUI `@` picker merges skills, reference aliases, and non-primary, non-hidden agents
  into one list (`packages/tui/src/component/prompt/autocomplete.tsx:407-441,543`).
- *Inferred.* An `@x` pointer is therefore not unique by artifact kind. A skill and an agent with the
  same ID would produce identical picker entries. No such pair existed at the evidence date.

## 9. Plugins, references, config schema, instructions, and hot reload

- *Documented, source, measured.* Plugins load from the `plugin/` and `plugins/` directories of
  each config root, from the config `plugins` key, or through `opencode plugin add|list|check|update|remove`
  (npm or Git). A module default-exports `{id, setup(ctx)}`
  (`packages/plugin/src/promise/plugin.ts:57-65`). Through `ctx`, a plugin can do the following:
  - add, update, or remove skills with in-memory content;
  - add commands as code (`{name, description, execute}`), not templates;
  - upsert agents through `update`;
  - add references, tools, MCP servers, and hooks.

  A dependency-free probe plugin hot-loaded without a restart and registered a skill, an agent, a
  command, and a reference.
- *Source, measured.* Config-root skills outrank plugin skills, because the config skill plugin
  runs in the internal `post` list after user plugins
  (`packages/core/src/plugin/internal.ts:249-263`). A plugin cannot rename a file-based skill: the
  measured rename attempt left the original ID.
- *Source.* The plugin skill and command domains changed shape during 2.0.x: `location` became
  `path`, and `slash` was removed (`git show 199aabe9e -- packages/schema/src/skill.ts`).
- *Documented, measured.* `references` map an alias to a local path or a cached Git checkout. A
  described reference is advertised, and references grant no extra permissions. A local reference
  resolved relative to its config file.
- *Measured.* The published schema <https://opencode.ai/config.json> is still OpenCode 1-shaped.
  It has `agent`, `command`, `permission`, `plugin`, and a `skills` object of `{paths, urls}`, and
  `additionalProperties` is false. Native OpenCode 2 config therefore fails editor validation
  against it. <https://opencode.ai/v2/config.json> returned 404, and
  <https://opencode.ai/v2/cli.json> returned 200 (`curl -sL` and `curl -w %{http_code}`). The config
  docs still name `config.json` as the source of truth.
- *Documented, source.* OpenCode 2 loads `AGENTS.md` only and never falls back to `CLAUDE.md`
  (<https://opencode.ai/v2/docs/instructions/>;
  `packages/core/src/config/plugin/instruction.ts:37,47,71`). The `instructions` config field is
  accepted but its entries are not loaded. `lsp` config is accepted, but no language server runs.
- *Measured.* Config folders are watched. On a running server, a new skill folder appeared in the
  API within about 2 seconds, and a new command file also appeared without a restart; its latency
  was not timed. Both held for dotted names. Agent hot reload was not tested. No restart instruction
  exists in the repository to remove.
- *Documented.* Open issue #47505, "core: global skill update terminates shared service on
  Windows", reports that a bulk update of global skills killed the shared service. Its timeline shows
  223 watcher stops and starts and 16 rescans in 3 seconds. A comment reports another occurrence on
  2.0.16 on Windows. *Inferred:* migrating a real profile to the new output removes about 100 bare
  skill folders and 38 commands and writes about 115 namespaced skill folders in one Apply. Such a
  bulk Apply against a running service is unmeasured.

## 10. Namespace probe

The probe asked whether a per-Module prefix can carry through OpenCode 2's file-derived IDs, and with
which separator. Fixture names registered with the exact characters of their folder names.

| Spelling | Kind | Result |
|---|---|---|
| `skills/deniz-process.brainstorming/SKILL.md` | skill | Works. ID `deniz-process.brainstorming` (measured). |
| `skills/deniz-process/brainstorming/SKILL.md` | skill | Fails. ID `brainstorming`; nesting keeps only the leaf (measured). |
| `skills/deniz-process.rootmd.md` | skill | Registers, but its base directory is the whole skills root, so supporting files are not isolated (measured). |
| `_`, `--`, `+`, `=` in a skill folder | skill | Register. `_` and `--` cannot be told apart from hyphens inside names (measured). |
| `@` in a skill folder | skill | Registers, but breaks web composer completion and TUI popup reopening (measured). |
| `:` in a file or folder name | any | Not a legal Windows file name. Only a plugin or a JSON `commands` key can create such an ID (measured). |
| `commands/deniz-process.handoff.md` | command | Works. `/deniz-process.handoff` (measured, including a session command that expanded its template). |
| `commands/deniz-process/handoff.md` | command | Registers as `/deniz-process/handoff`. The TUI popup does not reopen after a backspace or paste (source plus offline replay). |
| `agents/deniz-process.reviewer.md` | agent | Works. `@deniz-process.reviewer`, mode `subagent` (measured). |
| frontmatter `name: deniz-process:plain-skill` | skill | Only a label. The ID stays `plain-skill`, and the namespaced lookup returns 404 (measured). |

- *Source.* A plugin can register skills with any ID, including `:`. However, file-based skills
  cannot be renamed, and a plugin route would replace byte-preserving file Bundles. The colon form is
  therefore viable only as runtime plugin output (`packages/core/src/plugin/host.ts:444-456`).
- *Source, measured.* Lookups are exact and case-sensitive. A namespaced skill attached by ID
  returned 204, and wrong-case or slash-separated variants returned `SkillNotFoundError`. A dotted
  command ran through `POST /api/session/:sid/command`, and `/deniz-process/handoff` returned
  `CommandNotFoundError` (`packages/core/src/command.ts:74-84`). A skill and a command with the
  same dotted name coexisted, because they live in separate registries.
- *Source.* The TUI `@` trigger rejects only whitespace or an inner `@`
  (`packages/tui/src/prompt/display.ts:40-50`). The slash trigger rejects whitespace or an inner `/`
  (`display.ts:52-61`). The web composer uses `/(?:^|\s)@([^\s@]*)$/` and `/^\/(\S*)$/`
  (`packages/app/src/composer/suggestions/machine.ts:98,107`). A dotted ID passes every trigger.
- *Source, measured.* Permission wildcards escape `.` and turn `*` into `.*`, and on win32 they
  ignore case (`packages/core/src/util/wildcard.ts:3-14`). A resource of `deniz-process.*` covers a
  Module and does not match `deniz-process-extra.x`. This holds on the model path only, because a
  user attachment skips permissions (section 6).
- *Source.* The model list renders both `<id>` and `<name>`, the `skill_content` header uses `name`,
  and the `/skills` dialog shows only `name` (`packages/core/src/skill/instructions.ts:16-22`,
  `packages/core/src/skill.ts:36-41`, `packages/tui/src/component/dialog-skill.tsx:44-50`).
- *Inferred.* With a bare `name` and a namespaced ID, the model sees a label that is not a callable
  ID. Frontmatter `name` therefore has to equal the namespaced ID.
- *Source, measured.* Quoted bare skill-tool handles are a load-bearing hazard. The skill tool fails
  with "Unable to load skill" when an ID is not an exact key
  (`packages/core/src/tool/plugin/skill.ts:48-49`). At `9442efa`, 12 lines in the `deniz-process`
  OpenCode output told the model to call the skill tool with a quoted bare ID, for example
  `Skill tool with "grilling"`. They are in `grill-me`, `grill-with-docs`, `wayfinder`, and
  `improve-codebase-architecture`, and their targets are `grilling`, `domain-modeling`, `research`,
  `codebase-design`, and `prototype`. These are not `ns:name` references, so reference localization
  never rewrites them. About 60 more bare IDs appear in backticked prose.
- *Measured.* In a small model sample, the bare prose "use the handoff skill" resolved to
  `deniz-process.handoff` in 2 of 2 runs when no bare look-alike existed. With a bare
  `brainstorming` skill also present, "use the brainstorming skill" called the bare skill (1 of 1).
  Namespacing prevents shadowing, but a bare third-party skill with the same leaf name still
  attracts bare prose.
- *Measured.* Two offline replays of the TUI ranking disagreed. Both used `fuzzysort@3.1.0` with
  the keys, 0.5 threshold, 10-result limit, and prefix-bonus score function from
  `packages/tui/src/component/prompt/autocomplete.tsx:548-575`.
  - One replay used the 104 repository skill and agent names. It found that Module tokens compete
    with item names, so `gen` and `dotnet` queries dropped real matches out of the top 10. Dotted
    entries never earn the doubled prefix score, so a bare `brainstorming` outranked
    `deniz-process.brainstorming` for `@brainst` (1.749 against 0.801).
  - A second replay used the real profile's names plus the 102 repository skills and 22 queries. It
    found the same set and order for bare and dotted spellings.

  Both replays agree that dotted IDs stay above the threshold, and that typing the prefix, such as
  `@deniz-process.` or `@process.brain`, narrows the list to the Module. The web composer has no
  10-result cut, so it only reorders.
- *Inferred.* A namespace also lets a project-local copy and the curated copy coexist. For example,
  `aspire init` writes a project-local `aspireify` skill, and the curated Aspire body tells the model
  to defer to that copy. Under flat IDs, one copy shadows the other according to precedence.
- *Measured.* With the longest prefix, the longest installed path was about 128 to 135 characters on
  Windows, which is not a constraint. Plugin names start with `deniz-`, so a dotted folder can never
  form a reserved device name.

## 11. Distribution options considered

- **Keep the repository's per-Module installer and file Bundles.** *Inferred from the facts above.*
  - The Destination formula already matches OpenCode 2's root except for `OPENCODE_CONFIG_DIR`.
  - The plural `skills/`, `commands/`, and `agents/` roots are scanned.
  - An unknown `.deniz-skills/` folder is ignored.
  - No config entry is needed, and hot reload removes any restart.

  This keeps byte-preserving composition, Plan, Apply, Recovery, and reviewable generated output.
  The 2026-10-08 decision kept it.
- **An OpenCode 2 plugin package**, installed with `opencode plugin add` or as a local `plugins/*.js`,
  registering skills, commands, and agents through `ctx`. It would give one entry and npm updates,
  and it could add `/name` commands that attach hidden skills. The end-to-end attach path is
  unmeasured. Rejected or deferred for these reasons:
  - the skill and command domains changed shape during 2.0.x;
  - skills would become in-memory content with unmeasured supporting files;
  - commands would become code and lose template semantics;
  - config-root skills outrank plugin skills anyway;
  - reviewable Markdown would be packed into JavaScript.
- **The `skills` config array or an HTTP skill catalog.** Rejected or deferred. It covers skills
  only, so commands and agents still need files. It also needs config mutation, relative entries
  resolve from the working directory, a catalog needs hosting, and a root catalog `SKILL.md` gets
  the ID `SKILL`.
- **A shared `~/.agents/skills` tree with Codex.** Rejected for these reasons:
  - It conflicts with per-harness native frontmatter.
  - It has lower precedence than the global config root.
  - It carries no commands or agents.
  - Supporting files there are likely to prompt for external-directory access (inferred).
  - The repository already ships native Codex Plugins.
- **Emitting skill `deny` rules for manual items.** Rejected. It needs edits to the user's
  `opencode.json(c)`, which the installer never mutates, and the user-attachment behaviour under
  `deny` is disputed (section 6). Mapping agent permissions also stays deferred. If it is done, it
  can use only native OpenCode 2 keys and action names such as `shell`, `edit`, and `subagent`.
- **A project-local `.opencode` Destination.** After the stubs are removed, no artifact hard-codes
  the global root, so this becomes cheap. It was deferred until a concrete need appears.
- **A manual drop-in of Bundle folders** still works, but it is documentation only.

Real-profile migration evidence:

- *Measured on a scratch copy of a real profile at `9442efa`.*
  `XDG_CONFIG_HOME=<copy> node tools/install-opencode.ts status` and `update` both reported
  `.deniz-skills/install.json: invalid Install state: schemaVersion must be 2`, and `update` exited
  with status 1 (`tools/lib/opencode-install-state.ts:132-133`). The checkout installer cannot plan
  against either real profile until their schema-1 state is removed.
- *Measured, read-only.* The Windows profile also holds another tool's install marker and 36 agents,
  3 commands, and 3 skills that this repository does not own. The Linux profile holds the same
  install marker; its foreign file names were not listed. On Windows, none of the foreign names
  collides with this repository's bare or namespaced names, and no repository name uses a built-in
  ID (`opencode`, `report`, `init`, or `review`).

## 12. What changed from the OpenCode 1.18.x evidence

| Topic | OpenCode 1.18.x evidence | OpenCode 2.0.23 evidence |
|---|---|---|
| User access to a skill | None in the 1.18.7 measurements, where skills were model-only; in 1.18.35 source every skill is also an automatic `/name` command | `@id` mention or `/skills` dialog; skills are never slash commands from v2.0.4 |
| Hiding a skill from the model | No per-skill control | `metadata: {"opencode/autoinvoke": false}` from v2.0.0 (effective as manual from v2.0.4); `disable-model-invocation: true` from v2.0.23 |
| Skill identity | Frontmatter `name` won over the folder | Parent folder name or root-file basename; `name` is a label |
| Files under a skills root | `**/SKILL.md` only | `{*.md,**/SKILL.md}`: root Markdown files and nested `SKILL.md` become skills |
| Parked `BODY.md` folder | Ignored | Still ignored |
| `OPENCODE_CONFIG_DIR` | Added a discovery location | Replaces the global root |
| Project walk | Stopped at the worktree | Walks to the filesystem root |
| `.claude` skill tree switch | `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS=1` | No flag; only a plugin removal entry |
| Command template | `$ARGUMENTS`, `$1`, shell, `@file` (project-root relative) | `$ARGUMENTS`, `$N`, shell; no `@file` expansion |
| Agent subagent tool | `task` | `subagent` |
| Deterministic introspection | `opencode debug skill`, `debug config`, `opencode run --command` | `opencode serve` HTTP API; no `debug skill`, no `run --command` |
| Instructions | `AGENTS.md`, with a `CLAUDE.md` fallback | `AGENTS.md` only |

## 13. Conflicts found

Upstream documentation against upstream source, preferring source and measurement:

- The skills page says project discovery stops at the project root. Source and measurement show it
  walking to the filesystem root (section 2).
- The migration guide says "Existing skill files and automatic `.opencode/skills/` discovery do not
  change" (`services/www/src/docs/content/migrate-v1.mdx:278`). In fact IDs became path-derived,
  flat Markdown files became skills, and skills stopped being slash commands.
- The docs say `deny` rejects loading. A user attachment bypasses the check (#49891, section 6).
- Production docs documented skill `slash` after source had removed it (#51753, open, filed on
  2.0.18). The docs and the source drifted during 2.0.x, and the slash UX remains disputed upstream.
- The config docs name <https://opencode.ai/config.json> as the source of truth, but it is still
  OpenCode 1-shaped.
- The dev docs and `packages/web` docs still describe skill `slash`.

Repository canon and code at `9442efa` against the OpenCode 2 evidence. Each item names the owner
that carries the resolution:

- ADR-0005 said "`manual` is the strict boundary: a target must prevent implicit model selection".
  OpenCode 2 offers only "unadvertised, not forbidden", and an auto skill's body
  (`csharp-nullable-reference-types`) pointed at a manual item. The Codex mapping
  (`allow_implicit_invocation: false`) is the existing precedent for accepting an unadvertised
  manual surface. Owner: [ADR-0005](../adr/0005-invocation-intent-in-the-manifest.md), with the
  compensating leak rule in the roadmap's validation work.
- ADR-0002 rejected mandatory Module prefixes, and `CONTEXT.md` said a Module "is not an OpenCode
  namespace". The namespace probe and the shadowing evidence reverse that for OpenCode. Owners:
  [ADR-0002](../adr/0002-multi-harness-output.md) and [`CONTEXT.md`](../../CONTEXT.md).
- The installer refused a non-empty `OPENCODE_CONFIG_DIR` on the OpenCode 1 reasoning that the
  variable only adds a location. In OpenCode 2 it is the root. Owner:
  [Distribution and installation](../architecture/distribution-and-installation.md#destination-selection-and-ownership).
  One edge remains (inferred, section 2). The installer treats an empty `OPENCODE_CONFIG_DIR` as
  unset, while upstream's `??` may keep an empty value on POSIX.
- The identity preflight claimed OpenCode names per kind (`skill:<name>`, `agent:<name>`). The
  OpenCode 2 `@` picker merges skills and agents, so a same-named skill and agent would be
  ambiguous there (section 8). Owner:
  [Transformation and emission](../architecture/transformation-and-emission.md).
- Emission canon described the parked `BODY.md`, the global-only stub, and the duplicate `both`
  command. All of these rest on OpenCode 1 skills being model-only. Owner:
  [Transformation and emission](../architecture/transformation-and-emission.md#opencode). At the
  same snapshot, that document's `tools/build.ts` line anchors had drifted from the code.
- The emitter kept only `description` and `mode: subagent` for agents. That stays native in OpenCode
  2, but any emitted non-native key, such as `name`, would flip an agent into migration.
- The shipped `setup-matt-pocock-skills` body tells the agent to prefer editing `CLAUDE.md`, which
  OpenCode 2 never loads. This is an upstream-sync question for the roadmap.
- Some curation reasons cite OpenCode 1 mechanics. Examples are the `analyzing-dotnet-performance`
  reason in `curation/deniz-dotnet-general.yaml`, which says "forced-manual would ... wall-paste its
  body (OpenCode)", and the husk-removal reason in `curation/deniz-process.yaml`. These are curation
  work for the curator.
- `tools/repository-docs.test.ts` pins the `installer-v0.3.0` Release recipe. A new Release needs
  that pin changed together with the `package.json` version.
- The experiment harness depends on the OpenCode 1 CLI: `debug skill`, `run --command`,
  `--variant`, `models --verbose`, the built-in `customize-opencode` skill, and the
  `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS` isolation. `selftest.ps1` asserts some of those strings and
  is not run by `npm test` or CI.
- In the generated OpenCode output, 34 `/name` pointers aimed at 26 own items. Most came from
  `ask-deniz` and its phase-boundary reference. Under OpenCode 2, a skill target can no longer be
  reached with `/`.

## 14. Decision outcome

On 2026-10-08 the curator accepted the following, recorded in the current owners listed at the top:

- OpenCode 2 is the only OpenCode target. The floor is v2.0.4, and v2.0.23 is the measured version.
- Manual items become skills carrying the native `opencode/autoinvoke` key. The curator accepted the
  "unadvertised, not forbidden" meaning together with a compensating validation rule.
- `both` items become one plain skill. Explicit `as: command` stays the escape hatch for argument-
  shaped items.
- Every OpenCode ID becomes `<plugin>.<name>`, and frontmatter `name` equals that ID.
- The installer keeps file Bundles and adopts `OPENCODE_CONFIG_DIR` as the Destination when it is
  set.
- The OpenCode 1-bound probes retire in favour of a `serve`-based discovery check and one model
  record.

This note does not own any of those rules. The implementation gap, the measurement records, the
real-profile migration, and the Release follow the sequence in [`docs/ROADMAP.md`](../ROADMAP.md).
