# ADR-0002: Harness-native output instead of a common format

Date: 2026-08-19
Status: Accepted

## Context

Claude Code and OpenCode share the `SKILL.md` convention but differ in packaging, command and agent
shape, invocation controls, and supported metadata. A common output format would either leak
harness-specific fields or reduce both targets to their lowest common denominator.

## Decision

- One neutral authored source produces separate harness-native Plugin and Bundle output. There is no
  common emitted format that either harness must interpret.
- Each target receives only shapes and metadata it understands. An unrepresentable feature is
  dropped and reported rather than approximated or lost silently.
- OpenCode output is ready native content, not input to a runtime adapter. Installation may compose
  emitted files, but it does not reinterpret bodies, invocation, metadata, or references.
- Output names remain concise and are not universally prefixed with their Module name. Identity is
  flat within an artifact kind, while the same output name in different artifact kinds remains
  legal and retains its kind distinction.

Mandatory Module prefixes were rejected because they would tax every user-facing name to solve
occasional collisions. A common target format and runtime adaptation were rejected because both
would move harness differences out of the emitter and either leak unsupported concepts or reduce
the outputs to their lowest common denominator.

Current emitter and body mechanics live in
[Transformation and emission](../architecture/transformation-and-emission.md); current Package,
Destination, Selection, `OPENCODE_CONFIG_DIR`, and installer mechanics live in
[Distribution and installation](../architecture/distribution-and-installation.md). Reference
localization follows [ADR-0008](0008-references-are-symbols.md).

## Consequences

- Neither harness is constrained by what the other can express, at the cost of reviewing two
  intentionally different output trees.
- A new harness requires a new emitter that answers the transformation contract, not a redesign of
  the authored source.
- A reported drop turns an incompatibility into a curation decision instead of a latent runtime
  surprise.
- Flat names preserve a usable surface but require collision checks. The accepted kind distinction
  is not fully retained by current name-only semantic maps; that implementation gap remains in the
  [roadmap](../ROADMAP.md#known-gaps), not as a narrower decision here.
- Reconsider mandatory Module prefixes only if flat per-kind uniqueness becomes the limiting
  constraint.
