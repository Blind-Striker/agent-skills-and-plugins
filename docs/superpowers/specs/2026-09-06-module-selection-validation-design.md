# Dependency-aware Module Selection: iteration 1

Date: 2026-09-06

Status: Approved; implementation plan prepared.

Baseline: `50d94324c610f5e1a5c8034f94f8782ddc029977`.

Execution: [implementation plan](../plans/2026-09-06-module-selection-validation.md).

This is a temporary implementation specification, not current mechanics. Operational scope lives in
[ROADMAP](../../ROADMAP.md#next-up); current behavior remains in
[distribution and installation](../../architecture/distribution-and-installation.md) and
[references and linking](../../architecture/references-and-linking.md). Preserve the vocabulary in
[CONTEXT.md](../../../CONTEXT.md). Move implemented mechanics to their canonical owners and consume
this spec when the feature closes.

## Goal and scope

Reject an Install, Update, or Remove whose final Selection omits a Module required by another
selected Module. Explain the missing requirements without automatically changing the requested
Selection. Status reports the dependency validity of the actual recorded Selection.

The first iteration proves presence of required Modules, not compatibility between their versions
or the availability of every individual target in an older provider. Automatic dependency
installation, cascade remove, version-range resolution, and `original_skills` declarations belong
to iteration 2. Bare references, user-pointers, external optional tools, and undeclared original-skill
outgoing references are not promoted into dependencies by this feature.

The user explicitly chose a clean format break for this single-user installation. Do not build
backward-compatibility readers, migration paths, legacy digest matching, metadata adoption, or
automatic profile cleanup. This replaces the earlier controlled-migration alternatives, not the
installer's safety requirements.

## Compile-time requirement derivation

Use existing non-excluded curation items and their declared `depends_on` output targets. Resolve
targets through compile-time identities, including original skills as possible targets. A target
whose owning Module is unknown or ambiguous must fail compilation rather than select an arbitrary
owner. Keep the existing formal-fact/declaration and audience checks; this projection is not a second
authoring authority or permission to bypass those checks.

For each source Module, collect the owning Modules of its declared targets, discard same-Module
edges, deduplicate, and ordinal-sort. Emit only direct requirements. No new hand-authored Module
dependency list is introduced. Do not read generated ledger JSON as an authored input or infer a
graph from localized OpenCode prose.

Every Bundle gets its complete list, including an explicit empty list when it has no requirements.
The complete generated Package must contain every required Module. Build/validation must reject
unresolved requirements; the Package verifier must check graph target presence as well as each
Bundle's integrity.

## Bundle identity

Module manifests use `schemaVersion: 2` and add mandatory `requiredModules: string[]` alongside
`module`, `version`, `digest`, and `files`.

- Requirement names are nonempty Module identities with no path separators or NUL characters.
- Lists must not contain duplicates or the owning Module itself. Writers emit ordinal order;
  normalization and identity calculation use the same ordering.
- Missing, mistyped, or invalid requirement metadata is an error, never an implicit empty list.
- The digest covers the schema-2 identity payload: the normalized requirement list and the existing
  normalized path/hash/mode map. Serialize the payload deterministically in the fixed field order
  `schemaVersion`, `requiredModules`, `files`; file identities use `sha256`, then `mode`.
- Module name and curator-facing version remain outside this content/dependency digest. Existing
  directory/name and expected-version checks still apply.
- `manifest.json` still excludes itself from the file map. Dependency metadata remains distribution
  metadata, not a new file installed into the Native tree.

Changing only requirements must change the Module digest. Changing input enumeration order must not.
Removing a requirement without updating the digest must fail verification. Do not add a separate
unverified sidecar or a legacy digest fallback.

## Install state

Install state uses `schemaVersion: 2`. Each Module entry records `version`, `digest`, and mandatory
`requiredModules`; file Ownership remains unchanged. Dependency metadata is persisted with the
Module identity it describes, not fetched later from whichever Package happens to be running.

Preserve strict parsing, duplicate-member rejection, deterministic serialization, and exact state
hashes. Structural parsing may represent a Selection with missing required Modules so status can
explain it; closure is a separate validation result rather than a JSON parse error.

Format 1 is unsupported. Reject old Bundle and Install-state formats with explicit errors and no
conversion. An old transaction is not reinterpreted as a new-format transaction. Unsupported-format
handling never deletes state, claims unowned files, or performs cleanup of a real profile.

## Final Selection planning

Keep `planReconcile` as the side-effect-free planning interface. Construct the target Module-state
map according to the request before validating its required-Module closure:

| Request | Selected set | Requirement metadata |
|---|---|---|
| Install | Current Selection plus explicitly requested Modules | Current Bundle metadata for requested Modules; recorded state for unaffected Modules |
| Update | Current Selection, unchanged | Current Bundle metadata for every selected Module |
| Remove | Current Selection minus explicitly requested Modules | Recorded metadata for every surviving Module |

Preserve existing `--all` semantics: Install selects every Package Module; Remove subtracts the
whole recorded Selection. Update still reconciles the whole Selection. Do not silently reconcile
unrequested Modules during Install or Remove.

For every selected Module, require each name in its direct requirement list to be selected. Checking
all selected Modules establishes transitive closure without flattening metadata or imposing a
topological installation order. Cycles are valid when every required member is selected; removing
one member while a surviving Module still requires it is blocked. Empty Selection is valid for
Remove-all; the existing empty-Selection Update error remains.

Report a deterministic `missing_dependency` finding for each unsatisfied direct edge, identifying
both the requiring and missing Module. Do not speculate about dependencies of an unselected version
or pretend to solve version constraints. Direct findings are sufficient; an auto-expanded command or
dependency chain renderer is not required.

Any finding preserves the current state and clears operations and transfers, as today. The printed
Plan remains zero-write. `--yes` still recomputes under the lock, and a blocked Plan never reaches
Apply. An explicit later request may repair an incomplete Selection by adding the required Modules.

## Status

Status uses the actual Module entries and requirements in Install state. It must not substitute a
new Package's requirements for an older selected Module or confuse a blocked proposed Update with
the dependency validity of the current Selection.

Reuse one small, pure requirement-checking function for the actual and proposed Module-state maps.
Retain existing file-integrity, currency, lock, Recovery, and update-related diagnostics, but label
current-Selection dependency findings separately from proposed-Update findings. Missing current
dependencies produce a nonzero status result. Status never mutates Selection or state.

## Apply and Recovery

No new transaction engine is needed. Carry the new state fields through the existing canonical
serialization and exact old/new state evidence. The current journal, locking, rollback, finalization,
and file precondition protections remain the safety mechanism for new-format transactions.

Metadata-only changes can produce a valid state-changing Plan with zero file operations; they must
persist transactionally rather than being mistaken for a no-op. Recovery restores the exact old
requirements and identity or finalizes the exact committed new state. It never reruns dependency
resolution or replaces recorded requirements with current Package metadata.

Pending Recovery continues to take precedence over a requested mutation. Do not weaken hash checks,
drop crash tests, or add compatibility branches to make the new format fit old snapshots.

## Acceptance evidence

| Area | Required cases |
|---|---|
| Derivation | Same-Module edges omitted; cross-Module edges collected once; original skills allowed as targets; excluded items ignored; unresolved or ambiguous owners rejected; pointers and bare names not promoted |
| Identity | Explicit empty requirements; invalid metadata rejected; deterministic ordering; requirement-only digest change; dependency tamper detected; missing Package target rejected |
| Planning | Direct and transitive missing requirements; satisfied cycles; required-Module removal blocked; entire cycle removal allowed; repair by explicit Install; Remove-all; unchanged Selection on failure |
| Mixed versions | Install/Remove preserve surviving Modules' recorded requirements; Update uses new metadata for the whole Selection; presence checks make no version-compatibility claim |
| Status | Actual requirements differ from available update requirements; current and proposed findings distinguished; nonzero current dependency failure; no writes |
| Format boundary | Version-1 manifests/state rejected; absent requiredModules rejected; no adoption or cleanup; malformed and duplicate-member Install-state JSON remains rejected |
| Transaction safety | Metadata-only Apply; dependency-blocked Apply; under-lock replanning; exact new-format rollback/finalize; recorded requirement bytes preserved after injected failure/crash |
| Distribution | Both harness builds remain valid; generated manifests match derived requirements; exact packed installer/Bundle verification; isolated Plan/Apply/status on Linux |

Update existing fixtures to the intended new format rather than supporting old fixtures as a hidden
compatibility contract. Keep explicit unsupported-format tests and all applicable existing safety
assertions. Follow the full [quality gate](../../engineering/quality-gates.md), including a second
generation/idempotence pass and the experiment selftest for touched experiment material.

## Implementation boundaries and closeout

The work belongs in the build/Bundle identity code, Install-state parsing/serialization, the pure
planner, status reporting, their focused tests, and Package verification. Reuse the existing identity
resolution and reference grammar; do not introduce a general graph framework or refactor unrelated
installer paths. Apply/Recovery changes should be limited to what new-format state actually needs.

When implemented, update `CONTEXT.md` for dependency-aware identity, architecture canon for the new
formats and Plan/status guarantees, consumer guidance for the format break, and ROADMAP for completed
scope. Use the existing Module-version policy when emitted identities change. Keep iteration 2 and
remaining linker/original-skill limitations visible.

Do not publish a new Package or replace any existing Release asset as part of this design task.
Do not install into or clean a real user profile. An isolated smoke environment is the verification
target; any real-profile reset or installation is a separate explicitly authorized operation.
