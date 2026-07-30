# ADR-0003: Documentation structure and agent contract

Date: 2026-07-30
Status: Accepted

## Context

This repo is worked on almost entirely by LLM agents across more than one harness, so the operating
rules have to live in a file the agents actually read. Claude Code reads `CLAUDE.md`; OpenCode reads
`AGENTS.md`. Duplicating the rules in both guarantees they drift apart.

A second problem: documentation that mixes "how this works" with "where we are" goes stale as a whole.
Once one paragraph is known to be out of date, the reader stops trusting the rest of the file.

A third: the same facts matter to several audiences (repo visitor, working human, working agent), so
each audience's entry document tends to grow its own copy of them — and once copies exist, "which
copy is true" stops being answerable. The first drift this structure produced was exactly that kind:
an operational fact recorded in a handover prompt but missing from the roadmap.

## Decision

- **`AGENTS.md` is the canonical contract** for everyone who works in the repo, human or agent —
  harness-neutral, evergreen, and short because it loads in full at the start of every session. Its
  length is governed by that purpose, not by a line count: what earns a place is what an agent must
  hold before it proposes anything, and a rule that pushes the most load-bearing paragraph out to
  save lines has inverted the reason for keeping it short. The living rule text is there and only
  there; this ADR records the shape of the structure and why, without restating the rules.
- **Every fact has exactly one canonical home; every other appearance is a relay that links there.**
  `AGENTS.md`'s "Sources of Truth" table is the repo's one routing table.
- **Entry documents are relays, not homes.** `CLAUDE.md` relays Claude Code to `AGENTS.md` and holds
  nothing else. `README.md` is a pure front door — what this is, setup, commands, consuming — and
  sends anyone working in the repo to `AGENTS.md` with one line. There is deliberately no
  `CONTRIBUTING.md`: this is a solo repo, and a second rules-home would recreate the drift the
  structure exists to prevent.
- **Audience decides placement.** `docs/agents/` holds guidance only an AI agent needs — harness
  notes, session-handover prompts. The test: a human developer working without an agent never reads
  it. Knowledge shared by humans and agents lives in `docs/adr/`, `docs/research/` and
  `docs/ROADMAP.md`. ADRs are outside `docs/agents/` precisely because their audience is universal.
- **Evergreen vs operational split.** Evergreen documents answer "how it works and why"; the one
  operational document, `docs/ROADMAP.md`, answers "where we are" and shrinks as work lands. The
  test is mechanical: a sentence that needs rewriting when a task completes belongs in the roadmap.
- **Decision rationale lives next to the decision's artifact.** Architectural decisions get an ADR
  (start from `docs/adr/template.md`); per-item curation decisions get a comment beside the item in
  `curation/*.yaml` — an ADR per skill would be noise, and a separate decision log would drift from
  the manifest it describes.
- **Planning output is transient.** Specs and plans written by planning skills are scratch: their
  durable essence is condensed into ADRs, and the files are deleted in the merge that completes the
  work.
- **Documents describe the status quo, not their own history.** No amendment notes, no renumbering;
  git carries history. Hand-written documents in `docs/` carry a `Date:` line — generated files and
  the contracts are exempt.

## Consequences

- One contract to maintain, and OpenCode gets it for free.
- "Which copy is true" is always answerable: the canonical home; everything else links.
- Roadmap churn no longer touches the evergreen documents, which makes a stale line in them a bug
  rather than background noise.
- A new architectural decision costs a small ADR file. That is the intended cost.
