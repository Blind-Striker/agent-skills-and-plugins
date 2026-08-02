# ADR-0003: Documentation claims have single-purpose homes

Date: 2026-08-02
Status: Accepted

## Context

The repository needs permanent operating guidance, architectural rationale, research and evidence,
operational status, and short-lived session context. When one kind of claim has several homes,
readers cannot tell which version to trust, and temporary status gradually becomes stale
documentation.

The structure must let humans and agents find the canonical statement without making handovers or
planning scratch permanent sources of truth.

## Decision

- A documentation claim has one canonical home. Other documents may relay it by linking to that
  home, but do not restate it. When a claim becomes false, correct it in place or delete it.
- `AGENTS.md` is the evergreen, harness-neutral working contract.
- `docs/adr/` records durable architectural decisions and their rationale.
- `docs/research/` holds durable research and synthesis shared by human and agent readers.
- `experiments/` holds repeatable measurement methods and committed evidence.
- `docs/ROADMAP.md` is the operational home for status, next work, and known gaps.
- `docs/agents/handover-prompts/` holds temporary agent-session handovers. A handover is consumed
  against live Git and the canonical documents, then deleted when its follow-up ships.
- `docs/superpowers/` holds planning scratch. Its durable conclusions belong in their canonical
  homes; the scratch is deleted when the work merges.

Copying rules or operating status into every entry document is rejected because the copies drift.
Keeping handovers and plans as durable status records is rejected because their value expires with
the work they describe.

## Consequences

- Readers must follow a relay to the canonical home instead of relying on a convenient copy.
- A change can require an accompanying operational update or removal of temporary scratch. That
  maintenance cost keeps durable documentation narrow and trustworthy.
- Historical context belongs in Git rather than preserved status snapshots, which makes active
  documentation easier to keep current but less suitable as an archive.
