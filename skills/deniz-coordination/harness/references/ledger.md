Read when you inspect the run folder, event types or the fields in result files.

# Ledger

The ledger records evidence and effective configuration. Never edit it or rerun a recorded id.
Use `status`, `collect`, `resume`, `reply`, `cancel` and `clean` through the [CLI](cli.md).
Read `diff.patch` and `verify.log` alongside the final text; the worker's own account is not proof
that the result passed scope or verification.

## Layout

Run files live under `<ledger root>/<run id>/`. The default root and its overrides are described
in [configuration](config.md). Run ids contain a UTC calendar-day prefix and the chosen run name.

| File | Type | Default or presence | Contents and reader behaviour |
|---|---|---|---|
| `run.json` | Versioned JSON object | Created by the exclusive run claim | Run identity, repository, backend versions and `controlId`. |
| `config.json` | Versioned JSON object | Frozen during setup | Effective per-task settings, restrictions, source provenance and structured input hashes. |
| `tasks.jsonc` | Original task-file bytes | Copied during setup | Evidence of the supplied task file, and the frozen task specification that structured validation and recovery read. |
| `.sources/user.jsonc` | Original config bytes | If user config exists | User source snapshot. |
| `.sources/repo.jsonc` | Committed config bytes | If repository config exists at HEAD | Working-copy edits do not replace this snapshot. |
| `.sources/policy.jsonc` | Original config bytes | If `--policy` was supplied | CLI restriction source snapshot. |
| `events.jsonl` | Compact JSON object per line | Appended as the run progresses | Run and task events; first `run` event carries version 2. |
| `events.torn-<timestamp>.jsonl` | Raw bytes | After a writer repairs a trailing fragment | Preserves discarded final bytes; repair adds a `ledger-repaired` event. |
| `<task>/task.json` | Versioned JSON object | Queued, then updated during dispatch | Resolved spec, prompt text, settings, workspace, session and backend recovery state. |
| `<task>/final.md` | Markdown text | Settlement | Run/task heading, outcome and final assistant text, or a missing-text marker. |
| `<task>/diff.patch` | Binary-capable Git patch | Non-structured collection | Full-index patch; includes both sides of renamed paths through non-rename evidence collection. |
| `<task>/verify.log` | Text | Non-structured verification step | Command, stdout, stderr, exit and elapsed seconds; records a skip for non-succeeded outcomes. |
| `<task>/result.json` | Versioned JSON object | Successful settlement write, or some start failures | Final result fields below. Not every final event guarantees this file exists. |
| `<task>/input.json` | JSON array | Structured freeze | Resolved items, normalized and hashed. |
| `<task>/result.schema.json` | JSON object | Structured freeze | Normalized schema, hashed; common validation uses this copy. |
| `<task>/deliverable.json` | JSON document | Valid structured result only | Parsed and validated output; result points to it. Invalid recollection removes an older copy. |

The control folder is separate: `<state root>/control/<controlId>/`, outside worktrees and ledger
roots. Its `supervisor.lock` is held by mutating run commands. `setup.done` is an empty setup marker;
resume/collect refuse without it. Structured input files and their hashes are written and
`config.json` is rewritten before this marker, so a run with `setup.done` always has them.

| Control entry | Type | Default or presence | Purpose |
|---|---|---|---|
| `run.ref` | Versioned JSON object | Run setup | `schemaVersion` integer 2, `run` string and `ledgerRoot` string. The last two are required on read. Display-only for no-run `asks`. |
| `supervisor.lock` | Lock file | Run setup | OS file-lock ownership; do not infer supervision from file existence alone. |
| `setup.done` | Empty file | Last ordinary setup write | Required by resume/collect. |
| `asks/<askId>.json` | Versioned JSON object | While escalated ask is pending | Operator-visible ask. |
| `decisions/<askId>.json` | Versioned JSON object | Until supervisor reads a reply | Written by `reply`, never by callers directly. |
| `cancel/<task>.json` | Versioned JSON object | Until supervisor reads a cancel | Written by `cancel`, never by callers directly. |
| `profiles/` | Directory | Backend use | Temporary backend profiles, clean homes and associated process state. |
| `index-<task>` | Temporary Git index | In-place snapshot work | Leaves the operator's real index untouched. |
| `git-baseline.json` | JSON object | Workspace Git use | Configuration and hook fingerprints for the Git guard. |

