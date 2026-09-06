# Distribution and installation

Date: 2026-08-25

## Responsibility

This document owns the mechanics after OpenCode emission: Bundle identity, Package contents and
transport, global Native-tree composition, and the Selection/Ownership/Plan/Apply/Recovery lifecycle.
Capitalized terms keep their definitions in [`CONTEXT.md`](../../CONTEXT.md); this document describes
how the implementation composes them. [ADR-0001](../adr/0001-submodule-manifest-overlay-architecture.md)
records why generated Bundles and installer output are committed,
[ADR-0002](../adr/0002-multi-harness-output.md) records why OpenCode receives native files rather
than a runtime adapter, and [ADR-0004](../adr/0004-minimal-toolchain.md) records the consumer-side
compilation and toolchain trade-offs.

This installer is OpenCode-specific. Claude Code consumes the independently emitted Plugins through
the repository marketplace; installing a Plugin neither selects nor installs its same-named Module.

## Bundle and Package identity

The build writes one `opencode/<module>/` Bundle per curation manifest. Its `manifest.json` uses
`schemaVersion: 2` and records the Module name, curator-facing version, mandatory `requiredModules`,
and every other Bundle-relative path's SHA-256 and POSIX mode. The Module digest is the SHA-256 of a
locale-independent serialization whose field order is `schemaVersion`, `requiredModules`, then
`files`; requirement names and file paths use ordinal ordering, and each file identity is `sha256`
then `mode`. Module name and curator-facing version remain outside that content/dependency digest.
`manifest.json` excludes itself from the file map; a Module with no curated items still has a
manifest plus repository `LICENSE` and `THIRD_PARTY_NOTICES.md` distribution metadata. Missing
required Module names in the Package are a graph finding. Digest serialization and hashing are
implemented in
[`digestModulePayload`](../../tools/lib/opencode-bundle.ts), and manifest creation is implemented in
[`createModuleManifest`](../../tools/lib/opencode-bundle.ts).

Checkout Bundles and Install state are schema 2. The still-public Release Package
`installer-v0.3.0` is a schema-1 historical source snapshot; its download and digest recipe is
unchanged. There is no compatibility reader between the two formats.

