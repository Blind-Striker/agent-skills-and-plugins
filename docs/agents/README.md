# Agent Guide

Date: 2026-07-30

This directory contains repository-specific guidance for AI coding agents. Audience test: a human
developer working without an agent never needs anything in here. Shared human+agent knowledge lives
in `docs/adr/`, `docs/research/` and `docs/ROADMAP.md`.

`AGENTS.md` is the canonical always-on contract. Files here relay to it or extend it with
harness-native detail; they do not own policy.

## File map

| File | Purpose |
| --- | --- |
| `AGENTS.md` | Canonical repository contract |
| `CLAUDE.md` | Claude Code relay to `AGENTS.md` |
| `docs/agents/README.md` | Harness adapter guide (this file) |
| `docs/agents/handover-prompts/` | Session-pickup prompts for stateful handovers |

## Harness notes

- **Claude Code** consumes this repo as a plugin marketplace (`.claude-plugin/marketplace.json` →
  `plugins/deniz-*`). It addresses a plugin skill by its **directory name**, namespaced as
  `<plugin>:<skill-dir>`. The build forces emitted directory name = frontmatter `name`, so the two
  never diverge in our output. Human-facing install steps live in the root `README.md` (Consuming).
- **OpenCode** reads `AGENTS.md` natively and consumes the `opencode/` tree (SKILL.md is the open
  agent-skills standard). Wiring has not been exercised yet — see `docs/ROADMAP.md`; once done, the
  agent-relevant findings land here and research notes in `docs/research/`.
- Planning skills (superpowers brainstorming/writing-plans) write specs and plans under
  `docs/superpowers/`. That output is merge-transient scratch: delete it in the merge that completes
  the work; git carries the history.

## Handover prompts

At the end of a session that closed a non-trivial wave (an architectural change, a multi-commit
migration, a curation batch), write a pickup prompt from
`handover-prompts/session-pickup-template.md` so the next session starts primed instead of
re-deriving state. Routine fixes and doc edits do not get one.
