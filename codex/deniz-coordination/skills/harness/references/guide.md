Read when you choose a task's settings, or read a result and decide what it proves.

# Choosing settings

Each section says when to use a setting, why, and what it costs. Field details live in
[task files](task-file.md), backend differences in [backends](backends.md).

## Kind

| Use | When | Why |
|---|---|---|
| `code` | The worker changes files and the change is the product | On a worktree the harness commits the result on the task's branch (in-place, `commit` decides), records the diff, checks scope and runs verify |
| `mechanical` | A change with one right answer: a rename, a move, a quoted list | Same settlement as `code`; the kind tells the reader no design judgement is expected |
| `review` | The worker reads another task's change and reports findings | Read-only by default; with `reviewOf` it sees the author's branch |
| `research` | The worker reads and reports, and changes nothing | Read-only by default; the final text is the deliverable |
| `structured` | You need a validated JSON answer per input item | The harness checks the schema, identity and immutable fields; nothing is committed |

## Backend and route

- Choose the backend for the route you need: the model, its effort, its context window, and its
  family. A review counts as a second family only when its route's family differs from the author's,
  and only when the result shows `routeVerified: true` for both tasks.
- Codex runs its tools in an OS sandbox; opencode's Linux/macOS fence for read-only and workspace
  tasks is an [accident guard](backends.md#opencode-accident-guard), not a security boundary, and
  opencode on Windows is unfenced. In Codex's elevated Windows sandbox, .NET builds and tests work
  after an outside restore (`prepare`); unelevated mode and MSYS git (Git Bash, `git submodule`) still
  fail there ([backends](backends.md#codex-process-and-sandbox)). A task that hits those limits needs
  `access: full` on Codex, chosen on purpose, while on opencode its `workspace` access is already
  unfenced. Either Windows choice runs without a fence; choose the backend for its route. Prefer the
  [dependency recipes](#dependencies-and-the-sandbox) below. On Linux/macOS, for build-time cache
  writes, prefer workspace access with [writable roots](config.md#writable-roots) rather than removing
  the fence.
- Both backends refuse a server or CLI off the harness's pin: install the version the error names.

## Access, network and profile

| Setting | Choose | Because |
|---|---|---|
| `access: read-only` | Reviews, research, anything that must not change files | Codex's sandbox and Linux/macOS opencode's accident guard keep the shell; Windows opencode denies edits and, by default, every shell call |
| `access: workspace` | Code that changes only its worktree | The default for `code` and `mechanical`; Codex's sandbox and Linux/macOS opencode's accident guard grant writes to the workspace, temp and configured writable roots; Windows opencode is unfenced |
| `access: full` | Only when a task must write outside its worktree (build caches, temp) and no sandbox can host it | It removes the fence; the ledger records the choice, so choose it explicitly |
| `network: off` | A task that must not fetch anything | Codex enforces it; opencode denies its web tools only |
| `profile: clean` | Reviews, research, mechanical and structured work: their default | A minimal profile: no user MCP servers, plugins or instructions; measured at 13% fewer prompt tokens and 33% fewer uncached prompt tokens per task |
| `profile: full` | Code tasks (their default), and any task that needs the installed profile's tools or skills (an MCP server, a plugin) | It loads everything installed, about 7–10k tokens on every model call |
| `project: off` | The task must not read the repository's own agent rules | Rarely right for code; the default for structured tasks |

## Dependencies and the sandbox

`prepare` is a list of shell commands and names no ecosystem. Use it for dependency steps outside
the sandbox before the worker starts; [task files](task-file.md#preparation) own execution rules,
workspace protection and lifecycle. Name concrete offline build/test commands in the worker prompt
and forbid adding dependencies: the harness does not amend that prompt automatically.

`prepare` runs the task's base content as you, outside any sandbox: package install scripts and
build-system hooks run with your rights. On a `reviewOf` task or a base a worker wrote, that is
worker-authored code; review it first or use script-free forms such as `npm ci --ignore-scripts`.

```json
"prepare": ["dotnet restore", "dotnet tool restore"]
```

Each entry is one command; the list stops at the first failure.

| Ecosystem | `prepare` | The worker inside the sandbox |
|---|---|---|
| .NET | `dotnet restore`; `dotnet tool restore` | `dotnet build --no-restore`; `dotnet test --no-build` |
| Node | `npm ci` (or `pnpm install --frozen-lockfile`); set `cache=.npm-cache` in workspace `.npmrc` | `npm test --offline` (same workspace-local cache) |
| Python | `uv sync --frozen` | `uv run --offline pytest` |
| Go | `go mod download` | `GOCACHE=$TMPDIR/go-build go test ./...` (the build cache must be writable; the default is the user cache) (POSIX shell; in PowerShell use `$env:GOCACHE="$env:TEMP\go-build"; go test ./...`) |
| Rust | `cargo fetch` | `cargo test --offline` |

Only the .NET row has dated evidence (Codex elevated Windows,
[backends](backends.md#codex-process-and-sandbox)); the live tests FN11 (.NET) and FN12 (Node, with a
workspace-local npm cache) cover `prepare` itself. The Python, Go and Rust rows are untested starting
points: run them once in your sandbox before relying on them.

Prepare uses the worker environment, but not its sandbox: fenced opencode has private
`XDG_CACHE_HOME` and temp, and the harness removes `NUGET_*` variables from both `prepare`
and the worker, so a `NUGET_PACKAGES` set in your shell has no effect; use `RestorePackagesPath`.
Default `~/.nuget/packages` packages restored
outside are readable inside both sandboxes; `--no-restore` builds read them and write workspace
`obj/` and `bin/`. A project-local `RestorePackagesPath` in `Directory.Build.props` makes restore
and build agree without an environment variable. Everything `prepare` writes inside the workspace
must be git-ignored first (`node_modules/`, `.venv/`, `obj/`, a project-local package folder such as
`.pkgs/`); a non-ignored write fails the task with `prepare-changed-workspace`.
A tool that writes a user-wide cache during build/test (some analyzers or `dotnet tool` first runs)
still needs a workspace-local path or [writable root](config.md#writable-roots). The opencode fence
does not isolate network; cache-write denial is separate from Codex elevated Windows TLS failure.
Container tests run in `verify`, outside the sandbox.

Prepare and verify use the same `Proc.RunAsync`: process-tree kill happens on command timeout or
cancel only. Background processes left after successful command exit are not tracked; neither are
processes surviving supervisor exit. There is no job, `setsid` or group kill. Use finite dependency
steps (restore/install/download/fetch) that leave no background process; node reuse and the compiler
server are disabled. Interrupted preparation is never automatically replayed; use the
[recovery steps](cli.md#resume-run---continue---ledger-dir).

## Workspace and commit

- `workspace: worktree` (the default) gives each task its own branch from `base`, so parallel tasks
  never share files. `in-place` runs in `dir`; use it only when the task must see uncommitted state.
- A worktree task's result is committed by the harness on the task's branch, unless its worker stopped or
  was blocked: that work stays uncommitted and goes to `wip.patch`. An in-place task
  takes `commit: none` (the default: the diff is recorded, nothing is committed) or `commit: harness`
  (a clean tree is required, and the commit lands on the current branch).
  Harness commits use `chore(dispatch): <run>/<task>`, with the run id and task id.
- `base` takes a branch, a tag or a SHA. A SHA is exact; planning fetches remote-tracking bases before
  resolving them, including revision suffixes and branches without a local tracking ref. Local
  branches and tags use the repository's current refs without fetching. See [plan](cli.md#plan-tasksjsonc---repo-path---contract-path---policy-file).

## Verify, advisory verify and review

- `verify` lists the commands whose failure fails the task. They run after collection, on the task's
  result (the committed tree, or the working folder of an in-place `commit: none` task), as your user,
  outside any sandbox; a stopped or blocked worker's result is not verified. Run each one on the base once before you hand
  the task out, and check that its result means something: zero tests run is never a pass.
- `advisoryVerify` lists checks that may fail by design on this change (a baseline the coordinator
  owns, a known red gate). They run and are recorded, but a failure only adds a warning, so the task
  can settle `done` and a dependent review still starts.
- `reviewOf` starts a review on the author's branch once the author settles `done` with a commit; an
  author that fails or changes nothing cancels the review.

## Contracts and the status line

- `contracts` prepends files to the prompt; the harness adds none by default. Name the bundled ones by alias,
  `@worker-contract` and `@<model>-worker`, so a plugin update cannot leave a stale path; `plan` prints each
  task's resolved contracts and warns on a code or mechanical task without the worker contract.
- The harness asks every non-structured task to end with `Status: done`, `Status: stopped` or
  `Status: blocked`. A stopped or blocked worker fails the task (`worker-stopped`) even when the
  session succeeded: read its final text for the reason.

## Reading a result

| Field | Tells you | Act |
|---|---|---|
| `state` | `done`, `failed` or `cancelled` | Only `done` passed every check below |
| `failures` | The machine-readable causes | Each cause has a row in [the ledger](ledger.md) |
| `warnings` | Findings that did not fail the task | `worker-status-missing` means the success rests on the session outcome alone |
| `routeVerified` | Every route the backend reported is the requested one | Only `true` supports a claim about which model did the work |
| `workerStatus` | The worker's own verdict | `stopped` or `blocked`: fix the prompt or the environment, then run a new task |
| `zombie` (a failure) | A stop could not be confirmed | The worktree keeps the work; commit and verify were skipped |
| `wipPatch` | A stopped or blocked worker's unfinished changes, as `wip.patch` | Judge the partial state; verify did not run (`verify[].notRun`) |
| `recoveredErrors` | Model calls that failed while the session still succeeded | Not a failure; read them when a result looks thin |
| `tokens` | Input summed over every model call | Not a context size; `cacheRead` is the cached part |
