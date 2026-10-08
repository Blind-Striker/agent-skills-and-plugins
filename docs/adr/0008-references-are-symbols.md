# ADR-0008: References are symbols — one model, a linker, a ledger

Date: 2026-10-08
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
   emitter localizes that intent into its target address space, which need not reuse the neutral
   punctuation: OpenCode renders a model-edge as `<plugin>.<name>` and a user-pointer as
   `@<plugin>.<name>` for a skill or agent target or `/<plugin>.<name>` for a command target,
   because a user attaches an OpenCode skill rather than running it as a slash command. Touching
   candidate prose in an overlay or patch does not promote it; promotion requires an authored
   namespaced spelling.
3. **Each emitted tree is linked.** An authoritative fact must resolve in each harness and be
   reachable by the audience its spelling names. Admitted paths must land. A target is identified
   by its bare output name across the whole estate: bare names are unique across all Modules as
   curation policy ([ADR-0002](0002-multi-harness-output.md)), so the neutral symbol,
   `depends_on`, and the linker need no Module or artifact-kind qualifier.
4. **Model edges are declared twice.** The body carries the model-edge fact and `depends_on` carries
   its manifest declaration. Either an undeclared fact or a stale declaration fails. User-pointers,
   paths, and candidates are not dependency declarations, and dependency targets are not
   content-hashed because their updates should flow.
5. **The ledger is a deterministic review projection.** Generated per-item, per-harness state makes
   posture, shape, and edge changes reviewable, but it is intentionally not a complete serialization
   of emitted content or every finding.

Declaring edges only in the manifest was rejected because runtime prose could contradict a green
declaration. Deriving identity from built harness text was rejected because localized spellings
lose part of the neutral symbol: Codex renders both edge kinds with one spelling, and an OpenCode
model-edge or `@` pointer does not state the target's artifact kind. Warnings for undeclared facts
were rejected because they make the declaration contract optional. Hashing dependency targets was
rejected because reference targets should update without taking body ownership. Keeping
same-name, different-kind targets distinct in the linker was rejected in favour of bare-name
uniqueness: Codex flattens every resolved kind into one plugin-local skill namespace, so a
same-plugin pair cannot be emitted at all, and qualifying `depends_on` and the linker by kind and
Module would add grammar for a case the curation policy already excludes.

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
- Reachability is only as strong as the harness's own boundary. A Codex or OpenCode `manual` skill
  is not offered to the model, but it is not unloadable;
  [ADR-0005](0005-invocation-intent-in-the-manifest.md) accepts that meaning without a compensating
  rule.
- Candidate prose remains a deliberate blind spot. A body patch does not promote it merely by
  contact: its corpus convention persists until an author deliberately changes the spelling into a
  model-edge fact and declares it. In OpenCode a bare name is not an artifact ID, so a load-bearing
  bare handle fails at runtime even where its target exists; promotion is the fix, not a wider
  candidate tier.
- The path tier trades silence for narrowly scoped findings. It avoids treating all illustrative
  upstream paths as dependencies while still catching attributable breakage caused by rename,
  omission, exclusion, or conversion.
- Deterministic ledger diffs make semantic changes reviewable, but the ledger remains a selected
  projection rather than a complete serialization of emitted artifacts.
- Bare-name keys rest on the uniqueness policy, and the build enforces it: identity preflight
  rejects a bare output name claimed twice anywhere in the repository, whatever its kind, so a
  user-pointer, which is not declared in `depends_on`, cannot meet two owners either. Within one
  plugin, a same-name identity in another artifact kind is also impossible because the Codex
  namespace is flat, and across plugins a model-edge to a name with several owners also fails as an
  ambiguous dependency target, because every model-edge is declared in `depends_on`. Reconsider
  bare-name keys together with that policy in ADR-0002.
