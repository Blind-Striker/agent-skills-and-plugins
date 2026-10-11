# Research

Date: 2026-10-12

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

## Notes

- [Claude ultracode and orchestration ports](claude-ultracode-and-orchestration-ports.md): Claude
  Code's ultracode mechanism, OpenCode and Codex orchestration capabilities, prior-art ports, and
  the orchestration-skill decisions.
- [Codex generated-estate audit](codex-generated-estate-audit.md): the first target-fit pass over
  the generated Codex estate.
- [Codex native plugin and skill surfaces](codex-native-plugin-and-skill-surfaces.md): the Codex
  capabilities that constrained the Codex emitter.
- [Harness adapters](harness-adapters.md): Claude Code and OpenCode 1 discovery and
  instruction-loading findings behind the adapters.
- [OpenCode 2 as the OpenCode target](opencode-2-target.md): the evidence behind targeting OpenCode
  2 only.
- [OpenCode plugin packages](2026-08-07-opencode-plugin-package-artifacts.md): package caches,
  plugins, and per-Module distribution on OpenCode 1.18.
- [OpenSpec evaluation](openspec-evaluation.md): what OpenSpec is, how it overlaps the estate, the
  integration options, and why it is parked.
- [Skill framework landscape](skill-framework-landscape.md): community conclusions about the
  vendored frameworks and the evidence on skill quality.
- [Skill invocation across harnesses](skill-invocation-across-harnesses.md): who can invoke what in
  each harness, and the prior art.
- [Upstream recuration informed review](upstream-recuration-informed-review.md): the verified
  findings of one read-only review of a curation range.
- [Upstream repo layouts](upstream-repo-layouts.md): pin-relative layout traps of the vendored
  repositories.
