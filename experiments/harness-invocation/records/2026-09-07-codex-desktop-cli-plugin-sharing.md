---
record_id: codex-desktop-cli-plugin-sharing-2026-09-07
date: 2026-09-07
repo_head: ef6f9095959e848a02920c8c5199bac4ed4185da
kind: harness-structural
summary: A same-machine Codex CLI and fresh app-server shared one custom Git marketplace and plugin installation state in both directions.
isolation_ok: false
---

# Codex desktop-backend and CLI plugin-state sharing

This tier-2 record measures the boundary left open by the isolated CLI experiments: whether a custom
Git marketplace installed for Codex is shared between Codex CLI and the Codex app-server surface
used by rich Codex clients. The run used Codex CLI and app-server 0.153.4 on Windows against the
public repository at the `repo_head` above.

The real default Codex profile was deliberately required for this measurement, so `isolation_ok` is
false rather than pretending the run was disposable. Before the first mutation, the profile had no
`deniz-skills` marketplace or `deniz-*` plugin. The exact plugin and marketplace were removed after
each direction. Final cleanup restored `config.toml` byte-for-byte and left the marketplace clone,
plugin cache, marketplace row, and plugin row absent. No credential file was read, copied, or
recorded.

## Results

| Probe | Observed result | Status |
|---|---|---|
| Baseline | default profile contained no `deniz-skills` marketplace or `deniz-*` plugin | pass |
| CLI adds marketplace | public Git URL resolved to `deniz-skills` | pass |
| CLI installs plugin | `deniz-process@deniz-skills` 0.6.0 installed and enabled | pass |
| Fresh CLI execution | Luna loaded the installed `using-superpowers/SKILL.md` and returned the required marker | pass |
| Fresh app-server initialization | reported the same default Codex home as CLI | pass |
| App-server installed-plugin view | found `deniz-skills`; Process was installed and enabled at 0.6.0 with zero marketplace-load errors | pass |
| App-server skill discovery | forced reload listed 37 Process skills and included `deniz-process:using-superpowers` | pass |
| App-server adds marketplace | `marketplace/add` resolved the same Git URL to `deniz-skills` | pass |
| App-server installs plugin | `plugin/install` completed with no app authentication dependency | pass |
| CLI observes app-server install | listed the Git marketplace and Process 0.6.0 as installed and enabled | pass |
| CLI executes app-server install | a new Luna session loaded the installed skill body and returned the required marker | pass |
| Final recovery | configuration hash matched baseline; plugin cache and marketplace clone were absent | pass |

## Sanitized excerpts

CLI-to-app-server discovery, with machine paths replaced by semantic roots:

```json
{
  "codexHomeIsDefault": true,
  "totalSkills": 53,
  "denizProcessSkills": 37,
  "hasUsingSuperpowers": true,
  "pluginIds": ["deniz-process@deniz-skills"]
}
```

The app-server's installed-plugin projection:

```json
{
  "marketplaceFound": true,
  "pluginFound": true,
  "installed": true,
  "enabled": true,
  "localVersion": "0.6.0",
  "marketplaceLoadErrors": 0
}
```

Each new CLI process emitted a completed command event whose target was
`<CODEX_HOME>/plugins/cache/deniz-skills/deniz-process/0.6.0/skills/using-superpowers/SKILL.md`,
followed by the requested literal marker:

```text
CODEX-DENIZ-PROCESS-LOADED
APP-SERVER-TO-CLI-PASS
```

Both executions also logged failed connection attempts to an unrelated configured Rider MCP
endpoint. The processes exited successfully, and the completed skill-read event preceded each
literal marker; the unavailable MCP server was not part of this plugin.

Final recovery:

```json
{
  "marketplaceAbsent": true,
  "pluginAbsent": true,
  "cacheAbsent": true,
  "marketplaceCloneAbsent": true,
  "configByteIdentical": true
}
```

## Commands and RPCs

The CLI direction used the public transport already proven in the remote-marketplace record:

```text
codex plugin marketplace add https://github.com/Blind-Striker/agent-skills-and-plugins --json
codex plugin add deniz-process@deniz-skills --json
codex plugin list --json
codex exec --json --ephemeral --model gpt-5.6-luna ...
```

The reverse direction initialized a fresh `codex app-server --stdio` process, then used
`marketplace/add` and `plugin/install`. The install request passed the resolved
`.agents/plugins/marketplace.json` path, not the marketplace directory. `plugin/installed` and
`skills/list` with `forceReload: true` supplied the desktop-backend observations above.

## Proof boundary

This establishes same-machine, same-profile sharing between Codex CLI and a fresh Codex app-server:
either surface can persist the custom marketplace/plugin state and the other can discover and use
it. It does not establish visual rendering in a particular ChatGPT desktop build, hot reload inside
an already-running conversation, account-level or cross-device synchronization, availability in
Chat or Work outside the Codex host, IDE-extension Plugin support, or publication in OpenAI's
universal public directory. OpenAI's plugin documentation separately requires a new chat or CLI
session before newly installed capabilities are expected to appear:
<https://learn.chatgpt.com/docs/plugins>.
