# ADR-0003: Documentation structure and agent contract

Date: 2026-07-30
Status: Accepted

## Context

This repo is worked on almost entirely by LLM agents across more than one harness, so the operating
rules have to live in a file the agents actually read. Claude Code reads `CLAUDE.md`; OpenCode reads
`AGENTS.md`. Duplicating the rules in both guarantees they drift apart.

A second problem: documentation that mixes "how this works" with "where we are" goes stale as a whole.
Once one paragraph is known to be out of date, the reader stops trusting the rest of the file.

## Decision

- **`AGENTS.md` is the canonical contract** — short (under 70 lines), harness-neutral, and evergreen:
  purpose, hard rules, sources of truth, documentation hygiene, working style.
- **`CLAUDE.md` is a relay** that points at `AGENTS.md` and holds nothing else.
- **Evergreen vs operational split.** Evergreen: `AGENTS.md`, `docs/adr/` (decisions and their
  rationale), `docs/research/` (harness and integration notes). Operational: `docs/ROADMAP.md`
  (current state, next steps) — it is expected to change and to shrink as work lands. The test is
  mechanical: a sentence that needs rewriting when a task completes belongs in the roadmap.
- **Documents describe the status quo, not their own history.** No amendment notes, no renumbering,
  no changelog sections; git carries history. Every document carries a `Date:` line — except
  `AGENTS.md` and `CLAUDE.md`, which are contracts rather than dated notes.
- **`README.md` stays human-facing** (what this is, setup, commands, consuming) and links the rest.

## Consequences

- One contract to maintain, and OpenCode gets it for free.
- Roadmap churn no longer touches the evergreen documents, which makes a stale line in them a bug
  rather than background noise.
- A new architectural decision costs a small ADR file. That is the intended cost.

## Credit

Pattern adapted and heavily condensed from the AGENTS.md contract of Homerun's discount-service
(internal repo); service-specific policy, approval gates, and .NET-specific guidance deliberately
dropped — this is a personal tooling repo, not a production service.
