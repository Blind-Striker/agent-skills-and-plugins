# ADR-0008: References are symbols — one model, a linker, a ledger

Date: 2026-08-19
Status: Accepted

## Context

Cross-item references begin as strings in upstream and owned bodies, then land in harnesses with
different address spaces and reachability rules. Namespaced addresses can carry authority, while
bare names are often ordinary words and relative paths may be illustrative or already broken
upstream. Treating every resemblance as a dependency creates warning noise; treating every string
as prose allows real edges to disappear silently.

The system therefore needs one symbolic model before rendering, a proof over each emitted address
space, a declaration contract that can detect stale model edges, and a review surface that remains
useful without pretending to serialize the whole build.

## Decision

The reference system has five parts, decided together:

1. **One grammar with three evidence tiers.** Namespaced spellings are authoritative facts. Relative
   paths become build state only where breakage can be attributed to transformation. Bare known
   names are heuristic candidates for human review, never facts by resemblance alone. Detection
   happens while neutral identity is still explicit.
2. **Spelling encodes audience.** `ns:name` is a model-edge and `/ns:name` is a user-pointer. Each
   emitter localizes that intent into its target address space. Touching candidate prose in an
   overlay or patch does not promote it; promotion requires an authored namespaced spelling.
3. **Each emitted tree is linked.** An authoritative fact must resolve in each harness and be
   reachable by the audience its spelling names. Admitted paths must land. Legal same-name,
   different-kind identities retain their artifact-kind distinction rather than collapsing into one
   semantic target.
4. **Model edges are declared twice.** The body carries the model-edge fact and `depends_on` carries
   its manifest declaration. Either an undeclared fact or a stale declaration fails. User-pointers,
   paths, and candidates are not dependency declarations, and dependency targets are not
   content-hashed because their updates should flow.
5. **The ledger is a deterministic review projection.** Generated per-item, per-harness state makes
   posture, shape, and edge changes reviewable, but it is intentionally not a complete serialization
   of emitted content or every finding.

Declaring edges only in the manifest was rejected because runtime prose could contradict a green
declaration. Deriving identity from built OpenCode text was rejected because bare output text cannot
recover symbols. Warnings for undeclared facts were rejected because they make the declaration
contract optional. Hashing dependency targets was rejected because reference targets should update
without taking body ownership.

Current grammar, localization, linking, reachability, and ledger mechanics live in
[References and linking](../architecture/references-and-linking.md); manifest authoring lives in
[`curation/SCHEMA.md`](../../curation/SCHEMA.md). Overlay-lock and body-ownership mechanics are a
separate transformation concern owned by
[Transformation and emission](../architecture/transformation-and-emission.md) and the schema.

## Consequences

- A model-edge exists twice — body fact and manifest declaration — so body edits that add or remove
  one require a same-change manifest edit. The duplication buys a stale-edge error in either
  direction.
- Linking proves resolvability and audience reachability, not whether a model will traverse an edge
  or follow its discipline. Reachability is not propensity; runtime behavior is measured under the
  [harness-invocation protocol](../../experiments/harness-invocation/protocol.md), outside CI.
- Candidate prose remains a deliberate blind spot. A body patch does not promote it merely by
  contact: its corpus convention persists until an author deliberately changes the spelling into a
  model-edge fact and declares it.
- The path tier trades silence for narrowly scoped findings. It avoids treating all illustrative
  upstream paths as dependencies while still catching attributable breakage caused by rename,
  omission, exclusion, or conversion.
- Deterministic ledger diffs make semantic changes reviewable, but the ledger remains a selected
  projection rather than a complete serialization of emitted artifacts.
- The accepted kind distinction is not fully implemented: current rewrite and linker target maps
  use bare output names and can collapse a legal same-name cross-kind case. That is a known
  [implementation gap](../ROADMAP.md#known-gaps), not the decision.
