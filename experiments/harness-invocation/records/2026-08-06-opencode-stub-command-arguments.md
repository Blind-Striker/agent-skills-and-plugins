---
record_id: opencode-stub-command-arguments-2026-08-06
date: 2026-08-06
repo_head: 2d35edd004155df0cdd2f9b228450f10c1aff96c
kind: runtime-smoke
summary: The generated bundled manual beta command read its parked body and received a CLI-only argument from both project-local and isolated XDG-global mounts without exposing beta as a skill.
isolation_ok: true
supersedes: opencode-stub-command-mounts-2026-08-06
harness_name: OpenCode
harness_version: 1.18.11
emitter_commit: 2d35edd004155df0cdd2f9b228450f10c1aff96c
runner_revision: sha256:b5cc5b9ddbc7817512561bd466db6c129cd01484408146f1ef523f9908b96f71
model: xai/grok-4.5
variant: high
fixture_sha: 50b9065
---

# OpenCode parked-body command arguments

## Fixture and isolation

`stub-command-smoke.ps1` generated a fresh `manual` beta fixture through the exported `buildAll`
adapter. Its temporary source retained `references/keep.txt`, so the emitter selected the bundled
manual branch; the generated command and parked `skills/beta/` bundle alone were copied into each
mount. The replacement body instructed the model to do no other work, reply with
`ZX-STUB-BODY-RAN`, and append the exact value from the command message's `Arguments:` line. The
body did not contain the CLI-only argument marker `ZX-STUB-CLI-ARG`; the runner rejects a fixture
that does.

The runner required the external lab and the baseline scratch repository, used `Use-OpenCodeIsolation`,
and left `OPENCODE_CONFIG_DIR` unset. Before each smoke invocation it verified an empty `plugin:`
list, the `customize-opencode` built-in control, no skill location outside the lab, `beta` in resolved
commands, and no `beta` entry in `opencode debug skill`. The provider credential, declared model and
`high` variant, and a one-token liveness call all passed before either fixture mount was staged.

Both fixture mounts and scratch-repository changes were removed after their results were written.
Raw outputs remain at
[external-lab:stub-command-smoke/20260806T141234-85424218](external-lab:stub-command-smoke/20260806T141234-85424218).

## Results

| mount | exact beta mount | command discovery | skill discovery | completed read suffix | markers | status |
|---|---|---|---|---|---|---|
| project-local | `<scratch-repo>/.opencode/commands/beta.md` and `<scratch-repo>/.opencode/skills/beta/` | `beta` present | `beta` absent | `.opencode/skills/beta/BODY.md` | `ZX-STUB-BODY-RAN`, `ZX-STUB-CLI-ARG` | pass |
| isolated-global | `$XDG_CONFIG_HOME/opencode/commands/beta.md` and `$XDG_CONFIG_HOME/opencode/skills/beta/` | `beta` present | `beta` absent | `.config/opencode/skills/beta/BODY.md` | `ZX-STUB-BODY-RAN`, `ZX-STUB-CLI-ARG` | pass |

## Sanitized event excerpts

Only the tool input, relevant body content, CLI argument, completion state, and final response needed
for this claim are retained. Paths are mount-relative; session identifiers, timestamps, raw
configuration, and full unrelated output remain in the external lab.

### Project-local

```text
read (error) <isolated-XDG-global>/opencode/skills/beta/BODY.md  # beta is intentionally not mounted globally
read (completed) <scratch-repo>/.opencode/skills/beta/BODY.md
BODY.md: reply with ZX-STUB-BODY-RAN and the exact value after Arguments: in the command message
invocation argument, absent from BODY.md: ZX-STUB-CLI-ARG
final text after completed read: ZX-STUB-BODY-RAN ZX-STUB-CLI-ARG
step_finish
```

### Isolated-global

```text
read (completed) $XDG_CONFIG_HOME/opencode/skills/beta/BODY.md
BODY.md: reply with ZX-STUB-BODY-RAN and the exact value after Arguments: in the command message
invocation argument, absent from BODY.md: ZX-STUB-CLI-ARG
final text after completed read: ZX-STUB-BODY-RAN ZX-STUB-CLI-ARG
step_finish
```

The runner rejects marker text unless a completed read-tool input ending in the mount's expected
`BODY.md` suffix occurs first. It also rejects a generated fixture whose `BODY.md` contains the
CLI-only argument marker.