`clean` retains the ledger and, after successful cleanup, retains `supervisor.lock` and `run.ref`.
It removes the other control contents. Do not delete a recorded ledger to reuse its id.

## Encoding, versions and stability

Typed JSON uses camelCase. Null-valued typed members are omitted; object-valued event `data` may
contain explicit nulls. Harness JSON writers use UTF-8, LF and a final newline; frozen source copies
retain their original bytes. Human-readable JSON files are indented and event lines are compact.
Timestamps use UTC ISO 8601 strings; durations use numeric seconds unless a field says otherwise.

All versioned files require `schemaVersion: 2`. A higher version fails with
`written by a newer dispatch; update the plugin`; old versions fail too. Ledger readers skip
unknown fields, while task/config and control readers reject them. A rewrite by `collect` can drop
unknown fields because it writes this harness's own shape. Do not treat backend-specific recovery
state or incidental console formatting as a stable integration API.

Readers consume newline-terminated event lines. They ignore and report a final fragment without a
newline, even if its bytes could parse as JSON. A malformed complete line is corruption and fails
the read. On its next write a ledger writer preserves trailing bytes in a sidecar, truncates them,
adds `ledger-repaired`, then appends the new event. Whole typed-file writes publish through a
temporary file and replacement; text artifacts can be incomplete if settlement stops mid-write.

## `run.json`

| Field | Type | Default or absence | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | `2` | Version validation above. |
| `run` | string | Recorded id | Run identity. |
| `controlId` | string | Absent before assignment | Opaque 26-character lowercase base32 control-folder name; invalid ids are refused. |
| `harness` | object | Present | Harness metadata. |
| `harness.version` | string | `1.0.0` | Harness implementation version. |
| `created` | string timestamp | Run creation time | UTC claim metadata. |
| `repo` | string path | Absent if unset | Resolved repository or planning base directory. |
| `tasksFile` | string path | Input path | Original task-file location, not a recovery input to re-read. |
| `maxParallel` | integer | `3` before resolution | Effective concurrency, 1 through 8. |
| `backends` | map of objects | `{}` before setup | Each used backend has a version record. |
| `backends.<backend>.version` | string | Probe result | Version reported during preflight. |
| `host` | object | Present | Host platform metadata. |
| `host.os` | string | Detected | `windows`, `macos` or `linux`. |

## `config.json` and setting records

| Field | Type | Default or absence | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | `2` | Version. |
| `sources` | object array | `[]` | Sources and CLI provenance. |
| `sources[].layer` | string | Source layer | `user`, `repo`, `task-file` or `cli`. |
| `sources[].path` | string path | Absent for CLI args | Original source location. |
| `sources[].copy` | string relative path | Absent when no copy | Frozen source copy or `tasks.jsonc`. |
| `sources[].head` | string commit id | Repository source only | HEAD used to read committed config. |
| `sources[].dirty` | boolean | Repository source only | Whether working config bytes differ from committed bytes. |
| `sources[].args` | string array | CLI source only | Original argument tokens. |
| `maxParallel` | setting of integer | Resolved `3` | Value and source layer. |
| `locations` | object | Resolved roots | `ledgerRoot` and `worktreeRoot`; field semantics in [configuration](config.md). |
| `restrictions` | object | Accumulated restrictions | Effective shape below. |
| `instructions` | object array | `[]` | Deduplicated standing instructions. |
| `instructions[].text` | string | Authored text | Instruction body. |
| `instructions[].from` | string | Source layer | Layer supplying the first occurrence. |
| `warnings` | string array | `[]` | Config-resolution warnings; separate planner/live warnings are not all copied here. |
| `tasks` | map keyed by task id | `{}` | Frozen task settings. |
| `tasks.<id>.settings` | object | Resolved settings | Same setting shape used in `task.json`. |
| `tasks.<id>.contracts` | contract-record array | Absent without contracts | Same path, optional alias, source, hash and selected version-line metadata as `task.json`. |
| `tasks.<id>.inputSha256` | string | Structured only after freeze | Hash of normalized `input.json` bytes. |
| `tasks.<id>.resultSchemaSha256` | string | Structured only after freeze | Hash of normalized schema bytes. |

