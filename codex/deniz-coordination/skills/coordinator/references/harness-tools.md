Read with this skill's `SKILL.md`; this file is part of its instructions.

## Workers

Implementation goes to worker models through the dispatch harness: load
`$deniz-coordination:harness` and follow it for task files, runs, and the ledger.

How `delegating-to-worker-models` maps onto the harness:

- The task file's `contracts` lists the absolute paths to the loaded delegation skill's
  `references/worker-contract.md` and then the worker file that `references/models/README.md` names
  for the task's model and role, taken from the loaded skill's folder. A model or role without a
  worker file gets only `worker-contract.md`.
- The record of the model that ran is `routeVerified: true` in the task's result; the family claim
  of a review rests on it for the author and the reviewer.
- A task with `prepare` gets, in the prompt's Environment section in place of the authorized
  preparation line: "Dependencies were prepared before you started. Build only with <offline build
  command> and test only with <offline test command>, inside the sandbox. Adding dependencies is out
  of scope; the sandbox already holds what the task needs."

## Your own subagents

Check your tool list for the multi-agent tools (`spawn_agent` and its companions) and use them for
judgement work: design options, research, critique, or review. Leave a child's model unset unless
the user names one; the configured default child model applies.

## Goal-driven runs

When the user asks for a goal-driven or unattended run, you may create a goal for it, so the run
continues until its acceptance is met. Without that request, create no goal.
