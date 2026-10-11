# OpenCode 2 Tool Notes

Skills here already name OpenCode's own tools: the `skill` tool loads a
skill, and `Subagent (general)` marks a subagent dispatch. These notes
cover what those words leave out. When this file disagrees with your
actual tool list or with the `subagent` tool's own description, trust the
tool.

## Dispatch

- `Subagent (general):` means one `subagent` call with `agent: "general"`,
  a 3-5 word `description`, and the filled template as `prompt`. Choose
  another agent only from the "Available subagents" list in the `subagent`
  tool's description. `explore` is the search agent; it cannot edit files.
- A call without `sessionID` starts a new child session with an empty
  context. Put everything the child needs into `prompt`. OpenCode cannot
  copy your conversation into a child.
- Several `subagent` calls in one response run at the same time. One call
  per response runs them one after another.
- `general` and `explore` cannot dispatch subagents. `general` cannot ask
  the user a question, so a child that lacks information must say so in
  its report.

## Model and variant

- `model` is `"providerID/modelID"` or `"providerID/modelID#variant"`. The
  variant selects one of the model's presets, usually its reasoning
  effort. An unknown model fails the call. An unknown variant fails the
  call, and the error lists the variants that exist. Look up exact IDs
  with the `models` tool.
- Without `model`, the child runs the agent's configured model. If the
  agent has none, the child runs your session's model and variant. A
  `model` without `#variant` runs that model's default variant, not yours,
  so always write the variant.
- The `subagent` description says to set `model` only when the user asks
  for one. A loaded skill's model-selection rules count as that request.
  If your human partner has given you model routing rules, they decide the
  value.

## Fix rounds

- Each result names the child's `sessionID`. A finished child returns
  `<subagent sessionID="..." state="completed">`, and a failure or a
  cancellation names the `sessionID` too. Record it: it is the
  implementer's identity.
- To resume an implementer, call `subagent` again with its `sessionID`,
  the same `agent`, and only the open findings as `prompt`. The child keeps
  its context and its model. This works when the child is idle and when it
  is still running, but only for children of your own session.
- A `sessionID` with a different `agent` switches that child to the new
  agent and, if that agent has a configured model, to that model. For a
  fresh implementer, leave out `sessionID`.

## Waiting

- A foreground call (the default) returns when the child finishes. If you
  are interrupted during that call, the child is cancelled.
- `background: true` returns at once with the `sessionID`. When the child
  ends, a `<subagent sessionID="..." state="..." description="...">`
  message arrives in your session and starts a new turn if you were idle.
  Background children also report after a server restart.
- No tool waits for, polls, lists, or closes a child. Do not sleep or
  poll. Work on something that does not touch the child's files, or tell
  your human partner what you launched and end your response.
- If a foreground call returns "The subagent is working in the
  background", the user moved it to the background. Treat it as a
  background child.
- A finished child stays available: continue it by `sessionID` at any
  time.

## Nesting

`experimental.subagent_depth` in the OpenCode config limits nesting. The
default is 1: only the top-level session can dispatch, and a `subagent`
call from a child fails with "Subagent depth limit reached". The value 0
turns dispatch off for every session; if your own call fails with that
error, you have no subagent tool.

## Running a whole plan one layer down

Claude Code can run subagent-driven development inside ONE orchestrator
subagent on a cheaper model. On OpenCode this needs your human partner's
configuration first: `experimental.subagent_depth` of 2 or more, and an
agent in subagent mode that is allowed to call `subagent` (`general` and
`explore` are not). Do it only when your human partner asks for it and
names that agent. Dispatch the orchestrator in the foreground with the plan path and
the instruction to use subagent-driven development end to end. Its final
message must carry the "Rulings I made" list verbatim. Relay that list; do
not summarize it. Do this only for a whole plan, never for one task.

## Todos

OpenCode 2 has no todo tool. The OpenCode 1 `todowrite` tool was removed:
never call it. Track a skill's checklist in Markdown instead: in the ledger
or plan file the skill already keeps, or, when it keeps none, in a visible
Markdown checklist whose lines you tick as each is done.

## Step limits

An agent with a `steps` limit loses its tools at that step and must reply
with a "maximum steps reached" summary. A child that returns that summary
has not finished its task: handle it as BLOCKED.
