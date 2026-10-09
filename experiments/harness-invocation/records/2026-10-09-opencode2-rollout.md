---
record_id: opencode2-rollout-2026-10-09
date: 2026-10-09
repo_head: a8a942565a4eee7663089902e6a94ac01ed58c67
kind: runtime-smoke
summary: The public installer-v0.4.0 Package installed the four OpenCode 2 Modules fresh into the real OpenCode 2.0.26 profiles of the macOS workstation and the WSL distro on the Windows workstation (285 owned files each, every foreign file byte-identical); every existing Claude Code and Codex installation of the deniz-skills marketplace moved to Process 0.7.0, General 0.10.0, Akka 0.4.0, and Aspire 0.4.0; real-profile smoke tests on the Windows workstation and the Linux host passed self-diagnostics and 10 of 10 skill tries with openai/gpt-5.6-luna at variant low; the migration backups were then deleted.
isolation_ok: true
real_profile_rollout: authorized-2026-10-09
harness_name: opencode
harness_version: 2.0.26 (all four environments)
package_name: deniz-agent-skills-0.4.0.tgz
package_size: 885480
package_sha256: 5108a3ee3673196891644370bb92f538743cdcafad57db7876d8a40f0bf95dce
release_tag: installer-v0.4.0
smoke_model: openai/gpt-5.6-luna, variant low
---

# OpenCode 2 rollout and real-profile smoke tests

This record covers the curator-approved rollout that followed the
[`installer-v0.4.0` Release](2026-10-09-opencode-installer-v0.4.0.md) and the
[profile migration](2026-10-09-opencode2-profile-migration.md). The curator gave the go for each
step. The real-profile installs, plugin updates, smoke sessions, and backup deletions are authorized
changes, not isolated panels. `isolation_ok: true` describes only the macOS discovery check, which
ran against a copy of the installed tree in a lab. No credential was read or printed: running
services were read through the `opencode api` CLI, which authenticates by itself, and
`opencode auth list` was read for provider names only. `<root>` is the OpenCode config root and
`<home>` the user's home directory. Raw outputs, session event logs, and scripts stay outside the
repository.

Every Module digest below equals its `opencode/<module>/manifest.json` at `repo_head`, and the
`opencode/`, `plugins/`, and `codex/` trees at `repo_head` are byte-identical to the Release source
commit `07d98dc`.

| Module | Version | Module digest |
|---|---|---|
| `deniz-process` | `0.7.0` | `sha256:a902e274d69a5caee7b3f3d7cc40c224c50c8885cae40d850f075aee1951068f` |
| `deniz-dotnet-general` | `0.10.0` | `sha256:80688637e4230445d04326d05fd789b9e2c3ec77ccb03e3830e79208aa59a4d2` |
| `deniz-dotnet-akka` | `0.4.0` | `sha256:f5fb32c8341258fc72e21a0cd24a8d8cc14cc201116f130cc93d8b941a10e7ad` |
| `deniz-dotnet-aspire` | `0.4.0` | `sha256:6f4d92ddad0e37cc0eee0ae63e1e03e1cdb1b88877002bb6060dba0f7f746ab5` |

## Fresh installs from the public Package

Both environments ran the README "OpenCode from a Release Package" recipe as its bash equivalent:
`curl` of the public asset into a new empty directory, `sha256sum`/`shasum -a 256` against
`package_sha256`, then `npm exec --yes --package <tgz> -- deniz-skills install --all` (Plan), the
same with `--yes` (Apply), and `status`. Each run was followed by an independent read-only
verification.

### macOS workstation

**Status: INSTALLED.** This is the first measurement of the installer and of OpenCode 2 on macOS.
Together with the WSL install below, it is also the first POSIX measurement of discovery on OpenCode
2.0.26; earlier POSIX discovery was on 2.0.23.

- Inventory: macOS on Intel, OpenCode 2.0.26 from a Bun global install, Node v24.21.0 and npm 11.19.0
  from a version manager (already the default; no Node install was needed). `opencode debug paths`
  reports the default XDG config location under `<home>`; `OPENCODE_CONFIG_DIR` and
  `XDG_CONFIG_HOME` are unset. The config root existed and was empty, so there were 0 foreign files.
  No OpenCode process, service registration, or login database existed, and neither
  `<home>/.claude/skills` nor `<home>/.agents/skills` existed.
