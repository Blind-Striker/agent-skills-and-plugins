---
name: harness
description: Use when you run the bundled dispatch harness to send a task file to worker models through OpenCode or Codex, check or resume a run, answer an escalated ask, cancel or collect a task, or read its ledger. The harness's commands, task file, modes, configuration, ledger and exit codes; whether and how to delegate is delegating-to-worker-models' judgment.
---

# Dispatch harness

The harness runs tasks on worker models out of process, through two equal backends, opencode and
Codex. Per task it prepares a workspace, starts the worker, answers or escalates its asks, settles
the result from git, runs the verify commands on the committed tree, and records every step in a
ledger. This skill is the harness's contract.

## Boundary

- This skill says how to run a task, not what to delegate or whether the output is good. When you
  write a code task's prompt or judge a worker's output, load `deniz-dispatch:delegating-to-worker-models`.
- The harness prepends only the contract files you name (`contracts`, `--contract`); nothing by default.
- A calling skill that sends `structured` tasks owns their prompt and result schema.

## Invocation

From this skill's folder (its `global.json` selects the SDK), build, then run the command:

```text
dotnet build dispatch.cs
dotnet dispatch.cs -- <command> [arguments]
```

- Build before every command. A non-zero build exit means nothing started; fix the error or retry
  when two builds collided. The `--` keeps `dotnet` from taking flags such as `--force`.
- Paths on the command line are absolute (task file, `--repo`, `--contract`, `--ledger`); paths in a
  task file are relative to that file. In PowerShell use `$PWD.ProviderPath`.
- Run `selfcheck [--repo <path>]` on a new machine, after an update, and in a new repository. It
  checks each configured backend (version, sign-in, catalog, a clean profile), Windows readiness and
  every resolved folder, and sends no prompt.
- Commands: `selfcheck`, `routes`, `plan`, `run`, `status`, `asks`, `reply`, `cancel`, `collect`,
  `resume`, `clean`. Arguments, output and exit codes of each: `references/cli.md`.

## Backend and modes

Pick the backend per task: Codex when an operating-system sandbox must enforce read-only or
workspace-only writes; opencode for a model or roster agent only it serves; either for structured
output. Each task resolves four modes:

| Setting | Values | Code, mechanical | Review, research | Structured |
|---|---|---|---|---|
| `profile` | `clean` (sign-in only), `full` (installed profile) | `full` | `full` | `clean` |
| `project` | `on` (repository context loads), `off` | `on` | `on` | `off` |
| `access` | `read-only`, `workspace`, `full` | `workspace` | `read-only` | `read-only` |
| `workspace` | `worktree` (own branch from `base`), `in-place` (`dir`) | `worktree` | `worktree` | scratch folder |

- On Codex, `access` is the sandbox. On opencode there is no sandbox: `read-only` is a deny rule on
  the edit tool, and a shell command still writes. `full` only when the task or operator asks.
- A read-only task's write request is declined at once. Its shell request follows `readOnlyShell`:
  `decline` (default) answers no; `escalate` lets an escalation rule send it to the operator, and
  `once` then runs that one command outside the sandbox. Choose `escalate` only when a reviewed
  command must leave the sandbox, such as a review that has to run one build.
- A deny rule wins over an escalation rule. An ask that matches no rule: opencode allows it once,
  Codex declines it with a message. Codex asks only to leave its sandbox, so a deny holds only at
  that gate: with `workspace` access and network on, a `git push` deny does not stop a push.
- On opencode the worker runs as your user: a shell command can reach the harness's control folder
  and the run's ledger. Verify commands run the worker's code as your user on every backend.
- Preferences layer user config < repository `.dispatch/` < task file < command line. Restrictions
  (denies, scope, maximum access, network) only add up, and the route allow-list is the
  intersection of the layers that have one. Details: `references/config.md`.

## Task file

```jsonc
{
  "schemaVersion": 2,
  "contracts": ["worker-contract.md"],
  "tasks": [
    { "id": "fix-guide-typo", "kind": "code", "backend": "codex", "route": "<model>#<effort>",
      "base": "<branch>", "promptFile": "fix-guide-typo.md", "scope": ["docs/guide/"],
      "verify": ["<build command>", "<test command>"], "timeoutMinutes": 60 }
  ]
}
```

- `kind`: `code`, `review`, `research`, `mechanical` or `structured`. Each task names a `route` or an
  `agent`; the harness never uses a backend's default model. Routes: opencode
  `<provider>/<model>[#<variant>]`, Codex `<model>[#<effort>]`.
