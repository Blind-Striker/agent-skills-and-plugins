# References and linking

Date: 2026-08-19

## Responsibility

This document owns the current reference model from an assembled body through localization,
linking, dependency declarations, and ledger review. It does not define curation fields; authoring
syntax remains in [`curation/SCHEMA.md`](../../curation/SCHEMA.md). The reason strings are treated as
symbols and checked in tiers is [ADR-0008](../adr/0008-references-are-symbols.md); the reason each
harness gets its own spelling is [ADR-0002](../adr/0002-multi-harness-output.md).

## One grammar, three evidence tiers

[`tools/lib/refs.ts`](../../tools/lib/refs.ts) is the shared namespaced-reference scanner used by
rewriting, validation, and ledger generation. Detection happens while identity is still explicit;
OpenCode's final bare names cannot be parsed back into unambiguous symbols.

### Facts

A lowercase namespaced spelling `namespace:name` is a model-edge fact. A leading slash,
`/namespace:name`, changes the fact to a user-pointer; the slash is direction metadata and is not
part of the address itself. The scanner rejects token fragments, chained addresses, URLs, and
uppercase/snake-case lookalikes rather than guessing.

In an owned body, the spelling states the runtime audience:

- a model-edge requires a target the model can reach in both generated harness trees;
- a user-pointer requires a target the user can reach in both generated harness trees.

After localization, an own namespace is authoritative build state. A missing own target or an
audience mismatch is an error. A recognized upstream namespace left unreplaced is a per-reference
warning. Other namespaces warn once with occurrence count and example paths, except for a narrow
exact-address allowlist of known prose lookalikes. Unknown namespaces are not silently promoted into
dependencies.

### Paths

Relative Markdown links are deterministic paths, but the linker reports only breakage the
transformation can reasonably have caused:

- a sibling-item climb such as `../other-item/...` must still land after rename, exclusion,
  omission, parking, or conversion;
- a missing same-item file is a finding when the upstream item still contains that file;
- other same-item paths that upstream never contained remain ordinary illustrative prose.

