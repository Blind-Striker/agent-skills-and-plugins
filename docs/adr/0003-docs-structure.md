# ADR-0003: Documentation uses progressive disclosure and single-purpose homes

Date: 2026-08-19
Status: Accepted

## Context

The repository needs current technical and working guidance, durable decision rationale, dated
research and measurements, operational state, audience-specific instructions, and short-lived
session context. When one kind of claim has several homes, readers cannot tell which version to
trust. Entry documents grow into competing manuals, historical findings are mistaken for policy,
and temporary status gradually becomes stale documentation.

The structure must let humans and agents start from a small entry point and follow a claim to its
owner without deleting unique rationale or evidence. Handovers and execution plans must remain
useful without becoming permanent sources of truth, and current policy must remain distinguishable
from the history that produced it.

## Decision

The repository adopts progressive disclosure with one canonical owner for each substantive current
claim. Secondary documents use one-way links instead of restating the rule. Documentation refactors
are lossless relocations: useful unique material moves to current canon, ADR rationale, dated
evidence, experiment records, or operational state before its old copy is removed.

Live code, configuration, curation data, and generators remain the mechanical authority for current
behavior. Current canon and ADR intent must agree with them or explicitly name an implementation
gap; the governing procedure is the ADR guide's
[current-canon agreement rule](README.md#current-canon-agreement).

The layered model separates current architecture and engineering practice, accepted decision
rationale, dated evidence and history, repeatable experiment method and records, operational state,
audience-specific and transient material, generated projections, and entry-point relays. The single
detailed file-by-file authority map and its lifecycle rules live in
[`docs/engineering/documentation.md`](../engineering/documentation.md#authority-and-document-roles).
That document also owns the detailed
[research-versus-experiments split](../engineering/documentation.md#research-and-experiments).

The following alternatives are rejected:

- Treating research as evergreen synthesis and current guidance makes dated or superseded findings
  compete with current canon. Research remains accessible as evidence and decision history instead.
- Deleting every claim when it becomes false is too coarse. False current guidance is corrected or
  removed, while useful superseded evidence remains available in dated research or experiment
  records and durable decision rationale remains in ADRs.
- Leaving historical context primarily to Git makes useful evidence and rationale difficult to find.
  Git carries provenance and edit chronology, but dated evidence and decision context stay in their
  accessible documentary homes.
- Copying rules or operating status into every entry document creates copies that drift; relays are
  cheaper and identify the owner.
- Keeping handovers and plans as durable status records lets temporary context outlive the work it
  describes.

## Consequences

- Readers sometimes follow a relay instead of relying on a convenient copy. In return, a current
  rule has one place to change and entry points stay small.
- Moving a claim carries a loss audit: unique rationale and evidence remain accessible even when the
  current-policy wording becomes shorter. Git retains edit history, but ADRs, research, and records
  retain the context a future reader still needs.
- Dated evidence can remain historically accurate after current policy changes without masquerading
  as the current rule.
- A change can require updates in more than one authority layer. That maintenance cost keeps the
  boundaries trustworthy without giving any claim two current owners.
- Generated documents and convenience relays remain useful review and navigation surfaces without
  becoming additional policy owners.
