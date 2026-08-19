---
record_id: opencode-module-installer-local-pack-2026-08-18
date: 2026-08-18
repo_head: 5ab41171a894ed24931c704bdfbe7608c7d224de
kind: runtime-smoke
summary: The final local packed installer and the downloaded private GitHub Release asset both installed all four Modules into isolated Windows XDG Native trees with identical digests, bytes, and OpenCode discovery; the real profile was then migrated through the same Plan/Apply path with the 25 model-routing control-plane roots preserved; human permission and model-driven reads remain unmeasured.
isolation_ok: true
harness_name: OpenCode
harness_version: 1.18.18
package_name: deniz-agent-skills-0.1.0.tgz
package_sha256: 69532caf101f5626ea652cdb7e1046783b3d64fe79613e4d59f21e95eccb9460
transport: local-pack + private-release
release_tag: installer-v0.1.0
release_url: https://github.com/Blind-Striker/agent-skills-and-plugins/releases/tag/installer-v0.1.0
release_target: 5ab41171a894ed24931c704bdfbe7608c7d224de
---

# OpenCode Module installer: local packed package

## Scope and provenance

This record covers two free, isolated panels: the local packed package and, after explicit
authorization, the downloaded private GitHub Release asset. Both ran installer Plan/Apply/status and
OpenCode `debug skill`, `debug config`, and `debug paths` with no model call, TUI permission
decision, or real-profile Apply. The private Release download used authenticated `gh`; no credential
material was committed to this repository or supplied to OpenCode or to a model. The Release itself
was created in a separate, explicitly authorized mutation; it is not re-created or re-uploaded by
this record.

`repo_head` is the committed implementation base at measurement time. The measurement worktree also
contained the final review fixes, regression tests, experiment updates, documentation, and freshly
rebuilt generated output that this record accompanies. The package SHA-256 above pins the exact
measured tarball bytes.

## Isolation

