# Curation sanity panel playbook

Date: 2026-10-11

A read-only review of **emitted output** for the questions deterministic validation cannot decide:
whether two items compete for the same request, whether a transformed body still serves the intent
recorded beside its manifest item, and whether each harness's form still fits that harness.
`validate` proves identity, shape, linkage, ownership, and bytes
([quality gates](../engineering/quality-gates.md)); this panel judges what those proofs leave open.
It surfaces findings for the curator and never authorizes an agent to change curation
([ADR-0007](../adr/0007-control-beats-fidelity.md)). It is not a gate, and agreement between
reviewers is not policy.

The [reference-audit playbook](reference-audit-playbook.md) is its sibling: that sweep finds
bare-name reference candidates, while this panel judges trigger competition, body intent, and
harness fit.

## When to run

Run it after a body-ownership pass and before declaring a Module closed, and when the curator names
a trigger-competition case. The panel reviews committed output at one named commit: start from a
clean tree whose generated estate is current, and record that commit and the upstream pins in every
report.

## Cases and sweeps

A **case** is one pair or cluster of items whose triggers compete. The standing cases are:

- `subagent-driven-development` against `executing-plans`;
- `aot-trimming` against `dotnet-aot-compat`;
- the consolidated `msbuild` skill against the eight MSBuild specialists;
- `directory-build-organization` against `convert-to-cpm`.

`code-testing` against `systematic-debugging` is not a case: `code-testing` is `manual` in all
three harnesses, so no model chooses between them.

A **sweep** reviews trigger competition across the model-reachable items of one or more Modules and
leaves out every pair a case covers. The first run, at `c7e9f90`, reviewed the four standing cases
and swept Process on its own and the three .NET Modules together.

Each case and each sweep gets one reviewer with a fresh context and its own report.

## The bounded packet

A reviewer receives one bounded packet and reads nothing it needs beyond it:

- the pinned upstream body of each item and its bundled dependency closure;
- each item's manifest entry with its nearby reasons, and the Module header rules that apply;
- the item's overlay or patch and its lock entry, when it owns a body;
- all three emitted forms: `plugins/`, `opencode/`, and `codex/`;
- the canon sections that define what the reviewer judges, such as the invocation mapping in
  [transformation and emission](../architecture/transformation-and-emission.md) and the reference
  tiers in [references and linking](../architecture/references-and-linking.md).

A sweep's packet adds the model-reachable inventory per harness: which items carry no hiding key
and so are offered to the model, and the descriptions the model sees.

## Three lenses, reviewed separately

1. **Trigger and overlap.** What each harness offers the model, which item wins typical requests,
   where the request goes when the wrong item wins first, and what the collision costs. A listing
   budget that cuts or drops descriptions counts here.
2. **Body-intent preservation.** Whether the emitted body does what its manifest reason says: an
   edit that stops short of its own examples, merged content that was lost, or two items whose
   instructions contradict each other.
3. **Harness fit.** Whether facts, pointers, invocation flags, tool words, and paths render as each
   harness needs them.

Report each lens on its own. Every finding names its lens, and a lens with nothing to report says
so in one line.

## Finding format

Each finding carries:

- **Evidence**: `file:line` locations, repository-relative, naming the emitted tree, the manifest,
  the overlay, or the upstream file each one comes from.
- **Consequence**: what goes wrong at runtime, and for whom.
- **Confidence**: high, medium, or low; split it when the textual fact and its runtime effect
  differ, as "high that the texts disagree, low on how often it fires".
- **Verdict**: exactly one of:
  - `retain`: keep the item as it is emitted now;
  - `narrow`: keep the curation decision, but tighten its text or its trigger;
  - `reconsider`: reopen the curation decision itself;
  - `ambiguous`: the evidence does not decide it; it needs the curator's intent or a runtime sample.

Do not restate what `validate` already reports. Mark a trigger judgment drawn from text as a
prediction unless a recorded runtime sample backs it.

## Disagreements stay open

Keep every disagreement in the report: a dissenting reading beside the finding it disputes, two
reviewers' different confidence on one finding as two entries, and two readings of the curator's
intent side by side. The panel never chooses between them; the curator does.

## Reviewers never edit

A reviewer reads files and runs read-only commands only. It never edits, ejects, blesses, bumps a
version, runs a command that writes generated output, or commits. A finding that needs a change is
a recommendation in the packet, nothing more.

## Output: a curator decision packet

Each report ends with its findings ranked by consequence, as one table of lens, finding, verdict,
and confidence, followed by the disagreements it keeps. The run's coordinator assembles the reports
into one small decision packet: each finding with a recommendation, and the genuinely open choices
marked. The curator decides. Decisions land through the normal curation flow in
[workflow.md](../engineering/workflow.md), with each reason beside its item in `curation/*.yaml`;
the reports themselves stay outside the repository.
