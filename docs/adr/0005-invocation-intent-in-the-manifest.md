# ADR-0005: The manifest states invocation intent; each emitter picks the mechanism

Date: 2026-09-07
Status: Accepted

## Context

A curated skill may be passive knowledge the model selects or a ceremony the user starts
deliberately. Claude Code expresses that distinction with skill frontmatter, OpenCode can express it
through artifact shape, and Codex skills can disable implicit selection while remaining explicitly
addressable. The manifest needs one harness-neutral statement of required and forbidden initiation
capabilities rather than target-specific keys or a promise that every harness exposes the same
surface.

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
must prevent implicit model selection and provide an explicit user path. `both` requires both paths.
A capability marked unspecified may remain available when that is the target's native artifact
surface.

**Absent is not a fourth value with a default meaning.** An item that says nothing is an item that
states no intent, and upstream's own frontmatter passes through untouched. Stating a value replaces
whatever upstream said — that is the point of stating it.

`as:` stays orthogonal. It is the **shape** dial of
[ADR-0006](0006-output-is-a-transformation.md) — what artifact the item becomes — while `invocation`
is the **initiation-capability** dial. Emitters translate that neutral intent into their own native
mechanism; the current mapping and authoring details belong in
[Transformation and emission](../architecture/transformation-and-emission.md) and
[`curation/SCHEMA.md`](../../curation/SCHEMA.md), rather than being repeated here.

Where a manual OpenCode conversion has bundled files, its command stub is global-only and targets the
installed global OpenCode configuration root. The exact root resolution and parked-body mechanics
belong in [Transformation and emission](../architecture/transformation-and-emission.md). It does not
name or support a project-local mount. Project-local mounts observed in experiment history remain
evidence, not product support.

Using `as: command` as the trigger dial was rejected because shape cannot express `both` and remains
a useful independent decision. The value names `model` and `user` were rejected because the author
decides which initiation capabilities are required, not how a particular emitter names the actor.
Defining `auto` as universally model-only was rejected because it adds an explicit-invocation
prohibition that is neither required for automatic use nor representable by every native skill
surface. Hand-writing harness invocation keys in `frontmatter:` was rejected because it silently
fails to carry the same intent to other emitters.

## Consequences

- The manifest states initiation intent without making authors learn each emitter's mechanism.
- Absent must remain passthrough so intent can be adopted item by item. The cost is asymmetry:
  upstream posture may have no equivalent elsewhere, and an unstated item uses each target's own
  default after unsupported metadata is dropped and reported. These are emitter limits, not a reason
  to make absence a hidden default.
- `both` produces two OpenCode artifacts with one identity, while Claude Code and Codex need only
  one skill each.
- `auto` and `both` can produce the same physical artifact on a harness whose native skill is always
  explicitly addressable. The result still satisfies both declarations because `auto` leaves that
  capability unspecified; target projections must show the resolved surface rather than imply a
  universal model-only guarantee.
- A bundled `manual` conversion preserves its parsed body and assets under a non-discoverable
  `skills/<name>/BODY.md` park and emits a global-only command stub. Inline command copies can still
  strand skill-relative sibling-item paths; `validate` keeps that remaining shape cost visible
  without blurring `manual` and `command` into one concept.
- One assembled body currently feeds all three harnesses; the overlay mechanism has no per-harness body
  ownership. That capability limit can make target-specific prose or paths costly, and remains
  visible rather than narrowing the accepted neutral intent.
