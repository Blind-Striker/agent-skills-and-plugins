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

The Codex leg uses `codex-matrix.ps1`: its default path is credential-free structural marketplace,
install, cache, and removal verification; `-Behavioural` additionally requires credentials already
placed in the isolated Codex home or supplied through `OPENAI_API_KEY`. Behavioural runs default to
`gpt-5.6-luna` at low reasoning effort and cover fixture invocation policy, cross-skill handoff,
bundled-reference loading, uninstalled-plugin isolation, and generated-plugin execution under the
full catalog.

The OpenCode leg targets OpenCode 2 only. `oc2-discovery.ps1` starts an isolated `opencode serve`
(every root, including `OPENCODE_CONFIG_DIR`, below the lab; project walk and managed background
service off) and reports registered skills, commands, and agents with no model call. The OpenCode 1
probes are retired; their committed records remain historical evidence. Only the discovery check
and the `manual` model record were ported to OpenCode 2; porting any other OpenCode leg, such as
the CLI matrices, the variant check, or the intent matrix, is a new curator decision, not pending
work.
