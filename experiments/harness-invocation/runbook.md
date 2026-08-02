# Harness invocation runbook

Date: 2026-08-02

The lab must be outside both the repository and the real user profile. If the drive-root default is
not the prepared lab, point the runners at it for the current shell:

```powershell
$env:HARNESS_LAB = "<lab-root>"
```

Run the free, deterministic checks before using either harness:

```powershell
pwsh -File experiments/harness-invocation/selftest.ps1 -SkipLab
```

With an isolated lab prepared, walk the OpenCode matrix wiring without spending tokens:

```powershell
pwsh -File experiments/harness-invocation/matrix.ps1 -DryRun
```

Follow the [protocol](protocol.md) when designing a probe and write committed evidence according to
the [records schema](records/README.md). Full raw transcripts remain in the external lab.
