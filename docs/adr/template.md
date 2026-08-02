# ADR Template

Date: 2026-08-02

> **Matt gate:** write an ADR only when the decision is hard to reverse, surprising, and carries a
> real trade-off. If any part of that test fails, use the appropriate rule, authoring guide, code,
> research, or operational document instead.
>
> **How to use:** copy to `NNNN-<kebab-title>.md` (next free number), replace the skeleton below,
> and delete everything above the `---`. Title the decision, not the topic. Keep it small: an ADR
> that needs sections beyond these three is usually two decisions. In a living ADR, `Date:` is the
> date of its last substantive rewrite; Git carries its history.
>
---

# ADR-NNNN: <the decision, not the topic>

Date: YYYY-MM-DD
Status: Accepted <!-- or: Superseded by ADR-NNNN -->

## Context

The forces in tension, in two or three paragraphs at most — enough that the decision below reads as
the obvious move rather than a preference.

## Decision

The decision itself, stated actively ("X does Y"). Put the alternatives considered and why they
lost here; do not add a separate Alternatives section. Use bullets when there are several parts and
prose when there is one.

## Consequences

The non-obvious trade-offs: what this costs and what it buys, including deliberate costs. If
nothing gets worse, look again. Consequences explain trade-offs; they are not binding law. Do not
put version pins, rates or panels, schema ladders, catalogs of `validate` behavior, operational
status, architecture for individual item names, or incident diaries here.