- Package: 885,480 bytes with the recorded SHA-256.
- Plan: 4 Modules, 285 adds, no Collisions or findings; the config root was still empty afterwards.
- Apply exited 0. Its output repeats the Plan and prints no separate success line, so the result was
  taken from `status`: `deniz-dotnet-akka` 0.4.0, `deniz-dotnet-aspire` 0.4.0,
  `deniz-dotnet-general` 0.10.0, `deniz-process` 0.7.0, all current, Lock none, Recovery none.
- After: the config root holds only `.deniz-skills/install.json` (schema 2, 285 file records) and the
  285 owned files: 115 dotted skill folders and 2 dotted agents, no `commands/` folder. All 285
  recorded hashes match the files on disk, and the installed `skills/` and `agents/` trees are
  byte-identical to the repository's `opencode/*/` trees. Every `SKILL.md` frontmatter `name` equals
  its folder name. The 26 skills with `opencode/autoinvoke: false` are exactly the repository's
  `manual` set.
- Isolated discovery check: no service runs on this host, so a private foreground `opencode serve`
  read a copy of the installed tree (`OPENCODE_CONFIG_DIR` pointing at the copy, `service.json`
  `{"disabled":true}` added to the copy only, HOME, XDG folders, and the database in the lab, a
  random loopback port and a random password that was never printed). `/api/skill` was polled until
  4 consecutive reads agreed (6 polls): 117 skills, the 115 dotted IDs (Akka 5, Aspire 8, General
  65, Process 37; 26 with `autoinvoke` false), each under the copy's `skills/`, plus the built-ins
  `opencode` and `report`. `/api/agent` listed both deniz agents with `mode: subagent`;
  `/api/command` listed only `init` and `review`. The real config tree hash was the same before and
  after, and the server, port, and lab were gone afterwards.
- No model call was possible or made: the host has no OpenCode login.

### WSL distro on the Windows workstation

**Status: INSTALLED.** The [profile migration](2026-10-09-opencode2-profile-migration.md) found
no install here; this was the fresh schema-2 install it named.

- Inventory: Ubuntu 22.04.5 under WSL2, OpenCode 2.0.26 as the npm global `@opencode/cli` under Node
  v24.21.0 from a version manager (already the only installed version and the default; no Node
  install was needed). `opencode debug paths` reports the default XDG config location;
  `OPENCODE_CONFIG_DIR` and `XDG_CONFIG_HOME` are unset. The config root held only three foreign
  files: `opencode.jsonc` (same SHA-256 as in the migration inventory), one dated backup of it, and
  `service.json`. No `.deniz-skills/`, `skills/`, `commands/`, or `agents/` existed, and no OpenCode
  process was running.
- Package: 885,480 bytes with the recorded SHA-256, checked again just before Apply.
- Plan: 4 Modules, 285 adds, no findings, exit 0; the config root's file list and hashes were
  unchanged afterwards.
- Apply exited 0. `status`: the same four Modules, versions, and digests as on macOS, all current,
  Lock none, Recovery none.
- After: 289 files. The three foreign files are byte-identical to the inventory with the same mode
  `600`; the only new paths are the 285 owned files (283 under `skills/` in 115 folders, 2 agents)
  and `.deniz-skills/install.json` (schema 2). All 285 owned files match their recorded SHA-256 and
  mode `100644`, and the recorded set equals the repository manifests' set. The 26 `manual` skills
  match the repository's set.
- Discovery against the real profile: a private `opencode serve` on loopback with a random password
  and an empty project folder made no model call. It listed 119 skills: the 115 dotted IDs (89
  advertised, 26 not), each under `<root>/skills`, the built-ins `opencode` and `report`, and 2 skills
  that Claude Code had synced into `<home>/.claude/skills`, which OpenCode loads through its Claude
  compatibility path. Both deniz agents were subagents; the commands were `init` and `review`. The
  config root was unchanged by the serve run.
