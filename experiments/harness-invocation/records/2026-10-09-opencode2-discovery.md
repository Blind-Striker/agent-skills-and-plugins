---
record_id: opencode2-discovery-2026-10-09
date: 2026-10-09
repo_head: aa582a52914ff22739a0cd465fcbc5a35dc3974d
kind: structural
summary: OpenCode 2.0.23 discovered the four installed Bundles identically on the Windows workstation and the Linux host (115 dotted skills plus 2 built-ins, exactly the 26 manual IDs unadvertised, no commands, both Bundle agents as subagents), and an empty OPENCODE_CONFIG_DIR fell back like an unset one on Linux.
isolation_ok: true
harness_name: opencode
harness_version: 2.0.23
runner_revision: aa582a52914ff22739a0cd465fcbc5a35dc3974d
runner: experiments/harness-invocation/oc2-discovery.ps1
---

# OpenCode 2 discovery of the installed Bundles

This tier-1 record covers plan Task 15: the checkout's OpenCode Bundles, installed by the repository
installer into an isolated lab, as an isolated OpenCode 2 `serve` discovers them. It was run on the
Windows workstation and on the Linux host. An independent verifier repeated each host's run in a new
lab. No model or LLM call was made. Raw outputs, logs and absolute paths stay outside the repository;
`<lab>` below is the disposable lab root.

## Environment

| | Windows workstation | Linux host |
|---|---|---|
| OS | Windows 11 Pro 10.0.26300 | Ubuntu 26.04.1 LTS, x86_64 |
| OpenCode | `opencode v2.0.23` (CLI); `/api/info` 2.0.23 | 2.0.23 (CLI and `/api/info`) |
| Node / npm | v24.13.0 / 11.20.0 | v24.18.0 / 11.16.0 (version manager, put on `PATH` for the installer only) |
| PowerShell | 7.6.6 | portable 7.6.6 in a `mktemp -d` directory, tarball SHA-256 checked against the release `hashes.sha256` |
| Checkout | `master` at `repo_head`, clean | `master` at `repo_head`, moved over as a `git bundle` and cloned into the temporary directory |

The runner is `oc2-discovery.ps1` at `runner_revision`, unchanged, on both hosts. On the Linux host
it ran under the portable PowerShell recipe in the [runbook](../runbook.md).

## Method

1. Build a fresh lab outside the repository and the user profile with `Get-OpenCodeLabEnvironment`
   and `Initialize-OpenCodeLab` from [`common.ps1`](../common.ps1). `oc2-discovery.ps1 -DryRun`
   printed every root below the lab and created nothing.
2. Install the checkout output into the lab: `npm run install:opencode -- install --all --yes`, then
   `status`, with `OPENCODE_CONFIG_DIR=<lab>/config`. The verifiers and the Linux runs applied the
   full lab environment to the installer (home, XDG and temporary roots also inside the lab), so a
   lost `OPENCODE_CONFIG_DIR` could not reach the real config root.
3. Run `oc2-discovery.ps1 -Lab <lab>` (Windows: twice; Linux: once). It starts an isolated
   `opencode serve` on 127.0.0.1 with a random Basic password, reads `/api/skill`, `/api/command`,
   and `/api/agent` for `<lab>/project`, stops the server in `finally`, and fails closed if any
   skill path is neither `/builtin/` nor below the lab.
4. Compare the output with the expected estate. Expected skills are the ledger's OpenCode skill
   identities plus the original skill `deniz-dotnet-general.writing-tunit-tests`, which has no
   ledger entry, plus the built-ins `opencode` and `report`. Expected hidden skills are the ledger
   items with `invocation: manual`. The Linux verifier derived the expected estate independently
   from the generated `opencode/*/skills/*/SKILL.md` frontmatter instead of the ledger.
5. Snapshot the real state before and after, stop every lab process, delete the lab, and confirm
   the repository is clean.

Each verifier repeated steps 1-5 in a new lab. The Windows verifier ran the committed script 8 times: 3 times with
default settings, once with planted probe skills as a positive control, once after removing them,
and 3 times with `-StableSeconds 1`. The Linux verifier ran it 4 times: twice on
the same lab, once under `strace`, and once with `-StableSeconds 1`.

## Results

