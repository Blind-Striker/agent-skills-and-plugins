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
- Codex runs its tools in an OS sandbox; opencode's Linux/macOS fence for read-only and workspace tasks
  is an [accident guard](backends.md#opencode-accident-guard), not a security boundary. Windows is unfenced. On Windows, Codex's sandbox
  cannot run .NET builds and tests or MSYS git reliably ([backends](backends.md)): such a task needs
  `access: full` there, chosen on purpose, while on opencode its `workspace` access is already
  unfenced. Either Windows choice runs without a fence; choose the backend for its route. On
  Linux/macOS, prefer workspace access with the [writable-root recipe](config.md#writable-roots)
  for .NET/Bun caches rather than removing the fence.
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

## Workspace and commit

- `workspace: worktree` (the default) gives each task its own branch from `base`, so parallel tasks
  never share files. `in-place` runs in `dir`; use it only when the task must see uncommitted state.
- A worktree task's result is always committed by the harness on the task's branch. An in-place task
  takes `commit: none` (the default: the diff is recorded, nothing is committed) or `commit: harness`
  (a clean tree is required, and the commit lands on the current branch).
- `base` takes a branch, a tag or a SHA. A SHA is exact; planning fetches remote-tracking bases before
  resolving them, including revision suffixes and branches without a local tracking ref. Local
  branches and tags use the repository's current refs without fetching. See [plan](cli.md#plan-tasksjsonc---repo-path---contract-path---policy-file).

## Verify, advisory verify and review

- `verify` lists the commands whose failure fails the task. They run after collection, on the
  committed tree, as your user, outside any sandbox. Run each one on the base once before you hand
  the task out, and check that its result means something: zero tests run is never a pass.
- `advisoryVerify` lists checks that may fail by design on this change (a baseline the coordinator
  owns, a known red gate). They run and are recorded, but a failure only adds a warning, so the task
  can settle `done` and a dependent review still starts.
- `reviewOf` starts a review on the author's branch once the author settles `done` with a commit; an
  author that fails or changes nothing cancels the review.

## Contracts and the status line

- `contracts` prepends files to the prompt; the harness adds no contract by default.
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
| `tokens` | Input summed over every model call | Not a context size; `cacheRead` is the cached part |