- `scope` is required for `code` and `mechanical`; two such tasks of one run never share a path
  (`sharedPaths` exempts a path from that overlap check only). A change outside `scope` fails the task.
- `structured` carries `input` (items, id field) and `result` (a JSON Schema in the harness's keyword
  subset, identity, immutable fields); it never commits or verifies.
- Write the task file with an editor tool, not a shell heredoc. Every field: `references/task-file.md`.

## Run lifecycle

1. **Plan.** `plan <tasks.jsonc>` validates the file, contracts, allow-list and live catalog.
2. **Run.** `run <tasks.jsonc> [--run <name>]` is the supervisor: it blocks until every task is
   final. To keep working meanwhile, start it from a background shell and watch from another one.
3. **Watch.** `status <run>` shows each task's state, tool count and last tool call. States:
   `queued`, `dispatching`, `running`, `waiting` (escalated ask), `settling`, `interrupted`, and the
   final `done`, `failed`, `cancelled`.
4. **Answer.** `asks [<run>]` lists escalated asks; `reply <run> <askId> once|reject [--message <text>]`
   answers one. Answers are one-shot; the harness never grants a standing approval.
5. **Stop.** `cancel <run> <task>`. Never kill the supervisor.
6. **Read.** Each task has `result.json` and `final.md`; a code task adds `diff.patch` and
   `verify.log`, a structured task `deliverable.json` instead. These are the evidence; the worker's
   final text is not. Layout: `references/ledger.md`.
7. **Clean.** After the work is merged, `clean <run>` removes worktrees, branches, temporary folders
   and backend sessions; unmerged or dirty work stays unless `--force`.

## Exit codes

| Code | `run` |
|---|---|
| 0 | Every task done: verify green, changes in scope, structured deliverable valid |
| 1 | A task failed, was cancelled, is out of scope, or is not final; or the run aborted after it started |
| 2 | Nothing started: missing tool, signed-out backend, invalid task file, existing run id, usage error |

## Recover, never rerun

A recorded run is never run again and its ledger is never edited by hand.

- The supervisor stopped (restart, killed process): run `resume <run>` once. It settles what ended
  and settles every started Codex task that is not final as `lost`; `resume <run> --continue` also
  re-prompts interrupted opencode tasks and starts the tasks that never started.
- A task without a result while no supervisor holds the run: `collect <run> <task>`.
- Only what collect cannot recover goes into a new task file, under a new run id.

## Traps

- **Machine-wide state.** Environment isolation does not cover the Windows registry, scheduled
  tasks or the credential store. A new `DOTNET_CLI_HOME` makes the .NET SDK append a tools folder
  to the user PATH in the registry and may install an HTTPS development certificate; the harness
  switches both off for its workers, `selfcheck` warns on a long user PATH and `run` refuses a very
  long one. Do not point `DOTNET_CLI_HOME`, `CODEX_HOME` or `XDG_DATA_HOME` at new folders yourself:
  the backends' sign-in lives in their homes.
- **Inherited Codex rules.** Codex's app-server loads exec-policy rules it cannot be told to
  ignore: an `allow` rule can run a matching command outside the sandbox without asking. `profile:
  clean` drops your own rules; system-level rules always load. `plan` and `result.json`
  (`capabilityNotes`) state what still applies to each read-only Codex task.
- **Approval gates in the repository.** A worker that meets a rule asking for approval before any
  change hands the task back: `done`, empty diff, a final text asking for a go. The fix is in
  `delegating-to-worker-models`.
- **Read-only planning.** A read-only task cannot write a plan file: return the plan in the final
  text, or run with `access: workspace` and `scope` set to the plan path.
- **Long single-line inputs.** Worker read tools truncate them; give Markdown or pretty-printed JSON.
- **Windows paths.** Deep repositories exceed the path limit in worktrees: set
  `locations.worktreeRoot` to a short folder or turn on git's `core.longpaths`.
- **Shell deny rules match command text only.** Treat them as a guard against mistakes.

## References

Read on demand: `references/task-file.md` (every field, default and validation error),
`references/cli.md` (each command's arguments, output and exit codes), `references/ledger.md` (run
folder, event types, file fields), `references/config.md` (layers, policy rules, allow-list,
locations), `references/backends.md` (how each mode, rule and limit maps onto opencode and Codex).
