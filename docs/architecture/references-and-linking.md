# References and linking

Date: 2026-09-07

## Responsibility

This document owns the current reference model from an assembled body through localization,
linking, dependency declarations, and ledger review. It does not define curation fields; authoring
syntax remains in [`curation/SCHEMA.md`](../../curation/SCHEMA.md). The reason strings are treated as
symbols and checked in tiers is [ADR-0008](../adr/0008-references-are-symbols.md); the reason each
harness gets its own spelling is [ADR-0002](../adr/0002-multi-harness-output.md).

## One grammar, three evidence tiers

[`tools/lib/refs.ts`](../../tools/lib/refs.ts) is the shared namespaced-reference scanner used by
rewriting, validation, and ledger generation. Detection happens while identity is still explicit;
OpenCode's final bare names and Codex's converged `$` spelling cannot be parsed back into the full
neutral symbol semantics.

### Facts

A lowercase namespaced spelling `namespace:name` is a model-edge fact. A leading slash,
`/namespace:name`, changes the fact to a user-pointer; the slash is direction metadata and is not
part of the address itself. The scanner rejects token fragments, chained addresses, URLs, and
uppercase/snake-case lookalikes rather than guessing.

In an owned body, the spelling states the runtime audience:

- a model-edge requires a target the model can reach in every generated harness tree;
- a user-pointer requires a target the user can reach in every generated harness tree.

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
filesystem spelling. These checks live in [`tools/validate.ts`](../../tools/validate.ts#L754-L781)
and [`tools/validate.ts`](../../tools/validate.ts#L783-L895).

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
name. A renamed item therefore changes the symbol target, not just its destination filename.
Original skills add their authored `<plugin>:<top-level-directory>` identity to the same map, so a
curated item can guard an original-skill target without inventing an upstream address. Claude Code
receives `<plugin>:<output-name>` and preserves a pointer's leading slash; OpenCode receives the bare
output name and preserves the slash; Codex receives `$<plugin>:<output-name>` for both edge kinds.
Codex rendering consumes a pointer's neutral leading slash instead of producing `/$...`; model and
pointer remain separate semantic facts even though their final spelling converges. The map and in-place rewrite are
[`tools/lib/rewrite.ts`](../../tools/lib/rewrite.ts).

All three trees are rewritten from their own pre-localized copies. OpenCode and Codex are not
produced by adapting already localized Claude text. Validation rejects any own output namespace
that survives in `opencode/`, and rejects dangling, unrendered, or `/$` Codex references.

Curating one upstream source more than once is currently last-write-wins in the source-address map.
Validation warns with both outputs because every upstream fact for that source will localize to the
last item; the warning is not a proof that the ambiguity is harmless.

## `depends_on` and audience reachability

`depends_on` is the manifest-side declaration of model-edge facts only. It contains resolved output
names, while bodies keep neutral namespaced addresses. The linker derives model targets from all
Markdown shipped by each non-excluded manifest item and enforces exact agreement in both directions:
an undeclared fact and a declaration with no shipped fact are both errors. User-pointers, paths, and
candidates are not dependency declarations.

For each fact, the linker checks the canonical neutral kind and asks whether the target has the
required audience surface. Claude reachability comes from emitted invocation flags and artifact
posture; OpenCode reachability comes from the corresponding skill or command; Codex reachability
comes from the skill plus its implicit policy, while explicit invocation remains available for every
skill. This is a generated-estate link, not a runtime call graph
([`tools/validate.ts`](../../tools/validate.ts#L636-L740)).

Reachability is not propensity. A green link proves that the intended audience has a mechanism to
reach the target; it cannot prove that a model will select it, follow a pointer, or obey the target's
discipline. Those are runtime observations governed by the
[harness invocation protocol](../../experiments/harness-invocation/protocol.md), with bounded evidence
in the committed [records](../../experiments/harness-invocation/records/README.md).

## Ledger semantics

`docs/ledger.json` is regenerated after all three trees have their final reference spelling. Each
non-excluded manifest item has one key shaped
`<plugin>/<resolved-output-kind>/<output-name>`. OpenCode expansion does not create an extra top-level
entry: a skill resolved as `manual`, for example, remains under `/skill/` while its OpenCode artifact
list records a command.

Each entry projects the review-relevant state: source, declared invocation and body mode,
merge-source addresses, declared dependencies, emitted artifact kinds, description, own fact edges
in each harness spelling, emitted Claude boolean invocation flags, OpenCode drops and parked files,
and Codex identity, source/resolved/emitted kinds, material kind transformation, invocation
capabilities, policy files, metadata drops, and body transformations. OpenCode and Codex edges are
respelled from the known neutral facts rather than rediscovered from bare or converged final text.

The ledger is deterministic but intentionally incomplete. It does not serialize complete emitted
files, path-tier findings, candidates, overlay stamp filenames, or Module install dependencies.
Its dropped-key field follows the item projection's skill branch whenever an OpenCode skill exists.
For a `both` item, it can therefore omit keys dropped only from the companion command, while the
build report still reports those drops. Original skills under `skills/` are also outside the
manifest-item loop. Bundle integrity and installation state have their own manifests and are owned
by [Distribution and installation](distribution-and-installation.md).

## Proof boundary and current limits

- `validate` links the complete generated Claude Plugin, Codex Plugin, and OpenCode Module estate.
  An installer Selection is a later, independent boundary. Full-estate linking does not prove that
  every subset is a valid Selection or
  that selected Bundles are item/API compatible. The installer separately presence-checks recorded
  `requiredModules` against the Selection a request would produce; that is not automatic expansion
  and is owned by [Distribution and installation](distribution-and-installation.md).
- Same output names in different artifact kinds are rejected within one plugin because Codex flattens
  every resolved kind into one skill namespace. Cross-plugin names remain qualified in Codex, while
  OpenCode's global destination collision checks continue to apply independently.
- Linker target state is name-only and records audience reachability rather than target kind. A
  `both` target can satisfy either audience edge, so a semantically wrong target kind may remain a
  human review concern even when linkage is green.
- The `sync` CLI derives its candidate universe from the output-name segments of ledger keys
  (`<plugin>/<kind>/<name>`) plus authored original-skill directory names. Its candidate-edge report
  is still bounded by the tier: it compares known output names across one changed `SKILL.md`, so it
  surfaces edges for reading and never promotes one to a fact, a declaration, or build state.
- The namespaced fact scanner is lowercase-only. Capitalized spellings can evade fact detection.
- Bare-name composition can work at runtime while remaining deliberately unguarded. Its success does
  not make candidates authoritative after the fact.
- Original skills under `skills/` are guarded edge targets: curated items can author their
  `<plugin>:<directory>` fact, localize it for all three harnesses, and declare the output name in
  `depends_on`. The derived-edge source scan still walks manifest items only, so references
  originating in an original skill are not scanned or declared
  ([`validateRepo`](../../tools/validate.ts#L670-L740)).
- Relative-path checks are attribution-aware, not a general Markdown link checker. A silent path may
  still be wrong upstream; a warning on a converted command may still require a body or emitter
  decision.
- The linker proves symbol existence and audience reachability, not model behavior. Runtime samples
  remain version-, model-, prompt-, and repetition-bounded evidence rather than deterministic rates.
