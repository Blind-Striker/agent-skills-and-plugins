---
record_id: opencode2-profile-migration-2026-10-09
date: 2026-10-09
repo_head: aa2de1e67bbaff0a8feef9c3be675d047ca13ff3
kind: profile-migration
summary: The curator-approved one-off moved the real OpenCode 2 profiles on the Windows workstation and the Linux host from installer-v0.3.0 schema-1 state (310 owned files each) to the checkout's schema-2 output (four Modules current, 285 owned files, 115 dotted skills and 2 dotted agents discovered), with every unowned file byte-identical and the running service never restarted; the WSL distro on the Windows workstation had no install and was not applicable.
isolation_ok: true
real_profile_migration: authorized-one-off-2026-10-09
harness_name: opencode
harness_version: 2.0.26 (Windows workstation), 2.0.23 (Linux host)
measured_checkout: aa2de1e67bbaff0a8feef9c3be675d047ca13ff3
removal_package: deniz-agent-skills-0.3.0.tgz (Release installer-v0.3.0)
removal_package_sha256: a6e5c309cd4739684d908c9bae224941272c57471f278b9a738dac53f704ef22
---

# One-off OpenCode 2 profile migration

This tier-1 record covers plan Task 18: the one-off move of the curator's real OpenCode profiles
from `installer-v0.3.0` schema-1 Install state and OpenCode 1 shapes to the checkout's schema-2
output. It is a recorded procedure, not a product path; the installer gained nothing for it
([distribution and installation](../../../docs/architecture/distribution-and-installation.md)).
The curator gave the go for each environment (gate G6) before any change.

The real-profile mutations are authorized changes, not isolated panels. `isolation_ok: true`
describes only the isolated discovery check on a byte copy of the migrated Windows tree (below). No
model or LLM call was made. No credential was read or printed: the running services were read
through the `opencode api` CLI, which authenticates by itself. `<root>` is the OpenCode config root
and `<home>` the user's home directory. Raw outputs, inventories, and backups stay outside the
repository.

## Procedure

The same steps ran in each environment that held an install, each followed by an independent
read-only verification:

1. Read-only inventory: `opencode --version`; `opencode debug paths` (config root); whether
   `OPENCODE_CONFIG_DIR` and `XDG_CONFIG_HOME` are set; the schema of
   `<root>/.deniz-skills/install.json`; `deniz-skills status` from the `installer-v0.3.0` Package;
   SHA-256 of every file under `<root>/skills`, `<root>/commands`, `<root>/agents` and of
   `<root>/opencode.json(c)`; and that the running service reads the same root.
2. Full backup of the config root to a dated directory outside it, compared with the live root
   before any change. The backups are kept.
3. OpenCode left as it was; no service was stopped.
4. `installer-v0.3.0` Package downloaded with `gh release download` into a new empty directory and
   digest-checked against `removal_package_sha256` before each run; `remove --all` Plan, then Apply
   with `--yes`, then `status`.
5. `<root>/.deniz-skills/` confirmed to hold only `install.json` equal to
   `{"schemaVersion":1,"modules":{},"files":{}}` and no transaction or journal directory, then
   deleted.
6. From the checkout at `measured_checkout`: `npm run install:opencode -- install --all` Plan, then
   Apply with `--yes`, then `status`.
7. Verification: owned paths against state and Bundles, OpenCode 1 leftovers, unowned files against
   the inventory and the backup, and OpenCode discovery.

## Windows workstation

**Status: MIGRATED.**

- Inventory: OpenCode 2.0.26, Windows 11 Pro 10.0.26300, PowerShell 7.6.6, Node v24.13.0, npm
  11.20.0. The config root is the default user config location; `OPENCODE_CONFIG_DIR`,
  `XDG_CONFIG_HOME`, and `OPENCODE_TEST_HOME` are unset in the process, User, and Machine scopes.
  The v0.3.0 `status` printed the same Destination. Its Selection was `deniz-dotnet-akka` 0.3.0,
  `deniz-dotnet-aspire` 0.3.2, `deniz-dotnet-general` 0.9.0, and `deniz-process` 0.5.0, all
  current, Lock none, Recovery none. `install.json` was schema 1 with 310 owned files, all present
  with their recorded hashes. The managed service (`opencode serve --service`) watched the same
  root, according to its watcher log entries for `<root>` and `<root>/skills`.
