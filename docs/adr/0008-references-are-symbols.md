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
  error. Recognized upstream namespaces that remain after rewriting produce per-reference warnings.
  Other namespaces warn once per namespace, with occurrence count and example paths; a small
  exact-address list suppresses known CSS, label, placeholder, and runtime-address prose that is not
  an agent-artifact reference.
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

The linker keys target state by output name. Before rendering, both build and `validate` reject
duplicate `plugin.name` values and duplicate kind/name pairs among a manifest's non-excluded items;
the generated-tree scan retains the cross-plugin kind/name check, and `validate` detects an own skill
overwriting a curated item. The same output name in different artifact kinds is legal.

The manifest loader enforces the authoring enums before later consumers run: `invocation` accepts
`auto`, `manual`, or `both`; `as` accepts `skill`, `command`, or `agent`; and `body` accepts
`overlay` or `patch`. An absent optional field remains valid, while any other YAML value fails at
load time.

**4. `depends_on` declares model-edges in both directions.** Each item lists output names targeted by
its model-edge facts. An undeclared fact and a stale declaration are both errors. User-pointers are
not dependency declarations. Bodies speak neutral upstream addresses while the manifest speaks
output names; the linker owns that mapping. Dependency targets are not content-hashed because their
upstream updates should flow; merged content is guarded through ADR-0001 instead.

Overlay and patch ownership has a related boundary: before checking content hashes, the build
requires the primary lock keys in `overlays/overlays.lock.json` to equal the live upstream-backed
target set that `eject --bless` would stamp. Overlay-only additions and pure-add patch targets have
no upstream counterpart and remain outside that set. Declared merge inputs are guarded separately.

**5. The build emits a ledger.** `docs/ledger.json` is generated and committed per item and harness,
keyed by plugin, artifact kind, and output name. It records source, declared invocation, body mode,
merge-source addresses, declared dependencies, description, emitted artifact kinds, fact edges,
OpenCode dropped keys, and parked files in a deterministic order. It does not record the complete
Claude frontmatter or resolved invocation flags; full flag coverage is a Known Gap. The ledger is
the review surface for posture, shape, and edge changes, and CI's stale-output check keeps it aligned
with generated trees.

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