The measured package copy, profile, npm cache, OpenCode config/data/cache/state, and temporary
directory lived under one fresh `<external-lab>/installer-local` tree outside the repository and real
user profile. The exact final artifact was first packed into the approved temporary release directory,
then copied byte-for-byte into `<isolated-profile>/package` for the run. The run set `HOME`,
`USERPROFILE`, `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `TEMP`, `TMP`, and `npm_config_cache` below the
isolated profile, disabled compatibility discovery from Claude Code, and removed
`OPENCODE_CONFIG_DIR` from the environment. The working directory was the lab's empty project.

## Commands run

```text
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1
npm run build
npm pack --json --pack-destination <approved-temporary-release-directory>
Copy-Item <final-package.tgz> <isolated-profile>/package/deniz-agent-skills-0.1.0.tgz
npm exec --yes --package <package.tgz> -- deniz-skills install --all
npm exec --yes --package <package.tgz> -- deniz-skills install --all --yes
npm exec --yes --package <package.tgz> -- deniz-skills install --all --yes
npm exec --yes --package <package.tgz> -- deniz-skills status
opencode --version
opencode debug skill
opencode debug config
opencode debug paths
npm exec --yes --package <package.tgz> -- deniz-skills status
```

The final status command ran after OpenCode had initialized its own support files in the shared
global config root.

## Package and Selection

- Package size: 688,609 bytes.
- Package SHA-256: `69532caf101f5626ea652cdb7e1046783b3d64fe79613e4d59f21e95eccb9460`.
- Install-state SHA-256: `85d5992c94a1ad6388edcc74f9beb1476cbc5cf012545dbbfe7ed64fd6bd5e9d`.
- Owned Native-tree files: 238.

| Module | version | Module digest | status |
|---|---|---|---|
| `deniz-dotnet-akka` | `0.1.0` | `sha256:0fa6792f82d306f57883b6b84c17eea41097c2f2729c7da329c619923bb34feb` | current |
| `deniz-dotnet-aspire` | `0.1.0` | `sha256:f69cc07a04a4627e84d384dea1b2477ed3ecd0c828169b65b4b3d59421877fed` | current |
| `deniz-dotnet-general` | `0.2.0` | `sha256:607d4b4652f94552446f830f2823e7e0aca188b51da0c85e90893c65e84dd258` | current |
| `deniz-process` | `0.2.0` | `sha256:dcd6528402adff11326be2c535b7874a6b3bbd0bae6a7328222b9714a5fc642f` | current |

Status also reported `Lock: none` and `Recovery: none` before and after OpenCode introspection.

## Assertions

| assertion | observed | status |
|---|---|---|
| Full isolated subsystem selftest | Plan zero-write, Apply allowlist, config-dir refusal, lab resolution, and all matrix dry runs completed | pass |
| Packed Plan is zero-write at the Destination | exit 0; npm populated only its isolated cache; Destination remained absent | pass |
| Packed Apply installs all Modules | exit 0; four selected Modules and 238 Ownership claims | pass |
| Repeated packed Apply is idempotent | exit 0; every Module rendered `No changes`; Install-state bytes remained unchanged | pass |
| Checkout `Sync-Lab` uses installer composition | 84 skill directories, 33 commands, 1 agent | pass |
| Native-tree shape | 84 skill directories: 73 with `SKILL.md`, 11 BODY-only parked; 33 command files; 1 agent file | pass |
| Native tree contains no filesystem-visible link or junction | zero link-like descendants observed | pass |
| `debug skill` matches discoverable installed skills | 73 installed names, zero name diff, plus built-in `customize-opencode` | pass |
| Parked directories stay undiscoverable as skills | zero BODY-only names in `debug skill` | pass |
| Installed skill locations stay under Native tree | zero non-built-in locations outside `<destination>/skills` | pass |
| `debug config` matches installed commands | 33 expected, 33 resolved, zero name diff | pass |
| `debug config` matches installed custom agent | exactly `roslyn-incremental-generator-specialist` | pass |
| Runtime package adapter remains absent | resolved `plugin` count 0 | pass |
| Windows roots are isolated | all nine `debug paths` roots below `<isolated-profile>` | pass |
| npm materialization is isolated | `npm config get cache` equaled `<isolated-profile>/.npm-cache` | pass |
| OpenCode support files coexist with Install state | OpenCode added `.gitignore` and `opencode.jsonc`; subsequent installer status remained current | pass |

## Sanitized observed paths

| role | path |
|---|---|
| package | `<isolated-profile>/package/deniz-agent-skills-0.1.0.tgz` |
| Destination | `<isolated-profile>/.config/opencode` |
| Install state | `<destination>/.deniz-skills/install.json` |
| installed skills | `<destination>/skills` |
| installed commands | `<destination>/commands` |
| installed agents | `<destination>/agents` |
| npm cache | `<isolated-profile>/.npm-cache` |
| OpenCode temporary root | `<isolated-profile>/.tmp/opencode` |

## Private GitHub Release transport

After explicit authorization, the published private GitHub Release `installer-v0.1.0` (URL above)
was created pinned to commit `5ab4117`. The `deniz-agent-skills-0.1.0.tgz` asset was downloaded
through authenticated `gh` into a fresh `<external-lab>/installer-release` tree and run through the
same protocol as the local pack: `install --all --yes`, status, OpenCode introspection, then status
again. `OPENCODE_CONFIG_DIR` was absent; `HOME`, `USERPROFILE`, `XDG_CONFIG_HOME`, `XDG_DATA_HOME`,
`TEMP`, `TMP`, and `npm_config_cache` all resolved below the isolated profile. GitHub reports the
Release as non-immutable (`immutable: false`): the tag/target pin and the recorded SHA-256 identify
the intended bytes and detect replacement or corruption, but they do not prevent an authorized
re-upload of the asset.

```powershell
gh release download installer-v0.1.0 --repo Blind-Striker/agent-skills-and-plugins `
  --pattern "deniz-agent-skills-0.1.0.tgz" --dir <isolated-profile>
npm exec --yes --package <downloaded.tgz> -- deniz-skills install --all --yes
npm exec --yes --package <downloaded.tgz> -- deniz-skills status
opencode --version
opencode debug skill
opencode debug config
opencode debug paths
npm exec --yes --package <downloaded.tgz> -- deniz-skills status
```