- Shell finding: in this distro the version manager is initialized only for interactive shells, so a
  non-interactive login shell resolved the Windows workstation's `opencode` through the Windows
  interop path, and that binary reported Windows paths. All steps therefore ran in a shell that
  loaded the version manager explicitly, where `opencode` resolved to the distro's own install.

## Claude Code and Codex plugin updates

Every existing installation of the `deniz-skills` marketplace moved to `repo_head` with Process
0.7.0, General 0.10.0, Akka 0.4.0, and Aspire 0.4.0. No other marketplace or plugin was updated.

| Environment | Harness | Before | Commands | Result |
|---|---|---|---|---|
| Windows workstation | Claude Code (2.1.296 at verification), two configuration profiles | earlier versions | `claude plugin marketplace update deniz-skills`; `claude plugin update <p>@deniz-skills -s user`, and `-s project` from each project folder where a project-scoped install existed | all rows at the four versions, commit `a8a9425` |
| Windows workstation | Codex CLI (0.153.4 at verification), default profile | earlier versions | `codex plugin marketplace upgrade deniz-skills --json`; `codex plugin add <p>@deniz-skills --json` | the four versions installed; one disabled plugin had to be disabled again (below) |
| WSL distro | Claude Code 2.1.273 | marketplace at `83c09fa`, user and project scope | marketplace update, then `plugin update` in both scopes | all rows at the four versions, commit `a8a9425` |
| WSL distro | Codex 0.160.0 | no `deniz-skills` marketplace | none | not applicable |
| Linux host | Claude Code 2.1.286 | marketplace at `83c09fa`, user scope | marketplace update, then `plugin update` | all rows at the four versions, commit `a8a9425` |
| Linux host | Codex 0.159.3 | no `deniz-skills` marketplace | none | not applicable |
| macOS workstation | none installed | — | none | not applicable |

- Every Claude Code plugin cache folder for the new versions matches `plugins/<p>` file by file
  (Process 94, General 148, Akka 17, Aspire 46 files); the only extra file is the `.in_use/<pid>`
  marker that a running session writes. Every Codex cache folder matches `codex/<p>` file by file
  (Process 111, General 157, Akka 17, Aspire 46 files), and only the new version folders remain.
  Claude Code keeps the earlier version folders in its cache.
- No `settings.json` changed during the updates, and every `deniz-skills` entry in their
  `enabledPlugins` stayed enabled. In one Claude Code profile, the `lastUpdated` field of an
  unrelated plugin's row changed about a minute before the first update with its version and cache
  unchanged, most likely from a `claude` CLI start.
- **`codex plugin add` re-enables a disabled plugin.** `deniz-dotnet-akka` was disabled in the
  Codex configuration (`enabled = false`); the update set it to `true`. That one line was set back,
  and `codex plugin list` shows it installed, disabled, 0.4.0. The SHA-256 of the restored
  configuration was compared with a pre-update copy during the update; that copy was not kept, so
  the later verification confirmed the restored value, not the byte comparison.
- In WSL, the first project-scope update, started from Git Bash on the Windows workstation, failed
  because Git Bash rewrote the distro path argument; with that path conversion disabled it
  succeeded.
- Running Claude Code and Codex sessions keep the plugin versions they loaded until they restart;
  every Claude Code update printed "Restart to apply changes."

## Real-profile smoke tests

Each test used the real OpenCode profile and its running managed service, the `build` agent, an
empty working folder, and a fresh session per try with `openai/gpt-5.6-luna` at variant `low`. Each
session's stored model is `{providerID: openai, id: gpt-5.6-luna, variant: low}` with outcome
`succeeded`. The tries are real sessions that remain in each host's OpenCode history.

### Windows workstation

- Diagnostics: `opencode --version` (v2.0.26), `opencode service status`, `opencode debug paths`
  (default config root), `opencode debug config` (one global `opencode.jsonc` source; OpenCode
  itself masked the two secret header values; only a summary was kept), `opencode debug agents` (45
  agents, both deniz agents as subagents). The managed service runs the same binary as the CLI, and
  it started after that binary was last written, so the service and CLI versions agree.
