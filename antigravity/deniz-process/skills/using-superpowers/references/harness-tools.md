# Antigravity Tool Notes

Use the tools available in your session. Antigravity CLI and the T3 Antigravity
provider can expose different file tools.

## Subagents

- Send each brief through `invoke_subagent`. Set `TypeName` to `self` for
  full-capability work or `research` for read-only investigation. Give each
  child its task, file scope, and required checks.
- Use `define_subagent` when the brief calls for a custom role.
- Use `send_message` to send instructions to an existing child and
  `manage_subagents` to inspect or manage children.

## Task tracking

Antigravity has no todo tool. Keep a visible Markdown checklist and tick each
line as you finish it. To save it as a task artifact, use `write_file` in the
CLI or `write_to_file` in T3 with `ArtifactMetadata` as described by the
tool in that session. `manage_task` manages background processes.
