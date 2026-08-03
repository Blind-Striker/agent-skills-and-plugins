---
record_id: tdd-intent-fire-claude-2026-08-02
date: 2026-08-02
repo_head: 0473f05ef5ffb69f2b82ae765f8267e2090207fb
kind: model-panel
summary: Control invoked TDD in 3/6 attempts and intent in 6/6; all nine test-first sessions were partial because execution denials prevented observed red-green cycles.
isolation_ok: true
fixture_sha: 50b9065
harness_name: Claude Code
harness_version: 2.1.220
runner_revision: sha256:984667c083fff1567e54c0849cabcc1ce55c026f7a4b54e7a02e8945e0e311b6
---

# Claude TDD intent-fire panel

## Probe

Two models received the same bounded implementation request three times per condition. The intent
condition prefixed the control prompt with exactly `Let's go test-driven. `; neither prompt named a
skill. Every attempt started from fixture commit `50b9065`.

The recorded `984667...` runner revision is the exact run-time revision. Later post-panel timeout
hardening changed the current working runner, so this record retains the run-time hash and evidence
rather than claiming current-runner reproducibility.

`skill_invoked` comes from an observed `Skill` tool input naming
`deniz-process:test-driven-development`. `tdd_behavior` is a session-level interpretation of the
ordered tool inputs and outputs, final diff, and surrounding model text. `followed` requires a test
written before production, an observed relevant failing run, a production change, and an observed
later passing run. A permission denial is evidence that a command did not execute, not an observed
test failure.

## Results

| model | probe_id | condition | repeat | status | skill_invoked | tdd_behavior | cost | tools_observed | notes |
|---|---|---|---:|---|---|---|---:|---|---|
| `claude-opus-5` | `tdd-intent-fire` | control | 1 | pass | no | not-followed | 0.2666 | Bash, Edit, Glob, PowerShell, Read | Production was edited before any test, and attempted post-change execution was denied. |
| `claude-opus-5` | `tdd-intent-fire` | intent | 1 | pass | yes | partial | 0.4606 | Bash, PowerShell, Read, Skill, Write | A test was written first, but execution was denied before an observed red and production remained untouched. |
| `claude-opus-5` | `tdd-intent-fire` | control | 2 | pass | no | not-followed | 0.2088 | Bash, Edit, Glob, Read | Production was edited before any test, and attempted post-change execution was denied. |
| `claude-opus-5` | `tdd-intent-fire` | intent | 2 | pass | yes | partial | 0.3762 | Bash, PowerShell, Read, Skill, Write | A test was written first, but execution was denied before an observed red and production remained untouched. |
| `claude-opus-5` | `tdd-intent-fire` | control | 3 | pass | no | not-followed | 0.2785 | Bash, Edit, Glob, PowerShell, Read, Write | Production was edited before a throwaway check was written, and every attempted execution was denied. |
| `claude-opus-5` | `tdd-intent-fire` | intent | 3 | pass | yes | partial | 0.3639 | Bash, PowerShell, Read, Skill, Write | A test was written first, but execution was denied before an observed red and production remained untouched. |
| `claude-fable-5` | `tdd-intent-fire` | control | 1 | pass | yes | partial | 1.8869 | Agent, Bash, Edit, Glob, PowerShell, Read, Skill, Write | A test was written before production, but all runs were denied, so neither red nor later green was observed. |
| `claude-fable-5` | `tdd-intent-fire` | intent | 1 | pass | yes | partial | 1.8593 | Agent, Bash, Glob, PowerShell, Read, Skill, Write | A test was written first, but all runs were denied and the session stopped before production, so no red-green cycle completed. |
| `claude-fable-5` | `tdd-intent-fire` | control | 2 | pass | yes | partial | 1.5932 | Bash, Edit, Glob, PowerShell, Read, Skill, Write | A test was written before production, but all runs were denied, so neither red nor later green was observed. |
| `claude-fable-5` | `tdd-intent-fire` | intent | 2 | pass | yes | partial | 1.4213 | Agent, Bash, Glob, PowerShell, Read, Skill, Write | A test was written first, but all runs were denied and the session stopped before production, so no red-green cycle completed. |
| `claude-fable-5` | `tdd-intent-fire` | control | 3 | pass | yes | partial | 1.6282 | Bash, Edit, Glob, PowerShell, Read, Skill, Write | A test was written before production, but all runs were denied, so neither red nor later green was observed. |
| `claude-fable-5` | `tdd-intent-fire` | intent | 3 | pass | yes | partial | 1.9751 | Agent, Bash, Glob, PowerShell, Read, Skill, Write | A test was written first, but all runs were denied and the session stopped before production, so no red-green cycle completed. |

All twelve attempts completed with usable evidence. The control condition invoked the target skill
in 3/6 attempts; the intent condition invoked it in 6/6. Metadata and the exact `Skill` inputs agree
on all nine invocations.

All attempts were classifiable. In control, behavior was `followed` 0/6, `partial` 3/6, and
`not-followed` 3/6. In intent, behavior was `followed` 0/6, `partial` 6/6, and `not-followed` 0/6.
Overall, the distribution over 12 classifiable attempts was `followed` 0/12, `partial` 9/12, and
`not-followed` 3/12. There were 0 `unjudgeable` attempts to exclude from any behavior denominator.

