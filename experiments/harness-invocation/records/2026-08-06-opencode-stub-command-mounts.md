---
record_id: opencode-stub-command-mounts-2026-08-06
date: 2026-08-06
repo_head: 99475078cb08b7edccdf51e8e136772bf24f0387
kind: runtime-smoke
summary: The generated bundled manual beta command resolved and read its parked body from both project-local and isolated XDG-global mounts without exposing beta as a skill.
isolation_ok: true
harness_name: OpenCode
harness_version: 1.18.11
emitter_commit: 99475078cb08b7edccdf51e8e136772bf24f0387
runner_revision: sha256:415259ba365722b4f7813bf87c7ccde7b3f225bc46077126253610b028716aa3
model: xai/grok-4.5
variant: high
fixture_sha: 50b9065
---

# OpenCode parked-body command mounts

## Fixture and isolation

`stub-command-smoke.ps1` generated a fresh `manual` beta fixture through the exported `buildAll`
adapter. Its temporary source retained `references/keep.txt`, so the emitter selected the bundled
manual branch; the generated command and parked `skills/beta/` bundle alone were copied into each
mount. The replacement body instructed the model to do no other work and reply with
`ZX-STUB-BODY-RAN ZX-STUB-ARG`.

The runner required the external lab and the baseline scratch repository, used `Use-OpenCodeIsolation`,
and left `OPENCODE_CONFIG_DIR` unset. Before each smoke invocation it verified an empty `plugin:`
list, the `customize-opencode` built-in control, no skill location outside the lab, `beta` in resolved
commands, and no `beta` entry in `opencode debug skill`. The provider credential, declared model and
`high` variant, and a one-token liveness call all passed before either fixture mount was staged.

Both fixture mounts and scratch-repository changes were removed after their results were written.
Raw outputs remain at
[external-lab:stub-command-smoke/20260806T134917-b59d1f98](external-lab:stub-command-smoke/20260806T134917-b59d1f98).

## Results

| mount | exact beta mount | command discovery | skill discovery | completed read suffix | markers | status |
|---|---|---|---|---|---|---|
| project-local | `<scratch-repo>/.opencode/commands/beta.md` and `<scratch-repo>/.opencode/skills/beta/` | `beta` present | `beta` absent | `.opencode/skills/beta/BODY.md` | `ZX-STUB-BODY-RAN`, `ZX-STUB-ARG` | pass |
| isolated-global | `$XDG_CONFIG_HOME/opencode/commands/beta.md` and `$XDG_CONFIG_HOME/opencode/skills/beta/` | `beta` present | `beta` absent | `.config/opencode/skills/beta/BODY.md` | `ZX-STUB-BODY-RAN`, `ZX-STUB-ARG` | pass |

## Sanitized event excerpts

Only the tool input, completion state, and final response needed for this claim are retained. Paths
are mount-relative; session identifiers, timestamps, raw configuration, and full bodies remain in the
external lab.

### Project-local

```text
read (error) <isolated-XDG-global>/opencode/skills/beta/BODY.md  # beta is intentionally not mounted globally
read (completed) <scratch-repo>/.opencode/skills/beta/BODY.md
final text after completed read: ZX-STUB-BODY-RAN ZX-STUB-ARG
step_finish
```

### Isolated-global

```text
read (completed) $XDG_CONFIG_HOME/opencode/skills/beta/BODY.md
final text after completed read: ZX-STUB-BODY-RAN ZX-STUB-ARG
step_finish
```

The runner rejects marker text unless a completed read-tool input ending in the mount's expected
`BODY.md` suffix occurs first.
