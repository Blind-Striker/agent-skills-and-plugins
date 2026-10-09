# ADR-0005: The manifest states invocation intent; each emitter picks the mechanism

Date: 2026-10-08
Status: Accepted

## Context

A curated skill may be passive knowledge the model selects or a ceremony the user starts
deliberately. Claude Code expresses that distinction with skill frontmatter. OpenCode 2 can withhold
a skill from the list it offers the model while the user can still attach it, and Codex skills can
disable implicit selection while remaining explicitly addressable. The manifest needs one
harness-neutral statement of required and forbidden initiation capabilities rather than
target-specific keys or a promise that every harness exposes the same surface.

The native mechanisms differ in strength. OpenCode 2 skills are no longer slash commands: the user
attaches a skill by its ID. OpenCode 2 has no switch that makes a skill model-only, and its switch
that hides a skill from the model only stops advertising it; the skill tool still loads any
registered ID the model names.

## Decision

Each item has an optional `invocation` field with values `auto`, `manual`, or `both`. It states
initiation capability intent independently of resolved shape. A target that emits a native command
or agent may already provide only an explicit surface, while a target that adapts that same resolved
item to a skill still consumes the field.

| Value | Implicit/model initiation | Explicit/user initiation |
|---|---|---|
| `auto` | required | unspecified |
| `manual` | forbidden | required |
| `both` | required | required |

`auto` does not mean that explicit invocation must be impossible. It means the emitter must not
require a user ceremony before the model can use the item. `manual` is the strict boundary: a target
must not offer the item for implicit model selection and must provide an explicit user path. `both`
requires both paths. A capability marked unspecified may remain available when that is the target's
native artifact surface.

On Codex and OpenCode 2, `manual` means the same thing: the item is **not offered to the model** for
implicit selection, and the user invokes it explicitly. Codex sets `allow_implicit_invocation:
false`; OpenCode 2 keeps the item one skill, its native setting keeps it out of the skill list the
model is offered, and the user attaches it. If the model learns the ID some other way, the OpenCode
skill tool can still load it; a measured OpenCode 2 panel observed all three behaviors
([record](../../experiments/harness-invocation/records/2026-10-09-opencode2-manual-skill.md)).
Claude Code is stricter: `disable-model-invocation` blocks model invocation. Each harness's
native meaning is accepted as it is, so shipped text may name a `manual` item's ID, for example to
point the user at it. `auto` and `both` both emit one advertised skill that the user can also
attach; `both` adds no separate command.

**Absent is not a fourth value with a default meaning.** An item that says nothing is an item that
states no intent, and upstream's own invocation posture passes through: Claude Code keeps the
upstream frontmatter untouched, and another target with a native equivalent receives the same
posture in its own key. Stating a value replaces whatever upstream said — that is the point of stating it.

`as:` stays orthogonal. It is the **shape** dial of
[ADR-0006](0006-output-is-a-transformation.md) — what artifact the item becomes — while `invocation`
is the **initiation-capability** dial. Invocation never converts a skill into a command or agent.
An explicit `as: command` stays the per-item escape hatch for an item that truly needs a command
surface, such as a `/name` entry that takes arguments. Emitters translate the neutral intent into
their own native mechanism; the current mapping and authoring details belong in
[Transformation and emission](../architecture/transformation-and-emission.md) and
[`curation/SCHEMA.md`](../../curation/SCHEMA.md), rather than being repeated here.

Using `as: command` as the trigger dial was rejected because shape cannot express `both` and remains
a useful independent decision. The value names `model` and `user` were rejected because the author
decides which initiation capabilities are required, not how a particular emitter names the actor.
Defining `auto` as universally model-only was rejected because it adds an explicit-invocation
prohibition that is neither required for automatic use nor representable by every native skill
surface. Hand-writing harness invocation keys in `frontmatter:` was rejected because it silently
fails to carry the same intent to other emitters.

Emitting an OpenCode `manual` item as a command was rejected. OpenCode 2 attaches skills directly,
and a command copy needs either an inline body, which can strand skill-relative paths, or a stub that
reads a parked, non-discoverable body. Both turn an initiation decision into a shape change. A `both`
item emitted as a skill plus a duplicate command was rejected for the same reason. Making OpenCode
`manual` strictly forbidden was rejected because it needs skill permission rules in the user's
OpenCode configuration, and the installer does not change configuration. Whether such a rule also
blocks the user's explicit `@` attachment is disputed upstream and has changed between builds, so it
could also remove the explicit path `manual` requires.

## Consequences

- The manifest states initiation intent without making authors learn each emitter's mechanism.
- Absent must remain passthrough so intent can be adopted item by item. The cost is asymmetry:
  upstream posture may have no equivalent elsewhere, and an unstated item uses each target's own
  default after unsupported metadata is dropped and reported. These are emitter limits, not a reason
  to make absence a hidden default.
- `both` produces one skill on every harness. `auto` and `both` produce the same physical artifact
  on OpenCode 2 and Codex, whose native skills are always explicitly addressable. The result still
  satisfies both declarations because `auto` leaves that capability unspecified; target projections
  must show the resolved surface rather than imply a universal model-only guarantee.
- On Codex and OpenCode, `manual` withholds the item from implicit selection, and nothing
  documented denies a load: text that names a `manual` item, shipped or from the user, a project
  file, or a third-party skill, may still lead the model to it. No validation or plugin enforcement
  compensates for this. Reconsider it when OpenCode can deny model loading of one skill from the
  skill file itself.
- These OpenCode meanings hold on OpenCode 2 only. The accepted degradation under OpenCode 1, where
  `manual` skills become model-visible, is a consequence of [ADR-0002](0002-multi-harness-output.md).
- A `manual` item stays a skill on OpenCode, so its body, assets, and skill-relative paths resolve
  as they do in the other targets, with no parked body or stub command. The cost of a command-shaped
  body, including skill-relative paths that do not resolve from the command location, falls only on
  items that choose `as: command`. An item whose ceremony depends on command arguments loses that
  argument surface while it stays a skill; whether `as: command` is worth its cost is a per-item
  curation decision.
- One assembled body currently feeds all three harnesses; the overlay mechanism has no per-harness body
  ownership. That capability limit can make target-specific prose or paths costly, and remains
  visible rather than narrowing the accepted neutral intent.
