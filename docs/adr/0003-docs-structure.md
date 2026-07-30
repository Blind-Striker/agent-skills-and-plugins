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
- **Audience decides placement.** `docs/agents/` holds guidance only an AI agent needs — the harness
  adapter guide and session-handover prompts. The test: a human developer working without an agent
  never reads it. Knowledge shared by humans and agents lives in `docs/adr/`, `docs/research/` and
  `docs/ROADMAP.md`. ADRs are outside `docs/agents/` precisely because their audience is universal.
- **Evergreen vs operational split.** Evergreen: `AGENTS.md`, `docs/adr/` (decisions and their
  rationale), `docs/research/` (harness and integration notes), `docs/agents/` (agent guidance).
  Operational: `docs/ROADMAP.md` (current state, next steps) — it is expected to change and to
  shrink as work lands. The test is mechanical: a sentence that needs rewriting when a task
  completes belongs in the roadmap.
- **Planning output is transient.** Specs and plans written by planning skills (e.g. under
  `docs/superpowers/`) are scratch: their durable essence is condensed into ADRs, and the files are
  deleted in the merge that completes the work.
- **Documents describe the status quo, not their own history.** No amendment notes, no renumbering,
  no changelog sections; git carries history. Every hand-written document in `docs/` carries a
  `Date:` line — generated files (`docs/inventory.md`) and the contracts (`AGENTS.md`, `CLAUDE.md`)
  are exempt.
- **`README.md` stays human-facing** (what this is, setup, commands, consuming) and links the rest.

## Consequences

- One contract to maintain, and OpenCode gets it for free.
- Roadmap churn no longer touches the evergreen documents, which makes a stale line in them a bug
  rather than background noise.
- A new architectural decision costs a small ADR file. That is the intended cost.
