Read with this skill's `SKILL.md`; this file is part of its instructions. These instructions tell
you to call the Workflow tool: invoking this skill is the user's opt-in for this task.

## Launching a run

Call `Workflow` with the script inline in `script`. Its inputs are `script`, `name`, or
`scriptPath` for the script, `args` for its input, and `resumeFromRunId` to resume a run.
`resumeFromRunId` works only in the same session, or in a session reopened with `claude --resume`.

A run takes no user input. Ask every sign-off and destructive-step question in chat before the
launch, and use one run for each phase that needs a sign-off.

The tool returns at once with a task id, and completion arrives later as a task notification. Do
not poll. Record the runId, the scriptPath, and the transcript directory in the ledger.

## Mapping the method onto the script

- **Waves.** Feed the items to `pipeline()` in batches no larger than the ceiling, and await each
  batch before the next. Inside a batch, an item's producer and its skeptics run one after the
  other, so the pending `agent()` calls never pass the ceiling. The harness limit is the runtime's
  own concurrency limit: `min(16, max(2, CPUs - 2))` agents by default, or the value of
  `CLAUDE_CODE_WORKFLOW_MAX_CONCURRENT_AGENTS`. Use `parallel()` only for a barrier that needs
  every result: a dedupe, an early exit on zero findings, a ranking.
- **Briefs.** A brief is the `agent()` prompt, with `label` set to the item id and `phase` set to
  the wave.
- **Reports.** The report is a `schema` with the report's fields. A schema result gets 5
  validation attempts.
- **Agent type.** Every item uses the default workflow agent: set no `agentType`. Explore cannot
  write its results file, and it starts without the project's `CLAUDE.md`. The read-only rule
  lives in the brief.
- **Writers.** Every writer works in the main tree: set no `isolation`. A worktree is removed only
  when its agent changed nothing, and no child commits, so a writer's edits there would never reach
  the tree the gate checks. Writers on disjoint write sets may share a batch; overlapping writers go
  in separate batches, so they run in sequence.
- **Failed children.** `agent()` returns `null` when an agent is stopped or ends in an error, and
  `parallel()` and `pipeline()` keep that `null`. Mark every `null` FAILED under its item id before
  any filter. Never use `.filter(Boolean)` on results.

## The ledger

Put the ledger directory in the session scratchpad when the system prompt names one, and otherwise
in the system temp directory. A background agent's permission prompt appears in the main session.

The script cannot touch files, so the ledger's rows change only when the completion notification
arrives. Until then, each agent's results file is the record on disk.

## Commands the sandbox refuses

When the Bash sandbox is on, a confirmed sandbox denial can read `Operation not permitted` on
macOS, or `Read-only file system` on Linux and WSL2. A blocked network connection can instead look
like a name-resolution failure (`Could not resolve host`, `Temporary failure in name resolution`)
or `Network is unreachable`. Those texts also occur for ordinary DNS and network failures, so do not
treat every such failure as a sandbox denial. Escalate only a confirmed sandbox denial, once,
through the native permission flow: retry the refused command with `dangerouslyDisableSandbox`.
The retry goes through the permission flow, and a workflow agent's prompt appears in the main
session, so the user's answer is the approval. Do not disable the sandbox or change its
configuration. With `allowUnsandboxedCommands: false` there is no retry: the agent returns BLOCKED,
and the final report names the exact command for the user to run.

## Restart

1. In the same session, or in a session reopened with `claude --resume`: launch again with
   `resumeFromRunId` and the same `scriptPath`. Completed agents return their saved results. An
   agent that was still running cannot be continued: the relaunch starts it over from its prompt.
2. In a fresh session, or when a relaunch reports `nothing to resume`: rebuild the rows from
   `results/`. Then read `<transcript-dir>/journal.jsonl` for any agent that finished without a
   results file (the agent transcripts, `agent-<id>.jsonl`, when there is no journal), and launch
   a new run that skips the DONE items.

Before either launch, inspect `git status` and `git diff` on the write set of every write item that
is not DONE: the agent that stopped may have left work on disk.

## One authority

A running Workflow is the only orchestration authority: start no `Agent` fan-out beside it.

## When Workflow is not in your tool list

Workflows are turned off, or not yet turned on for the user's subscription (on Pro, the user turns
them on in the Dynamic workflows row of `/config`). Then:

1. Start the children as background `Agent` calls (`run_in_background: true`) on the default
   subagent type, at most the ceiling live. Overlapping writers run in sequence.
2. If those cannot start either, use the labeled sequential run in `SKILL.md`.

A "No" at the launch approval is not this case. It is a refusal: report it and stop that path.
