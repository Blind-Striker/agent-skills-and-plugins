# Harness invocation runbook

Date: 2026-10-12

The lab must be outside both the repository and the real user profile. If the drive-root default is
not the prepared lab, point the runners at it for the current shell:

```powershell
$env:HARNESS_LAB = "<lab-root>"
```

Run the free, deterministic checks before using either harness:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab
```

Walk the Codex runner without changing state, then run its credential-free structural panel. The
real run creates a fresh `CODEX_HOME` below the external lab, exercises the nonsense-control
marketplace, and installs all four generated repository plugins:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/codex-matrix.ps1 -DryRun -GeneratedPlugins
pwsh -NoProfile -File experiments/harness-invocation/codex-matrix.ps1 -GeneratedPlugins
```

Do not copy credentials from the real profile as part of the structural panel. For a later
behavioural run, create an isolated lab directory first, seed its `auth.json` out of band or provide
`OPENAI_API_KEY`, and pass that directory explicitly:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/codex-matrix.ps1 `
  -GeneratedPlugins -Behavioural -CodexHome <LAB_CODEX_HOME> `
  -Model gpt-5.6-luna -ReasoningEffort low
```

The runner relies on the disposable `CODEX_HOME` for configuration isolation. For model calls it
uses Codex's reviewed workspace mode and adds only that profile's plugin cache, because installed
skill bodies and bundled references sit outside `<LAB>/project`. Do not replace this with a
machine-wide sandbox bypass, and do not add `--ignore-user-config`: Codex CLI 0.153.4 hides the
isolated profile's installed plugins when that flag is present.

The behavioural runner begins with a one-token liveness check, sends explicit `$plugin:skill`
prompts through stdin, repeats implicit controls, checks manual suppression and an uninstalled
negative control, exercises a cross-skill handoff and bundled reference, then runs one explicit and
one implicit probe against the generated estate while all four plugins are installed. It kills
timed-out processes and retains raw JSONL only under the external lab. Override `-Model` or
`-ReasoningEffort` when a comparison run is needed; never let a profile default choose the model for
a recorded panel.

Check OpenCode 2 discovery against an isolated `opencode serve` without a model call. The dry run
prints the isolated environment and the server command and starts nothing; the real run creates the
lab tree, disables the managed background service in `<lab>/config/service.json`, serves on
127.0.0.1 with a random password, prints the registered skills (with `advertised`), commands, and
agents (with `mode`) as JSON, and stops the server in all cases:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/oc2-discovery.ps1 -Lab <lab-root> -DryRun
pwsh -NoProfile -File experiments/harness-invocation/oc2-discovery.ps1 -Lab <lab-root>
```

On a host without PowerShell, such as the Linux host, run the same script with a portable
PowerShell; do not rebuild the isolation by hand or in a second script, which would have to repeat
the environment table, the cleared variables, and the `service.json` placement. Download the
official `powershell-<version>-linux-x64.tar.gz` and `hashes.sha256` from the PowerShell GitHub
release into a `mktemp -d` directory (`hashes.sha256` is UTF-16; convert it with `iconv` before
comparing SHA-256), extract the archive there, and give PowerShell its own home so that it writes
nothing below the real one. Nothing is installed system-wide; remove the directory afterwards:

```sh
tmp=$(mktemp -d)   # holds the verified tarball, the extracted pwsh/, pwsh-home/ and lab/
HOME="$tmp/pwsh-home" POWERSHELL_TELEMETRY_OPTOUT=1 POWERSHELL_UPDATECHECK=Off \
  "$tmp/pwsh/pwsh" -NoProfile -File experiments/harness-invocation/oc2-discovery.ps1 -Lab "$tmp/lab"
rm -rf "$tmp"
```

Measured on the Linux host (PowerShell 7.6.6, OpenCode 2.0.23): the dry run and a real run with one
lab skill and an inherited `OPENCODE_CONFIG` passed unchanged, listed the lab skill plus the
built-ins, left no lab server running, and changed nothing in the real OpenCode, `~/.claude`, or
`~/.agents` trees. Only `oc2-discovery.ps1` with an explicit `-Lab` is portable; `Get-LabRoot` and
`Use-OpenCodeIsolation` in `common.ps1` assume a drive-letter lab.

