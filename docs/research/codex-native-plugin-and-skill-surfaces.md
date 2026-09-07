# Codex native plugin and skill surfaces

Date: 2026-09-07

This note records the Codex capabilities that constrain a third repository emitter. It is dated
evidence and design input, not current repository policy. The proposed policy lives temporarily in
the Codex design specification and, once accepted, belongs in the existing ADR and architecture
owners.

## Evidence boundary

The findings below use official OpenAI documentation fetched on 2026-09-06 and 2026-09-07, local
CLI introspection of `codex-cli 0.153.4`, the isolated credential-free structural run recorded as
[`codex-plugin-structural-2026-09-07`](../../experiments/harness-invocation/records/2026-09-07-codex-plugin-structural.md),
and the later Luna panel recorded as
[`codex-plugin-behaviour-2026-09-07`](../../experiments/harness-invocation/records/2026-09-07-codex-plugin-behaviour.md).
The two runs installed both a nonsense-control fixture and all four generated plugins without
modifying the real Codex plugin state or repository. CLI invocation and discovery are measured;
ChatGPT desktop discovery remains unmeasured.

Official sources:

- [Package your plugin](https://developers.openai.com/plugins/build/plugins)
- [Build skills](https://learn.chatgpt.com/docs/build-skills)
- [Submit your Claude Code plugin to OpenAI](https://developers.openai.com/plugins/guides/submit-claude-plugin)
- [Plugins](https://learn.chatgpt.com/docs/plugins)
- [Submit your plugin](https://developers.openai.com/plugins/deploy/submission)
- [Connect and test](https://developers.openai.com/plugins/deploy/connect-chatgpt)
- [Subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents)
- [Environment variables](https://learn.chatgpt.com/docs/config-file/environment-variables)

Local commands:

```text
codex --version
codex plugin --help
codex plugin marketplace --help
```

The observed CLI exposes `plugin add`, `plugin list`, `plugin remove`, and marketplace `add`, `list`,
`upgrade`, and `remove` commands. The isolated run successfully exercised JSON add/list/remove with
a local marketplace and installed all four generated repository plugins.

## Plugin packaging and distribution

- A plugin has a required `.codex-plugin/plugin.json` entry point. Its optional native components
  include `skills/`, lifecycle hooks, bundled MCP configuration, registered MCP mappings, and
  presentation assets.
- A repository marketplace lives at `.agents/plugins/marketplace.json`. Plugin source paths are
  relative to the marketplace root, begin with `./`, and may point to plugin directories elsewhere
  inside that root. The legacy `.claude-plugin/marketplace.json` location is recognized by the
  desktop host, but it does not make a Claude plugin tree a Codex-native plugin.
- Codex CLI can add a Git or local marketplace directly. Installed plugin skills become available in
  a new CLI session. The ChatGPT desktop app installs marketplace plugins into a Codex cache rather
  than loading the marketplace source directory in place.
- On the measured CLI, local installation materialized all 117 generated `SKILL.md` files and 27
  manual `agents/openai.yaml` files below the isolated plugin cache. This is structural evidence;
  individual skill advertisement and execution still require a model run.
- Plugins are supported by Codex in the ChatGPT desktop app and by Codex CLI. The Codex IDE
  extension does not support plugins. IDE coverage therefore requires a standalone skill transport
  rather than being implied by plugin output.

## OpenAI universal directory

The universal directory is OpenAI's reviewed public discovery surface for plugins. It is separate
from publishing this repository's `.agents/plugins/marketplace.json`: submission requires verified
publisher identity and public product/legal/support metadata, starter prompts, positive and negative
test cases, review, and an explicit publish step. The current submission path accepts skills-only
plugins, so these generated Codex plugins are structurally eligible, but directory publication is a
later product/distribution decision rather than a prerequisite for CLI or personal marketplace use.

## Skill invocation

Codex skills have both native invocation paths:

- implicit selection when the task matches the skill description; and
- explicit selection with `$` in Codex CLI or the IDE extension.

A skill can include `agents/openai.yaml` with
`policy.allow_implicit_invocation: false`. This disables implicit selection but leaves explicit
invocation available. Codex does not document a skill policy that preserves implicit selection
while forbidding explicit selection.

This does not require the repository to approximate an unrepresentable `model-only` promise. A
harness-neutral definition can instead state the capabilities each value requires or forbids:

| Manifest value | Implicit invocation           | Explicit invocation           |
| -------------- | ----------------------------- | ----------------------------- |
| `auto`         | required                      | unspecified                   |
| `manual`       | forbidden                     | required                      |
| `both`         | required                      | required                      |
| absent         | passthrough or target default | passthrough or target default |

Under that definition, an ordinary Codex skill satisfies both `auto` and `both`; the distinction is
observable only on a harness that can suppress or synthesize a separate explicit surface. A Codex
skill with `allow_implicit_invocation: false` satisfies `manual`.

## Commands and agents

The official Claude-plugin conversion guidance says to turn reusable Markdown commands into skills,
move reusable agent procedures into skills, and merge useful persona instructions into the relevant
skill. That is the documented Codex-plugin adaptation path rather than a claim that Claude
`commands/` or `agents/` directories are Codex plugin components.

Codex separately supports personal custom-agent TOML under `~/.codex/agents/` and project custom
agents under `.codex/agents/`. The official documentation describes these as configuration layers,
warns that their authoring and sharing format may evolve, and does not list them as a plugin
component. Installing native custom agents would therefore require a separate profile/project
transport with its own ownership and recovery semantics.

## Catalog pressure

Codex initially exposes skill name, description, and path using at most two percent of the model
context window, or 8,000 characters when the context window is unknown. It shortens descriptions
first and may omit skills from the initial list with a warning when the catalog is large.

The generated ledger contains a large enough catalog to trigger this pressure, especially for the
General Module. In the isolated 117-skill panel every call warned that descriptions were shortened,
while stating that all skills remained visible. Explicit fixture skills, repeated implicit controls,
one generated manual skill, and one generated implicit skill all succeeded. This establishes bounded
runtime support for the measured CLI/model combination, not a guarantee for every skill or model.

## Design implications

- A separate Codex emitter and output tree are feasible using native plugin and marketplace shapes.
- `manual` maps directly to a Codex skill policy. `auto` is also representable when its neutral
  contract requires implicit availability without forbidding a native explicit escape hatch.
- Codex-plugin commands and agents should become skills for the first distributable baseline.
- Plugin output can cover Codex CLI and the ChatGPT desktop app, but not the IDE extension.
- Installed-cache structure, all-four-plugin installation, public Git marketplace add/install/upgrade,
  description shortening, explicit and implicit invocation controls, manual suppression,
  cross-skill execution, bundled references, and two generated-skill paths are measured on Codex CLI
  0.153.4 with Luna. ChatGPT desktop state sharing and standalone IDE skill transport remain separate
  experiment questions.
