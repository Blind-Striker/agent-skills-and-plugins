# ADR-0005: The manifest states invocation intent; each emitter picks the mechanism

Date: 2026-08-02
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

Each emitter derives its own mechanism:

| `invocation` | Claude Code | OpenCode |
|---|---|---|
| *(absent)* | upstream's frontmatter, untouched | `skills/` |
| `auto` | skill, `user-invocable: false` | `skills/` |
| `manual` | skill, `disable-model-invocation: true` | `commands/` |
| `both` | skill, neither key set | `skills/` **and** `commands/` |

`as:` stays orthogonal. It is the **shape** dial of
[ADR-0006](0006-output-is-a-transformation.md) — what artifact the item becomes — while `invocation`
is the **trigger** dial. The table above gives each invocation value its default shape per harness;
`as:` states the shape explicitly when that default is wrong.

Using `as: command` as the trigger dial was rejected because shape cannot express `both` and remains
a useful independent decision. The value names `model` and `user` were rejected because the author
decides whether an item fires automatically or waits to be asked, not how a particular emitter names
the actor. Hand-writing Claude invocation keys in `frontmatter:` was rejected because it silently
fails to carry the same intent to OpenCode.

## Consequences

- The manifest states trigger intent without making authors learn either emitter's mechanism.
- Absent must remain passthrough so intent can be adopted item by item. The cost is asymmetry:
  upstream Claude posture has no OpenCode equivalent and an unstated item is model-only there.
- `both` produces two OpenCode artifacts with one identity, while Claude Code needs only one skill.
- Converting a directory-shaped skill into a user-facing OpenCode command can strand bundled-file
  references. That is a shape cost tracked by `validate` and the ROADMAP, not a reason to blur
  `manual` and `command` into one manifest concept.
