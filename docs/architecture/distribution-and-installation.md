# Distribution and installation

Date: 2026-10-08

## Responsibility

This document owns the mechanics after OpenCode emission: Bundle identity, Package contents and
transport, global Native-tree composition, the OpenCode runtime the installed tree targets, and the
Selection/Ownership/Plan/Apply/Recovery lifecycle.
Capitalized terms keep their definitions in [`CONTEXT.md`](../../CONTEXT.md); this document describes
how the implementation composes them. [ADR-0001](../adr/0001-submodule-manifest-overlay-architecture.md)
records why generated Bundles and installer output are committed,
[ADR-0002](../adr/0002-multi-harness-output.md) records why OpenCode receives native files rather
than a runtime adapter, and [ADR-0004](../adr/0004-minimal-toolchain.md) records the consumer-side
compilation and toolchain trade-offs.

This document states the decided OpenCode 2 behavior. Where the implementation has not caught up,
the gap and its responsible files are tracked in [`docs/ROADMAP.md`](../ROADMAP.md#known-gaps).

This installer is OpenCode-specific. Claude Code and Codex consume independently emitted native
Plugins through separate repository marketplaces; installing either Plugin neither selects nor
installs its same-named Module.

Codex distribution does not use the npm-format OpenCode Package or this repository's transactional
installer. `.agents/plugins/marketplace.json` points at `codex/<plugin>`, and native Codex marketplace
and plugin commands own installation, update, and removal. The supported Plugin hosts for this
milestone are Codex CLI and Codex in the ChatGPT desktop app; the IDE extension and native custom-
agent profile transport are not included.

## Target OpenCode runtime

Installed Bundles target OpenCode 2 only. The floor is v2.0.4, the first release in which skills are
no longer slash commands (upstream commit `199aabe9e2`, first contained in tag `v2.0.4`); the
measured reference is v2.0.23 (`anomalyco/opencode@0fd7e2829449b052abf0078666669302923d77af`).
OpenCode 2 ships as the npm package `@opencode/cli` with the binary `opencode`, and it is a parallel
opt-in line while OpenCode's default install channels still deliver OpenCode 1. OpenCode 1 (v1.18.x)
is not supported and there is no compatibility layer: the same output installed under OpenCode 1
degrades, because OpenCode 1 ignores the manual-skill metadata and exposes every skill as a slash
command. The installer does not check the OpenCode version. The emitted shapes that set this floor
are owned by [Transformation and emission](transformation-and-emission.md).

OpenCode 2 watches its config folders, so Native-tree changes need no restart: on v2.0.23 a newly
written skill appeared in the running service's `/api/skill` list within about two seconds, and a
newly written command appeared in `/api/command` without a restart. On Windows, after a
full-estate install or remove, the skill list matched the installed tree within 0.6 seconds of the
end of Apply and the two agents appeared or disappeared with it
([bulk-Apply record](../../experiments/harness-invocation/records/2026-10-09-opencode2-bulk-apply-windows.md));
changed files were not measured. A running service can therefore observe a partly applied tree
while Apply moves files, and on Windows it listed partial skill sets during an Apply. Apply's
transaction protects Install state and Recovery, not what the harness sees between two file moves.

Bulk changes can crash a running OpenCode 2 server on Windows:
[`anomalyco/opencode#47505`](https://github.com/anomalyco/opencode/issues/47505) reports that a bulk
update of global skills terminated the shared OpenCode 2 service, with a second report on v2.0.16.
The
[bulk-Apply record](../../experiments/harness-invocation/records/2026-10-09-opencode2-bulk-apply-windows.md)
reproduced it on v2.0.23: during a full-estate install or remove, an isolated server died with a Bun
segmentation fault in its file watcher in 4 of 33 sessions, all with the lab on the system volume
(4 of 18 there, 0 of 15 on a data volume). The installer's Apply itself completed every time; only
the server died, and the next `opencode` invocation starts it again. The mitigation is therefore a
warning, not a block: the installer neither detects nor stops a running OpenCode and never refuses
Apply because of it. After an Apply on Windows that wrote, removed, or changed the mode of at least
one Native path, it appends a warning to the printed Plan that a running OpenCode's background
service may have stopped (citing `anomalyco/opencode#47505`) and that reopening `opencode` restarts
it; the exit code is unchanged. A no-op Apply, a Plan without `--yes`, and Apply on other platforms
print no warning. [`tools/install-opencode.ts`](../../tools/install-opencode.ts) owns the wording.
Bulk Apply against a running server on Linux is not measured.

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
`installer-v0.3.0` is a schema-1 historical source snapshot whose Bundles carry OpenCode-1-shaped
output; its download and digest recipe is unchanged until the next Release replaces it. There is no
compatibility reader between the two formats: the installer has no schema-1 reader, and it rejects a
schema-1 Install state instead of converting it
([`parseInstallState`](../../tools/lib/opencode-install-state.ts)).

Bundle verification rejects missing, extra, tampered, or linked files and checks the recorded mode
on POSIX. Repository validation also runs the case-insensitive alias checks, requires exactly the
Module roots named by curation, and checks each manifest's Module name and version against its
manifest source. These are integrity checks over final emitted bytes, not another transformation
pass ([`verifyModuleManifest`](../../tools/lib/opencode-bundle.ts#L354-L446)).

The npm-format Package contains package metadata, README, the repository license and notices,
committed `dist/` installer JavaScript, and every generated Bundle. Each Bundle also carries its
source-specific notice and exact upstream license copies. The Package excludes TypeScript authoring
sources, upstream worktrees, Claude and Codex Plugin output, overlays, experiments, and other documentation. Focused
package tests require the packed installer, licenses and notices, and every Bundle file and manifest
to match the committed emit byte-for-byte
([`tools/install-opencode.test.ts`](../../tools/install-opencode.test.ts#L1046-L1107)). Consumers do
not compile the installer.

Remote delivery uses that exact tarball as a GitHub Release asset, not an npm publication or Git
package install. The Release is versioned but not immutable: the tag and target commit identify the
intended source point, while the repository-recorded Package SHA-256 detects replacement or
corruption but does not prevent an authorized re-upload. A runnable recipe is published only after a
current asset passes the release gate; the root [`README.md`](../../README.md#opencode-from-a-release-package)
owns consumer instructions. Those instructions describe the published Release, so they change only
in the release step. A new Release moves the `package.json` version, which names the Package asset,
and updates the recipe pins guarded by
[`tools/repository-docs.test.ts`](../../tools/repository-docs.test.ts) in the same change. A
Release requires a documented mitigation for the Windows bulk-Apply crash; the post-Apply warning
described under [Target OpenCode runtime](#target-opencode-runtime) is that mitigation.

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

Every Native-tree path already carries its Module's OpenCode namespace, because the emitter names
each artifact `<module>.<name>`: `skills/<module>.<name>/SKILL.md` with its bundled files below that
folder, `commands/<module>.<name>.md`, and `agents/<module>.<name>.md`. The installer places these
paths verbatim and never renames them. OpenCode 2 derives a skill ID from the skill's leaf folder
name, so the folder name is the ID a user attaches with `@<module>.<name>`. The emitter owns the
naming and the rule that a Bundle carries no `.md` file directly under `skills/` and no nested
`SKILL.md`, which OpenCode 2 would also register as skills
([Transformation and emission](transformation-and-emission.md)). Because each path starts with its
own Module name, two Modules of one Package do not claim the same Native path; the double-claim and
Collision findings still guard the Destination. An artifact with the same ID in another OpenCode
discovery root is resolved by OpenCode's own precedence, outside Ownership.

The resulting Native tree is therefore a composition of already transformed Bundle Native payloads,
not a copy of Bundle distribution metadata. The
packed-bin integration test compares its paths, bytes, Install state, and status output with the
checkout CLI ([`tools/install-opencode.test.ts`](../../tools/install-opencode.test.ts#L1125-L1198)).

## Destination, Selection, and Ownership

The installer resolves exactly one global Destination, the global config root OpenCode 2 reads:
`OPENCODE_CONFIG_DIR` when it is set and non-empty, otherwise `$XDG_CONFIG_HOME/opencode` when XDG
config home is set, otherwise `<home>/.config/opencode`. In OpenCode 2 `OPENCODE_CONFIG_DIR`
replaces the global root rather than adding a second one
(`anomalyco/opencode@0fd7e28 packages/util/src/global.ts:79`), so the installer honors it as the
Destination. OpenCode 2 also treats an empty value as unset: on v2.0.23 on the Linux host,
`opencode debug paths` reported the same XDG config root with the variable empty as with it unset
([discovery record](../../experiments/harness-invocation/records/2026-10-09-opencode2-discovery.md#empty-opencode_config_dir-on-the-linux-host-plan-task-15-step-4)). The installer refuses a relative `OPENCODE_CONFIG_DIR`, because it cannot know the
working directory of the OpenCode process that will read it. `<home>` is `os.homedir()` only, the
same home source OpenCode 2 uses; the installer never reads `HOME` or `USERPROFILE` directly, so a
shell that sets `HOME` to another folder (Git Bash on Windows does) cannot move the Destination away
from the root OpenCode reads. There is no project-local target. OpenCode may discover artifacts
through other locations, including project `.opencode` directories found by its ancestor walk and
the always-on compatibility roots `~/.claude/skills` and `~/.agents/skills`; that harness capability
does not make those locations supported installer Destinations
([`resolveDestination`](../../tools/lib/opencode-install-state.ts#L504-L522)).

The installer resolves the Destination from its own environment. By default the OpenCode 2 CLI talks
to a long-lived managed background service that keeps the environment it started with, so the
installed tree reaches that service only when the service's config root is the same directory. The
installer does not inspect or restart the service.

Install state lives at `<Destination>/.deniz-skills/install.json`. Checkout state uses
`schemaVersion: 2`. It persists Selection and one Ownership claim per managed Native-tree path,
including the responsible Module, hash, and mode. Each selected Module also records its version,
digest, and required Modules. Selection is read from this state, never inferred from files present on
disk. Deleting the state does not turn owned files into a supported fresh install; it loses the
ownership evidence needed to distinguish them from Unowned paths. The journal envelope remains
schema 1; its old/new state evidence now contains schema-2 Install state. OpenCode 2 discovers
content in its config root through named directories such as `skills/`, `commands/`, `agents/`,
`modes/`, and `plugins/` (at `anomalyco/opencode@0fd7e28`:
`packages/core/src/config/plugin/skill.ts:85-88`, `packages/core/src/config/plugin/command.ts:144`,
`packages/core/src/config/plugin/agent.ts:21-24`, and
`packages/core/src/plugin/source-directory.ts:7-33`). `.deniz-skills/` is not one of them, so
OpenCode 2 does not read it as content.

A Destination that still holds schema-1 Install state is not a supported starting point. The
curator's two real profiles that held such state were migrated once by a manual procedure: remove
their Modules with the schema-1 installer, remove the then-empty schema-1 state, and install
schema-2 output. That procedure is a one-off recorded in the
[profile migration record](../../experiments/harness-invocation/records/2026-10-09-opencode2-profile-migration.md),
not a supported product path, and the installer gains no migration or schema-1 import for it.

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
or range-resolve those names. Named-subset requirements are judged against the Selection the request
would produce, not against Modules that had to be selected before the request. Any finding clears
operations and leaves the next state equal to
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

A finding-free Plan with no file operations still commits next Install state when only recorded
requirements or Module identity changed. Apply does not invent a dummy file mutation for metadata.

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
  guessing. Schema-1 or otherwise unsupported Install-state evidence is blocked even when the raw
  bytes hash to the journal digest; the journal envelope itself remains schema 1.

Recovery restores the prior state or finalizes cleanup of an already committed state. It does not
resume or finish the original Install, Update, or Remove request. The classification and execution
are in [`inspectRecovery`](../../tools/lib/opencode-install-apply.ts#L1469-L1545) and
[`applyRecovery`](../../tools/lib/opencode-install-apply.ts#L2462-L2502).

## Full estate versus installed Selection

Compilation and `validate` reason over the complete generated estate: every Claude Plugin, Codex
Plugin, Module, formal fact, and Bundle is present together. The OpenCode installer, by contrast,
still takes an explicit Selection
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

- The installer has no force, reset, legacy-takeover, schema-1 import, project-local, or JSON
  configuration mutation path. Existing files and lost ownership state require manual resolution.
- The repository-owned installer and file Bundles are the only OpenCode distribution path. The
  installer does not use an OpenCode 2 plugin package, the `skills` config array or HTTP skill
  catalogs, or the shared `~/.agents/skills` root. It emits no `skill` permission rules, because
  those would need configuration mutation. OpenCode agent permission mapping stays deferred in
  [`docs/ROADMAP.md`](../ROADMAP.md#deferred).
- The CLI verifies every Package Bundle before any action, even when the request names only one
  Module, and rejects a Package whose required Module names are absent. Plan then refuses an
  incomplete Selection. Neither step automatically adds Modules.
- Release hash verification detects changed Package bytes; it cannot make a mutable Release asset
  immutable.
- Committed tests establish Plan/Apply behavior and byte equality. The
  [installer record](../../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md#explicitly-unmeasured)
  measured Native discovery on OpenCode 1.18.18, which is no longer a supported runtime. The
  [OpenCode 2 discovery record](../../experiments/harness-invocation/records/2026-10-09-opencode2-discovery.md)
  measured discovery of the installed full estate on v2.0.23 on the Windows workstation and the
  Linux host: every installed skill and agent appeared under its `<plugin>.<name>` ID, exactly the
  `manual` skills were unadvertised, and both agents reported `mode: subagent`. The manual-skill
  posture with a model and whether a read of a bundled support file from the global Native tree
  prompts a human for permission are not yet measured.
