Read for each command's arguments, output and exit codes.

# Command line

Run from the installed harness skill folder. Its `global.json` selects stable SDK 10.0.300 or a
later compatible feature band. The entry file uses this shebang on Linux and macOS:
`#!/usr/bin/env -S dotnet --`.

```text
dotnet build dispatch.cs
dotnet dispatch.cs -- <command> [arguments]
```

Build before every command. A nonzero build exit means no harness command ran. Concurrent builds
can contend on the file-based app cache; retry a failed build before invoking the command. The
`--` separates application arguments from `dotnet` arguments, including flags such as `--force`.

Command-line file and directory paths must be absolute. In PowerShell, form paths from
`$PWD.ProviderPath`. Paths inside a task file follow [task-file rules](task-file.md).
`DISPATCH_HOME` relocates configuration and state; [configuration](config.md) describes its layout
and the platform defaults. Backend executable overrides are user config fields, not environment
variables.

## Flags

Options use separate tokens, such as `--max 2`, not an equals sign. Repeat only `--route` and
`--contract`. The current parser does not reject arbitrary unknown options, so acceptance alone
does not mean a flag has an effect. Use the flags below with their owning commands.

| Flag | Type | Default | Commands and validation |
|---|---|---|---|
| `--repo <path>` | absolute directory | Task-file `repo` for plan/run | Target repository. Required by current `selfcheck`; routes accepts it instead of `--dir`. Relative: `--repo must be an absolute path`. |
| `--dir <path>` | absolute directory | `--repo` for routes | Routes location. Relative: `--dir must be an absolute path`. It does not set a task's in-place directory. |
| `--ledger <path>` | absolute directory | User location, then state default | Overrides ledger root for run-addressing commands and run creation. Relative: `--ledger must be an absolute path`. |
| `--policy <path>` | absolute config-shaped file | No added CLI policy | Plan/run/selfcheck and routes load it. Only restrictions and instructions; recovery uses frozen policy. Relative: `--policy must be an absolute path`. |
| `--contract <path>` | repeatable absolute file | None | Plan/run, prepended to every task after file contracts. Relative: `--contract must be an absolute path`. |
| `--run <name>` | string | Task-file `run`, then task filename | Run creation name; must match `^[a-z0-9][a-z0-9-]{0,40}$`. The harness adds a UTC day prefix. Plan uses it when checking generated workspace names. |
| `--max <n>` | integer | Layered parallelism, tool default `3` | Plan/run; range 1 through 8. Non-integer: `--max needs an integer`; out of range names `maxParallel` and source. |
| `--backend <name>` | enum | No task default; catalog commands select configured preference or `opencode` | Plan/run preference; current routes/selfcheck select one backend. `opencode` or `codex`. |
| `--route <entry>` | repeatable qualified route | Effective task or configured route | Plan/run; one per backend, such as `codex:<model>` or `opencode:<provider>/<model>`. Duplicate backend: `--route may be given once per backend`. Invalid qualification fails normalization. |
| `--effort <value>` | non-empty string | Layered effort | Plan/run; same-layer suffix disagreement is an error. Must exist in the live catalog. |
| `--timeout-minutes <n>` | integer | Layered timeout, tool default `60` | Plan/run; 1 through 720. Non-integer: `--timeout-minutes needs an integer`. |
| `--profile <value>` | enum | Kind default | Plan/run; `clean` or `full`. |
| `--project <value>` | enum | Kind default | Plan/run; `on` or `off`. |
| `--access <value>` | enum | Kind default | Plan/run; `read-only`, `workspace`, `full`; cannot exceed policy ceiling. |
| `--network <value>` | enum | `on`, subject to policy | Plan/run; `on` or `off`; Codex full access with off is refused. |
| `--workspace <value>` | enum | `worktree` | Plan/run; `worktree` or `in-place`. |
| `--commit <value>` | enum | `none` in-place, `harness` worktree | Resolver preference for plan/run; `none` or `harness`. In-place runtime uses the effective recorded value. Prefer setting it in the task entry so the planning dirty check sees it too. |
| `--raw-prompt <value>` | boolean text | `false` | Plan/run; literal `true` or `false`, otherwise `--raw-prompt needs true or false`. Contracts, instructions and structured input conflict. |
| `--all` | switch | Off | opencode routes includes disabled catalog models. |
| `--message <text>` | string | Default decline feedback | Reply with `reject` only: `--message goes with reject: it is the feedback the model sees`. |
| `--continue` | switch | Off | Resume also starts unprompted tasks and re-prompts interrupted opencode sessions. |
| `--force` | switch | Off | Clean removes dirty or unmerged worktrees and branches too. |

