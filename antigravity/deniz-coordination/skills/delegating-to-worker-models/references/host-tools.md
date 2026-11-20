Read with this skill's `SKILL.md`.

## Native subagents

Use `invoke_subagent` with `TypeName: self` for full-capability work or
`TypeName: research` for read-only investigation. Give each child a complete
brief. Use `send_message` for follow-up instructions and `manage_subagents`
to inspect or manage existing children. For a custom role, use `define_subagent`
and keep its model set to `inherit` unless the user asks for a specific model.

## Dispatch harness

The dispatch harness starts workers on OpenCode and Codex models from a task
file, fences their writes, and records the model each worker used. Load
`deniz-coordination:harness` before writing the first task file. Its section
on this skill maps the brief, verification, and handoff onto the task file
and ledger.
