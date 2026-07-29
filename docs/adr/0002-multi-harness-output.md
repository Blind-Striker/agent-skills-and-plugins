# ADR-0002: Harness-native output instead of a common format

Date: 2026-07-30
Status: Accepted

## Context

Two harnesses matter: Claude Code (primary) and OpenCode (secondary). They agree on the important
part — `SKILL.md` is an open standard (agentskills.io) and OpenCode reads skills natively — but they
diverge everywhere else: plugin packaging and `marketplace.json` are Claude Code concepts, command
and agent markdown differ in shape, and features like hooks and `allowed-tools` have no OpenCode
equivalent.

The wrong move is to invent an intermediate format rich enough for both and generate from it. That
lands on the lowest common denominator, and every harness gets a slightly worse artifact than it
could have had.

## Decision

One source (`curation/` + `overlays/` + `skills/`), one build, and a separate native artifact tree per
harness: `plugins/` for Claude Code, `opencode/` for OpenCode (`opencode/skill/`, `opencode/command/`,
`opencode/agent/`). Per harness:

- **Skills pass through** — the shared `SKILL.md` format, no translation.
- **Commands and agents are transformed** into each harness's markdown shape.
- **Unmappable features are dropped, not approximated**, and every drop is listed in the build report.
  Silent loss is the failure mode being designed against.

Model borrowed from wshobson/agents: harness-native artifacts, not lowest-common-denominator
translations.

## Consequences

- Claude Code output is not constrained by what OpenCode can express, and vice versa.
- Per-harness adapters stay small because skills — the bulk of the content — need no adapter at all.
- A third harness (Codex, Cursor, Gemini) means a new emitter, not a redesign. None is planned.
- Anything OpenCode cannot represent is visible in the build report, so a curation decision can be
  made instead of discovering the gap in use. OpenCode agent permission mapping is deliberately absent
  in the first version.