- Hash scope before: 367 files (287 under `skills/`, 41 under `commands/`, 38 under `agents/`, and
  `opencode.jsonc`; no `opencode.json`). 57 of them were unowned: another tool's 36 model-routing
  agents, its 3 router commands, the 3 skill folders `open-browser-use`, `open-computer-use`, and
  `subagent-model-routing`, and `opencode.jsonc`. The root also holds that tool's install marker,
  `AGENTS.md` and its backups, `cli.json`, `package.json`, `package-lock.json`, `node_modules/`,
  `service.json`, `.gitignore`, and `opencode.jsonc` backups. None of the 285 schema-2 paths
  overlapped an unowned file or folder.
- Backup: full copy of the config root in a dated directory under `<home>`, 3836 files, about 51 MB,
  no reparse points; SHA-256 of every file equal to the source.
- Removal: Plan listed exactly the 310 owned files with 0 findings, 0 Collisions, and 0 Local
  modifications. Apply exited 0; `status` showed Selection empty, Lock none, Recovery none.
- Install: Plan added 4 Modules and 285 paths with no Collisions. Apply exited 0 and printed the
  post-Apply Windows warning (`anomalyco/opencode#47505`). `status`: `deniz-dotnet-akka` 0.4.0,
  `deniz-dotnet-aspire` 0.4.0, `deniz-dotnet-general` 0.10.0, `deniz-process` 0.7.0, all current,
  Lock none, Recovery none.
- After: `install.json` is schema 2 and owns 285 files, each equal on disk, in state, and in the
  checkout Bundle; each Module's version and digest equals its Bundle manifest. The hash scope holds
  342 files (300 under `skills/`, 3 under `commands/`, 38 under `agents/`, and `opencode.jsonc`):
  285 owned plus the 57 unowned. No path of the 310 schema-1 set remains unless schema 2 owns it; no
  `BODY.md`, no empty folder, and no bare-named deniz skill, command, or agent remains.
- Unowned files: the 57 hash-scope files are byte-identical to the inventory. Every file outside
  `.deniz-skills/` and outside both installers' owned sets, including `node_modules/` and all root
  files, matched the backup: 3525 before, 3525 after, 0 differences. The counts reconcile: backup
  3836 = 3525 + 310 + 1 `install.json`; live 3811 = 3525 + 285 + 1.
