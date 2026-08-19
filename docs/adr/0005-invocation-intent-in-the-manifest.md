# ADR-0005: The manifest states invocation intent; each emitter picks the mechanism

Date: 2026-08-19
Status: Accepted

## Context

A curated skill may be passive knowledge the model selects or a ceremony the user starts
deliberately. Claude Code expresses that distinction with skill frontmatter; OpenCode expresses it
through artifact shape because skills are model-only and commands are its user surface. The
manifest needs one harness-neutral statement of intent rather than target-specific keys.

## Decision

Each item has an optional `invocation` field with values `auto`, `manual`, or `both`. It applies to
skill output; commands and agents are already explicit artifacts and the field has no meaning on
them.

**Absent is not a fourth value with a default meaning.** An item that says nothing is an item that
states no intent, and upstream's own frontmatter passes through untouched. Stating a value replaces
whatever upstream said — that is the point of stating it.

`as:` stays orthogonal. It is the **shape** dial of
[ADR-0006](0006-output-is-a-transformation.md) — what artifact the item becomes — while `invocation`
is the **trigger** dial. Emitters translate that neutral trigger intent into their own native
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
decides whether an item fires automatically or waits to be asked, not how a particular emitter names
the actor. Hand-writing Claude invocation keys in `frontmatter:` was rejected because it silently
fails to carry the same intent to OpenCode.

## Consequences

- The manifest states trigger intent without making authors learn either emitter's mechanism.
- Absent must remain passthrough so intent can be adopted item by item. The cost is asymmetry:
  upstream Claude posture has no OpenCode equivalent and an unstated item uses OpenCode's own skill
  default, so it is model-only there. Claude and OpenCode also have different frontmatter surfaces:
  unsupported OpenCode keys are dropped and reported rather than silently carried. These are current
  emitter limits, not a reason to make absence a hidden default.
- `both` produces two OpenCode artifacts with one identity, while Claude Code needs only one skill.
- A bundled `manual` conversion preserves its parsed body and assets under a non-discoverable
  `skills/<name>/BODY.md` park and emits a global-only command stub. Inline command copies can still
  strand skill-relative sibling-item paths; `validate` keeps that remaining shape cost visible
  without blurring `manual` and `command` into one concept.
- One assembled body currently feeds both harnesses; the overlay mechanism has no per-harness body
  ownership. That capability limit can make target-specific prose or paths costly, and remains
  visible rather than narrowing the accepted neutral intent.
