Read with this skill's `SKILL.md`; this file is part of its instructions.

## Your own subagents

Check your tool list for the multi-agent tools (`spawn_agent` and its companions) and use them for
design options, research, critique, review, or a piece the configured default child model suits. Leave a
child's model unset unless the user names one; the configured default child model applies, and that
model is the child's record.

## The dispatch harness

The dispatch harness starts workers on OpenCode and Codex models from a task file, fences their
writes, and records the model that ran. Load `$deniz-coordination:harness` before your first task file:
its section on this skill maps each step here onto the task file and the ledger.
