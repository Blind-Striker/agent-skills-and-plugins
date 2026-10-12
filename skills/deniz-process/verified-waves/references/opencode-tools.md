Read with this skill's `SKILL.md`; this file is part of its instructions.

## Starting a child

Call `subagent` with these inputs:

- `agent`: `general` for every item. `explore` denies edit and write, so it cannot write its
  results file. The read-only rule lives in the brief.
- `description`: 3–5 words that start with the item id.
- `prompt`: the brief.
- `background`: `true`.
- `model`: leave it unset unless the user asks for one.
- `sessionID`: set it only to continue a child of this session.

A `general` child cannot ask the user or start children of its own. A child that lacks information
returns BLOCKED.

Record each child's `sessionID` as its handle in the ledger.

## Running a wave

OpenCode has no concurrency cap, so hold to the ceiling yourself: launch at most as many children
as the ceiling. Then do work that does not overlap theirs, or say what you launched and end the
turn. When one completes, launch the next PENDING item. Never sleep, poll, or ask a child for its
status.

Depth is `experimental.subagent_depth`, default 1, so a child cannot start children.

## Completion

Completion is a `<subagent sessionID state description>` message that holds the child's whole final
text. The states `error` and `cancelled` are FAILED. So is a `completed` message whose text is
"Subagent completed without a text response." or holds no `REPORT` block.

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
