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

Each item gains an optional `invocation` field with values `auto`, `manual` or `both`. It applies to
items emitted as skills; upstream command and agent components are user-invoked by nature and the
field is meaningless on them (`validate` warns if it is set).

**Absent is not a fourth value with a default meaning.** An item that says nothing is an item that
states no intent, and upstream's own frontmatter passes through untouched. Stating a value replaces
whatever upstream said — that is the point of stating it.

Each emitter derives its own mechanism:

| `invocation` | Claude Code | OpenCode |
|---|---|---|
| *(absent)* | upstream's frontmatter, untouched | `skills/` |
| `auto` | skill, `user-invocable: false` | `skills/` |
| `manual` | skill, `disable-model-invocation: true` | `commands/` |
| `both` | skill, neither key set | `skills/` **and** `commands/` |

`as:` is untouched and stays orthogonal. It is the **shape** dial of
[ADR-0006](0006-output-is-a-transformation.md) — what artifact the item becomes — while `invocation`
is the **trigger** dial. The table above gives each invocation value its default shape per harness;
`as:` states the shape explicitly when that default is wrong.

### Alternatives considered

- **Keep `as: command` as the invocation dial.** Rejected *as the trigger dial* — it names what a
  file is, not who pulls the trigger, and cannot express `both`. It is not removed, because shape is
  a real second axis: converting an upstream skill into a command is a curation decision worth
  making on its own, and on OpenCode a command is a genuinely different artifact with its own
  frontmatter (`agent`, `model`, `subtask`, `template`) that a skill cannot carry.
- **Name the values `model` / `user` instead of `auto` / `manual`.** Rejected: `model`/`user` names
  the *actor*, which is the emitter's view of the problem. The manifest is read by the person
  deciding, and the decision they are actually making is whether the thing fires on its own or waits
  to be asked for. `auto`/`manual` says that in the words the decision is thought in, and stays true
  on a harness where the actor distinction is expressed by a different artifact rather than a flag.
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
- Suppressing model invocation rests on one harness key, and that bet has now been settled by
  measurement rather than documentation: on Claude Code 2.1.220 the flag leaves the user's own slash
  invocation working, and the model reports the skill as absent from the list it can name at all.
  The suppression is structural, so a `manual` item cannot be reached by the model whatever its
  description says.
- Nothing forces an item to declare intent, and silence changes nothing: an item without the field
  emits exactly what it emits today. That is why absent means passthrough rather than defaulting to
  `auto`. A default of `auto` would have made every curated skill model-only the moment the field
  landed — pulling them out of the `/` menu, and inverting upstream's stated intent wherever it had
  set `disable-model-invocation` itself. Adopting the field item by item is only possible if not
  adopting it is free.
- Passthrough is honest on the Claude side and lossy on the OpenCode one. Upstream's keys are
  meaningful to Claude Code and meaningless to OpenCode, so an item that states no intent arrives in
  OpenCode as a plain model-only skill whatever upstream wanted. Stating `manual` is the only way to
  say otherwise there — which is the asymmetry ADR-0006 axis 1 exists to name, showing up in the
  first field that has to cross it.
