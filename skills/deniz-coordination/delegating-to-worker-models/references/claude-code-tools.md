Read with this skill's `SKILL.md`; this file is part of its instructions.

## Your own subagents

`Agent` starts a Claude subagent: a `general-purpose` one for design options, research, critique,
review, or a piece you give to Claude, and `Explore` for wide searches. Its family is Claude, and the
model it ran is the one you passed or, with none passed, your own. A Workflow run is for work the user
opted into.

## Sessions

`ListAgents` shows the other sessions and `SendMessage` addresses one by name. A message to a
session in another permission mode can be held until its user approves it, so a held message is not
a delivered one.

## The dispatch harness

The dispatch harness starts workers on OpenCode and Codex models from a task file, fences their
writes, and records the model that ran. Load `deniz-coordination:harness` before your first task file:
its section on this skill maps each step here onto the task file and the ledger.
