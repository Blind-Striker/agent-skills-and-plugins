Read with this skill's `SKILL.md`; this file is part of its instructions.

## Workers

Implementation goes to worker models through the dispatch harness: load
`deniz-coordination:harness` and follow it for task files, runs, and the ledger.

## Your own family's subagents

Start them with `Agent` calls for judgement work: a `general-purpose` subagent for design options,
research, critique, or review, and `Explore` for wide searches. A Workflow run is for work the user
opted into.

## Sessions

`ListAgents` shows the other sessions and `SendMessage` addresses one by name. A message to a
session in another permission mode can be held until its user approves it, so a held message is not
a delivered one.
