Read with this skill's `SKILL.md`; this file is part of its instructions.

## Workers

Implementation goes to worker models through the dispatch harness: load
`$deniz-coordination:harness` and follow it for task files, runs, and the ledger.

## Your own subagents

Check your tool list for the multi-agent tools (`spawn_agent` and its companions) and use them for
judgement work: design options, research, critique, or review. Leave a child's model unset unless
the user names one; the configured default child model applies.

## Goal-driven runs

When the user asks for a goal-driven or unattended run, you may create a goal for it, so the run
continues until its acceptance is met. Without that request, create no goal.