Every scalar setting uses this wrapper. Not all wrappers have all optional metadata.

| Field | Type | Default or absence | Meaning |
|---|---|---|---|
| `value` | Setting's scalar type | Resolved value | Actual requested preference or effective restriction. |
| `from` | string | Source layer | Includes tool defaults, task-file defaults, task, CLI, worktree, or a clamp explanation. |
| `actual` | string | Absent unless recorded | Profile result, such as `clean`, `clean-partial` or `full`. |
| `reason` | string | Absent without explanation | Profile fallback reason. |
| `enforcement` | string | Absent unless backend applied it | Access/network enforcement, `sandbox`, `policy` or `none` when populated. The base records these on Codex settings; opencode scalar wrappers may omit them. |

| Settings field | Type | Tool default | Meaning |
|---|---|---|---|
| `backend` | string setting | Required | Chosen backend. |
| `route` | string setting | Required effective route or agent | Qualified requested route. |
| `effort` | string setting | Absent | Explicit effort or route variant. |
| `timeoutMinutes` | integer setting | `60` | Task timeout. |
| `profile` | string setting | Kind default | Requested and, when checked, actual profile. |
| `project` | string setting | Kind default | Repository-context mode. |
| `access` | string setting | Kind default | Requested or clamped access. |
| `network` | string setting | `on` | Requested or clamped network. |
| `workspace` | string setting | `worktree` | Workspace preference. |
| `dir` | string setting | `.` | Task or defaults directory preference. |
| `commit` | string setting | `none`, forced `harness` in worktree | Commit preference. |
| `rawPrompt` | boolean setting | `false` | Raw prompt preference. |
| `scope` | object | Empty task scope and ceilings | `value` string array plus `ceilings`, each with `globs` string array and `from` layer string. |
| `rules` | object array | `[]` | Each has `name` string and `enforcement` string: `policy` or `approval-gate`. |

| Effective restriction field | Type | Default | Meaning |
|---|---|---|---|
| `routes` | object | Absent means unrestricted | `value` qualified string array and `from` layer-name array. Empty value allows nothing. |
| `deny` | rule array | `[]` | Appended effective denies. |
| `escalate` | rule array | `[]` | Appended effective escalations. |
| `scope` | ceiling array | `[]` | Each has `globs` and `from`. |
| `maxAccess` | string setting | `full` | Lowest access ceiling. |
| `networkAllowed` | boolean setting | `true` | Any restrictive layer can set false. |
| `scrub` | string array | Absent | Accumulated extra variable-name regexes. |
| Rule `name` | string | Layer-qualified name | Identifies the rule. |
| Rule `action` | string | Authored pattern | Tool-action wildcard. |
| Rule `resources` | string array | Authored patterns | Resource wildcards. |
| Rule `enforcement` | backend-to-string map | Recorded per backend | opencode `policy`, Codex `approval-gate`. |

## `task.json`