A rollout checks discovery against a real profile instead: what its real config root lists. Start a
short-lived private `opencode serve` on 127.0.0.1 with a random Basic-auth password, with an empty
throwaway folder as its working directory. Keep `HOME`, `XDG_CONFIG_HOME`, and
`OPENCODE_CONFIG_DIR` as the real profile sets them: the check reads the real config root and the
Claude compatibility paths below the home. Point the data, state, cache, and database roots in
the table below at the throwaway folder (OpenCode 2.0.26 `packages/util/src/global-roots.ts`).
This is not total write isolation: the method does not move OpenCode's temp-dir root,
`path.join(os.tmpdir(), "opencode")`. Do not add a temp redirect to this method. Real-profile
rollout belongs to `harness-and-os-ai-config-centralize`; this section keeps the historical method
until the curator chooses where it lives.

| Variable | What it holds |
|---|---|
| `XDG_DATA_HOME` | The log, `<XDG_DATA_HOME>/opencode/log/opencode.log`, which every OpenCode process on the machine appends to, and shell output, tool output, and snapshots (`packages/util/src/global.ts:12-27`, `packages/util/src/observability/logging.ts:59-62`). A new database also imports `<XDG_DATA_HOME>/opencode/auth.json` if one exists (`packages/core/src/database/migration/20260805200742_import_legacy_credentials.ts:36-40`), so a real data root would put a real credential in the throwaway database |
| `XDG_STATE_HOME` | Locks and the service registration (`packages/util/src/effect-flock.ts:109`, `packages/cli/src/services/service-config.ts:108`) |
| `XDG_CACHE_HOME` | Plugin packages and remote skills (`packages/util/src/npm.ts:141`, `packages/core/src/skill/discovery.ts:106`). A plugin install locks below the state root (`npm.ts:255`), so a real cache beside a throwaway state root would be written without the real server's locks. A fresh cache may download a configured plugin or remote skill, which needs the network |
| `OPENCODE_DB` | The database. It defaults below the data root (`packages/cli/src/database-path.ts`); name it so the check can confirm that the throwaway database was created |

Read `GET /api/skill` until four consecutive reads agree, then `GET /api/agent`. Then kill the
server's process tree, confirm that it stopped, and delete the throwaway folder. Record the
modification times of the real log, database, state root, and cache root before and after. Those
times cover only those roots. They do not cover the temp-dir root, which this method leaves on the
process temp directory. Where no other OpenCode process runs, none of the recorded roots changes;
a running service still writes its own entries.
The rollouts through `installer-v0.7.0` moved only the database and the project folder, so the
real log grew during their discovery runs
([v0.7.0 rollout record](records/2026-10-12-v0.7.0-rollout.md#proof-limits)).

To measure installed output, first run the installer with `OPENCODE_CONFIG_DIR=<lab-root>/config`
and the lab's other roots (`Get-OpenCodeLabEnvironment -Root <lab-root>` in `common.ps1` names them
all). A run fails rather than reporting a skill that resolved outside the lab. The OpenCode 1
probes (the parked-body command smoke, the OpenCode matrix, the variant check, and the single-run
OpenCode probe) are retired; their records remain historical evidence only
([2026-08-06 mounts](records/2026-08-06-opencode-stub-command-mounts.md),
[2026-08-06 arguments](records/2026-08-06-opencode-stub-command-arguments.md)).

Run the paired TDD intent probe's complete wiring without spending tokens:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/intent-matrix.ps1 -DryRun
```

Run the Claude half of the approved panel (12 attempts; the OpenCode leg is retired) only after the
dry-run is clean:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/intent-matrix.ps1
```

The runner writes raw evidence to the external lab. It records skill events but does not classify
TDD behavior; review every session before writing a tier-2 record.

Each runner claims its output path atomically and leaves that claim in place after an error. This is
deliberate: liveness output and partial attempt evidence must not be silently lost or overwritten,
and even an empty claim is a conservative collision marker. Move the claimed path before retrying;
unrelated interactive harness sessions do not write to it.

Forward-test panel for the `verified-waves` skill: [verified-waves-panel.md](verified-waves-panel.md).

Follow the [protocol](protocol.md) when designing a probe and write committed evidence according to
the [records schema](records/README.md). Full raw transcripts remain in the external lab.
