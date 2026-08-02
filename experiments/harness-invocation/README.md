# Harness invocation experiments

This subsystem measures harness discovery, invocation, and model behavior without turning runtime
experiments into product or CI gates. Its scripts create reproducible fixtures, isolate supported
harnesses in an external lab, and capture evidence under the repository's claim lifecycle.

Run the deterministic checks without a prepared lab:

```powershell
pwsh -File experiments/harness-invocation/selftest.ps1 -SkipLab
```

- [Protocol](protocol.md): isolation, probe design, controls, and recording method
- [Runbook](runbook.md): operator commands
- [Records](records/README.md): committed evidence tiers and schema
