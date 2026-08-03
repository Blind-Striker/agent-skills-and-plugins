---
record_id: tdd-intent-fire-opencode-2026-08-02
date: 2026-08-02
repo_head: 0473f05ef5ffb69f2b82ae765f8267e2090207fb
kind: model-panel
summary: One control session missed both skill invocation and red-green; the other eleven sessions invoked the skill and followed TDD.
isolation_ok: true
fixture_sha: 50b9065
harness_name: OpenCode
harness_version: 1.18.7
runner_revision: sha256:fe8f63a176a5ec3833d579b6c29acda9370a9b385fd444b220e200b7f176df60
---

# OpenCode TDD intent-fire panel

## Probe

Two models received the same bounded implementation request three times per condition. The intent
condition prefixed the control prompt with exactly `Let's go test-driven. `; neither prompt named a
skill. Every attempt started from fixture commit `50b9065`.

The recorded `fe8f63...` runner revision is the exact panel revision. Later Claude-isolation changes
and post-panel timeout hardening changed the current working runner; the earlier body was not retained
in git, so its hash, raw external-lab evidence, and tier-2 excerpts are retained rather than claiming
current-runner reproducibility.

`skill_invoked` comes from an observed `skill` tool input naming `test-driven-development`.
`tdd_behavior` is a session-level interpretation of the ordered tool inputs and outputs, final diff,
and surrounding text. `followed` means the session wrote a test before production code, observed a
relevant failing run, changed production, and observed a passing run. It is reviewer judgement, not
runner pattern matching.

## Results

| model | probe_id | condition | repeat | status | skill_invoked | tdd_behavior | cost | tools_observed | notes |
|---|---|---|---:|---|---|---|---:|---|---|
| `openai/gpt-5.6-sol@xhigh` | `tdd-intent-fire` | control | 1 | pass | yes | followed |  | skill, glob, read, apply_patch, bash | Test first; relevant red then green |
| `openai/gpt-5.6-sol@xhigh` | `tdd-intent-fire` | intent | 1 | pass | yes | followed |  | skill, glob, read, apply_patch, bash | Import shape corrected before production |
| `openai/gpt-5.6-sol@xhigh` | `tdd-intent-fire` | intent | 2 | pass | yes | followed |  | skill, read, glob, apply_patch, bash | Assertion red observed before production |
| `openai/gpt-5.6-sol@xhigh` | `tdd-intent-fire` | control | 2 | pass | yes | followed |  | skill, glob, read, apply_patch, bash | Test first; relevant red then green |
| `openai/gpt-5.6-sol@xhigh` | `tdd-intent-fire` | control | 3 | pass | yes | followed |  | skill, glob, read, apply_patch, bash | Test first; relevant red then green |
| `openai/gpt-5.6-sol@xhigh` | `tdd-intent-fire` | intent | 3 | pass | yes | followed |  | skill, glob, read, apply_patch, bash | Import shape corrected before production |
| `xai/grok-4.5@high` | `tdd-intent-fire` | control | 1 | pass | no | not-followed | 0.0484 | glob, grep, read, edit | Edited production directly; no test written or run |
| `xai/grok-4.5@high` | `tdd-intent-fire` | intent | 1 | pass | yes | followed | 0.0658 | skill, glob, grep, read, write, bash, edit | Test first; missing-export red then green |
| `xai/grok-4.5@high` | `tdd-intent-fire` | intent | 2 | pass | yes | followed | 0.0621 | skill, glob, read, bash, write, edit | Test first; missing-export red then green |
| `xai/grok-4.5@high` | `tdd-intent-fire` | control | 2 | pass | yes | followed | 0.0639 | skill, glob, read, bash, write, edit | Test first; missing-export red then green |
| `xai/grok-4.5@high` | `tdd-intent-fire` | control | 3 | pass | yes | followed | 0.0567 | skill, glob, bash, read, write, edit | Test first; missing-export red then green |
| `xai/grok-4.5@high` | `tdd-intent-fire` | intent | 3 | pass | yes | followed | 0.0776 | skill, glob, bash, read, write, edit | Test first; missing-export red then green |

