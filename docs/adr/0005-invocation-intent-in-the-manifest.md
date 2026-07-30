# ADR-0005: The manifest states invocation intent; each emitter picks the mechanism

Date: 2026-07-30
Status: Accepted

## Context

A curated skill carries an intent that curation exists to express: some are background knowledge the
model should reach for on its own, and some are ceremonies the user wants to start deliberately —
a plan, a review, a debugging protocol — precisely because they are expensive and shouldn't fire on
a guess.

The two target harnesses express that intent incompatibly. Claude Code treats skills and commands as
one mechanism and puts the dial in frontmatter: `disable-model-invocation: true` for user-only,
`user-invocable: false` for model-only. OpenCode has no such dial, because its skills are
model-only by construction — a user cannot invoke one at all, and its skill loader ignores unknown
frontmatter, so Claude's keys reach the OpenCode tree and do nothing. There, the only user-invocable
artifact is a command. The mechanics are recorded in
[skill-invocation-across-harnesses.md](../research/skill-invocation-across-harnesses.md).

So the same intent is a flag on one artifact in one harness and a different artifact in the other.
The manifest today has no word for the intent at all — only `as: command|agent`, which names an
output shape.

## Decision

Each item gains an optional `invocation` field with values `model`, `user` or `both`, defaulting to
`model`. It applies to items emitted as skills; upstream command and agent components are
user-invoked by nature and the field is meaningless on them (`validate` warns if it is set).

Each emitter derives its own mechanism:

| `invocation` | Claude Code | OpenCode |
|---|---|---|
| `model` (default) | skill, `user-invocable: false` | `skill/` |
| `user` | skill, `disable-model-invocation: true` | `command/` |
| `both` | skill, neither key set | `skill/` **and** `command/` |

`as: agent` is unchanged. **`as: command` is removed**: it was the only way to say "the user starts
this", and it says it badly.

### Alternatives considered

- **Keep `as: command` as the dial.** Rejected: it names where a file goes, not who pulls the
  trigger. It cannot express `both`, and since Claude Code merged custom commands into skills the
  conversion buys nothing on that side — the same file under `commands/` and under `skills/`
  behaves identically. The path has never run on real data, so removing it costs nothing.
- **Set `disable-model-invocation` by hand through the existing `frontmatter:` override.** Rejected:
  it works for Claude Code only and silently no-ops for OpenCode, which is exactly the
  lowest-common-denominator failure ADR-0002 was written to avoid. It also puts a harness-specific
  key into a manifest that is meant to stay harness-neutral.

## Consequences

- The manifest reads as a statement of taste rather than of packaging: one word per item says who
  holds the trigger, and knowing the per-harness rules stops being the author's problem.
- The OpenCode tree stops being a mirror of the Claude one. Today `opencode/skills/` is byte-identical
  to the built plugin skills; once an item is `user`, the two harnesses receive different artifacts.
  Reasoning about the build now requires reading both emitters, and `validate`'s duplicated findings
  across the two trees no longer duplicate uniformly.
- `both` emits the same item twice into OpenCode. Output names are already required to be unique
  across all plugins (ADR-0002), and a skill and a command sharing one name is a new collision class
  `validate` has to cover.
- Suppressing model invocation is a bet on one harness key. Claude Code has an open report that
  `disable-model-invocation: true` also blocks the user's own slash invocation; until that is checked
  against a live install, `invocation: user` is unproven on the Claude side.
- Nothing forces an item to declare intent. The `model` default reproduces today's behaviour, so
  curation can adopt the field item by item rather than in one pass.