- `opencode api skill.list`: the first call returned an empty list while the skill locations
  loaded; the second returned 134 skills: the 115 dotted IDs (Akka 5/0 hidden, Aspire 8/0, General
  65/9, Process 37/17), all under `<root>/skills`, plus 19 others (the 2 built-ins, the 3 unowned
  config-root skills, and 14 from the Claude compatibility path).
- Foreign files: a re-hash of the profile's `skills/`, `commands/`, `agents/`, and `opencode.jsonc`
  after the tries gave 342 files with 0 differences from the post-migration inventory.
- Tries a to c used `opencode run --format json`. `opencode run` cannot attach a skill, so tries d
  and e used the service API through the CLI (`opencode api` session create, then prompt, then
  context read). The PowerShell `opencode.ps1` shim breaks arguments with embedded quotes, so the
  binary was called directly; even so, the stored user text of tries a to c begins with a literal
  double quote. The skill choices are unaffected, but those prompts did not arrive byte-exact.

| model | probe_id | status | tools_observed | notes |
|---|---|---|---|---|
| `openai/gpt-5.6-luna#low` | `win-a-list-advertised` | `pass` | none | Asked to list the deniz skills it can see verbatim. Criterion: no `manual` skill and no unknown ID listed. It listed 88 of the 89 advertised IDs, none of the 26 hidden, none unknown; it omitted `deniz-process.requesting-code-review`, which the registry advertises (the same recall miss as the earlier panel). |
| `openai/gpt-5.6-luna#low` | `win-b-auto-flaky-test` | `pass` | `skill(deniz-process.systematic-debugging)` completed | A flaky-test debugging request with no skill named; the answer follows that skill's phased method. |
| `openai/gpt-5.6-luna#low` | `win-c-explicit-grilling` | `pass` | `skill(deniz-process.grilling)` completed; 3 × `execute` | "Load the skill deniz-process.grilling and start" on a wiki-migration plan; numbered questions with recommendations in the skill's format. The 3 `execute` calls were read-only searches in a configured memory MCP server; nothing was stored. |
| `openai/gpt-5.6-luna#low` | `win-d-attach-grill-me` | `pass` | `skill(deniz-process.grilling)` completed | `@deniz-process.grill-me` attached through the API; the user message carried the rendered body "Call the `skill` tool with \"deniz-process.grilling\"." and the model then loaded `grilling` and asked numbered questions. The handoff skill was not tried because it writes a file into the OS temp folder. |
| `openai/gpt-5.6-luna#low` | `win-e-auto-tunit` | `pass` | `skill(deniz-dotnet-general.writing-tunit-tests)` completed | A TUnit data-driven test question with no skill named; the answer shows a correct `[Arguments]` example. |

### Linux host

- Diagnostics: OpenCode v2.0.26 (it was 2.0.23 at the migration), Node v24.18.0 from a version
  manager, the systemd user service active; `service status`, `debug paths` (default config root),
  `debug config` (reduced to its sources: the global `opencode.jsonc`, the config folder, and a
  per-user `.opencode` folder), `mcp list` (four servers connected; one IDE-backed server failed with
  connection refused because the IDE was not running, unrelated to this estate), and `debug agents`
  (the built-ins and both deniz agents). `opencode auth list` was read for provider names only and
  showed OpenAI logged in.
- `opencode api skill.list`: 117 skills, the 115 dotted IDs (Akka 5, Aspire 8, General 65, Process
  37; 26 with `autoinvoke` false) and the 2 built-ins.
- Installer: the public Package, downloaded again and digest-checked, reported all four Modules
  current with Lock none and Recovery none; `install.json` is schema 2 with 285 records whose hashes
  equal the repository manifests and the files on disk.
- Foreign files: a fingerprint of the whole config root (493 entries) before and after the tries was
  identical. Content files, including `skills/`, `commands/`, `agents/`, `opencode.jsonc`, and
  `AGENTS.md`, were SHA-256 hashed; credential and service files were compared by size and mtime
  only and never read; `node_modules/` by a hash of its listing. The filter for credential files
  also caught 3 installer-owned skill files, which the verification confirmed by SHA-256 against the
  repository instead. A later `find -newer` over the whole root, `node_modules/` included, found no
  file changed after the before snapshot.
