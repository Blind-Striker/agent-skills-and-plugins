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
3. **Overlays for body edits** (`overlays/<plugin>/<item>/`): a full copy of the file, not a patch.
   `npm run eject` creates it.
4. **Build output committed** (`plugins/`, `opencode/`, `.claude-plugin/marketplace.json`) so a clone
   of the marketplace works immediately, with CI failing if the committed output is stale.

### Alternatives considered

- **Vendor upstream files directly and 3-way-merge on update.** Rejected: every upstream change turns
  into a conflict resolution in the file we edited, and the boundary between "ours" and "theirs" is
  lost after the first merge.
- **Quilt-style patch series per file.** Rejected: patches rot against upstream churn, and reading a
  patch stack is a worse way to answer "what does my version say" than reading the file.

## Consequences

- Nothing in `plugins/`/`opencode/` is authored — every change goes through `curation/` or `overlays/`
  and a build. That is one layer of indirection, and it is the price of the property we want.
- Upstream diffs stay clean: a submodule bump plus a report, with overlay conflicts surfaced for a
  human decision (`sync` prints the exact diff command) rather than merged silently.
- Overlays are full files, so an upstream improvement to an overlaid item does not arrive
  automatically; `sync` flags it and the diff is applied by hand. Acceptable, because overlays are
  chosen deliberately and stay rare.
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
