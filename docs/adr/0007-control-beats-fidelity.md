# ADR-0007: Curation serves the curator's taste; control beats fidelity

Date: 2026-07-31
Status: Accepted

## Context

ADR-0006 fixes what the output *is* — a transformation along three axes, resolved per harness. It
does not say what the transformation is *for*, and that gap has cost the same conversation in every
session: proposals optimise for faithfulness to upstream, treat divergence as risk, and ask the
curator to restate the goal from scratch. This ADR records that goal so it stops being oral
history.

The frameworks this repo vendors are engineered to be adopted whole and to decide for themselves
when they run — superpowers most of all, with its SessionStart bootstrap and trigger pressure
written into descriptions. Adopting them whole is exactly what is not wanted. What is wanted is a
skill set that works the way its curator works.

## Decision

The product is a skill set that reflects the curator's own working style. Upstream is raw
material, not a standard to track. Concretely:

- **Control beats fidelity.** "Behaves exactly like upstream" is not a goal; "fires only when and
  how the curator intends" is. Upstream superpowers tunes for *never miss* — bootstrap
  amplification, coercive trigger prose. This repo tunes for *never surprise*, and accepts the
  cost knowingly: without the amplification the result is best-effort by upstream's yardstick, and
  that yardstick does not apply here.
- **The trigger is decided per item, by the curator.** Some items are passive knowledge the model
  reaches for on its own, some are ceremonies the curator starts deliberately, some are both.
  There is no framework-wide posture; `invocation` (ADR-0005) is the word each decision is
  recorded in.
- **Heavy modification is unfeared.** The overlay machinery and its hash blessings (ADR-0001)
  exist precisely so bodies can be rewritten without silent drift; a heavily modified item is a
  normal outcome, not a smell. The mechanism ladder still applies — take the lowest rung that says
  what you mean — but no rung is off-limits, and "diverges from upstream" is never, by itself, a
  finding.
- **Frameworks are quarries, not allegiances.** Where two upstreams cover the same job — a TDD
  discipline, a review ceremony, requirements interrogation — the choice is made per job by
  reading both bodies: take one, take the other, take both as genuinely different tools, or take
  neither. Entering a workflow through one framework's trigger and continuing with another's
  ceremony is an expected shape, not an exception. Nothing routes except the curator.
- **Decisions see the whole dependency closure.** A skill can lean on other skills, commands,
  agents, bundled scripts and reference files — in all three reference spellings
  ([upstream-repo-layouts.md](../research/upstream-repo-layouts.md)). The closure is surfaced at
  decision time; taking a package whole, cutting the edge, or rewiring it are all legitimate
  outcomes. Deciding an item in isolation is not.
- **Both harnesses, every time.** An item's fate is decided for the Claude Code and OpenCode trees
  at once — ADR-0006 resolves each axis per harness. Claude-first with OpenCode as an afterthought
  is the named anti-pattern.

## Consequences

- Reviews and proposals lose upstream as their yardstick. The measure of a curated item is the
  recorded intent — the manifest comment beside it and this ADR — not similarity to what upstream
  ships.
- Overlays are expected to accumulate. ADR-0001 prices each one honestly — a hard build failure on
  upstream drift until re-blessed — and that price is accepted deliberately rather than read as a
  signal to curate less.
- Stripping upstream's trigger pressure is half an edit. An item meant to fire on its own must be
  given an honest trigger description in the same decision, or it simply never fires
  ([skill-framework-landscape.md](../research/skill-framework-landscape.md)). An item the curator
  triggers needs the opposite: a human-facing line for a person browsing a menu.
- The canon itself bends. When a need and an ADR disagree, the ADR is rewritten in place
  (AGENTS.md, Working Style) — learning on the road is expected to reshape these documents.