- Service: the managed service kept the same process and start time through both Applies, so the
  watcher crash did not occur this time and no reopen was needed. Read through `opencode api`,
  `/api/skill` listed the 115 dotted IDs (Akka 5, Aspire 8, General 65, Process 37), equal in both
  directions to the 115 `skills/<id>/SKILL.md` entries in state and each under `<root>/skills/<id>/`,
  plus the 2 built-ins, the 3 unowned config-root skills, and 14 skills that OpenCode's Claude
  compatibility path loads from `<home>/.claude/skills` (not this repository's). The first poll
  returned an empty list while the location loaded; the second was complete. `/api/agent` listed
  `deniz-dotnet-akka.akka-net-specialist` and
  `deniz-dotnet-general.roslyn-incremental-generator-specialist` as subagents; `/api/command` listed
  `init`, `review`, the 3 router commands, and MCP prompts, and no deniz command.
- Isolated discovery check: the migrated `skills/`, `commands/`, and `agents/` trees (341 files, no
  `opencode.jsonc`) were copied into a lab and read by `oc2-discovery.ps1` at `measured_checkout`
  (isolated `serve` on loopback with a random password, managed service disabled). `/api/skill`
  listed 120 skills: the 115 dotted IDs (26 not advertised, the `manual` set), the 3 unowned skills,
  and the 2 built-ins; the agents and commands matched the live service.

## WSL distro on the Windows workstation

**Status: NOT_APPLICABLE.** The distro never held a deniz-skills install, so there was no schema-1
state to migrate.

- Inventory (read-only): OpenCode 2.0.26 and Node v24.21.0, both from a version manager.
  `opencode debug paths` reports the default XDG config location; `OPENCODE_CONFIG_DIR` and
  `XDG_CONFIG_HOME` are unset. The config root holds only `opencode.jsonc`, one backup of it, and
  `service.json`; it has no `.deniz-skills/`, `skills/`, `commands/`, or `agents/`. A depth-5 search
  under `<home>` found no `.deniz-skills/` folder and no OpenCode `install.json`. No OpenCode process
  was running.
- Steps 2 to 7 were not run: no backup, no removal, no install, and nothing was written in the
  distro. `opencode.jsonc` was hashed and is unchanged.
- A deniz-skills install in this distro would be a fresh schema-2 install, not a migration; it was
  not part of this task.

## Linux host

**Status: MIGRATED.**

- Inventory: OpenCode 2.0.23, Node v24.18.0 from a version manager. The config root is the default
  XDG config location; `OPENCODE_CONFIG_DIR` and `XDG_CONFIG_HOME` are unset in the shell and in the
  running service's environment. The service is a systemd user unit running
  `opencode serve --service` with the same home. The v0.3.0 `status` printed the config root as
  Destination with the same four Modules and versions as on Windows, all current, Lock none,
  Recovery none. `install.json` was schema 1 with 310 owned files, all present with their recorded
  hashes; 14 skill folders held parked `BODY.md` files. The hash scope held 311 entries: the 310
  owned files and `opencode.jsonc`, so no unowned file existed under `skills/`, `commands/`, or
  `agents/`. Other root entries: another tool's install marker, `AGENTS.md` and five backups, four
  `opencode.jsonc` backups, `.gitignore`, package files, `node_modules/`, a manual backup folder,
  `cli.json`, `service.json`, and two credential files whose contents were never read. Through
  `opencode api`, the service listed 90 skills: the 88 `SKILL.md` folders under `<root>/skills` and
  the 2 built-ins, so it read the same root.
- Backup: full `cp -a` of the config root into a dated directory under `<home>` (64 MB, mode 700);
  `diff -rq` against the live root showed no difference before any change. The downloaded Package
  and raw evidence are kept beside it on the host.
- Removal: the PowerShell Release recipe was run as its bash equivalent (`gh release download` and
  `sha256sum`). Plan: 4 Modules, 310 removes, no Collisions or Local modifications. Apply exited 0;
  `status` showed Selection empty, Lock none, Recovery none. The removal left `skills/`,
  `commands/`, and `agents/` as empty folders, which the install then filled.
- Install: the checkout moved to the host as a `git bundle` and was checked out at
  `measured_checkout` in a temporary directory, removed afterwards. Plan: 4 Modules, 285 adds, no
  Collisions or findings. Apply exited 0; `status` showed the same four Modules, versions, and
  digests as on Windows, all current, Lock none, Recovery none.
- After: `install.json` is schema 2 with 285 owned files; for all 285 the on-disk, recorded,
  manifest, and Bundle bytes agree, with matching Module attribution. The files under `skills/`,
  `commands/`, and `agents/` are exactly the 285 owned files: 115 dotted skill folders, no bare-named
  folder, no `BODY.md`, an empty `commands/` (the 38 old command files are gone), and only the 2
  dotted agents.
- Unowned files: `opencode.jsonc` has the same SHA-256 in the inventory, the backup, and the live
  root. `diff -rq --no-dereference` of the live root against the backup, excluding `skills/`,
  `commands/`, `agents/`, and `.deniz-skills/`, found no difference across 3639 files on each side,
  7 symlinks included; the credential files were compared by `diff` only. The top-level listing and
  each top-level entry's mode, size, mtime, and type are unchanged. The counts reconcile: backup
  3950 = 3639 + 310 + 1 `install.json`; live 3925 = 3639 + 285 + 1.
- Service: the systemd unit stayed active with the same main process, its start time unchanged, and
  0 restarts; it hot-reloaded the new tree. Through `opencode api`, `skill.list` returned 117 skills:
  the 115 dotted IDs, each at `<root>/skills/<id>/SKILL.md` with 26 `autoinvoke: false`, and the 2
  built-ins, with no bare deniz ID. `agent.list` shows the 2 dotted agents as subagents.
  `command.list` holds only `init`, `review`, and MCP prompts. The first call can return 0 skills
  while the location loads, and piping its output to `jq` truncated it at about 200 to 290 KB;
  redirecting to a file worked.

## Findings

- **The README Release recipe fails as written under PowerShell 7 when `npm` resolves to the
  `npm.ps1` shim** (as with a Windows Node version manager). PowerShell drops the `--` separator
  when it calls a script, so npm consumes the installer's own flags: `remove --all` was refused with
  `unknown_module remove requires explicit Modules or all` and wrote nothing, and a `--yes` Apply
  would silently degrade to a Plan; `status` works because it takes no flags. Calling `npm.cmd`
  explicitly worked. This is input for the Task 19 README recipe.
- The live Windows skill list (134) is larger than the isolated count (120) because OpenCode also
  loads skills from the Claude compatibility folder; those are outside this repository's scope.
- Pre-existing backups from earlier configuration work remain in both config roots, untouched; they
  are the curator's to prune.

## Explicitly unmeasured

- Linux bulk Apply against a running service under stress; this one-off is a single observation per
  host, not a repetition panel.
- Model-driven skill loading in the migrated profiles; only discovery was checked.
