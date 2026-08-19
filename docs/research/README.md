# Research

Date: 2026-08-19

Directory index for dated research, source-backed prior art, advisory synthesis, corrections, and
decision history. [`docs/engineering/documentation.md`](../engineering/documentation.md#authority-and-document-roles)
owns this directory's authority and
[`Date:` semantics](../engineering/documentation.md#dates-by-document-class). Follow each note's
relay to current canon rather than treating a research snapshot as current local policy.

## Conventions

- Keep one bounded topic per file and name the file for that topic, for example
  `skill-invocation-across-harnesses.md`. Preserve a legacy date-prefixed filename when the date is
  useful provenance, but do not create a chronological research log.
- When retrieving a finding, check its versions, pins, scope, and whether it was observed,
  source-derived, or reasoned. Trace experiment claims to stable `record_id` citations and inspect
  any supersession chain in [`experiments/harness-invocation/records/`](../../experiments/harness-invocation/records/).
- Preserve corrections and useful superseded positions, and phrase recommendations as advisory
  synthesis. Repeatable methods and committed observations stay under [`experiments/`](../../experiments/);
  research synthesizes and cites them rather than duplicating their evidence.
