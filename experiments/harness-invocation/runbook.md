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

Run the parked-body command smoke only after its dry run is clean. It generates a disposable
`manual` beta fixture through `buildAll`, checks only the supported isolated XDG-global mount, and
keeps raw event streams only in the external lab:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/stub-command-smoke.ps1 -DryRun -Leg grok
pwsh -NoProfile -File experiments/harness-invocation/stub-command-smoke.ps1 -Leg grok
```

Do not write a committed measurement record unless the single isolated-global leg passes: command
discovery must find `beta`, skill discovery must not, and the event stream must show the parked
`BODY.md` read before the body marker and a separate CLI-only argument marker that is absent from
`BODY.md`. The earlier dual-mount results remain historical evidence only
([2026-08-06 mounts](records/2026-08-06-opencode-stub-command-mounts.md),
[2026-08-06 arguments](records/2026-08-06-opencode-stub-command-arguments.md)); project-local mounts
are not a current supported Destination.

With an isolated lab prepared, walk the OpenCode matrix wiring without spending tokens:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/matrix.ps1 -DryRun
```

Run the paired TDD intent probe's complete wiring without spending tokens:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/intent-matrix.ps1 -DryRun
```

Run the approved 24-attempt panel only after the dry-run is clean:

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
