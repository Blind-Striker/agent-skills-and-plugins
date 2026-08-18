---
record_id: opencode-module-installer-local-pack-2026-08-18
date: 2026-08-18
repo_head: f1277ed615b739d954003047a492e2dfc839fdd7
kind: runtime-smoke
summary: The local packed installer planned without Destination writes, installed all four Modules into an isolated Windows XDG Native tree, and matched OpenCode discovery; Release and human permission observations remain unmeasured.
isolation_ok: true
harness_name: OpenCode
harness_version: 1.18.18
package_name: deniz-agent-skills-0.1.0.tgz
package_sha256: 3eca19418b5f516500008219a4b7eb71230456a8b1d567911002d62fc9fd1eb0
transport: local-pack
---

# OpenCode Module installer: local packed package

## Scope and provenance

This record covers only free, local measurements: the PowerShell subsystem selftest, checkout
`Sync-Lab`, an npm-format package produced by `npm pack`, installer Plan/Apply/status, and OpenCode
`debug skill`, `debug config`, and `debug paths`. No credential, model call, TUI permission decision,
real-profile Apply, GitHub Release creation/upload, or Release download was used.

`repo_head` is the committed implementation base at measurement time. The measurement worktree also
contained the Task 9 integration tests, Plan-rendering correction, experiment updates, documentation,
and freshly rebuilt generated output that this record accompanies. The package SHA-256 above pins the
exact measured tarball bytes.

## Isolation

The package, profile, npm cache, OpenCode config/data/cache/state, and temporary directory lived
under one fresh `<external-lab>/installer-local` tree outside the repository and real user profile.
The run set `HOME`, `USERPROFILE`, `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `TEMP`, `TMP`, and
`npm_config_cache` below that tree, disabled compatibility discovery from Claude Code, and removed
`OPENCODE_CONFIG_DIR` from the environment. The working directory was the lab's empty project.

An initial path diagnostic, before `TEMP` and `TMP` were relocated, correctly found OpenCode's
reported temporary root outside the lab. No isolation claim was taken from that attempt. The protocol
was corrected, the diagnostic was rerun, and all nine reported roots then resolved below
`<isolated-profile>`.

## Commands run

```text
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1
npm run build
npm pack <repo> --json --pack-destination <isolated-profile>/package
npm exec --yes --package <package.tgz> -- deniz-skills install --all
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

- Package size: 686,587 bytes.
- Package SHA-256: `3eca19418b5f516500008219a4b7eb71230456a8b1d567911002d62fc9fd1eb0`.
- Install-state SHA-256: `9a54498476e078ab00684217c41fc448fbc20431de8e6a3adf0123cc8ae1d8a8`.
- Owned Native-tree files: 238.

| Module | version | Module digest | status |
|---|---|---|---|
| `deniz-dotnet-akka` | `0.1.0` | `sha256:62a23e77e1db501cd833791f44c1e901715a8f92b127646be2946c29ec2b9f2a` | current |
| `deniz-dotnet-aspire` | `0.1.0` | `sha256:a0fc21f9f0465c4f4526d98cffc88f8641c4a03b06e8993bcc8ecdbbce49db2e` | current |
| `deniz-dotnet-general` | `0.2.0` | `sha256:41509daee39199248aefefcb711ddd6317a971ff6d7f9b512b85750a8bc2cf53` | current |
| `deniz-process` | `0.2.0` | `sha256:0c986fcab085a876f80541382198764aa7858298612d8490ecf6ed6e90267593` | current |

Status also reported `Lock: none` and `Recovery: none` before and after OpenCode introspection.

## Assertions

| assertion | observed | status |
|---|---|---|
| Full isolated subsystem selftest | Plan zero-write, Apply allowlist, config-dir refusal, lab resolution, and all matrix dry runs completed | pass |
| Packed Plan is zero-write at the Destination | exit 0; npm populated only its isolated cache; Destination remained absent | pass |
| Packed Apply installs all Modules | exit 0; four selected Modules and 238 Ownership claims | pass |
| Checkout `Sync-Lab` uses installer composition | 84 skill directories, 33 commands, 1 agent | pass |
| Native-tree shape | 84 skill directories: 73 with `SKILL.md`, 11 BODY-only parked; 33 command files; 1 agent file | pass |
| Native tree contains no filesystem-visible link or junction | zero link-like descendants observed | pass |
| `debug skill` matches discoverable installed skills | 73 installed names, zero name diff, plus built-in `customize-opencode` | pass |
| Parked directories stay undiscoverable as skills | zero BODY-only names in `debug skill` | pass |
| Installed skill locations stay under Native tree | zero non-built-in locations outside `<destination>/skills` | pass |
| `debug config` matches installed commands | 33 expected, 33 resolved, zero name diff | pass |
| `debug config` matches installed custom agent | exactly `roslyn-incremental-generator-specialist` | pass |
| Runtime package adapter remains absent | resolved `plugin` count 0 | pass |
| Windows roots are isolated | all nine `debug paths` roots below `<isolated-profile>` after `TEMP`/`TMP` correction | pass |
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

## Explicitly unmeasured

- **GitHub Release transport:** no Release was created, uploaded, mutated, or downloaded. The private
  `installer-v0.1.0` asset hash and behavior are not established by this local package.
- **Human permission behavior:** no TUI/model run was performed, so there is no observation of whether
  a global BODY-backed command prompts for folder access or what scope an approval would have.
- **Runtime support-file reads:** introspection proved discovery paths, not that a model followed a
  command stub and read the intended parked body.
- **Post-initialization lifecycle:** status after OpenCode initialization was measured; Update and
  Remove after initialization were not run in this external package panel.
- **Real profile:** neither Plan nor Apply was run against the real profile in this task.

Raw package output and unsanitized paths remain only in the external lab.