Blank Sol cost cells mean the harness reported no billed amount; Grok cells retain the reported
billed costs.

All twelve attempts were valid. The control condition invoked the skill in 5/6 attempts and followed
red-green in 5/6. The intent condition invoked it in 6/6 and followed red-green in 6/6. The one-row
difference came from one model and one repeat; it is a sample observation, not a deterministic rate
or a general claim about either model.

## Sanitized event excerpts

Paths are reduced to fixture-relative names. Skill bodies and unrelated discovery reads are omitted;
the raw event streams remain in the external lab.

### `opencode-sol-r1-control`

```text
skill test-driven-development
write duration.test.js
run node with unsupported flag -> command error
run node --test duration.test.js -> FAIL, formatDuration export missing
edit duration.js
run node --test duration.test.js -> PASS, 1/1
```

### `opencode-sol-r1-intent`

```text
skill test-driven-development
write duration.test.js
run node --test duration.test.js -> FAIL, named export missing
edit test import only
run node --test duration.test.js -> FAIL, formatDuration is not a function
edit duration.js
run node --test duration.test.js -> PASS, 1/1
```

### `opencode-sol-r2-intent`

```text
skill test-driven-development
write duration.test.mjs
run node --test duration.test.mjs -> FAIL, named export missing
edit test import only
run node --test duration.test.mjs -> FAIL, expected 1h30m but got undefined
edit duration.js
run node --test duration.test.mjs -> PASS, 1/1
```

### `opencode-sol-r2-control`

```text
skill test-driven-development
write duration.test.js
run node with unsupported flag -> command error
run node --test duration.test.js -> FAIL, formatDuration export missing
edit duration.js
run node --test duration.test.js -> PASS, 1/1
```

### `opencode-sol-r3-control`

```text
skill test-driven-development
write duration.test.mjs
run node --test duration.test.mjs -> FAIL, formatDuration export missing
edit duration.js
skill verification-before-completion
run node --test duration.test.mjs -> PASS, 1/1
```

### `opencode-sol-r3-intent`

```text
skill test-driven-development
write duration.test.js
run node with unsupported flag -> command error
run node --test duration.test.js -> FAIL, named export missing
edit test import only
run node --test duration.test.js -> FAIL, formatDuration is not a function
edit duration.js
run node --test duration.test.js -> PASS, 1/1
```

### `opencode-grok-r1-control`

```text
read duration.js
edit duration.js to add formatDuration
reply done
no skill event; no test file or test command
```

### `opencode-grok-r1-intent`

```text
skill test-driven-development
write duration.test.js
run node --test duration.test.js -> FAIL, formatDuration export missing
edit duration.js
run node --test duration.test.js -> PASS, 1/1
```

### `opencode-grok-r2-control`

```text
skill test-driven-development
write duration.test.js
run node --test duration.test.js -> FAIL, formatDuration export missing
edit duration.js
run node --test duration.test.js -> PASS, 1/1
```

### `opencode-grok-r2-intent`

```text
skill test-driven-development
write duration.test.js
run node --test duration.test.js -> FAIL, formatDuration export missing
edit duration.js
run node --test duration.test.js -> PASS, 1/1
```

### `opencode-grok-r3-control`

```text
skill test-driven-development
write duration.test.js
run node --test duration.test.js -> FAIL, formatDuration export missing
edit duration.js
run node --test duration.test.js -> PASS, 1/1
```

### `opencode-grok-r3-intent`

```text
skill test-driven-development
write duration.test.js
run node --test duration.test.js -> FAIL, formatDuration export missing
edit duration.js
run node --test duration.test.js -> PASS, 1/1
```

## Limits

This panel covers one tiny JavaScript task, one prompt pair, two model configurations, three repeats,
and one OpenCode version. The observed one-attempt difference is consistent with propensity having a
tail; it neither proves the intent phrase caused the difference nor estimates a stable population
rate.
