# Harness invocation runbook

Date: 2026-08-02

The lab must be outside both the repository and the real user profile. If the drive-root default is
not the prepared lab, point the runners at it for the current shell:

```powershell
$env:HARNESS_LAB = "<lab-root>"
```

Run the free, deterministic checks before using either harness:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab
```

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
