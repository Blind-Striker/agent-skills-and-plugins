Read with this skill's `SKILL.md`; this file is part of its instructions.

The user invoked this skill to request sub-agents for this task.

## Which multi-agent version you have

Check your tool list, and trust it over this file. If it has `tool_search` but no `spawn_agent`, the
multi-agent tools are deferred: call `tool_search` for `spawn_agent`, `wait_agent`, and
`close_agent` first, then check the version on the tools that load.

- **V1** has `close_agent` and `resume_agent`.
- **V2** has `followup_task`, `send_message`, `interrupt_agent`, and `list_agents`, and no
  `close_agent`.

## V1

- Start a child with `spawn_agent`: the brief as `message`, and `fork_context` unset, so the brief
  stands alone. Set `agent_type` (`explorer` for a read item, `worker` for a write item) only when
  `spawn_agent` lists it.
- Run a rolling wave with at most min(6, thread limit) children live:
  1. Call `wait_agent` with `targets` set to every live id and `timeout_ms` 300000.
  2. Record the first finisher in the ledger.
  3. Call `close_agent` on it: a finished agent holds its slot until it is closed.
  4. Spawn the next PENDING item.
- `send_input` sends a correction to the same child. `resume_agent` reopens a closed child, and
  restart step 4 uses it.
- V1 allows 6 threads and depth 1 by default, set under `[agents]`.

## V2

- `spawn_agent` needs `task_name`, set to the item id, and `message`, set to the brief. Add
  `fork_turns: "none"`, because the default copies your whole transcript into the child.
- There is no `close_agent` and no `resume_agent`.
- `wait_agent` wakes on activity, and a child's final text arrives with your next turn.
- Corrections go through `followup_task` with `target` set to the item's `task_name`. Restart step
  4 continues a RUNNING item the same way.
- Expect 3 children live by default.

## Both versions

- A spawn that fails on the thread limit waits for a finisher. It is not an error to report.
- An error result is FAILED under its item id.
- This skill's model rule: for the children it spawns, leave `model` and `reasoning_effort` unset
  unless the user asks. Model routing belongs in the user's `[agents] default_subagent_model`, not
  in this skill.
- The explorer role adds no read-only restriction, so the brief carries it. Its rule to trust
  explorer results does not apply here: an explorer report is input, and it goes through
  verification.
- Every writer's brief carries the coordination line, `[writers]` in the brief template.
- Create no goal unless the user asks for unattended continuation.

## The ledger

`/tmp` and `$TMPDIR` are writable in the default sandbox, so put the ledger directory there. On
Windows, use the system temp directory, and report it if a write is refused. Never use `.git`: the
default sandbox keeps it read-only.