| Check | Expected | Windows workstation | Linux host | Status |
|---|---|---|---|---|
| Installer Plan/Apply | 4 Modules, no Collisions | exit 0; 285 files added | exit 0; Plan and Apply output byte-identical; 285 file lines | pass |
| Installer status | all current | `deniz-dotnet-akka` 0.4.0, `deniz-dotnet-aspire` 0.4.0, `deniz-dotnet-general` 0.10.0, `deniz-process` 0.7.0 current; Lock none; Recovery none | same; a re-Plan after Apply showed no changes | pass |
| Installed files | 115 `SKILL.md`, 2 agent files | 115 and 2 under `<lab>/config` | 115 under `<lab>/config/skills` | pass |
| Skills discovered | 117 (115 installed + 2 built-ins) | 117; none missing, none extra | 117; none missing, none extra | pass |
| Hidden skills (`advertised: false`) | the 26 `manual` IDs | 26; set equals ledger `manual`, ledger `opencode.advertised: false`, and Bundle `opencode/autoinvoke: false` | 26; set equals the Bundle `opencode/autoinvoke: false` files | pass |
| Advertised skills | every other skill, built-ins included | 91, including `writing-tunit-tests`, `opencode`, `report` | same | pass |
| Commands | 0 installed | built-ins `init` and `review` only | same | pass |
| Agents | the 2 Bundle agents, `mode: subagent` | `deniz-dotnet-akka.akka-net-specialist`, `deniz-dotnet-general.roslyn-incremental-generator-specialist`, both `subagent` | same | pass |
| Built-in agents (aside) | not asserted | `build`, `compaction`, `plan`, `summary`, `title` primary; `explore`, `general` subagent | same | n/a |
| Installed IDs dotted | every ID `<plugin>.<name>` with a known plugin | 117 of 117 (115 skills, 2 agents); 0 violations | same | pass |
| Skill paths | `/builtin/` or below the lab | fail-closed check passed; a path-capture variant showed 2 `/builtin/` and 115 `<lab>/config/skills/<id>/SKILL.md` | fail-closed check passed | pass |
| Repeatability | identical output | both runs and the 7 verifier runs without probes byte-identical (SHA-256 `20a31d22...3fa524`) | all 4 verifier runs byte-identical to each other and to the first run | pass |

Per Module, the 115 installed skills split as Akka 5, Aspire 8, General 65, and Process 37.
After normalization of line endings, the Windows and Linux discovery JSON objects are identical.
One script run took about 5-6 s on Windows (2.9-3.0 s with `-StableSeconds 1`) and 3.3-7.6 s on
Linux (3.3 s with `-StableSeconds 1`; the 7.6 s run was under `strace`).

The 26 hidden IDs:

- `deniz-dotnet-general`: `code-testing-agent`, `convert-to-cpm`, `dotnet-aot-compat`,
  `dotnet-trace-collect`, `dump-collect`, `generate-testability-wrappers`,
  `migrate-nullable-references`, `migrate-static-to-wrapper`, `thread-abort-migration`.
- `deniz-process`: `ask-deniz`, `grill-me`, `grill-with-docs`, `handoff`, `implement`,
  `improve-codebase-architecture`, `setup-matt-pocock-skills`, `teach`, `to-questionnaire`,
  `to-spec`, `to-tickets`, `triage`, `using-superpowers`, `wait-what`, `wayfinder`, `wizard`,
  `writing-skills`.

## Empty `OPENCODE_CONFIG_DIR` on the Linux host (plan Task 15 Step 4)

In an isolated `mktemp` lab on the Linux host (home and the four XDG roots inside the lab,
`OPENCODE_DISABLE_PROJECT_CONFIG=1`), `opencode debug paths` reported:

| `OPENCODE_CONFIG_DIR` | Reported config root |
|---|---|
| unset | `<lab>/<xdg-config>/opencode` (XDG fallback) |
| set to the empty string | `<lab>/<xdg-config>/opencode`, the same as unset |
| `<lab>/oc` | `<lab>/oc` (the variable replaces the root) |

The data, cache, state, and database roots did not move in any case. OpenCode 2.0.23 therefore
treats an empty value as unset on Linux, which matches the installer's "set and non-empty" rule, so
the plan's stop condition for this question does not apply. This was measured with
`opencode debug paths`, the CLI's own view; it does not show what an already running managed service
inherited.

## Isolation

- **Environment.** The server and the installer had `HOME`, `USERPROFILE`, `OPENCODE_TEST_HOME`,
  the four XDG roots, `OPENCODE_CONFIG_DIR=<lab>/config`, `OPENCODE_DB`, and `TEMP`/`TMP`/`TMPDIR`
  below the lab, plus `OPENCODE_DISABLE_PROJECT_CONFIG=1`, `OPENCODE_DISABLE_AUTOUPDATE=1`, and
  `OPENCODE_DISABLE_MODELS_FETCH=1`. `OPENCODE_CONFIG`, `OPENCODE_CONFIG_CONTENT`,
  `OPENCODE_CONFIG_PROJECT_DISABLE`, `OPENCODE_PASSWORD`, `OPENCODE_SERVER_PASSWORD`, and
  `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS` were cleared. `<lab>/config/service.json` was
  `{"disabled":true}`. On the Linux host, decoy values of `OPENCODE_CONFIG` and
  `OPENCODE_CONFIG_CONTENT` set in the parent shell were cleared, and `strace` showed no access to
  the decoy paths.
