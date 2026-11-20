# Antigravity native plugin and skill surfaces

Date: 2026-11-20

This note records the Google Antigravity capabilities that constrain the fourth repository emitter.
It is dated evidence and design input, not current repository policy. The accepted invocation
decision is [ADR-0005](../adr/0005-invocation-intent-in-the-manifest.md); the current Antigravity
mapping lives in [`curation/SCHEMA.md`](../../curation/SCHEMA.md) and
[Transformation and emission](../architecture/transformation-and-emission.md#antigravity).

## Evidence boundary

The findings below combine official Google Antigravity documentation fetched on 2026-11-19/20,
local CLI introspection and live headless probes on `agy.exe 1.5.13` (with `gemini-3.8-flash-low`),
the committed measurement panel recorded as
[`antigravity-plugin-panel-2026-11-20`](../../experiments/harness-invocation/records/2026-11-20-antigravity-plugin-panel.md),
and live probes on the T3 Code bundled ACP server (`agy_acp_server.exe 1.4.5` +
`localharness_external.exe`, with `gemini-3.8-flash` low thinking) on Windows. Temporary probe
plugins, loose skills, discovery manifests, and an HTTP MCP entry were tested and then removed so
the user profile and repository stayed clean.

Measured surfaces:

- `agy 1.5.13` plugin validation (`agy plugin validate --strict --json`), local install, GitHub
  subfolder URL install, zero-copy `plugins.json` discovery, skill advertisement,
  `disable-model-invocation: true` hiding, explicit `/<plugin>:<skill>` (observed passing when
  installed under `~/.gemini/config/plugins/`, while hitting a headless `read_file` permission
  denial when pointed outside the workspace via `plugins.json` in
  `antigravity-plugin-panel-2026-11-20`) and bare `/<skill>` slash invocation, native
  `agents/<name>.md` subagent discovery (including both generated estate agents), MCP loading, and
  live tool schema.
- `agy acp` on `agy 1.5.13` (launched inside T3 Code by setting the provider **Binary path** to
  `agy`): installing `deniz-dotnet-akka` and `deniz-process` via `agy plugin install` into
  `~/.gemini/config/plugins/` exposed all 28 auto/both plugin skills alongside the 4 built-ins, hid
  all 22 `disable-model-invocation: true` manual skills (`handoff`, `ponytail-review`, etc.), and
  exposed the native `akka-net-specialist` subagent with no `GEMINI_HOME` junction or loose-skills
  bridge.
- `agy_acp_server 1.4.5` (under T3 Code with an NTFS junction from `<GEMINI_HOME>\config` to
  `~/.gemini/config`) loose skill discovery under `config/skills/`, `disable-model-invocation: true`
  hiding on loose skills, and negative checks confirming `1.4.5` does not load `config/plugins/`,
  `config/plugins.json`, `config/skills.json`, `config/agents/`, or global `config/mcp_config.json`.

Unmeasured surfaces: visual Antigravity 2.0 App / Desktop UI behavior, Antigravity IDE (`agy-ide`)
plugin loading, and cross-device synchronization.

Official sources:

- [Build plugins](https://antigravity.google/docs/build-plugins)
- [Plugins](https://antigravity.google/docs/plugins)
- [Customization discovery](https://antigravity.google/docs/customization-discovery)
- [Skills](https://antigravity.google/docs/skills)
- [ACP CLI server](https://antigravity.google/docs/cli/acp)

Local commands:

```text
agy --version
agy --help
agy plugin --help
agy plugin validate --strict --json <dir>
agy plugin install <local-dir-or-github-tree-url>
agy plugin list --json
```

## Plugin packaging and distribution

- A native Antigravity plugin is a directory containing `plugin.json` (or
  `.antigravity-plugin/plugin.json` / `.gemini-plugin/plugin.json`) validating against
  `https://antigravity.google/schemas/v1/plugin.json` (`additionalProperties: false`). Required /
  standard fields include `name` (`^[a-z0-9]+(-[a-z0-9]+)*$`), `displayName` (without platform
  names such as `Plugin`, `Antigravity`, `Gemini`, `Claude`, `Cursor`, `MCP`), `version` (semver),
  `description`, `author`, `homepage`, `repository`, `license`, and `keywords`.
- `agy plugin validate --strict --json` exited `0` with zero errors and zero warnings on a native
  plugin containing `plugin.json`, `skills/<name>/SKILL.md`, and `agents/<name>.md` (emitting only
  an informational `note` when `logo` is omitted).
- **Third-party marketplaces:** Official marketplace installs currently accept only
  `antigravity-plugins-official`. Probing `agy plugin validate --json` against this repository's
  `.claude-plugin/marketplace.json` and `.agents/plugins/marketplace.json` failed because `agy`'s
  marketplace parser expects a `url` source rather than local relative paths (`source: "./..."` or
  `{ source: "local", path: "./..." }`).
- **Direct GitHub subfolder install works:** Running
  `agy plugin install https://github.com/<owner>/<repo>/tree/<branch>/<subdir>` cloned the
  repository, navigated to the subdirectory, and installed the plugin into
  `~/.gemini/config/plugins/<plugin>` with exit code `0`.
- **Zero-copy local discovery manifest works on `agy 1.5.13`:** Listing a local plugin folder in
  `~/.gemini/config/plugins.json` (`{ "entries": [{ "path": "..." }] }`) loaded both its skills and
  subagents into `agy --print` without copying files (note: `agy plugin list --json` lists only
  plugins installed under `~/.gemini/config/plugins/`, not manifest-discovered ones).
- **Profile isolation (`GEMINI_HOME`) asymmetry:** `agy 1.5.13` ignored `GEMINI_HOME` and wrote to
  `~/.gemini/config/plugins/`. In contrast, T3 Code's `agy_acp_server 1.4.5` respected
  `GEMINI_HOME`, isolating its profile under T3's userdata directory until `<GEMINI_HOME>/config`
  was linked to `~/.gemini/config`.

## T3 Code ACP (`agy_acp_server 1.4.5`) vs. `agy 1.5.13`

Live testing revealed a sharp capability split between current standalone `agy 1.5.13` and T3
Code's bundled `agy_acp_server 1.4.5`:

| Surface | `agy 1.5.13` | T3 `agy_acp_server 1.4.5` (via `config` junction) |
| --- | --- | --- |
| `config/plugins/<plugin>/` | Loaded (skills + agents) | Ignored |
| `config/plugins.json` / `config/skills.json` | Loaded | Ignored |
| `config/skills/<skill>/SKILL.md` | Loaded | **Loaded** |
| `disable-model-invocation: true` | Enforced (hidden from model; explicit slash works) | **Enforced** on loose skills in `config/skills/` |
| `agents/<name>.md` (plugin or `config/agents/`) | Loaded into `invoke_subagent` | Ignored (only built-in `self`, `research`) |
| `config/mcp_config.json` | Loaded | Ignored (only T3-injected `t3-code` MCP visible) |

Because configuring T3 Code's Antigravity provider with `Binary path: agy` (`agy >= 1.5.13`) runs
`agy acp` and loads native plugins (skills, `disable-model-invocation: true` hiding, and native
subagents) directly from `~/.gemini/config/plugins/` without any `GEMINI_HOME` junction, no
loose-skills sync helper is required in this repository; T3's older bundled `1.4.5` server is left
unsupported.

## Skill invocation and namespacing

- Frontmatter supports `name` (1–64 kebab-case, matching directory), `description` (1–1,024 chars,
  matching the repository's shared description cap), `disable-model-invocation: true|false`,
  `license`, `compatibility`, and `icon`.
- There is no documented `user-invocable` key to hide an `auto` skill from slash invocation, so
  `auto` and `both` emit the same plain skill while `manual` emits `disable-model-invocation: true`
  (matching ADR-0005's capability table, identical in expressiveness to OpenCode 2 and Codex).
- In `agy 1.5.13`, a plugin skill with `disable-model-invocation: true` was absent from the model's
  prompt list, and **both** `/<plugin>:<skill>` and `/<skill>` expanded and executed it in print
  mode. Official `customization-discovery` docs name plugin skills `<plugin>:<skill>` to prevent
  cross-plugin collisions, while the model's skill list displayed the bare name `probe-auto`.
- Rendering references as `<plugin>:<skill>` (and `/<plugin>:<skill>` for user pointers) matches
  the neutral reference syntax, works natively in `agy 1.5.13` explicit slash commands, and stays
  unambiguous across Modules.

## Commands and agents

- **Agents (`agents/<name>.md`):** Supported natively as plugin components. Frontmatter accepts
  `name`, `description`, `mainAgent` (`false` keeps it out of top-level `--agent` selection),
  `subagent` (`true` exposes it to `invoke_subagent`), and `model` (`inherit` | `flash_lite` |
  `flash` | `pro`). Live testing on `agy 1.5.13` confirmed `probe-specialist` appeared directly as
  an available subagent type under `invoke_subagent`.
- **Commands:** Deprecated in favor of skills with `disable-model-invocation: true`;
  `agy plugin validate` warns on `commands/`. The repository currently has zero curated commands;
  any future `as: command` item targeting Antigravity flattens cleanly to a manual skill
  (`skills/<name>/SKILL.md` with `disable-model-invocation: true`).

## Tool surface and upstream tool-notes drift

Live tool declarations on both `agy 1.5.13` and T3 `agy_acp_server 1.4.5` revealed two constraints
that directly shape `tools/lib/rewrite.ts` and `harness_files`:

1. **No `Skill` tool and no todo tool:** Antigravity has neither a `Skill` tool nor a checklist
   todo tool (`manage_task` manages background processes, not checklists). Skill calls localize to
   direct invocation phrasing (like Codex), and todo prose localizes to Markdown-checklist wording
   (sharing OpenCode 2's closed todo table and validator check).
2. **`send_message` vocabulary overlap with Codex:** Antigravity's native subagent tools are
   `invoke_subagent`, `define_subagent`, `manage_subagents`, and `send_message`. Because Codex V2
   also exposes `send_message`, `HARNESS_ONLY_VOCABULARY` cannot treat `send_message` as
   Codex-exclusive; vocabulary enforcement must permit `send_message` in both Codex and Antigravity
   while forbidding it in Claude Code and OpenCode.
3. **Upstream `using-superpowers` Antigravity note is stale:** The vendored
   `external/superpowers/skills/using-superpowers/references/antigravity-tools.md` instructs the
   model to pass `IsArtifact: true` and `ArtifactMetadata.ArtifactType: "task"` to `write_to_file`
   and edit with `multi_replace_file_content`. Live inspection confirmed neither `IsArtifact` nor
   `ArtifactType` exists in either `agy 1.5.13` (where the tool is `write_file` with
   `ArtifactMetadata`) or T3 `1.4.5` (where `write_to_file` takes `ArtifactMetadata` with
   `{ RequestFeedback, Summary, UserFacing }` and no `multi_replace_file_content`). The repository
   therefore replaces that stale note via its own Antigravity tool notes rather than shipping
   upstream's file verbatim.

## Design implications

- Emit one native Antigravity plugin per Module under `antigravity/<plugin>/` with strict
  `plugin.json`, `skills/<name>/SKILL.md`, native `agents/<name>.md`, and `THIRD_PARTY_NOTICES.md`.
- No custom tarball installer, Release Package transport, or T3 loose-skills bridge is required:
  direct GitHub subfolder `agy plugin install` and zero-copy `~/.gemini/config/plugins.json` work
  natively on `agy 1.5.13`, and T3 Code reaches the same native plugin surface by setting its
  Antigravity provider **Binary path** to `agy`.
- Run a repeatable Antigravity discovery and invocation panel under `experiments/harness-invocation/`
  and commit its measurement record to close the bounded runtime proof loop.
