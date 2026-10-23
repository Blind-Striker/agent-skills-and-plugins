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

Command-line file and directory paths must be absolute, except bundled `--contract` aliases
(`@worker-contract`, `@<model>-worker`). Resolution, worker-block extraction, errors, hashing and
ordering follow [Contracts](task-file.md#contracts). In PowerShell, form paths from
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
| `--repo <path>` | absolute directory | Task-file `repo` for plan/run and selfcheck | Target repository. Required by `routes` (`routes needs --repo`); optional for `selfcheck`, which skips repository checks without one. Relative: `--repo must be an absolute path`. |
| `--dir <path>` | absolute directory | None | Parsed but not used by `routes`. It does not set a task's in-place directory. |
| `--ledger <path>` | absolute directory | User location, then state default | Overrides ledger root for run-addressing commands and run creation. Relative: `--ledger must be an absolute path`. |
| `--policy <path>` | absolute config-shaped file | No added CLI policy | Plan/run/selfcheck and routes load it. Only restrictions and instructions; recovery uses frozen policy. Relative: `--policy must be an absolute path`. |
| `--contract <path or alias>` | repeatable absolute file or bundled alias | None | Plan/run, prepended to every task after file contracts. Ordinary relative paths: `--contract must be an absolute path`. Alias semantics: [Contracts](task-file.md#contracts). |
| `--run <name>` | string | Task-file `run`, then task filename without extension and a trailing `.tasks` | Run creation name; must match `^[a-z0-9][a-z0-9-]{0,40}$`. The harness adds a UTC day prefix. Plan uses it when checking generated workspace names. |
| `--max <n>` | integer | Layered parallelism, tool default `3` | Plan/run; range 1 through 8. Non-integer: `--max needs an integer`; out of range names `maxParallel` and source. |
| `--backend <name>` | enum | None | Plan/run preference. For `routes` and `selfcheck` it limits the check to that backend; without it they cover every backend the task file and configuration layers use. Task-file-free `selfcheck` discovers installed backends when those layers name none. `opencode` or `codex`. |
| `--route <entry>` | repeatable qualified route | Effective task or configured route | Plan/run; one per backend, such as `codex:<model>` or `opencode:<provider>/<model>`. Duplicate backend: `--route may be given once per backend`. Invalid qualification fails normalization. |
| `--effort <value>` | non-empty string | Layered effort | Plan/run; same-layer suffix disagreement is an error. Must exist in the live catalog. |
| `--timeout-minutes <n>` | integer | Layered timeout, tool default `60` | Plan/run; 1 through 720. Non-integer: `--timeout-minutes needs an integer`. |
| `--profile <value>` | enum | Kind/backend/access default | Plan/run; `clean` or `full`. |
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

Every command that takes `<run>` (`status`, `asks`, `reply`, `cancel`, `collect`, `resume`,
`clean`) accepts the full run id or the bare run name. A ledger folder named exactly as given wins;
otherwise a bare name resolves when exactly one ledger is named `<yyyy-MM-dd>-<name>`. With several
matches the command refuses with exit 2 and lists their run ids.

### `selfcheck [<tasks.jsonc>] [--repo <path>] [--backend <name>] [--policy <file>]`

Sends no prompt. Prints the allow-list and policy, the state, worktree and control folders, and on
Windows a warning when git's `core.longpaths` is not true and when the user PATH in the registry
exceeds 8,000 characters. The backends checked are `--backend`, else every backend the task file and
the configuration layers name (preferred backend, default routes, allow-list, task backends, then
configured backends). If none is named and no task file was supplied, it discovers installed
backends in order: `opencode`, then `codex`, before preparing a worktree or creating backend
instances. Installed means the executable resolves through that backend's launch lookup, not
that its version, sign-in or server readiness has passed. OpenCode retains native npm-shim
selection and the pinned SDK's PATH/PATHEXT lookup, including relative PATH entries; Codex uses
the shared resolution described below. Each unresolved candidate prints exactly
`SKIP  backend <name>: not installed (<name> on PATH)`; skips are not failed checks. If neither
resolves, stderr prints `dispatch: no backend installed; install opencode or Codex, or set backends.<name>.executable`
and the command exits 2. A supplied task file retains the empty-selection refusal:
`dispatch: no backend configured; set preferences.backend, defaultRoutes or --backend`.
Named backends are never skipped: a missing executable remains a failed check (exit 1).
Resolved executables undergo every existing probe and launch check; version, login and launch
failures remain failures and do not prevent checking the next backend.
An executable that resolves but is unusable, including PowerShell-only Codex, is installed for
discovery purposes and fails its backend check with the resolver's diagnostic, rather than being skipped.
Each backend gets its own `backend <name>` section. opencode
checks its server pin, event bus, catalog, stored permissions and the creation and removal of an
idle session; Codex probes version, sign-in and catalog and reports the sandbox mode a task would
request. Both backends also perform a `launch` check: build a synthetic code task through the same
launch builder as real tasks, create its worker, then release it without a prompt or model call
(no Codex `turn/start` or opencode prompt request). `PASS  launch: <worker id>` is printed only after
release succeeds; otherwise `FAIL  launch: <exception message>` makes the exit code 1, and checks
continue for other backends. Synthetic task temporary folders and backend-owned homes are cleaned up.
With `--repo` or a task-file `repo`, it also creates a detached test worktree at HEAD and
removes it afterwards; without one it prints `repository checks skipped`. Its temporary folder is
removed on exit. Prints `PASS`, `WARN` or `FAIL` per check. A successful clean-profile validation
that falls back to `clean-partial` is `WARN`, including the profile reason; validation errors remain
`FAIL`. With no failures, the closing line is `selfcheck passed (<n> warning(s))`, counting only
`WARN` check lines, not free-form advisory warnings. Otherwise it prints `selfcheck: <n> check(s) failed`.

With a repository on Windows, `worktree path depth` measures
`<resolved worktree root>\<yyyy-MM-dd>-selfcheck\selfcheck` (today's UTC date) plus the longest tracked
path at HEAD: `PASS` below 248 characters, `WARN` at 248 or more, never `FAIL`. A measurement error
prints `WARN` with its reason and checks continue; cancellation still propagates.
See [worktree root configuration](config.md#files-and-locations) for the trade-off.

On Windows only, a successful Codex probe also prints exactly one
`PASS  windows sandbox: mode <mode>; readiness <status>` check. Mode comes from the user's Codex
home configuration: `elevated`, `unelevated`, `mxc`, or `not set` when unset. Readiness is the raw
`windowsSandbox/readiness` status (`ready`, `notConfigured`, or `updateRequired`), not the requested
task sandbox. Any status other than `ready` makes this line `WARN`, never `FAIL`; it contributes
only to the warning count, not a failure exit. Existing sandboxed-access readiness validation can
still fail. This line is absent on Linux/macOS and from OpenCode sections.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | No performed check failed; warnings and automatic-discovery skips are allowed. |
| `1` | integer | A reported check failed, including worktree preparation or a caught live backend failure. |
| `2` | integer | No installed backend was discovered, checks could not start, or an uncaught setup/configuration error occurred. |

### Configured backend coverage

`selfcheck` and `routes` cover every configured backend, each in its own labelled section, unless
`--backend` limits selection. Only task-file-free `selfcheck` falls back to installed-backend
discovery when none is named; `routes` retains the empty-selection refusal above. A task
route may carry its backend prefix, such as `codex:<model>#<effort>`, when it matches the task's
resolved `backend`; a prefix naming another backend is a plan error that names both.

Codex executable resolution is shared by version/login commands, clean-home probes and worker
launches. A configured absolute `backends.codex.executable` takes precedence over PATH. On Windows,
PATH discovery skips relative directories and prefers supported `.exe`, `.cmd`, `.bat` and `.com`
shims over `.ps1`, preserving PATH and PATHEXT order among supported candidates. It remembers the
first `.ps1` only to diagnose a lookup with no supported candidate. A configured `.ps1` is also
rejected, without falling back to PATH. Both cases report:
`codex resolves only to a PowerShell script (<path>); set backends.codex.executable to its .cmd or .exe`.

### Windows user PATH guard

Selfcheck warns when the user PATH in the Windows registry exceeds 8,000 characters. Run refuses
above 16,000 characters with exit 2. Run records a `warning` event if the user PATH changes.
Environment isolation does not isolate registry state; avoid assigning a new `DOTNET_CLI_HOME`.
Workers receive `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` to prevent the SDK's tools-path append.
The warning event carries `reason: user-path-changed`, both lengths, and the added and removed
entries. Inspect the value with `reg query HKCU\Environment /v Path`.

### `routes --repo <path> [--backend <name>] [--all]`

Lists the selected backend's catalog at the location. opencode prints server version, agents,
manual/hidden/allowed flags, model variants and allow-list diagnostics. `--all` also prints disabled
models. Codex prints its version, qualified model names and supported efforts, plus whether a route
restriction applies. With `--backend` it lists that backend; without it, every configured backend,
each in its own labelled section. No prompt is sent.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | Catalog read successfully; opencode listing can include unusable routes. |
| `2` | integer | Missing location, invalid config/arguments, failed probe or catalog read. |

### `plan <tasks.jsonc> [--repo <path>] [--contract <path>]... [--policy <file>]`

Applies preference flags, checks task fields, contracts, structured schemas, repository refs,
workspace collisions, writer scopes, route allow-list and live catalogs. Every advisory line
starts with its level: `INFO`, `WARN` or `ERROR`; any `ERROR` refuses the plan. Then it prints
`plan ok: <count> task(s) against <repo>` or `plan refused: <count> error(s)`. Sends no prompt and
creates no recorded run.

For every non-structured task, in task-file order, `plan` also prints
`INFO <task>: temp <resolved folder> (tempRoot from <layer>)`, or
`INFO <task>: temp <resolved folder> (default)` when unset. It creates no temporary folder.
`tempRoot` is a layered preference, not a CLI option; see
[configuration](config.md#temporary-folders) and [layouts](task-file.md#scope-and-workspaces).

The live check adds, per task, the model's context window when the backend's catalog lists it:
`INFO <task>: context window <n> tokens (opencode:<route>)`. Codex's catalog lists none, so a Codex
task prints `INFO <task>: context window of codex:<model> is known after the run (result.json contextWindow)`.

Remote-tracking bases are recognised by the repository's configured remote names, even without a local
tracking ref. They are fetched before resolution with `git fetch --quiet <remote> <branch>`, once per
distinct remote and branch per planning call. Trailing revision suffixes (`~n`, `^n`, `^{...}`) are
removed for the fetch; the full base is then resolved as written. Fetch uses the operator's process
environment, with the harness's per-call git overrides and `GIT_TERMINAL_PROMPT=0`; worker and verify
git calls remain scrubbed. A failed fetch refuses the plan without retry:
`base <base>: fetch from <remote> failed: <first line of git's stderr>`.
Before warnings, plan and run print `INFO <task>: base <base> resolves to <12 hex digits>` for each
base, appending ` (fetched from <remote>)` for remote-tracking bases. Local branches, tags and SHAs
are not fetched.

On Windows, plan measures worktree path depth once per distinct repository and HEAD and reports each
task's resulting depth when it reaches 248 characters. A measurement failure is advisory:
`WARN <task>: worktree path depth not measured (<reason>)`; it does not refuse the plan.

| Exit | Type | Meaning |
|---|---|---|
| `0` | integer | All checks passed; warnings are allowed. |
| `2` | integer | Any planning or setup error. |

### Read-only shell lines in plan

For each read-only task, an `INFO` line states its shell, per backend, with the resolved
`readOnlyShell` value and its layer. A `workspace` or `full` task gets no such line.

| Backend | `decline` | `escalate` |
|---|---|---|
| Codex | `INFO <task>: sandboxed shell; escalations declined (readOnlyShell decline, from <layer>)` | `INFO <task>: sandboxed shell; escalations reach the operator through a matching rule (readOnlyShell escalate, from <layer>)` |
| opencode | `INFO <task>: no shell for a read-only task (readOnlyShell decline, from <layer>)` | `INFO <task>: shell asks reach the operator through a matching rule (readOnlyShell escalate, from <layer>)` |

### Inherited Codex rules on read-only tasks

The pinned Codex app-server cannot be told to ignore exec-policy rules, and an explicit allow rule
can run a matching command outside the sandbox without asking. For every read-only Codex task, plan
prints a `WARN` line and `result.json` records it in `capabilityNotes`, worded by profile: with
`full`, the user's and the system's rules apply; with `clean`, only system-level rules do. A clean
profile that fell back to `clean-partial` counts as `full`.

### `run <tasks.jsonc> [--repo <path>] [--run <name>] [--max <n>] [--contract <path>]... [--policy <file>]`

Performs plan and live checks, prints each plan warning once, locks and claims a new run, freezes
inputs, queues tasks, supervises workers, settles results and runs verification. Once it claims the
run it prints `run <run id>: ledger <folder>`. It blocks until the scheduler ends and prints a status summary.
Ledger writes also print event lines. The `run` process is the supervisor and owns the backend
processes: never kill it (use `cancel`). To keep working while it runs, start it from a
background shell and watch with `status <run>` from another shell; when a run's own process ends
without settling every task, `resume <run>` settles what it left. Watch a run through its ledger
(`status`, `events.jsonl`), not through the shell that started it: a host under memory pressure
can end that shell while the supervisor keeps working.

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
A task whose escalated asks closed without an answer (the worker's call was declined) gets a line
`asks closed without an answer: <count> (<action>: <reason>; ...)`.
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
| `1` | integer | Settled task is not done, or an exception occurs inside settlement. |
| `2` | integer | Pre-settlement refusal: active supervisor, incomplete setup, missing task/session/workspace/config, a released done opencode session, or a structured task whose frozen `input.json` or `result.schema.json` no longer matches its recorded hash. |

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

Recovery validates structured tasks against the frozen task specification and checks the frozen
input and schema hashes before any reattachment or further prompt; a mismatch refuses with exit 2
before a backend starts. Do not alter any ledger inputs by hand.

### `clean <run> [--force] [--ledger <dir>]`

Refuses while supervised. Releases opencode sessions, removes recognized worktrees and task
branches, temp directories and temporary indexes. Without force, keeps dirty worktrees or branches
not merged into their recorded base. It never removes an in-place folder or the ledger. Successful
control cleanup keeps `supervisor.lock` and `run.ref`; a second clean is safe.

Cleanup is resumable: missing artifacts are already removed; retry reconciles the recorded folder,
its registration, branch, task temp and temporary index independently. A folder with a missing or
dangling `.git` link is partially removed: without force, clean refuses with
`<task>: worktree partially removed (<path>); clean --force finishes the removal`.
Force finishes that removal and skips dirtiness assessment, but never bypasses Git
configuration/filter checks, worktree locks, structured-scratch ownership or in-place safety. Clean
finds the task's registration by its recorded path, or by its branch when Git records the path
differently (a root reached through a link); a detached worktree is found by path. Only that
registration is removed, never repository-wide pruning.

After all tasks succeed, clean removes empty run/temp containers bottom-up, including an empty
`.dispatch-tmp/git` and its empty parent. Nonempty containers and links remain and are printed as
`retained: <path>`; the worktree root remains. A task failure prints task, cleanup step and error,
records `clean-failed` rather than `cleaned`, continues other tasks, and returns exit 1. A session
that cannot be released records `clean-failed` at `session`, and clean still removes that task's
worktree, branch and temporary folders.

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