A missing value at the end of a value option produces `--<flag> needs a value`. Missing positional
arguments produce `missing <argument>`. Invalid preference choices name the value, layer and choices.

## Commands

### `selfcheck [<tasks.jsonc>] [--repo <path>] [--backend <name>] [--policy <file>]`

Sends no prompt. Prints the allow-list and policy, the state, worktree and control folders, and on
Windows a warning when git's `core.longpaths` is not true and when the user PATH in the registry
exceeds 8,000 characters. The backends checked are `--backend`, else every backend the task file and
the configuration layers use, else opencode; each gets its own `backend <name>` section. opencode
checks its server pin, event bus, catalog, stored permissions and the creation and removal of an
idle session; Codex probes version, sign-in and catalog and reports the sandbox mode a task would
request. With `--repo` or a task-file `repo`, it also creates a detached test worktree at HEAD and
removes it afterwards; without one it prints `repository checks skipped`. Its temporary folder is
removed on exit. Prints `PASS` or `FAIL` per check, then `selfcheck passed` or a failed-check count.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | All performed checks passed. |
| `1` | integer | A reported check failed, including worktree preparation or a caught live backend failure. |
| `2` | integer | Checks could not start or an uncaught setup/configuration error occurred. |

### Configured backend coverage

`selfcheck` and `routes` cover every configured backend, each in its own labelled section. A task
route may carry its backend prefix, such as `codex:<model>#<effort>`, when it matches the task's
resolved `backend`; a prefix naming another backend is a plan error that names both.

### Windows user PATH guard

Selfcheck warns when the user PATH in the Windows registry exceeds 8,000 characters. Run refuses
above 16,000 characters with exit 2. Run records a `warning` event if the user PATH changes.
Environment isolation does not isolate registry state; avoid assigning a new `DOTNET_CLI_HOME`.
Workers receive `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` to prevent the SDK's tools-path append.
The warning event carries `reason: user-path-changed`, both lengths, and the added and removed
entries. Inspect the value with `reg query HKCU\Environment /v Path`.

### `routes --dir <path>|--repo <path> [--backend <name>] [--all]`

Lists the selected backend's catalog at the location. opencode prints server version, agents,
manual/hidden/allowed flags, model variants and allow-list diagnostics. `--all` also prints disabled
models. Codex prints its version, qualified model names and supported efforts, plus whether a route
restriction applies. The current command selects one backend. No prompt is sent.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Catalog read successfully; opencode listing can include unusable routes. |
| `2` | integer | Missing location, invalid config/arguments, failed probe or catalog read. |

### `plan <tasks.jsonc> [--repo <path>] [--contract <path>]... [--policy <file>]`

Applies preference flags, checks task fields, contracts, structured schemas, repository refs,
workspace collisions, writer scopes, route allow-list and live catalogs. Prints `warning:` and
`error:` lines, followed by `plan ok: <count> task(s) against <repo>` or
`plan refused: <count> error(s)`. Sends no prompt and creates no recorded run.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | All checks passed; warnings are allowed. |
| `2` | integer | Any planning or setup error. |

### Inherited Codex rules on read-only tasks

The pinned Codex app-server cannot be told to ignore exec-policy rules, and an explicit allow rule
can run a matching command outside the sandbox without asking. For every read-only Codex task, plan
prints a warning and `result.json` records it in `capabilityNotes`, worded by profile: with `full`,
the user's and the system's rules apply; with `clean`, only system-level rules do. A clean profile
that fell back to `clean-partial` counts as `full`.

### `run <tasks.jsonc> [--repo <path>] [--run <name>] [--max <n>] [--contract <path>]... [--policy <file>]`

Performs plan and live checks, locks and claims a new run, freezes inputs, queues tasks, supervises
workers, settles results and runs verification. It blocks until the scheduler ends and prints a
status summary. Ledger writes also print event lines. The current process owns the supervisor and
backend processes; killing the caller is not a reliable way to keep the run alive.

A recorded id cannot run again. The refusal names `collect` and `resume` as recovery commands.
Use a new name for a new run rather than editing or deleting recorded input.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Every task is done, with valid scope, verification and structured deliverable as applicable. |
| `1` | integer | Any task is not done, or an exception/abort occurs after the run claim. |
| `2` | integer | Refusal before claim, including invalid plan, existing id, invalid max, backend or configuration failure. |

### `status [<run>] [--ledger <dir>]`

With no run, lists directories in the ledger root and counts their task states, or prints `no runs`.
With a run, prints state, outcome, cost/tokens if known, changed-file and scope counts, asks,
escalations and verify results. Live task rows also show tool count and the last completed call.
It reports whether the run lock is held and whether asks are pending. It does not wait for success.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Status read successfully, regardless of task outcomes. |
| `2` | integer | Unknown/unreadable run, newer schema, or corrupt complete event line. |

