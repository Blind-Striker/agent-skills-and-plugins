Read with this skill's `SKILL.md`; this file is part of its instructions.

## Workers

Implementation goes to worker models through the dispatch harness: load
`deniz-coordination:harness` and follow it for task files, runs, and the ledger.

How `delegating-to-worker-models` maps onto the harness:

- The record of the model that ran is `routeVerified: true` in the task's result; the family claim
  of a review rests on it for the author and the reviewer.
- A task with `prepare` gets, in the prompt's Environment section in place of the authorized
  preparation line: "Dependencies were prepared before you started. Build only with <offline build
  command> and test only with <offline test command>, inside the sandbox. Adding dependencies is out
  of scope; the sandbox already holds what the task needs."

## Your own family's subagents

Start them with `Agent` calls for judgement work: a `general-purpose` subagent for design options,
research, critique, or review, and `Explore` for wide searches. A Workflow run is for work the user
opted into.

## Sessions

`ListAgents` shows the other sessions and `SendMessage` addresses one by name. A message to a
session in another permission mode can be held until its user approves it, so a held message is not
a delivered one.
