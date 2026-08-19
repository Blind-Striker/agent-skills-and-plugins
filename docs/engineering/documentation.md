# Documentation

Date: 2026-08-19

This document owns documentation authority, placement, lifecycle, and maintenance. Verification
commands live in [quality-gates.md](quality-gates.md), repository task flow lives in
[workflow.md](workflow.md), and ADR governance lives in [the ADR guide](../adr/README.md).

## Authority and document roles

Live code, configuration, curation data, and generators are the mechanical authority for current
behavior. In particular, `package.json` owns script definitions, `curation/*.yaml` owns item-level
curation intent and its nearby reasons, and `curation/SCHEMA.md` owns manifest authoring grammar.
Prose explains those sources; it does not overrule what they do.

That precedence is not permission to resolve disagreement silently. Current canon and accepted ADRs
must agree with the implementation or name the mismatch as an operational gap. Memory, transient
plans, handovers, and dated evidence never outrank live repository sources.

| Role | Canonical home |
|---|---|
| Harness-neutral bootstrap and task routing | `AGENTS.md` |
| Current technical mechanics and boundaries | `docs/architecture/` |
| Current documentation, quality, and repository practice | `docs/engineering/` |
| Accepted decision context, rationale, trade-offs, and consequences | `docs/adr/` |
| Dated evidence, prior art, and decision history, including superseded positions | `docs/research/` |
| Repeatable measurement method and committed measurement records | `experiments/` |
| Current domain vocabulary and avoided synonyms | `CONTEXT.md` |
| Operational status, next work, open questions, and known gaps | `docs/ROADMAP.md` |
| Guidance needed only by an AI agent | `docs/agents/` |
| Temporary plans and specifications | `docs/superpowers/` |
| Scanner-visible upstream catalog | generated `docs/inventory.md` |
| Resolved per-item, per-harness review state | generated `docs/ledger.json` |

The root `README.md` is the human onboarding and consumption entry point. It may hold runnable setup
or installation recipes, but it relays engineering and architecture policy to their owners.
`CLAUDE.md` only relays `AGENTS.md` into Claude Code. `docs/cheatsheet.md` is a convenience routing
relay; item posture stays in the manifests and generated ledger.

Generated documents are projections, not edit surfaces. Regenerate them through the owning command.
If a projection disagrees with its inputs or generator, fix the input or generator and regenerate;
do not repair the projection by hand.

## One canonical home

Every substantive current claim has one owner. A secondary document may provide a short, one-way
relay to that owner, but it does not paraphrase the full rule. Entry points route inward; canonical
documents do not create a reverse relay merely to complete a loop.

Prefer consolidating a claim into an existing owner over creating another document. Add a document
only when it has a distinct, durable responsibility that no existing owner can carry without mixing
roles.

Prefer a link to a mechanical source over copying a value that can drift. Script definitions belong
in `package.json`; inventory counts belong in the generated inventory; resolved artifact state
belongs in the generated ledger. Current-state documents describe the status quo, not their own
amendment history: do not add amendment notes. Git carries edit chronology while ADRs and research
retain rationale and useful evidence.

A sentence that must change when a task completes is operational. Put it in `docs/ROADMAP.md` or a
temporary handover, not in evergreen architecture or engineering canon. After work completes,
remove it from the roadmap once any durable rationale, evidence, or current rule has been relocated
to its canonical home. The roadmap shrinks as work lands; it is not an accumulating changelog.

## Lossless relocation

A documentation refactor is a relocation, not a summarization exercise. Before removing substantive
text, classify each unique claim and give it one destination:

- current mechanics or practice in architecture or engineering canon;
- decision context and trade-offs in an ADR;
- dated evidence or a useful superseded position in research;
- repeatable method or observed results under `experiments/`;
- operational state in the roadmap; or
- audience-specific or temporary context in the appropriate agent, handover, or plan file.

The change's claim audit records either that destination or an explicit ruling that the claim is
stale and has no remaining evidentiary value. Shortening alone never justifies deletion. Git history
is useful provenance, but it is not a substitute for accessible current canon, rationale, or
evidence.

## Dates by document class

Every hand-written document under `docs/` carries a `Date:` line. Generated documents are exempt.

| Document class | Meaning of `Date:` |
|---|---|
| Current architecture, engineering, indexes, relays, and agent playbooks | Latest substantive update to the current content |
| `docs/ROADMAP.md` | Latest substantive update to operational state |
| ADR | Date of the accepted decision represented by the current record; a material decision revision moves it, an editorial correction does not |
| Research | Evidence snapshot date, or latest evidence session included in a chronological log; editorial correction does not rewrite the historical date |
| Handover, plan, or specification | Date the current temporary artifact was issued or substantively revised |

A prose-only spelling or link repair is not a substantive date change. A change that alters the
claim, scope, decision, evidence boundary, or current instruction is substantive.

## References and audience

Use repository-relative links. Cite current architecture or live code for mechanism, an ADR for
rationale, research or an experiment record for bounded evidence, and the roadmap for unfinished
work. A source comment may name a stable ADR when the rationale matters, but the local code and
comment must still state the invariant clearly enough to remain understandable without movable
prose.

Audience decides placement. Put material under `docs/agents/` only when a human developer working
without an agent would not need it. Shared knowledge belongs in architecture, engineering, ADR,
research, domain, or operational canon. Human onboarding belongs in the root README; harness-specific
entry files relay rather than fork the working contract.

## Research and experiments

Research is dated synthesis and decision history. It may preserve a finding or position that current
canon later supersedes, so it must not act as current local policy. A current rule derived from
research moves into architecture, engineering, or an ADR and the research points forward to that
owner.

Experiments own the repeatable method and committed observations. Research cites records instead of
duplicating their evidence; raw credentialed or machine-specific output remains outside the
repository as required by the experiment protocol. Measurement records follow their own
[append-only supersession rules](../../experiments/harness-invocation/records/README.md#supersession).
A measurement can motivate a decision, but it becomes policy only when the appropriate current canon
and, when warranted, ADR are updated.

## Handovers and plans

Handovers contain unfinished session context only. Consume one against live Git and current canon,
then delete it when its follow-up ships or a newer handover replaces it. A handover never becomes a
status archive.

Plans and specifications under `docs/superpowers/` are transient execution material, not canonical
or operational authority. Move durable conclusions to their proper homes and delete the temporary
artifact when the work merges.

## Guarded prose and contradictions

Some prose is protected by tests or experiment self-checks because exact content carries a safety
property. When guarded prose moves or changes, update the guard in the same change and preserve the
invariant it was testing; deleting or weakening a guard merely to permit a rewrite is not a
documentation refactor. The guard locations and commands are listed in
[quality-gates.md](quality-gates.md#documentation-guards).

Correct a stale current claim in its canonical home in the same change as the behavior it describes,
preserve useful historical evidence in research, and remove or redirect stale relays. If
implementation and ADR intent disagree, do not resolve the conflict here or choose a winner
silently; follow the ADR guide's
[current-canon agreement procedure](../adr/README.md#current-canon-agreement).
