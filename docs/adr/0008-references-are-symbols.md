# ADR-0008: References are symbols — one model, a linker, a ledger

Date: 2026-08-02
Status: Accepted

## Context

Cross-item references begin as strings in upstream and owned bodies, then land in harnesses with
different address spaces and reachability rules. A generated artifact is self-contained only when
every dependency the build can identify resolves for its intended audience where that artifact
lands. Bare-name prose and relative paths complicate that promise: not every apparent reference is
an identity, and not every broken illustrative path was broken by this build.

The build therefore needs one neutral reference model before rendering, a linker over each emitted
tree, an explicit declaration for authoritative model edges, and a durable review surface for the
resolved result.

## Decision

The reference system has five parts, decided together.

**1. One reference model.** `tools/lib/refs.ts` extracts references from final neutral bodies after
overlays and before per-harness rewriting. Rewrite, validation, and sync consume that grammar.
References have three tiers according to the authority their spelling earns:

- **Facts** are namespaced spellings. Own plugin namespaces are linked and a missing target is an
  error. Recognized upstream namespaces that remain after rewriting produce warnings. Unknown
  namespaces are currently ignored; treating them as facts is a Known Gap.
- **Paths** are relative file links. They become build state only where the transformation could
  have broken them: a sibling-item climb must still land, and a missing same-item file is a finding
  when upstream still ships it.
- **Candidates** are bare known item names. They are heuristic ordinary words, surfaced for human
  review and never promoted to build state without an authored spelling change.

Detection runs before rendering because OpenCode's bare output names cannot be parsed back into
unambiguous identities.

**2. Spelling records edge direction.** Runtime audience determines validity, so owned body text
carries the edge kind:

| Neutral spelling | Kind | Valid target posture |
|---|---|---|
| `ns:name` | model-edge | `auto` or `both` |
| `/ns:name` | user-pointer | `manual` or `both` |

Each emitter localizes both forms into its own address space. The convention binds overlays,
patches, and original skills; untouched upstream bare prose remains candidate-tier until curation
touches it.

**3. `validate` links each emitted address space.** For own-plugin facts, every target must exist and
be reachable by the audience encoded in the spelling. Referenced parked files and admitted relative
paths must land. A wrong-kind edge to a `both` target remains a review concern because either
audience can reach it. When a skill-relative path works from the skill copy but not from a converted
command, the linker warns rather than errors: the reference is sound and artifact shape caused the
break.

The linker keys target state by output name. `validate` rejects duplicate kind/name pairs it can
observe across distinct plugin directories and detects an own skill overwriting a curated item. It
does not yet catch duplicates produced within one plugin or manifests that repeat `plugin.name`;
those uniqueness holes are a Known Gap.

Reachability assumes `invocation` is one of `auto`, `manual`, or `both`. The TypeScript type states
that enum, but `loadManifest` does not validate YAML values at runtime and build switch defaults do
not define invalid-value semantics. Invalid values therefore have undefined behavior until the
loader enforces the enum.

**4. `depends_on` declares model-edges in both directions.** Each item lists output names targeted by
its model-edge facts. An undeclared fact and a stale declaration are both errors. User-pointers are
not dependency declarations. Bodies speak neutral upstream addresses while the manifest speaks
output names; the linker owns that mapping. Dependency targets are not content-hashed because their
upstream updates should flow; merged content is guarded through ADR-0001 instead.

Overlay and patch ownership has a related boundary: the build compares paths already stamped in
`overlays/overlays.lock.json`, including declared merge inputs. It does not reconcile the live set
of overlay files or patch targets against that lock, so a newly targeted path can skip review. That
target-set reconciliation is a Known Gap.

**5. The build emits a ledger.** `docs/ledger.json` is generated and committed per item and harness.
It records source, declared invocation, body mode, merge-source addresses, declared dependencies,
description, emitted artifact kinds, fact edges, OpenCode dropped keys, and parked files in a
deterministic order. It does not record the complete Claude frontmatter or resolved invocation
flags; full flag coverage is a Known Gap. The ledger is the review surface for posture, shape, and
edge changes, and CI's stale-output check keeps it aligned with generated trees.

Declaring edge kinds only in the manifest was rejected because runtime prose could contradict a
green declaration. Deriving identity from built OpenCode text was rejected because bare words are
not symbols. Warnings for undeclared facts were rejected because they would make the declaration
contract optional. Hashing dependency targets was rejected because a reference wants target
updates to flow, unlike content an overlay owns.

## Consequences

- A model-edge exists twice — body fact and manifest declaration — so body edits that add or remove
  one require a same-change manifest edit. The duplication buys a stale-edge error in either
  direction.
- The linker proves resolvability and audience reachability, not whether a model will traverse an
  edge or follow its discipline. Runtime behavior is measured under the
  [harness-invocation protocol](../../experiments/harness-invocation/protocol.md), outside CI.
- Candidate prose remains a deliberate blind spot. Curation contact promotes it into the convention
  and forces incoherent posture or naming decisions at that boundary.
- The path tier trades silence for narrowly scoped findings. It avoids treating all illustrative
  upstream paths as dependencies while still catching breakage caused by rename, omission,
  exclusion, or conversion.
- Deterministic ledger diffs make semantic changes reviewable, but the ledger remains a selected
  projection rather than a complete serialization of emitted artifacts.
