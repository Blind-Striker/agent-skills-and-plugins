# Harness invocation runbook

Date: 2026-09-07

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
agents as JSON, and stops the server in all cases:

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

Follow the [protocol](protocol.md) when designing a probe and write committed evidence according to
the [records schema](records/README.md). Full raw transcripts remain in the external lab.
