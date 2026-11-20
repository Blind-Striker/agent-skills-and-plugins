Read with this skill's `SKILL.md`. The user invoked this skill to request
subagents for the task.

## Launch and settle a wave

- Start each child with `invoke_subagent` and the full brief. Set `TypeName`
  to `self` for work that can edit files, or `research` for read-only work.
  For a custom role defined through `define_subagent`, keep `model: "inherit"`
  unless the user asks for a specific model.
- Keep the ledger and its Markdown checklist in the session's temporary
  directory. Record each item's child ID, state, and result there.
- Send a focused follow-up to a live child with `send_message`. Use
  `manage_subagents` when you need to inspect or manage a child, such as
  reconciling a RUNNING entry after a session interruption.
- Continue other independent work while children run. When a completion
  message wakes you, record the result and launch the next PENDING item
  within the wave limit. Use completion messages to drive the wave and
  avoid polling loops.