The linker also checks explicit `skills/<name>/...` references in parked commands and bodies against
the parked file set. A relative path that works from a skill copy but not from a converted command is
reported as a warning: the symbol is present, but the additional artifact location broke the
filesystem spelling. These checks live in [`tools/validate.ts`](../../tools/validate.ts#L688-L829).

### Candidates

A known output name appearing as a standalone bare word is a candidate, not a fact. `/name` without
a namespace is still candidate-tier prose. Candidate matching deliberately over-reports ordinary
words, so candidates are surfaced for human reading and never enter `depends_on`, linker success, or
the ledger merely because a patch touched the surrounding body.

The intended automatic surface is the pin-change report in `sync`, which compares candidate hits
before and after a changed upstream `SKILL.md`. Promotion requires an authored namespaced spelling,
not confidence in a heuristic.

## Localization

The rewrite map keys the scanner's upstream namespace and filesystem address to the resolved output
name. A renamed item therefore changes the symbol target, not just its destination filename. Claude
Code receives `<plugin>:<output-name>` and preserves a pointer's leading slash; OpenCode receives the
bare output name and preserves the slash. The map and in-place rewrite are
[`tools/lib/rewrite.ts`](../../tools/lib/rewrite.ts#L14-L66).

Both trees are rewritten from their own pre-localized copies. OpenCode is not produced by stripping
namespaces from already localized Claude text. Validation rejects any own output namespace that
survives in `opencode/`.

Curating one upstream source more than once is currently last-write-wins in the source-address map.
Validation warns with both outputs because every upstream fact for that source will localize to the
last item; the warning is not a proof that the ambiguity is harmless.

## `depends_on` and audience reachability

`depends_on` is the manifest-side declaration of model-edge facts only. It contains resolved output
names, while bodies keep neutral namespaced addresses. The linker derives model targets from all
Markdown shipped by each non-excluded manifest item and enforces exact agreement in both directions:
an undeclared fact and a declaration with no shipped fact are both errors. User-pointers, paths, and
candidates are not dependency declarations.

For each fact, the linker checks the canonical namespaced Plugin body and asks whether the target is
reachable in both emitted address spaces. Claude reachability comes from emitted invocation flags
and artifact posture; OpenCode reachability comes from the existence of the corresponding skill or
command. This is a generated-estate link, not a runtime call graph
([`tools/validate.ts`](../../tools/validate.ts#L570-L674)).

Reachability is not propensity. A green link proves that the intended audience has a mechanism to
reach the target; it cannot prove that a model will select it, follow a pointer, or obey the target's
discipline. Those are runtime observations governed by the
[harness invocation protocol](../../experiments/harness-invocation/protocol.md), with bounded evidence
in the committed [records](../../experiments/harness-invocation/records/README.md).

## Ledger semantics

`docs/ledger.json` is regenerated after both trees have their final reference spelling. Each
non-excluded manifest item has one key shaped
`<plugin>/<resolved-output-kind>/<output-name>`. OpenCode expansion does not create an extra top-level
entry: a skill resolved as `manual`, for example, remains under `/skill/` while its OpenCode artifact
list records a command.

Each entry projects the review-relevant state: source, declared invocation and body mode, merge-source
addresses, declared dependencies, emitted artifact kinds, description, own fact edges in each
harness spelling, emitted Claude boolean invocation flags, an item-level OpenCode dropped-key list,
and parked files. OpenCode edges are respelled from the known namespaced facts rather than
rediscovered from bare text ([`tools/lib/ledger.ts`](../../tools/lib/ledger.ts#L52-L139)).

The ledger is deterministic but intentionally incomplete. It does not serialize complete emitted
files, path-tier findings, candidates, overlay stamp filenames, or Module install dependencies.
Its dropped-key field follows the item projection's skill branch whenever an OpenCode skill exists.
For a `both` item, it can therefore omit keys dropped only from the companion command, while the
build report still reports those drops. Original skills under `skills/` are also outside the
manifest-item loop. Bundle integrity and installation state have their own manifests and are owned
by [Distribution and installation](distribution-and-installation.md).

## Proof boundary and current limits

- `validate` links the complete generated Plugin/Module estate. An installer Selection is a later,
  independent boundary; a full-estate proof does not establish dependency closure for every subset.
- Same output names in different artifact kinds are legal, but the current rewrite and linker target
  maps use bare output name rather than kind-qualified identity. A same-name cross-kind case can
  overwrite target state or lose the intended kind distinction. This is an implementation limit,
  not a promise that kind never matters.
- Linker target state is name-only and records audience reachability rather than target kind. A
  `both` target can satisfy either audience edge, so a semantically wrong target kind may remain a
  human review concern even when linkage is green.
- The `sync` CLI currently derives its candidate universe from segment 1 of ledger keys
  (`<plugin>/<kind>/<name>`), so it supplies artifact kinds instead of output names
  ([`tools/sync.ts`](../../tools/sync.ts#L116-L123)). Unit tests inject correct names directly. Until
  that defect is fixed, CLI candidate-edge reporting is not a reliable complete surface.
- The namespaced fact scanner is lowercase-only. Capitalized spellings can evade fact detection.
- Bare-name composition can work at runtime while remaining deliberately unguarded. Its success does
  not make candidates authoritative after the fact.
- Original skills under `skills/` may be edge targets, but the current derived-edge source scan walks
  manifest items only, so references originating in an own skill are not scanned or declared
  ([`validateRepo`](../../tools/validate.ts#L604-L674)).
- Relative-path checks are attribution-aware, not a general Markdown link checker. A silent path may
  still be wrong upstream; a warning on a converted command may still require a body or emitter
  decision.
- The linker proves symbol existence and audience reachability, not model behavior. Runtime samples
  remain version-, model-, prompt-, and repetition-bounded evidence rather than deterministic rates.
