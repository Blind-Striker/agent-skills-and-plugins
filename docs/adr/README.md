# Architecture Decision Records

Date: 2026-08-19

This directory records accepted decisions whose context, alternatives, rationale, trade-offs, and
reversal value need to survive. Current mechanics live in `docs/architecture/`; current working
rules live in `docs/engineering/`; dated evidence lives in `docs/research/`. ADRs explain why the
current canon has its shape instead of becoming a second copy of that canon.

Each ADR is a standalone `NNNN-kebab-title.md` file. The directory listing is the index; use the
next unused/free number. Never renumber an existing record.

## Current-canon agreement

Live code and configuration are the mechanical source for observed current behavior. An accepted
ADR records decision intent, and the corresponding architecture or engineering document states the
current rule. Those sources must agree; investigate rather than silently choosing one when they
diverge.

If the implementation is the accepted real guarantee, correct whichever ADR or current-canon claim
is stale in the same change. If the implementation is a known gap against retained intent, keep the
decision accurate and add a `docs/ROADMAP.md` gap that names the responsible file or symbol. Do not
bend a new need merely to protect old ADR wording.

## When to write an ADR

All three legs must hold:

1. **Hard to reverse.** Changing course later carries meaningful cost.
2. **Surprising without context.** A future maintainer would reasonably ask why this choice was made.
3. **A real trade-off.** Genuine alternatives existed, and the choice buys something while costing
   something.

If any leg is absent, do not mint an ADR. Put current mechanics in architecture, current process or
authoring policy in engineering or the relevant authoring guide, enforcement in code, dated findings
in research, and operational state in the roadmap.

## Required format

Use the copyable [`template.md`](template.md). The required structure is deliberately small:

```text
# ADR-NNNN: <decision, not topic>

Date: YYYY-MM-DD
Status: Accepted

## Context
## Decision
## Consequences
```

Keep `Context` to two or three paragraphs at most. State a one-part `Decision` in prose and use
bullets when it has several parts; include rejected alternatives when their loss matters.
`Consequences` records non-obvious costs and benefits, plus a reversal trigger where useful;
consequences explain the trade-off but are not binding current law.

Keep one decision per ADR. A record that needs more decisions should be split rather than expanded
with extra sections. Version pins, rates, inventories, validation catalogs, incident diaries, and
work-queue state belong in their mechanical, evidence, or operational homes, not in an ADR.

## Living revision and supersession

ADRs are living records. Revise an ADR in place when the same underlying decision gains a clearer
scope, a corrected guarantee, or better-understood consequences. Preserve useful context and
alternatives while removing stale current claims. Date handling follows the single
[`docs/engineering/documentation.md` Date policy](../engineering/documentation.md#dates-by-document-class).

Create a new ADR when a distinct decision replaces the old one and keeping both records makes the
change in trade-off easier to understand. Mark the old record `Status: Superseded by ADR-NNNN`, link
the predecessor from the new record's context, and update current architecture or engineering canon
in the same change. Git and dated research retain chronology; supersession keeps the decision chain
readable without turning current canon into a history log.
