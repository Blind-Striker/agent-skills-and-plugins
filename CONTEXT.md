# Skill Marketplace

Personal multi-harness skill marketplace: curated items are transformed into harness-native output.

## Language

### Distribution

**Claude Plugin**:
The Claude Code packaging unit of exactly one curation manifest. It shares its name with its Module.
_Avoid_: plugin (when the target is ambiguous), module, package, bundle

**Codex Plugin**:
The Codex-native packaging unit of exactly one curation manifest, emitted under `codex/` and listed
in the repository Codex marketplace. It shares its name with the Claude Plugin and Module but has
its own manifest, skill namespace, and installation surface.
_Avoid_: Claude Plugin, module, package, bundle

**Module**:
The installable OpenCode distribution of exactly one curation manifest. It is one-to-one with a
Claude Plugin and Codex Plugin, and its name is the namespace of every OpenCode ID it emits.
_Avoid_: plugin (for OpenCode output), package, bundle

**OpenCode ID**:
The name OpenCode registers for one emitted skill, command, or agent: `<plugin>.<name>`, the Module
name and the item's output name joined by `.`, for example `deniz-process.brainstorming`. It is the
skill folder name or the command or agent file name without `.md`, and users type it after `@` or
`/`.
_Avoid_: bare name, namespaced reference, `ns:name` (the neutral reference spelling)

**Bundle**:
The build-produced on-disk payload of one Module: its manifest and every file the Module emits.
_Avoid_: module, package, output

**Package**:
The npm-format tarball that contains the emitted installer, repository license and notices, and every
Module Bundle. Remote delivery uses that exact tarball as a pinned GitHub Release asset (tag plus
target commit, SHA-256 verified); it is not an npm publication.
_Avoid_: module, plugin, bundle

### Installation

**Ownership**:
A recorded claim that exactly one Module is the sole authority for one destination path.
_Avoid_: installer-owned (for files), possession

**Local modification**:
An owned path whose current bytes or POSIX executable mode no longer match the recorded claim.
_Avoid_: drift, dirty, user edit

**Unowned**:
A destination path with no ownership claim.
_Avoid_: foreign, unmanaged, unknown file

**Selection**:
The persisted set of Modules the user chose to keep. It is not inferred from disk.
_Avoid_: installed (as a synonym), chosen set

**Installed**:
A selected Module whose every owned path still matches its ownership claim.
_Avoid_: selected, current, present

**State drift**:
An owned path that is missing from the Destination. It is not a deselection. It blocks
reconciliation when its Module is affected by Install or Update; unrelated State drift does not
block a request. Remove of that Module may drop the already-missing Ownership claim.
_Avoid_: local modification, collision, deleted

**Collision**:
A destination path a Bundle cannot take because something else is already there that this Module does not own.
_Avoid_: local modification, state drift, conflict

**Module digest**:
The identity of a Bundle's file set and required Modules. Checkout Bundles use schema 2: the
digest covers the normalized requirement list plus path/hash/mode claims. Module name and
curator-facing version stay outside it.
_Avoid_: version, git ref, package hash

**Version**:
Curator-facing provenance copied from the curation manifest. It does not identify a Bundle.
_Avoid_: digest, release

**Current**:
A selected Module whose recorded digest equals this Package's Bundle digest.
_Avoid_: installed, up to date, latest

**Reconcile**:
The exact file-set difference between a Module's current Ownership and a target Bundle.
_Avoid_: sync, deploy, copy

**Install**:
A request to add Modules to the Selection and reconcile those Modules to their Bundles.
_Avoid_: update, sync

**Update**:
A request to reconcile the whole Selection without changing it.
_Avoid_: install, upgrade, sync

**Remove**:
A request to subtract Modules from the Selection and reconcile them to an empty Bundle. A missing
owned path of a Module being removed is already gone. A Local modification blocks the Remove before
Selection or Ownership changes.
_Avoid_: uninstall, clean, purge

**Destination**:
The global OpenCode config root: `OPENCODE_CONFIG_DIR` when it is set and non-empty, because
OpenCode 2 then uses it in place of the global root; otherwise `$XDG_CONFIG_HOME/opencode`;
otherwise `<home>/.config/opencode`. Ownership paths are relative to it.
_Avoid_: module directory, package tree, config dir

**Native tree**:
The flat layout OpenCode 2 reads at the Destination: `skills/<id>/SKILL.md`, `commands/<id>.md`, and
`agents/<id>.md`, where each `<id>` is an OpenCode ID. Artifacts of every selected Module sit side by
side in it.
_Avoid_: module directory, bundle layout, plugin tree

**Module manifest**:
The Bundle's schema-2 inventory of paths, hashes, modes, digest, version, and required Modules.
_Avoid_: install state, manifest (alone)

**Install state**:
The Destination record of Selection plus Ownership. Checkout state is schema 2: each selected Module
records version, digest, and required Modules. Deleting it is unsupported ownership loss, not
factory reset.
_Avoid_: module manifest, manifest (alone)

**Plan**:
The computed Reconcile, and any Selection change, before disk moves.
_Avoid_: preview, dry-run

**Apply**:
Committing a Plan to the Destination and Install state.
_Avoid_: install (as a synonym), sync

**Recovery**:
A Plan that restores transaction-managed Destination changes and the prior Install-state boundary
before commit, or finalizes cleanup after new Install state is committed. Unrelated Destination
files are preserved. It never resumes or finishes the original request.
_Avoid_: resume, retry