## Sanitized event excerpts

Paths are reduced to fixture-relative names. Skill bodies, unrelated discovery reads, identifiers,
and raw profile or lab locations are omitted; the raw event streams remain outside the repository.

### `claude-opus-r1-control`

```text
read duration.js
edit duration.js to add formatDuration
run inline node sample -> DENIED, command requires approval
run the same sample through PowerShell -> DENIED, command requires approval
final diff -> duration.js production change only; no test file
final text -> implementation was hand-checked, not executed
no Skill event
```

### `claude-opus-r1-intent`

```text
Skill deniz-process:test-driven-development
write package.json with node --test script
write duration.test.js for formatDuration(5400) === "1h30m"
run node --test through Bash and PowerShell -> DENIED, command requires approval
run npm test -> DENIED, command requires approval
final diff -> test and package files only; duration.js untouched
final text -> stopped before implementation because no failing run was observed
```

### `claude-opus-r2-control`

```text
read duration.js
edit duration.js to add formatDuration
run inline node round-trip check -> DENIED, command requires approval
retry node check -> DENIED, command requires approval
final diff -> duration.js production change only; no test file
final text -> values were hand-traced, not observed
no Skill event
```

### `claude-opus-r2-intent`

```text
Skill deniz-process:test-driven-development
write duration.test.js for formatDuration(5400) === "1h30m"
run node --test duration.test.js through Bash and PowerShell -> DENIED, command requires approval
final diff -> test file only; duration.js untouched
final text -> stopped before implementation because the test could not be watched failing
```

### `claude-opus-r3-control`

```text
read duration.js
edit duration.js to add formatDuration
attempt inline node check -> DENIED
write temporary _check.mjs after the production edit
run temporary check through PowerShell and Bash -> DENIED, command requires approval
delete temporary check
final diff -> duration.js production change only; no retained test file
final text -> outputs were traced by hand, not observed
no Skill event
```

### `claude-opus-r3-intent`

```text
Skill deniz-process:test-driven-development
write package.json with node --test script
write duration.test.js for formatDuration(5400) === "1h30m"
run node --test and npm test through Bash and PowerShell -> DENIED, command requires approval
final diff -> test and package files only; duration.js untouched
final text -> stopped before implementation because no failing run was observed
```

### `claude-fable-r1-control`

```text
Skill deniz-process:test-driven-development
write duration.test.js before production
run node --test and direct node forms -> DENIED, command requires approval
Agent retry -> both commands denied; no test output or exit code
expand duration.test.js, then edit duration.js to add formatDuration
no test command after the production edit
final diff -> production and test changes
final text -> test-first ordering without observed failure or pass
```

### `claude-fable-r1-intent`

```text
Skill deniz-process:test-driven-development
write duration.test.js for formatDuration(5400) === "1h30m"
run node --test, direct node, and npm test -> DENIED, command requires approval
write package.json with node --test script
Agent retry -> both commands denied
final diff -> test and package files only; duration.js untouched
final text -> stopped before implementation because no failing run was observed
```

### `claude-fable-r2-control`

```text
Skill deniz-process:test-driven-development
write package.json and duration.test.js before production
run npm test, node --test, and direct node forms -> DENIED, command requires approval
expand duration.test.js, then edit duration.js to add formatDuration
no test command after the production edit
final diff -> production, test, and package changes
final text -> mandatory watched-fail and watched-pass steps remained outstanding
```

### `claude-fable-r2-intent`

```text
Skill deniz-process:test-driven-development
write package.json and duration.test.js before production
run node --test, npm test, and direct node forms -> DENIED, command requires approval
Agent retry -> command still required approval; no test output
final diff -> test and package files only; duration.js untouched
final text -> stopped before implementation because the test red was unverified
```

### `claude-fable-r3-control`

```text
Skill deniz-process:test-driven-development
write duration.test.js before production
run node --test and direct node forms -> DENIED, command requires approval
write package.json; retry npm test and node --test -> DENIED
edit duration.js to add formatDuration, then expand duration.test.js
no test command after the production edit
final diff -> production, test, and package changes
final text -> tests were written but unverified
```

### `claude-fable-r3-intent`

```text
Skill deniz-process:test-driven-development
write duration.test.js for formatDuration(5400) === "1h30m"
run node --test and direct node forms -> DENIED, command requires approval
write package.json; retry npm test -> DENIED
Agent retry -> all three execution forms denied; no test output
final diff -> test and package files only; duration.js untouched
final text -> stopped before implementation because no failing run was observed
```

## Limits

This panel covers one tiny JavaScript task, one prompt pair, two model configurations, three repeats,
and one Claude Code version. The execution permission policy denied every attempted JavaScript test
or check, so the panel distinguishes implementation-first from incomplete test-first behavior but
contains no observed red-green completion. Models are non-deterministic sessions; the counts are
sample observations, not deterministic model rates or proof that the intent phrase caused them.