### `asks [<run>] [--ledger <dir>]`

With a run, lists its pending asks with action, resources, rule and age, and says if its supervisor
has ended. With no run, scans control folders and displays `run.ref`'s run id and ledger root plus
asks. It skips command-only `cmd-*` folders. Missing or invalid `run.ref` is displayed, not used as
a gate. No-run enumeration does not restrict itself to `--ledger`.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Listing finished, including no pending asks and missing/invalid display-only run references. |
| `2` | integer | Invalid configuration, arguments, missing named-run control folder or uncaught I/O error. |

### `reply <run> <askId> once|reject [--message <text>] [--ledger <dir>]`

Writes a one-shot decision for an existing ask while the supervisor holds the run. Waits up to 30
seconds for a matching operator `permission` event. Prints answered, attempted-but-unconfirmed or
queued feedback. `once` allows one request; `reject` carries supplied or default feedback. The
harness never replies with a standing approval.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Supervisor confirmed the decision. |
| `1` | integer | Decision queued or attempted but not confirmed. |
| `2` | integer | Invalid decision, message with once, missing run/control/ask, or no supervisor. |

### `cancel <run> <task> [--ledger <dir>]`

If supervised, writes a cancel request and waits up to 30 seconds for its matching `cancel` event.
Otherwise marks the task cancelled in the ledger. An already-final task is a no-op. Cancel targets
the task; do not stop it by killing the supervisor.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Request confirmed, unsupervised task marked cancelled, or task already final. |
| `1` | integer | Request written but not confirmed in time. |
| `2` | integer | Unknown run/task, missing control folder, invalid arguments or setup error. |

### `collect <run> <task> [--ledger <dir>]`

Takes the run lock and requires completed setup. Reloads the task and frozen effective config,
collects backend evidence and repeats settlement: final text, commit/diff, verification or structured
validation. Prints events and writes a final event. A task needs a recorded session and workspace.
It does not re-prompt. A released done opencode session is not collectible.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Recollected task is done. |
| `1` | integer | Settled task is not done, or an exception occurs inside settlement. This includes structured frozen-input hash validation failure. |
| `2` | integer | Pre-settlement refusal: active supervisor, incomplete setup, missing task/session/workspace/config, or a released done opencode session. |

### `resume <run> [--continue] [--ledger <dir>]`

Takes the run lock, requires `setup.done`, reloads frozen settings, resets stale asks and reconciles
non-final tasks. Without `--continue`, never-prompted tasks remain queued and interrupted opencode
sessions settle as failed. With it, queued tasks start and interrupted opencode sessions receive a
fixed continuation line. Started Codex tasks cannot continue; either form recovers their backend
outcome as `lost`, with recorded cancellation or timeout taking precedence in settlement.
Already-final tasks are skipped. Prints `nothing to resume` when none need action, or events and a
status summary. Recovery is backend-specific; see [backends](backends.md).

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Every recorded task is done after reconciliation. |
| `1` | integer | Any task remains not done, or reconciliation aborts inside the command's recovery block. |
| `2` | integer | Pre-recovery refusal, including active lock, incomplete setup, missing/unreadable frozen config or task records, invalid schema or configuration. |

The base does not re-read `tasks.jsonc` during recovery. Frozen structured input checks run when
starting or settling a structured task; failures within settlement produce a failed task rather
than a universal exit-2 refusal. Do not alter any ledger inputs by hand.

### `clean <run> [--force] [--ledger <dir>]`

Refuses while supervised. Releases opencode sessions, removes recognized worktrees and task
branches, temp directories and temporary indexes. Without force, keeps dirty worktrees or branches
not merged into their recorded base. It never removes an in-place folder or the ledger. Successful
control cleanup keeps `supervisor.lock` and `run.ref`; a second clean is safe.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Cleanup succeeded or nothing needed removal. |
| `1` | integer | Any cleanup was refused or failed. |
| `2` | integer | Unknown run, held supervisor lock, invalid configuration or uncaught setup error. |

## Common error handling

| Exit | Type | Default meaning |
|---|---|---|
| `0` | integer | Command-specific success; status and listings do not prove task success. |
| `1` | integer | Incomplete execution, unconfirmed control request or failed reported checks. |
| `2` | integer | Usage error, unknown command, configuration failure, or error caught by the top-level CLI. |

Ctrl-C cancels the current command. The top-level CLI returns 2 for cancellation or unexpected
errors; a claimed run or recovery error caught internally returns 1. A corrupt complete event line
is an error; a non-newline-terminated final fragment is ignored. Consult [ledger](ledger.md) before
building a consumer that derives outcomes from events.
