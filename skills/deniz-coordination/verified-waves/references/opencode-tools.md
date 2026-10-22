Read with this skill's `SKILL.md`; this file is part of its instructions.

## Starting a child

Call `subagent` with these inputs:

- `agent`: the agent the user's configuration or instructions name for delegated work, such as a
  routing agent that maps to a model; with none named, `general`. It must allow edit and write:
  `explore` denies both, so it cannot write its results file. The read-only rule lives in the brief.
- `description`: 3–5 words that start with the item id.
- `prompt`: the brief.
- `background`: `true`.
- `model`: leave it unset unless the user asks for one. The agent's own configuration picks the
  model; this skill never does.
- `sessionID`: set it only to continue a child of this session.

A `general` child cannot ask the user or start children of its own. A child that lacks information
returns BLOCKED.

Record each child's `sessionID` as its handle in the ledger.

## Running a wave

OpenCode has no concurrency cap, so hold to the ceiling yourself: launch at most as many children
as the ceiling. Then do work that does not overlap theirs, or say what you launched and end the
turn. When one completes, record it and launch the next PENDING item in that same turn: a
completion is not a check-in, so never ask the user whether to go on. Never sleep, poll, or ask a
child for its status.

Depth is `experimental.subagent_depth`, default 1, so a child cannot start children.

## Completion

Completion is a `<subagent sessionID state description>` message that holds the child's whole final
text. The states `error` and `cancelled` are FAILED. So is a `completed` message whose text is
"Subagent completed without a text response." or holds no `REPORT` block.

## Questions

The `question` tool returns when the user answers, and also when the user dismisses it. A
dismissal, or a missing answer, is not approval. For a yes/no question from `SKILL.md`, do not
pass the gate, do not treat dismissal as yes, and do not ask it again. End the turn and wait for
an explicit answer. A question asked in text ends your turn.

## Commands the sandbox refuses

OpenCode has no command sandbox: a shell command runs once its permission check passes. A `deny`
rule, or a rejected permission prompt, is a permission denial, not a sandbox refusal: the child
returns BLOCKED with the command, and `SKILL.md` says how to handle a permission denial.

## Fixes and verifiers

A fix goes to the same `sessionID` and agent, with only the open findings. A verifier is always a
new child, started without a `sessionID`. Restart step 4 continues a RUNNING item through its
recorded `sessionID` only within the live session.

## Restart

After a session restart, a RUNNING item's old child cannot be reached: from a new session,
`subagent` refuses its `sessionID` as not a child of the current session. A server restart ends
the child's run, so do not wait for it to report. After either restart, inspect the item's write
set with `git status` and `git diff`, then dispatch it again as a new child, without a
`sessionID`.

## The ledger

Put the ledger directory in the system temp directory. A path outside the project asks for
`external_directory` permission, for you and for each child that writes its results file. Tell the
user before the first write that an "always" answer for the ledger directory covers the children's
writes too.
