# Skill Marketplace

Personal multi-harness skill marketplace: curated items are transformed into harness-native output.

## Language

### Distribution

**Plugin**:
The Claude Code packaging unit of exactly one curation manifest. It shares its name with its Module.
_Avoid_: module (for Claude output), package, bundle

**Module**:
The installable OpenCode distribution of exactly one curation manifest. It is one-to-one with a
Plugin and is not an OpenCode namespace.
_Avoid_: plugin (for OpenCode output), package, bundle

**Bundle**:
The build-produced on-disk payload of one Module: its manifest and every file the Module emits.
_Avoid_: module, package, output

**Package**:
The npm-format tarball that contains the emitted installer and every Module Bundle. Remote delivery
uses that exact tarball as a pinned private GitHub Release asset (tag plus target commit, SHA-256
verified); it is not an npm publication.
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
An owned path that is missing from the destination. It is not a deselection. It blocks Install and
Update. When its Module is being removed, that path is already gone.
_Avoid_: local modification, collision, deleted

**Collision**:
A destination path a Bundle cannot take because something else is already there that this Module does not own.
_Avoid_: local modification, state drift, conflict

**Module digest**:
The identity of a Bundle's file set.
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
The global OpenCode config root (`$XDG_CONFIG_HOME/opencode`, with OpenCode's normal home fallback).
Ownership paths are relative to it.
_Avoid_: module directory, package tree, config dir

**Native tree**:
The flattened skills, commands, and agents layout OpenCode reads at the Destination.
_Avoid_: module directory, bundle layout, plugin tree

**Module manifest**:
The Bundle's inventory of paths, hashes, modes, digest, and version.
_Avoid_: install state, manifest (alone)

**Install state**:
The Destination record of Selection plus Ownership. Deleting it is unsupported ownership loss, not
factory reset.
_Avoid_: module manifest, manifest (alone)

**Plan**:
The computed Reconcile, and any Selection change, before disk moves.
_Avoid_: preview, dry-run

**Apply**:
Committing a Plan to the Destination and Install state.
_Avoid_: install (as a synonym), sync

**Recovery**:
A Plan that only restores the prior Destination and Install state after an interrupted Apply. It does not finish the original request.
_Avoid_: resume, retry