Equivalence with the local packed run recorded above:

| measurement | value | measured in |
|---|---|---|
| package bytes | 688,609 | both panels |
| package SHA-256 | `69532caf…c9460` | both panels |
| install-state SHA-256 | `85d5992c…5e9d` | both panels |
| Module digests (4) | identical, every Module `current` | both panels |
| owned Native-tree files | 238 (1 agents + 33 commands + 204 skills) | both panels |
| Native-tree shape | 84 dirs (73 + 11 parked), 33 commands, 1 agent | both panels |
| link-like descendants | 0 | both panels |
| Lock / Recovery | none / none | both panels |
| repeat-Apply idempotence | second packed Apply rendered `No changes`; Install-state bytes unchanged | local pack |
| installed-file bytes | all 238 files hash-identical to the repo Bundle files and to the install-state records | downloaded Release |

OpenCode 1.18.18 discovery against the downloaded Release tree:

| assertion | observed | status |
|---|---|---|
| `debug skill` names | 74: 73 installed plus built-in `customize-opencode`; zero name diff | pass |
| parked BODY-only dirs | zero names in `debug skill` | pass |
| skill locations | every non-built-in location below `<destination>/skills` | pass |
| `debug config` commands | 33 installed, 33 resolved, zero name diff | pass |
| custom agent | exactly `roslyn-incremental-generator-specialist` | pass |
| runtime package adapter | `plugin` list empty | pass |
| `debug paths` | all nine roots below `<isolated-profile>` | pass |
| npm cache | `npm config get cache` equaled `<isolated-profile>/.npm-cache` | pass |
| OpenCode support files | coexist; final installer status remained `current` | pass |

## Real profile migration (2026-08-18)

With explicit user approval, the real OpenCode profile (the machine's `~/.config/opencode`) was
migrated through the same Plan/Apply path as the isolated panels:

- Preflight reclassification of the 238 desired paths: 227 exact-final, 11 exact-old (bytes matched
  the pre-review flat output tree), 0 modified, 0 absent, 0 type/link mismatches; no `.deniz-skills`
  existed.
- Backup: the 118 takeover roots (84 skill dirs, 33 command files, 1 agent file) were copied with
  relative layout plus a SHA-256 manifest to
  `<temp>/opencode-profile-cleanup-backup-2026-08-18` (238 files, 1,968,647 bytes) and verified
  hash-for-hash before any deletion. The backup is kept, not deleted.
- The 25 routing control-plane roots (skill `subagent-model-routing`; commands `router-code.md`,
  `router-research.md`, `router-review.md`; 21 model-routing agent files) were preserved byte-for-byte
  and remain outside Module Ownership: install.json claims only the 238 Module files.
- `opencode.jsonc` remained byte-identical (no config edit; no Superpowers plugin entry existed).
- Plan after cleanup: exit 0, 0 findings, 238 Add operations, 4 Selection additions.
- Apply (`install --all --yes`): exit 0. Status: four Modules current, Lock none, Recovery none.
- Final verification: all 238 installed files hash-identical to the repo Bundles and install-state
  records; routing roots unchanged; zero link-like descendants; OpenCode discovery reports 74
  discovered skills (73 curated plus `subagent-model-routing`) and the built-in `customize-opencode`,
  36 commands (33 curated plus 3 router commands), and 24 agents (curated specialist plus 21 routing
  files plus built-in `build`/`plan`); plugin list empty.

## Explicitly unmeasured

- **Human permission behavior:** no TUI/model run was performed, so there is no observation of whether
  a global BODY-backed command prompts for folder access or what scope an approval would have.
- **Runtime support-file reads:** introspection proved discovery paths, not that a model followed a
  command stub and read the intended parked body.
- **Post-initialization lifecycle:** status after OpenCode initialization was measured; Update and
  Remove after initialization were not run, in the external panels or the migrated real profile.

Raw package output and unsanitized paths remain only in the external lab.
