# ADR-0002: Harness-native output instead of a common format

Date: 2026-08-02
Status: Accepted

## Context

Claude Code and OpenCode share the `SKILL.md` convention but differ in packaging, command and agent
shape, invocation controls, and supported metadata. A common output format would either leak
harness-specific fields or reduce both targets to their lowest common denominator.

## Decision

One neutral authored source (`curation/`, `overlays/`, and `skills/`) produces a separate native tree
per harness: `plugins/` for Claude Code and `opencode/skills/`, `opencode/commands/`, and
`opencode/agents/` for OpenCode.

- Skill bodies retain the shared `SKILL.md` shape, while each emitter filters or adds the metadata
  its target understands.
- Commands and agents are transformed into the target's native markdown shape.
- Features a target cannot represent are dropped rather than approximated, and the build reports
  each drop. Silent loss is forbidden.
- References are localized independently from the neutral body into each target address space, per
  ADR-0008.

OpenCode's flat namespace makes output-name uniqueness a curation requirement. Items keep concise
names and use item-level `name:` only when a collision must be resolved. Prefixing every OpenCode
artifact with its plugin was rejected because it taxes every user-facing command to solve occasional
collisions; it can be reconsidered if global uniqueness itself becomes the limiting constraint,
because a prefix would buy back duplicate names across modules.

## Consequences

- Neither harness is constrained by what the other can express, at the cost of reviewing two
  intentionally different output trees.
- A new harness requires a new emitter that answers the transformation contract, not a redesign of
  the authored source.
- Flat names make identity global across the build: the rewrite map and `depends_on` use output
  names, while ledger entries qualify them with plugin and artifact kind. Manifest preflight rejects
  duplicate `plugin.name` values and same-plugin duplicate kind/name pairs before either can
  overwrite output; `validate` retains the generated-tree cross-plugin check. The same name in
  different artifact kinds remains legal.
- A reported drop turns an incompatibility into a curation decision instead of a latent runtime
  surprise.
