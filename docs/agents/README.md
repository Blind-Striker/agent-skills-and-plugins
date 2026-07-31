# Agent Guide

Date: 2026-07-31

This directory contains repository-specific guidance for AI coding agents. Audience test: a human
developer working without an agent never needs anything in here. Shared human+agent knowledge lives
in `docs/adr/`, `docs/research/` and `docs/ROADMAP.md`.

`AGENTS.md` is the canonical always-on contract, and its "Sources of Truth" table is the repo's one
routing table. Files here relay to it or extend it with harness-native detail; they do not own
policy.

## Harness notes

- **Claude Code** consumes this repo as a plugin marketplace (`.claude-plugin/marketplace.json` →
  `plugins/deniz-*`). It addresses a plugin skill by its **directory name**, namespaced as
  `<plugin>:<skill-dir>`. The build forces emitted directory name = frontmatter `name`, so the two
  never diverge in our output. Human-facing install steps live in the root `README.md` (Consuming).
- **Claude Code does not read `AGENTS.md` natively** — `CLAUDE.md` pulls it in via the `@AGENTS.md`
  import, which inlines the contract at session start (no Read call, no obedience dependency).
  Import parsing applies to the imported file too: a bare `@token` in AGENTS.md outside backticks
  would be treated as an import path — keep such tokens in code spans. Caveat: the built-in
  Explore and Plan subagents skip `CLAUDE.md` entirely, so the contract does not reach them.
- **OpenCode** reads `AGENTS.md` natively and consumes the `opencode/` tree (SKILL.md is the open
  agent-skills standard). The built tree has been mounted three ways and exercised end to end in
  a TUI; the durable findings live in
  [`skill-invocation-across-harnesses.md`](../research/skill-invocation-across-harnesses.md)
  ("Verified on this repo's real output"), the method that produced them in
  [`harness-probing.md`](harness-probing.md). The install mechanism is an open decision —
  `docs/ROADMAP.md`.
- Planning skills (superpowers brainstorming/writing-plans) write specs and plans under
  `docs/superpowers/`. That output is merge-transient scratch: delete it in the merge that completes
  the work; git carries the history.

## Measuring a harness

Every emitter encodes an assumption about a harness, and both the documentation and plain reasoning
have been wrong about those assumptions repeatedly. [`harness-probing.md`](harness-probing.md) is the
method: how to build a throwaway lab, how to isolate each harness (they differ), which introspection
commands answer a question for free, what only a human at a TUI can observe, and the traps that have
already produced wrong entries in `docs/research/`. Read it before adding a fact about harness
behaviour anywhere.

## Handover prompts

At the end of a session that closed a non-trivial wave (an architectural change, a multi-commit
migration, a curation batch), write a pickup prompt from
`handover-prompts/session-pickup-template.md` so the next session starts primed instead of
re-deriving state. Routine fixes and doc edits do not get one.