| Field | Type | Default or absence | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | `2` | Version. |
| `spec` | task object | Resolved task | [Task fields](task-file.md); several preferences have been replaced with effective values. |
| `promptText` | string | Loaded task text | Does not include assembled standing instructions or contract bodies. |
| `settings` | object | Effective settings | Wrapper tables above. |
| `workspace` | object | Initially empty | Core workspace record. |
| `workspace.path` | string path | Absent before preparation | Worker directory. |
| `workspace.branch` | string | Worktree tasks only | Task branch. |
| `workspace.baseRef` | string | Worktree tasks only | Original base or reviewed task's branch. |
| `workspace.baseSha` | string | Git tasks with HEAD | Starting commit id. |
| `contracts` | object array | Absent without contracts | Records below, not copies of text. |
| `contracts[].path` | string path | Resolved path | Original contract path. |
| `contracts[].alias` | string | Absent for ordinary file paths | Bundled alias supplied at the first occurrence, such as `@worker-contract` or `@sol-worker`. |
| `contracts[].from` | string | First occurrence's layer | `task-file`, `task` or `cli`. |
| `contracts[].sha256` | string | Selected-content hash for aliases; file-byte hash otherwise | Lowercase SHA-256. Alias hashes cover selected UTF-8 text before harness separators, excluding BOM and model coordinator preamble. Ordinary file-byte hashes retain any BOM. See [Contracts](task-file.md#contracts). |
| `contracts[].version` | string | Selected text's first line | Trimmed and limited to 120 characters; model aliases record the worker marker, not coordinator guidance. |
| `agent` | string | Absent until backend details | Actual persona or agent detail. |
| `lineage` | string | Absent until backend details | Catalog lineage. |
| `routeResolved` | string | opencode `agent` tasks only, after backend details | Qualified route of the model the roster agent pinned at start; settlement compares reported routes with it. |
| `session` | object | Initially empty | Worker session identity. |
| `session.id` | string | Absent before creation | Backend session or thread id. |
| `session.turn` | string | Absent before prompting | Last recorded prompt or turn id. |
| `backendState` | object | Initially empty | Backend-owned recovery fields below. |

| Backend-state field | Type | Default or absence | Meaning |
|---|---|---|---|
| `promptId` | string | opencode after prompting | Prompt identity. |
| `promptCreated` | number | opencode after prompting | Backend creation time. |
| `projectId` | string | opencode when known | Backend project identity. |
| `host` | string | opencode when known | Profile/project mode key, not a network host address. |
| `pid` | integer | Codex when known | Process id. |
| `processGroup` | boolean | Codex when known | Whether a private process group was created. |
| `processStartTime` | string timestamp | Codex when known | Guards against recycled process ids. |
| `descendants` | object array | Codex when known | Each has `pid` integer and `startTime` timestamp. |
| `codexHome` | string path | Codex when known | Selected home. |
| `codexHomeTemporary` | boolean | Codex when known | Whether that home is temporary. |

These fields support this version's recovery. They are not instructions for a caller to start,
stop or attach processes independently.

## `result.json`

Ordinary settlement is done only when outcome is succeeded, final text is non-blank, no execution
or settlement error exists, every verify command passed without timeout, scope is valid, no
reported route drifts from the requested one, the worker did not report itself stopped or blocked,
and a structured deliverable validates. Cancelled outcome produces cancelled state and may retain a
`zombie` failure. Some pre-dispatch errors produce only a final event; inspect events as well.

| Field | Type | Default or absence | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | `2` | Version. |
| `task` | string | Task id | Result identity. |
| `run` | string | Run id | Result identity. |
| `kind` | string | Recorded when known | Task kind. |
| `backend` | string | Recorded when known | Backend name. |
| `state` | string | Set at settlement | `done`, `failed` or `cancelled`. |
| `failures` | string array | `[]` | Machine-readable causes below. |
| `warnings` | string array | Absent without findings | Non-failing findings: non-blocking `git-config-changed`, `advisory-verify-failed` when an `advisoryVerify` command failed or timed out, and `worker-status-missing` when a non-structured, non-raw task's final text has no status line, so its success rests on the session outcome alone. |
| `workerStatus` | string | Absent for structured tasks or without a status line | `done`, `stopped` or `blocked`, from the last `Status:` line of the final text. `stopped` and `blocked` add the `worker-stopped` failure. |
| `outcome` | string | Absent if unavailable | Execution outcome, distinct from settled state. |
| `session` | object | Recorded when available | `id` and `turn` strings, as in task record. |
| `routeRequested` | string | Absent if unavailable | Qualified requested route; for an `agent` task, the agent route. |
| `routeResolved` | string | opencode `agent` tasks only | Route the agent pinned at start, copied from the task record. |
| `routesUsed` | string array | `[]` | Routes actually reported by backend. |
| `routeVerified` | boolean | `false` | True only when the backend reported at least one route and every reported route is the requested one (`routeResolved` for an `agent` task). A request without a variant accepts any variant of its model. False when nothing was reported or a route cannot be read; only a true value supports a claim about which model did the work. |
| `routeDrift` | string array | Absent without drift | Reported routes of another model, or of another variant when the request named one. Adds the `route-drift` failure. |
| `cost` | number | Absent if not reported | Backend-reported cost, no synthesized estimate. It is an estimate at the model's list price, not money spent: a subscription or negotiated rate bills differently. Codex reports no cost, so a Codex task is measured by `tokens` alone. |
| `tokens` | object | Absent if no counts reported | Nullable numeric counts below. |
| `tokens.input` | number | Absent if unavailable | Input count, summed over every model call of the task: each call re-sends the conversation so far, so 300k over ten calls is not a 300k context. |
| `tokens.output` | number | Absent if unavailable | Output count. |
| `tokens.reasoning` | number | Absent if unavailable | Reasoning count. |
| `tokens.cacheRead` | number | Absent if unavailable | The cached part of `tokens.input`; `input` minus `cacheRead` is what the task sent uncached. |
| `tokens.cacheWrite` | number | Absent if unavailable | Cache-write count. |
| `tokensComplete` | boolean | Absent if not populated | Whether backend usage was complete. |
| `contextWindow` | number | Absent when the backend reported none | The context window, in tokens, of the model that ran: Codex reports it with token usage; opencode's comes from its model catalog. |
| `asks` | integer | `0` | Observed permission asks. |
| `asksEscalated` | integer | `0` | Asks sent to the operator. |
| `formsCancelled` | integer | `0` | Cancelled form questions. |
| `retries` | integer | `0` | Observed retry count. |
| `timedOut` | boolean | `false` | Harness timeout occurred. |
| `cancelRequested` | boolean | `false` | Cancellation requested. |
| `workerStopped` | boolean | Absent unless hard-stop result recorded | Whether stop was confirmed. |
| `baseSha` | string | Absent for structured/no starting HEAD | Starting Git commit. |
| `commitMode` | string | Absent for structured | `harness` or `none`. |
| `commit` | string | Absent for none or unchanged HEAD | Resulting commit id. |
| `diffStat` | string | Absent before collection | Git shortstat. |
| `filesChanged` | string array | `[]` | All collected changed paths. |
| `outOfScope` | string array | `[]` | Paths failing scope, including protected `.dispatch/` changes. |
| `verify` | object array | `[]` | Per-command verification records. |
| `verify[].command` | string | Authored command | Shell text executed. |
| `verify[].exitCode` | integer | Process exit | Nonzero fails verification. |
| `verify[].seconds` | number | Measured duration | Rounded to one decimal. |
| `verify[].timedOut` | boolean | `false` | Timeout fails verification even if exit is zero. |
| `verify[].advisory` | boolean | Absent for `verify` commands | `true` for an `advisoryVerify` command: its failure is the `advisory-verify-failed` warning, not `verify-failed`. |
| `verifyShell` | string path | Absent if no verify ran | Absolute resolved shell executable. |
| `deliverable` | string relative filename | Valid structured only | `deliverable.json`. |
| `validation` | object | Structured only | Common schema and identity validation. |
| `validation.ok` | boolean | `false` until validated | Whether all checks passed. |
| `validation.errors` | object array | `[]` | All collected validation errors. |
| `validation.errors[].at` | string | Empty for root | JSON Pointer location. |
| `validation.errors[].message` | string | Diagnostic | Cause and relevant keyword. |
| `executionError` | string | Absent without backend error | Backend execution failure detail. |
| `error` | string | Absent without harness error | Collection, commit, validation or cleanup failure detail. |

| Outcome | Type | Meaning |
|---|---|---|
| `succeeded` | string | Backend execution succeeded; settlement can still fail. |
| `failed` | string | Backend execution failed. |
| `interrupted` | string | Unrequested interruption; opencode can later continue. |
| `cancelled` | string | Harness cancellation. |
| `timeout` | string | Harness timeout. |
| `lost` | string | Session/process could not supply a continuing worker, including recovered Codex tasks. |
| `unknown` | string | No reliable execution outcome. |

| Failure | Type | Meaning |
|---|---|---|
| `outcome:<value>` | string | Outcome was not succeeded. |
| `no-final-text` | string | Final assistant text is missing or blank. |
| `commit-failed` | string | Harness commit command failed. |
| `verify-failed` | string | Verify command failed or timed out. |
| `out-of-scope` | string | Collected changes exceed scope. |
| `route-drift` | string | The backend reported a route other than the requested one; see `routeDrift`. |
| `worker-stopped` | string | The worker's status line says `stopped` or `blocked`: it did not do the task, whatever the session outcome. |
| `invalid-output` | string | Structured deliverable did not validate. |
| `settle-error` | string | Execution or harness settlement error. |
| `git-config-changed` | string | Blocking Git configuration change prevented safe collection/commit. |
| `zombie` | string | A stop was not confirmed (a hard stop after cancel or timeout, or a Codex turn that ended while a worker process could not be observed): a process may still write, so commit and verify were skipped. The worktree keeps the work. |
| `workspace-dirty` | string | In-place harness-commit task became dirty before dispatch; no worker started. |

The `workspace-dirty` start failure writes a minimal result before its error and final events.

## Events

| Envelope field | Type | Default or absence | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | `2` on `run`, absent otherwise | Version for the event stream. |
| `ts` | string timestamp | Event time | UTC. |
| `run` | string | Run id | Event identity. |
| `task` | string | Absent for run-level events | Task identity. |
| `type` | string | Required event discriminator | See below. |
| `data` | object | Absent if no payload | Event-specific data; optional entries can be null. |

Each row describes the emitted payload, not a guarantee that every optional member exists.

| Event type | Payload types and fields | Presence and meaning |
|---|---|---|
| `run` | `repo` string, `max` integer, `server` string, `tasks` integer, `policy` string, `deny`/`escalate`/`removed` string arrays; optional `sharedPaths` object, `rawPrompt` boolean | Initial event. Shared paths object has `paths` string array and `mergedBy: coordinator`. |
| `queued` | `kind` string, `route`/`agent`/`reviewOf` optional strings | One per queued task. |
| `worktree` | `path` string; worktree `branch`/`base`/`baseSha` strings; in-place `startTree` string | Workspace ready. Structured scratch has no worktree event. |
| `scratch` | `path`, `root`, `resolvedRoot` strings | Structured scratch allocated. `root` is the candidate as given (system temp, then state root); `resolvedRoot` is that root with its links resolved, such as macOS `/var` into `/private/var`. The path and every later check use the resolved root. |
| `route` | `route`/`agent`/`lineage` strings when known | Actual route details recorded. |
| `session` | `sessionId` string, optional `projectId` string and `rules` string array | Worker created. |
| `environment` | `variables` integer, `removed` string array | Names/count only; no secret values. |
| `prompt` | `turn`/`promptId` strings, `chars` integer, `sha256` string; optional `contracts` hash array, `rawPrompt`/`continue`/`resume` booleans | Sent prompt metadata; continuation omits contract bodies. |
| `tool` | `tool`/`resource`/`outcome` strings, `duration` number | One completion per call, duration in seconds. Resource is a bounded summary: the path, URL, command, pattern or query; for the opencode `skill` tool, the skill id. Codex reports no event when a skill loads; only its shell and edit items are recorded. A resource that carries a credential (an assignment or flag whose name the environment scrub treats as secret, URL credentials, an authorization header) is recorded as `[REDACTED]`; a PowerShell `$env:NAME=value` assignment is judged by `NAME`. Missing start means zero duration. |
| `permission` | `id` string, optional `session`, `action`, `resources`, `answer`, `by`, `message`, `waitedSeconds`, `status` | Answer evidence; `by` is harness, operator or server. `answer` is once or reject. |
| `escalated` | `id`/`action`/`rule`/`file` strings, `resources` string array, optional child `session` | Ask waits for operator. |
| `escalation-dropped` | `id`/`action`/`reason` strings | Ask no longer applies. |
| `decision-refused` | `askId`/`reason` strings | Unknown ask, reason `unknown-ask`; pending asks are untouched. |
| `form` | `id`/`title`/`answer` strings, optional `session` string and `status` integer | Question cancelled or rejected after task end. |
| `retry` | `attempt` integer, `message` string, optional `next` number, `reason`/`provider` strings | `next` uses Unix milliseconds, not duration seconds. |
| `status` | `execution` string, optional `error` object or `reason` string | Backend progress/outcome signal. |
| `interrupted` | Optional `reason` string | Task left interrupted without a harness stop request. |
| `timeout` | Optional `interrupted` boolean and `status` integer | Timeout interrupt attempt. |
| `cancel` | Request `time`/`state` strings, or interrupt `interrupted` boolean and `status` integer | Request receipt or stop attempt. |
| `hard-stop` | `kind` string, `stopped` boolean, `detail` string | One hard-stop report; kind is cancel or timeout. Stopped value feeds result `workerStopped`. |
| `stop-unconfirmed` | `outcome` string, `detail` string | The turn ended but the backend could not confirm its processes stopped; the task settles at once as a `zombie`, with the worker's final report. |
| `collected` | `outcome` string, `routesUsed` string array; Git adds `commit`/`diffstat`, `files` integer, `outOfScope` string array and optional `endTree` | Evidence collected. `endTree` is an in-place no-commit snapshot. |
| `verified` | `commands`/`failed` integers; `advisoryFailed` integer when advisory commands ran | Verify summary; `failed` counts `verify` commands only. Zero commands can mean skipped or absent. |
| `validated` | `ok` boolean, `errors` array of `{at, message}` strings | Structured validation result. |
| `usage` | Optional `input`, `cacheRead`, `output`, `reasoning` numbers | One task-scoped event per reported model call, in call order, before `outcome` on every settlement that writes a result, including failed and structured tasks. Unavailable fields are omitted; reported zeros are kept; cache-write is not included. Codex uses each current-turn own-thread token notification's `last` breakdown (not child usage); opencode uses token-bearing assistant messages across the task session, including continuations. Timestamps are settlement write times, not model-call times. No calls means no events, even when task totals exist; repeated settlement writes another batch. |
| `outcome` | `outcome` string, optional `cost`/`tokensIn`/`tokensOut` numbers, `tokensComplete` boolean, `error` string | Settlement outcome summary. |
| `final` | `state` string, optional `outcome`/`reason`/`via` strings | Final task state; collect uses `via: collect`. |
| `resume` | `continue` boolean, `tasks` integer | Reconciliation began. |
| `cleaned` | Optional `worktree`/`branch`/`error` strings, `force` boolean | Cleanup result. |
| `bus` | `state` string, optional `reason` string | Disconnected or reconnected event stream. |
| `error` | Optional `while`/`message`/`id`/`file`/`backend` strings | Operation error. Owner-chain cuts appear here with `while: owner-chain-cut`; they are not a separate event type. |
| `aborted` | `message` string, or `reason` string and `running` string array | Run/recovery aborted. |
| `git-config-changed` | `keys` string array, `blocking` boolean | Git guard finding. Non-blocking changes may populate warnings. |
| `index-lock-removed` | `path` string | Lock created by a timed-out killed Git call was removed. |
| `ledger-repaired` | `file` string, `bytes` integer | Torn tail archived before append. |

The pending `warning` event for Windows user PATH changes is described under the
[Windows user PATH guard](cli.md#windows-user-path-guard).

## Derived states

`status` derives state from event order, not process liveness. Unrecognized events preserve the
previous state when one exists. A killed supervisor writes nothing, so last progress can remain
visible until resume reconciles it. A lock probe distinguishes current supervision.

| State | Type | Event mapping |
|---|---|---|
| `queued` | Non-final string | `queued`. |
| `dispatching` | Non-final string | `worktree`, `route`, `session`, `environment`. |
| `running` | Non-final string | `prompt`, permission/form/retry/status/timeout/cancel or escalation-dropped progress. |
| `waiting` | Non-final string | `escalated`. |
| `settling` | Non-final string | `collected`, `verified`, `outcome`. |
| `interrupted` | Non-final string | `interrupted`; permission/form/retry/status/timeout/cancel/escalation-dropped preserve it until reconciliation. |
| `done` | Final string | `final.data.state: done`. |
| `failed` | Final string | `final.data.state: failed`. |
| `cancelled` | Final string | `final.data.state: cancelled`. |

## Asks, decisions and cancellation

| Ask field | Type | Default or presence | Meaning |
|---|---|---|---|
| `schemaVersion` | integer | `2` | Strict control-file version. |
| `run` | string | Run id | Owning run. |
| `task` | string | Task id | Owning task. |
| `backend` | string | Backend name | Where the ask came from. |
| `session` | string | Child or root session id | Asking session. |
| `rootSession` | string | Root worker id | Owning worker. |
| `askId` | string | Generated task/counter id | Identifier for `reply`. |
| `backendRef` | object | Contains `requestId` | Backend correlation data. |
| `action` | string | Observed action | Tool action. |
| `resources` | string array | Observed resources | Requested resources. |
| `rule` | string | Matched rule | Layer-qualified escalation rule. |
| `time` | string timestamp | Queue time | UTC. |
| `reply` | string array | CLI argument tokens | Suggested reply invocation, with ledger override when needed. |

| Decision field | Type | Default | Validation |
|---|---|---|---|
| `schemaVersion` | integer | Required, `2` | Strict version check. |
| `askId` | string | Required | Safe lowercase identifier; `askId must be a safe ask identifier`. |
| `decision` | string enum | Required | `once` or `reject`; otherwise `decision must be once or reject`. |
| `message` | string | Default rejection feedback when omitted | Only with reject: `message goes with reject only`. |
| `time` | string timestamp | Required | Written by reply. |
| `by` | string | `operator` | Decision origin. |

| Cancel field | Type | Default | Validation |
|---|---|---|---|
| `schemaVersion` | integer | Required, `2` | Strict version check. |
| `run` | string | Required | Must name this run. |
| `task` | string | Required | Must name a non-final task of this run. |
| `time` | string timestamp | Required | Correlates request and confirmation. |

| Run-reference field | Type | Default | Validation |
|---|---|---|---|
| `schemaVersion` | integer | Required, `2` | Strict version check. |
| `run` | string | Required | Display-only run id. |
| `ledgerRoot` | string path | Required | Display-only ledger root. |

Unknown members or missing required fields fail strict reading. The supervisor accepts
only a non-final task of the current run, deletes each processed file and records invalid requests
as `error`. A decision for an unknown ask records `decision-refused`; pending asks remain pending.
Malformed decisions are deleted with an error; an I/O-unreadable decision produces an error and may
remain for a later read. Callers must use commands instead of writing these files.

Codex sandboxed sessions cannot write the control folder but may read it. Full access removes that
boundary. opencode edit-tool rules deny writes to control and ledger, but shell commands run as the
same user and can change both. Verify can also change control files on either backend. There is no
authentication boundary against another process running as that user.
