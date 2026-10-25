---
name: harness
description: The bundled dispatch harness, which runs a task file on worker
  models through OpenCode or Codex, fences their writes, escalates their asks to
  the operator, settles each result from git and its verify commands, and
  records every step in a ledger. Its commands, task file, modes and write
  fence, configuration, runs and their recovery, ledger and exit codes.
---

# Dispatch harness

The harness runs tasks on worker models out of process, through two equal backends, opencode and
Codex. Per task it prepares a workspace, starts the worker behind a write fence where the platform
has one, answers or escalates its asks, settles the result from git, runs the verify commands on
that result, and records every step in a ledger. This skill is the harness's contract.

## Boundary

- This skill says how to run a task. What to hand out and how to judge the result belong to the caller.
- The harness prepends only the contracts you name (`contracts`, `--contract`); `plan` warns on a code or
  mechanical task that names no worker contract.
- A calling skill that sends `structured` tasks owns their prompt and result schema.
- Use `prepare` for outside-sandbox dependency steps; recipes and limits: [Dependencies and the sandbox](references/guide.md#dependencies-and-the-sandbox).

## Invocation

From this skill's folder (its `global.json` selects the SDK), build, then run the command:

```text
dotnet build dispatch.cs
dotnet dispatch.cs -- <command> [arguments]
```

- Build before every command. A non-zero build exit means nothing started; fix the error or retry
  when two builds collided. The `--` keeps `dotnet` from taking flags such as `--force`.
- Paths on the command line are absolute (task file, `--repo`, `--contract`, `--ledger`); paths in a
  task file are relative to that file, except `dir` (relative to the repository root). `--contract` also accepts bundled aliases
  `@worker-contract` and `@<model>-worker`; semantics: `references/task-file.md#contracts`.
  In PowerShell use `$PWD.ProviderPath`.
- Run `selfcheck [--repo <path>]` on a new machine, after an update, and in a new repository. It
  checks each backend the configuration selects, or every installed one when none is selected
  (version, sign-in, catalog, a clean profile), Windows readiness and every resolved folder, and sends
  no prompt. Discovery and exit behavior: `references/cli.md`.
- Commands: `selfcheck`, `routes`, `plan`, `run`, `status`, `asks`, `reply`, `cancel`, `collect`,
  `resume`, `clean`. Arguments, output and exit codes of each: `references/cli.md`.

## Backend and modes

Codex provides an OS sandbox; opencode's Linux/macOS fence is an
[accident guard](references/backends.md#opencode-accident-guard), not a security boundary;
Windows runs unfenced. Both support structured output. Modes and defaults:

| Setting | Values | Code, mechanical | Review, research | Structured |
|---|---|---|---|---|
| `profile` | `clean` (minimal profile), `full` (installed profile) | code `full`; mechanical `clean` | `clean` | `clean` |
| `project` | `on` (repository context loads), `off` | `on` | `on` | `off` |
| `access` | `read-only`, `workspace`, `full` | `workspace` | `read-only` | `read-only` |
| `workspace` | `worktree` (own branch from `base`), `in-place` (`dir`) | `worktree` | `worktree` | scratch folder |

- A read-only Codex task of any kind also defaults to `profile: clean`; explicit profiles win. A task
  that needs the installed profile's tools (MCP servers, plugins, skills) sets `profile: full`. Codex `access: full` removes
  its sandbox. opencode `read-only` denies edit and keeps the shell inside the fence; on Windows it also
  declines every shell call by default.
- `workspace: in-place` runs in `dir`, main checkout included. `commit: none` (default) records the
  diff without committing; `commit: harness` requires a clean tree and commits on the current branch.
- Read-only writes are declined. For escalated shell asks, `readOnlyShell: decline` (default) rejects;
  `escalate` lets a matching rule reach the operator. Reply `once` permits one command, outside
  the sandbox on Codex.
- Denies beat escalation. Both backends decline unmatched asks in read-only tasks, MCP tools included.
  Otherwise opencode answers `once`; Codex declines with feedback. Codex denies apply only at
  approval: with `workspace` and network on, a `git push` deny cannot stop a push.
- An unfenced opencode shell can write the harness's control folder and run ledger. Build caches a
  fenced or sandboxed task must write go in `writableRoots` (`references/config.md`). Verify commands
  run the worker's code as your user, outside sandboxing, on every backend.
- Preferences layer user config < repository `.dispatch/` < task file < command line. Restrictions
  (denies, scope, maximum access, network) only add up, and the route allow-list is the
  intersection of the layers that have one. Details: `references/config.md`.

## Task file

```jsonc
{
  "schemaVersion": 2,
  "contracts": ["@worker-contract", "@<model>-worker"],
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
   A background run wakes you only through a waiter you arm in the same step: your tool's
   completion notice for that shell, or a loop on `status`. When you come back to a run without one,
   start with `status <run>`; `(no supervisor: …)` means nothing is driving it.
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

- `status <run>` tells a stalled run from a working one: `(supervised now)` while a supervisor
  drives it, `(no supervisor: resume <run> settles it)` once none does. Check it whenever a run's
  completion notice is late or the host killed the shell that waited for it.
- The supervisor stopped (restart, killed process): run `resume <run>` once. It settles what ended
  and settles every started Codex task that is not final as `lost`; fenced opencode tasks whose
  server is gone are also lost, never reconstructed. `resume <run> --continue` re-prompts interrupted
  unfenced opencode tasks and starts the tasks that never started.
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
  change returns `done` with an empty diff. The fix belongs in that repository: scope its approval
  rules to the session that talks to the user; a dispatched worker runs the task it was given.
- **Read-only planning.** A read-only task cannot write a plan file: return the plan in the final
  text, or run it as a `code` task with `access: workspace` and `scope` set to the plan path (any change
  by a `review` or `research` task fails scope).
- **Long single-line inputs.** Worker read tools truncate them; give Markdown or pretty-printed JSON.
- **Windows paths.** Deep repositories pass the path limit in worktrees: see `references/config.md`.
- **Shell deny rules match command text only.** Treat them as a guard against mistakes.

## References

Read on demand: `references/guide.md` (which setting to choose for a task, why, and what a result
proves), `references/task-file.md` (every field, default, validation error), `references/cli.md`
(arguments, output and exit codes), `references/ledger.md` (run folder, events, fields),
`references/config.md` (layers, rules, allow-list, locations), `references/backends.md` (each mode,
rule and limit on opencode and Codex).