- **Roots.** The installer's Destination was `<lab>/config` on both hosts. On the Linux host,
  `opencode debug paths` in the same environment put every root (home, config, data, cache, state,
  tmp, log, database) below the lab.
- **Watchers.** The serve logs subscribed watchers only on `<lab>/config` (with its `AGENTS.md`,
  `skill`, and `skills`) and `<lab>/home`, with `<lab>/project` as the location. No log on
  either host held a path from the real profile, and the Linux logs held no WARN or ERROR line.
- **Compatibility roots.** On Windows, a positive control planted probe skills: the run found them
  in `<lab-home>/.claude/skills` and `<lab-home>/.agents/skills` (`<lab-home>` is the lab's home
  folder), so those always-on roots follow the redirected home. It did not find probes in `<lab>/xdg/config/opencode/skills`, in the project's
  `.opencode`, `.claude`, or `.agents` skill folders, or in a lab-root ancestor. None of the skill
  names in the real `~/.claude/skills` or the real config root appeared. On the Linux host the real
  compatibility roots are empty, so the absence check there could not fail; `strace` instead showed
  the compatibility roots looked up only below `<lab>/home`.
- **Real state.** Windows: the real `~/.config/opencode`, `~/.claude/skills`,
  `~/.local/state/opencode`, and `~/.cache/opencode` had no added, removed, or changed entries
  (SHA-256 and mtime), and `~/.agents` stayed absent. Linux: the SHA-256 of all 9492 files under the
  real `~/.config/opencode`, `~/.claude`, and `~/.agents` was identical before and after, and
  `find -newer` over the real OpenCode data, state, and cache roots, `.npm`, and the PowerShell roots
  found no changed file. `strace` of `serve` and of the installer showed no write, `mkdir`, or
  `unlink` outside the lab.
- **Managed service.** On both hosts the real managed `opencode serve --service` kept the same
  process and was never contacted.
- **Cleanup.** Every lab server stopped in `finally`. A `/proc` scan (Linux) and a process listing
  (Windows) found no leftover lab process. Every lab and the Linux temporary directory were deleted,
  and the repository stayed clean.

The real `~/.local/share/opencode` on the Windows workstation changed during the window (database,
log, and shell-output rotation). The cause is the already running real service and other sessions'
processes that used the real profile at the same time: every lab used its own database, and the real
log never mentions a lab path. This record does not claim that directory was static.

## Caveats and corrections

- **Plan count.** Plan Task 15 Step 3 expected "the 27 manual IDs". The current estate has 26:
  `deniz-process.writing-for-agents` moved from `manual` to `both` in curation after the plan's
  packet, and it is discovered as advertised. The plan's "115 skills" equals the 114 ledger skill
  identities plus the original `writing-tunit-tests`.
- **PowerShell npm shim.** On the Windows workstation, `npm run install:opencode -- install --all --yes`
  run through PowerShell's `npm.ps1` shim loses the bare `--`. The installer then receives only
  `install` and refuses with `unknown_module install requires explicit Modules or all` (exit 1,
  nothing written). The verifier reproduced this. The same command through `npm.cmd` works.
- **Linux binary identity.** The `opencode` on the Linux host's `PATH` is a shell shim that runs the
  version manager's default `opencode`, the same binary as the real managed service. The lab version
  therefore follows the real version-manager alias, which the shim reads from the real home
  (read-only).
- **Local provider probes.** At startup on the Linux host, `serve` attempted TCP connections to
  127.0.0.1 ports 1234, 11434, and 8000 (local model-provider autodetection), even with
  `OPENCODE_DISABLE_MODELS_FETCH=1`. All were refused there. On a host running such a service, a lab
  server would query it. This is contact outside the lab, though not a model call.
- **First-run checks that could not fail.** The Linux first run checked leftover processes with
  `pgrep` on the temporary path, which a `serve` command line does not contain. The verifier's
  `/proc` scan closes that gap.
- **Lab placement.** `oc2-discovery.ps1` refuses a lab inside the repository but not one inside the
  user profile. With `OPENCODE_DISABLE_PROJECT_CONFIG=1` the control above shows that ancestor
  directories are not read, so this is safe while that variable is set.
- **Concurrent activity.** On the Windows workstation, other sessions started `opencode run`
  processes against the real profile and a sibling workflow started its own isolated `serve`
  during the window. None was started by this measurement.

## Explicitly unmeasured

Model behavior with these skills (the manual-skill posture, `@` attachment, and model calls by
namespaced ID) is the separate Task 16 record. A running managed service's environment, macOS, and
a human permission prompt for bundled support files were not measured here. Bulk Apply against a
running server is a separate record.
