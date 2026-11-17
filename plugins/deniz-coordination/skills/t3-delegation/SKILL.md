---
name: t3-delegation
description: Use when you hand work to other agents inside T3 Code, through
  `delegate_task` children, lane threads, or another machine.
---

# Delegating in T3 Code

T3 Code hosts agent sessions on several machines, and its tools start children and lanes on every
provider and model it lists. Its own orchestration instructions sit in your system prompt; where they
and this file differ, T3's win. This file maps the steps of
`deniz-coordination:delegating-to-worker-models` onto T3.

## Start a worker

- `orchestrator_capabilities` lists the providers, models and options `delegate_task` reaches; its
  `search` finds an exact id, and `environmentId` asks another machine. A listed model is a candidate
  until one small read-only child on that route has run.
- One `delegate_task` child per bounded task: an implementation, a fix-up pass, a contradiction scan, a
  review, a research read. `target` names the provider, the model and its options (effort or variant);
  `target.environmentId` with that machine's `projectId` runs it on another machine.
- An implementation child runs with `isolation: "worktree"`; a read-only scan or review runs in the
  shared checkout.

## The brief

A child starts with only its task text, and T3 prepends nothing. Its task text holds, in this order:

1. The worker contract, verbatim, then the worker block a loaded profile names for the model and role.
2. The base: `git checkout --detach <BASE_SHA>`, and "if HEAD is not <BASE_SHA>, stop and report". A
   worktree starts from the parent checkout's HEAD, which can be behind the base.
3. The per-task fill-in from the craft's prompt template.

## Settle from git

A T3 child writes with its runtime mode's rights, and no fence holds it to the task's scope. Settle
each implementation child from git: compare `git diff --stat <BASE_SHA>..<child head>` with the scope
line, read the diff, and rerun the verify commands yourself on its committed tree.

## The model that ran

1. When the child's run reports an `executedModel`, that is the record.
2. When it reports none and the child ran on OpenCode, read the record from that machine's OpenCode
   database, `~/.local/share/opencode/opencode.db`, opened read-only (`file:<path>?mode=ro`): in table
   `session_v2`, the row whose `title` is the child's task title and whose `directory` is its
   checkout; its `model` column holds `id`, `providerID` and `variant`, and its `cost` column the
   session's cost. Table `session_message`, rows whose `type` is `assistant`, carries the same `model`
   per reply in its `data` column. Read those two tables only.
3. Otherwise the model that ran is unverified. The report says so, and a review of that work counts
   as a review by an unknown family.

The same `session_message` rows hold the child's usage: each `assistant` row whose `data` has `tokens`
is one model call, with `tokens.input` (fresh input, cache excluded), `tokens.cache.read`,
`tokens.output` and `tokens.reasoning`. A request option such as a service tier is recorded as the
reply reports it, not as the route asked for it.

## Continue, restart and clean up

- A stopped child: start a new child whose brief names the stop, takes the stopped child's head as its
  base, and carries the amendment.
- A stop of your own turn stops your children too: relaunch the ones still needed.
- After a restart of the machine, of T3, or of the OpenCode service, T3 can show a child as cancelled
  while its OpenCode session still runs: a `session_message` row of `type` `synthetic` whose
  `data.metadata.notice` is `restart`, followed by `assistant` rows after T3's cancel, shows it. Before
  you start a replacement, have the service's owner ask it `GET /api/session/active`, which needs the
  service's credential; a session listed there still runs. An empty list does not prove that none runs
  on another OpenCode server of that machine. The owner ends a running one with
  `POST /api/session/<id>/interrupt`, which also ends its tool processes, so no process kill is needed.
- A settled child's worktree stays until you remove it, and it can be large (a checkout plus a private
  submodule store). Once its work is landed, or it left no commits and a clean working tree, remove it
  with `git worktree remove <path>`, adding `--force` only after that check, because a worktree with
  initialized submodules needs it. Deleting the folder alone leaves its store until
  `git worktree prune`. One that holds work that is uncommitted or not landed stays, and its removal is
  a reserved action.

## Lanes and other machines

- A lane is a T3 thread (`t3_thread_launch`) for a stream with its own queue, machine or repository,
  opened when the user asked for lanes; one bounded task is a child.
- A lane reads pushes, merges and CI itself and checks back on its own schedule (`schedule_task`),
  so its next step never waits on you as a relay.
- A child runs in its parent thread's project. Work in a second repository goes to a lane there, or to
  a child that edits a clean checkout of it by path.
- A send reaches another machine only over a full-access link, and only a thread whose runtime mode is
  no broader than yours. When a send is refused, the sender ends its turn with its report and the other
  side reads its thread.
- A message to another session carries your own `environmentId` and `threadId` (`t3_thread_list` shows
  `currentThreadId`) and asks it to reply with `t3_thread_send`. Reading its thread instead is the
  fallback only after a refused send. A session that `ListAgents` does not list may still be a T3
  thread: try `t3_thread_send` before calling it unreachable.
- A send to another machine's thread names that machine's `environmentId`. A continuation sent to a
  child on another machine does not wake it: read its status. Right after that machine's T3 restarts,
  a send can answer "thread not found" once.
- `t3_thread_read` pages from the start of a thread, so the first page of a long lane thread is old:
  to read its latest items, start near its `itemCount` with `afterPosition`.
- T3's tools can drop and come back mid-session: load them again before you conclude they are gone.
- Every message to an idle lane starts a paid turn: batch what you send.