Bundle verification rejects missing, extra, tampered, or linked files and checks the recorded mode
on POSIX. Repository validation also runs the case-insensitive alias checks, requires exactly the
Module roots named by curation, and checks each manifest's Module name and version against its
manifest source. These are integrity checks over final emitted bytes, not another transformation
pass ([`verifyModuleManifest`](../../tools/lib/opencode-bundle.ts#L278-L370)).

The npm-format Package contains package metadata, README, the repository license and notices,
committed `dist/` installer JavaScript, and every generated Bundle. Each Bundle also carries its
source-specific notice and exact upstream license copies. The Package excludes TypeScript authoring
sources, upstream worktrees, Plugin output, overlays, experiments, and other documentation. Focused
package tests require the packed installer, licenses and notices, and every Bundle file and manifest
to match the committed emit byte-for-byte
([`tools/install-opencode.test.ts`](../../tools/install-opencode.test.ts#L889-L950)). Consumers do
not compile the installer.

Remote delivery uses that exact tarball as a GitHub Release asset, not an npm publication or Git
package install. The Release is versioned but not immutable: the tag and target commit identify the
intended source point, while the repository-recorded Package SHA-256 detects replacement or
corruption but does not prevent an authorized re-upload. A runnable recipe is published only after a
current asset passes the release gate; the root [`README.md`](../../README.md#opencode-from-a-release-package)
owns consumer instructions.

## Byte-preserving composition

Installation does not parse Markdown, resolve invocation, localize references, or synthesize harness
configuration. Before planning, the CLI loads and verifies every Bundle in the Package, including
distribution-only licenses and notices. Planning then selects only manifest paths under `skills/`,
`commands/`, and `agents/` as Native-tree content; the known Bundle-root license and notice paths are
verified Package metadata and never become Destination Ownership. Any other non-Native manifest path
is rejected. For each add or replacement, Apply stages the selected source bytes, verifies their hash
and intended mode, and places them at the same relative path in the Destination. POSIX mode
participates in matching and is applied; on Windows the intended mode remains recorded while byte
identity is the enforced filesystem comparison.

The resulting Native tree is therefore a composition of already transformed Bundle Native payloads,
not a copy of Bundle distribution metadata. The
packed-bin integration test compares its paths, bytes, Install state, and status output with the
checkout CLI ([`tools/install-opencode.test.ts`](../../tools/install-opencode.test.ts#L968-L1041)).

## Destination, Selection, and Ownership

The installer resolves exactly one global Destination: `$XDG_CONFIG_HOME/opencode` when XDG config
home is set, otherwise `$HOME/.config/opencode`. A non-empty `OPENCODE_CONFIG_DIR` is refused, and
there is no project-local target. OpenCode may discover artifacts through other locations; that
harness capability does not make those locations supported installer Destinations
([`resolveDestination`](../../tools/lib/opencode-install-state.ts#L495-L507)).

Install state lives at `<Destination>/.deniz-skills/install.json`. Checkout state uses
`schemaVersion: 2`. It persists Selection and one Ownership claim per managed Native-tree path,
including the responsible Module, hash, and mode. Each selected Module also records its version,
digest, and required Modules. Selection is read from this state, never inferred from files present on
disk. Deleting the state does not turn owned files into a supported fresh install; it loses the
ownership evidence needed to distinguish them from Unowned paths. The journal envelope remains
schema 1; its old/new state evidence now contains schema-2 Install state.

Only paths under `skills/`, `commands/`, or `agents/` can be owned. Destination, metadata, and managed
ancestors must be ordinary directories and managed leaves ordinary files; symlinks and junctions are
not followed. The installer does not edit OpenCode JSON/JSONC, add a plugin entrypoint, or claim
OpenCode's own support files. An existing Unowned file is a Collision even when its bytes happen to
match a Bundle; ownership is never silently taken over.

## Plan

Every mutating request computes a Plan before disk moves:

- Install adds named Modules (or all Package Modules) to Selection and reconciles those Modules.
- Update keeps Selection unchanged and reconciles the whole Selection against the current Package.
- Remove subtracts named selected Modules (or all of Selection) and reconciles them to no files.

Planning compares current Ownership, target Bundle claims, and explicit observations of every
currently owned or Package-manifest path. It emits deterministic add, replace, mode-change, remove,
and missing-claim-drop operations plus any ownership transfers and Selection changes. A Local
modification, State drift, Collision, type/link mismatch, unknown Module, missing observation,
unresolved double claim, or missing required Module becomes a finding. After constructing the
proposed next state's Module records, Plan reports a `missing_dependency` finding for each required
Module absent from that Selection. Affected non-remove Modules take current Package metadata;
surviving unaffected Modules keep their recorded requirements. The check is presence-only: it does
not compare versions or item/API compatibility, and it does not automatically add, cascade-remove,
or range-resolve those names. Any finding clears operations and leaves the next state equal to
current state ([`planReconcile`](../../tools/lib/opencode-install-plan.ts)).

Without `--yes`, a mutating command prints this Plan without taking the mutation lock or creating the
Destination. `status` is also read-only; it reports Selection, currency against Package digests,
recorded Selection dependency findings, proposed Update dependency findings separately from other
Plan findings, lock state, and Recovery. A recorded incomplete Selection exits nonzero even when a
proposed Update would repair it. Status never mutates Install state. A missing owned path of an
affected Module blocks Install or Update, but Remove of that Module can drop the already-missing
claim. A Local modification blocks Remove so neither Selection nor Ownership changes around altered
bytes.

Pending Recovery takes precedence over a requested Plan. A plan-only mutating command prints the
Recovery action and exits nonzero because no Plan was produced; `--yes` applies only Recovery and
requires the original request to be issued again.

## Apply

`--yes` acquires the Destination lock and recomputes the Plan under that lock. It never applies the
previously printed snapshot. If Recovery is present, the invocation applies only Recovery and exits;
the original request must be issued again.

For a finding-free Plan, Apply:

1. validates Destination topology and keeps transaction data on the same filesystem;
2. records immutable old and new Install-state bytes and a write-ahead journal in a transaction
   directory;
3. stages and verifies every add/replace source in the Plan before moving managed files;
4. rechecks each managed path immediately before backup, placement, or mode change;
5. renames replaced/removed files into transaction backups, places staged files, and applies modes;
6. commits the new Install state only after file placement, verifies the committed result, prunes
   only now-empty managed subdirectories, and removes transaction data.

Unknown files and non-empty directories survive pruning, and the top-level Native roots are retained.
Apply refuses a Plan with findings, a stale or unheld lock, cross-filesystem rename topology,
ambiguous path evidence, or a pending transaction
([`applyPlan`](../../tools/lib/opencode-install-apply.ts#L2200-L2460)).

## Recovery

The journal stores operation intent, applied evidence, exact old/new state digests, immutable state
copies, backups, created directories, and post-commit prune candidates. Inspection validates the
journal and persisted evidence before classifying Recovery:

- when the Destination still matches old Install state, rollback restores exact prior bytes and
  modes, restores prior Install-state bytes, and removes directories created by the transaction;
- when new Install state is already committed, finalize verifies the committed files, completes safe
  pruning, and removes transaction debris;
- ambiguous, malformed, linked, missing, or digest-inconsistent evidence blocks Recovery without
  guessing.

Recovery restores the prior state or finalizes cleanup of an already committed state. It does not
resume or finish the original Install, Update, or Remove request. The classification and execution
are in [`inspectRecovery`](../../tools/lib/opencode-install-apply.ts#L1469-L1545) and
[`applyRecovery`](../../tools/lib/opencode-install-apply.ts#L2462-L2502).

## Full estate versus installed Selection

Compilation and `validate` reason over the complete generated estate: every Plugin, Module, formal
fact, and Bundle is present together. The installer, by contrast, still takes an explicit Selection
rather than installing every Package Module. Schema-2 Module manifests and Install state record
`requiredModules` with file identity. Plan refuses a final Selection that omits a Module another
selected Module records as required. The installer does not consume `docs/ledger.json` or treat
compile-time `depends_on` as an install-time expansion source. Presence-checking recorded
`requiredModules` is not automatic Selection expansion.

Consequently, successful full-estate linking plus successful installation of a subset proves Bundle
integrity, collision-free composition, and that every recorded required Module is also selected. It
does **not** prove cross-version item or API compatibility. Automatic Selection expansion and
version-range resolution remain out of scope. The durable symbol-side proof boundary is detailed in
[References and linking](references-and-linking.md).

## Other current limits

- The installer has no force, reset, legacy-takeover, project-local, `OPENCODE_CONFIG_DIR` target, or
  JSON configuration mutation path. Existing files and lost ownership state require manual
  resolution.
- The CLI verifies every Package Bundle before any action, even when the request names only one
  Module, and rejects a Package whose required Module names are absent. Plan then refuses an
  incomplete Selection. Neither step automatically adds Modules.
- Release hash verification detects changed Package bytes; it cannot make a mutable Release asset
  immutable.
- Committed tests and the installer experiment establish Plan/Apply behavior, byte equality, and
  Native discovery for the measured environment. They do not establish that a model follows every
  parked-body stub or whether a global Native-tree body read prompts a human for permission; the
  [current installer record](../../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md#explicitly-unmeasured)
  keeps those runtime observations explicitly unmeasured.