- Tries used `opencode run -m openai/gpt-5.6-luna#low --format json` from an empty working folder;
  each prompt ended with an instruction to use only the skill tool. Over ssh, `opencode run` waits
  on stdin when it is not a terminal: the first attempt produced no output until a 420-second timeout
  and left no session; with stdin redirected from `/dev/null` every run exited 0 with empty stderr in
  3.7 to 8.0 seconds. Each try's event log holds one `skill` call, completed, whose output contains
  the repository `SKILL.md` body.

| model | probe_id | status | tools_observed | notes |
|---|---|---|---|---|
| `openai/gpt-5.6-luna#low` | `linux-p1-auto-verification` | `pass` | `skill(deniz-process.verification-before-completion)` completed | "About to tell my teammate the fix is complete; load whichever available skill fits." The answer: run the full verification command fresh and report only what it confirms. |
| `openai/gpt-5.6-luna#low` | `linux-p2-named-auto-skill` | `pass` | `skill(deniz-dotnet-general.modern-csharp-coding-standards)` completed | Named skill. Asked for the first `# ` heading, the model returned the harness-injected `# Skill: <id>` line rather than the body heading; the load itself is correct. |
| `openai/gpt-5.6-luna#low` | `linux-p3-auto-aspire-redis` | `pass` | `skill(deniz-dotnet-aspire.aspireify)` completed | AppHost Redis question, "load the most relevant available skill". It chose `aspireify`, not the closer `aspire-orchestration`; the answer (`AddRedis`, `WithReference`, `AddRedisClient`) is correct. |
| `openai/gpt-5.6-luna#low` | `linux-p4-named-manual-skill` | `pass` | `skill(deniz-dotnet-general.convert-to-cpm)` completed | Named load of a `manual` (`autoinvoke: false`) skill succeeded. The heading answer is again the injected `# Skill: <id>` line. |
| `openai/gpt-5.6-luna#low` | `linux-p5-auto-akka-testing` | `pass` | `skill(deniz-dotnet-akka.akka-net-testing-patterns)` completed | Akka.Hosting actor test question; the answer names `Akka.Hosting.TestKit`, `ConfigureServices`/`ConfigureAkka`, `ActorRegistry`, and `TestProbe`. |

All 10 tries passed (10 of 10, denominator `pass` plus `fail`). The automatic tries (`win-b`,
`win-e`, `linux-p1`, `linux-p3`, `linux-p5`) named no skill, but the three Linux ones asked the model
to load whichever skill fit, so they show selection among offered skills, not unprompted use.

## Backup deletion

After both smoke tests passed, each live profile was checked again and the migration backups were
deleted.

- Windows workstation: `install.json` schema 2; `deniz-skills status` from the digest-checked Package
  listed the four Modules current, Lock none, Recovery none; no running process referenced the
  backup. The backup folder under `<home>` (3,836 files, 53,845,809 bytes) was deleted; the path no
  longer exists and `install.json` is in place.
- Linux host: the same checks, plus no process with its working folder or an open file inside the
  backup. The backup folder under `<home>` (3,950 files, 55,150,371 bytes by `du -sb`) was deleted;
  the path no longer exists and `install.json` is in place.

## Explicitly unmeasured

- No model try ran on the macOS workstation or in the WSL distro: macOS has no OpenCode login, and
  the WSL check was discovery only.
- The model-facing skill guidance of the real-profile sessions was not hashed. On Windows,
  `opencode api GET /api/event` wrote no output, and reading the stream directly would need the
  service credential. That the 26 `manual` skills are not advertised rests here on the registry
  data and the try-a listing; the isolated [manual-skill panel](2026-10-09-opencode2-manual-skill.md)
  holds the hashed proof.
- No try isolates a rendered handoff as the cause of a load: `win-d`'s target `grilling` is an auto
  skill whose triggers match the prompt.
- Whether reading a bundled support file from the global tree prompts for permission; no try read
  one.
- Linux bulk Apply against a running service: the Linux profile was not reinstalled in this
  rollout.
