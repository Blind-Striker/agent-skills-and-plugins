# ADR-0002: Harness-native output instead of a common format

Date: 2026-09-07
Status: Accepted

## Context

Claude Code, OpenCode, and Codex share the `SKILL.md` convention but differ in packaging, artifact
shape, invocation controls, namespacing, and supported metadata. A common output format would either
leak harness-specific fields or reduce the targets to their lowest common denominator.

## Decision

- One neutral authored source produces separate harness-native Claude Plugin, OpenCode Bundle, and
  Codex Plugin output. There is no common emitted format that any harness must interpret.
- Each target receives only shapes and metadata it understands. An unrepresentable feature is
  dropped and reported rather than approximated or lost silently.
- A target may expose a capability that the neutral manifest intentionally leaves unspecified. That
  is a native realization rather than an approximation; the resolved target surface remains
  reviewable beside the authored intent.
- Every output tree is ready native content, not input to a runtime adapter. Installation may
  compose emitted files, but it does not reinterpret bodies, invocation, metadata, or references.
- Output names remain concise and are not universally prefixed with their Module name. Identity is
  checked in each target's native namespace: Claude and OpenCode can distinguish artifact kinds,
  while Codex flattens every resolved kind to a plugin-local skill and therefore rejects same-name
  cross-kind collisions within that plugin.

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

- No harness is constrained by what another can express, at the cost of reviewing intentionally
  different output trees.
- A new harness requires a new emitter that answers the transformation contract, not a redesign of
  the authored source.
- A reported drop turns an incompatibility into a curation decision instead of a latent runtime
  surprise.
- A native capability left unspecified by neutral intent may differ across targets without becoming
  a reported loss; the ledger must still show what each emitter actually produced.
- Flat names preserve a usable surface but require target-specific collision checks before output is
  deleted. Codex keeps cross-plugin identity through its plugin namespace.
- Reconsider mandatory Module prefixes only if flat per-kind uniqueness becomes the limiting
  constraint.
