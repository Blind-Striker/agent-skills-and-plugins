# ADR-0001: Submodule + manifest + overlay architecture

Date: 2026-07-30
Status: Accepted

## Context

This repo curates skills from external upstream repositories (vendored in `external/`) into a small
set of personal `deniz-*` plugins. Curation is not just subsetting: it includes frontmatter and trigger edits, body rewrites,
renames, type conversions (skill → command/agent) and original skills of our own. Upstream keeps
moving, and we want to track it deliberately — see what changed, decide, and never be surprised by an
automatic update.

The two forces in tension: local edits must survive upstream updates, and upstream diffs must stay
readable enough to make a decision on.

## Decision

Four pieces:

1. **Upstream as git submodules** in `external/`, read-only. Pinned commits; `npm run sync` moves the
   pins on request and reports the impact on curated items.
2. **A curation manifest per plugin** (`curation/<plugin>.yaml`) listing the items to take and their
   per-item customizations (`exclude`, `frontmatter` overrides, `name`, `as: command|agent`,
   `body: overlay`). Deliberate rejections stay in the file as `exclude: true` so they remain visible.
3. **Overlays for body edits** (`overlays/<plugin>/<item>/`), in two kinds, both created by
   `npm run eject`:
   - `body: patch` stores an `overlay.patch`, cut from an edited working copy and applied to the
     emitted item. Most edits are surgical, and a diff both expresses that intent and lets an
     upstream improvement arrive without re-merging a whole file by hand.
   - `body: overlay` stores full replacement files, for edits too sweeping to read as a diff.

   Both kinds are blessed against the upstream content they were written from: the hash of every
   file the overlay replaces, or that its patch touches, is recorded in
   `overlays/overlays.lock.json`. Any upstream change to one of those files stops the build until
   `npm run eject -- <plugin> <item> --bless` re-blesses it.
4. **Build output committed** (`plugins/`, `opencode/`, `.claude-plugin/marketplace.json`) so a clone
   of the marketplace works immediately, with CI failing if the committed output is stale.

### Alternatives considered

- **Vendor upstream files directly and 3-way-merge on update.** Rejected: every upstream change turns
  into a conflict resolution in the file we edited, and the boundary between "ours" and "theirs" is
  lost after the first merge.
- **Full-file overlays as the only mechanism.** Rejected: most edits are surgical — deleting a
  block, rewording a gate, repointing a path — and expressing twenty lines of intent as a
  hundred-and-fifty-line copy hides what we changed, silently discards every later upstream
  improvement to that file, and offers no way to notice it went stale.
- **Patches as the only mechanism.** Rejected: an edit that rewrites most of a file is unreadable as
  a diff, and the escape hatch of simply owning the file outright is worth keeping.

## Consequences

- Nothing in `plugins/`/`opencode/` is authored — every change goes through `curation/` or `overlays/`
  and a build. That is one layer of indirection, and it is the price of the property we want.
- Upstream diffs stay clean: a submodule bump plus a report, with overlay conflicts surfaced for a
  human decision (`sync` prints the exact diff command) rather than merged silently.
- `git apply` is not a staleness guard, which is why both kinds are hashed. It searches for a
  hunk's context with an unbounded line offset and takes the first match, failing only when that
  context appears nowhere in the file. So a hunk silently relocates when upstream inserts lines
  above it, and — where a file repeats a passage — lands on a different region that still matches
  while the region we meant to edit is gone. Both exit 0. The hash is what actually notices.
- Hashing costs the ability to absorb an unrelated upstream edit without looking at it. That is the
  intended trade: this repo exists to decide what its plugins contain, and an upstream change
  arriving unreviewed is the same failure as a stale overlay, only faster.
- Both failures are hard, not warnings. A stale overlay that still builds is the failure mode this
  design exists to prevent, and a warning in a green build is one nobody reads.
- Re-blessing is a deliberate act (`npm run eject -- <plugin> <item> --bless`), which means a submodule
  bump that touches an overlaid file blocks the build until someone looks. That is friction by
  choice; it scales badly only if overlays stop being rare, which is itself the signal to stop.
- Patches apply to skill-shaped output only. A `command`/`agent` conversion re-serializes
  frontmatter around a body, so there is no stable file for a diff to land on; those items take a
  full-file overlay.
- The eject workflow is the escape hatch: any item can graduate from passthrough to fully owned
  without changing the pipeline.
- Committed output must be machine-independent and self-contained, so symlinks are never copied
  into it — Node's `cpSync` absolutizes relative symlink targets (an upstream fixture symlink once
  leaked an absolute local path into a committed blob). The build warns per skipped symlink;
  `validate` errors on any symlink found in output.
- For a converted item (`as: command|agent`) the build reads exactly one overlay file, named after
  the source (`SKILL.md` when the source is a skill, otherwise the source file's own name) —
  renaming that file inside the overlay breaks it. `eject` copies the whole item directory even
  though only that one file is ever read.
- A manifest `frontmatter:` block cannot rename an item: the build forces the output `name` last,
  so a `frontmatter.name` override is dead weight (`validate` warns). Renaming is the item-level
  `name:` field.
- Taking the same source into two items (say, once as a command and once as an agent) is legal, but
  the cross-reference map keys on the upstream address, so all upstream references resolve to the
  last such item in manifest order — `validate` warns, naming both outputs.
- The build resolves every source before deleting old output (fail-fast), but a crash mid-emission
  still leaves `plugins/` and `opencode/` partial; rerunning the build repairs it.
