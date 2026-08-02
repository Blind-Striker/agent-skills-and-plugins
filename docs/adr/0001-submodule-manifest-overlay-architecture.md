# ADR-0001: Submodule + manifest + overlay architecture

Date: 2026-08-02
Status: Accepted

## Context

This repo transforms components from moving upstream repositories into locally controlled output.
Local edits must survive pin changes, while upstream diffs must remain legible enough for the
curator to decide what flows in. Directly editing vendored content or generated output would erase
that boundary.

## Decision

The architecture has four pieces:

1. **Upstreams are read-only git submodules** under `external/`. Pins move deliberately; local
   authorship never enters an upstream worktree.
2. **One curation manifest per plugin** records inclusion, deliberate rejection, metadata, naming,
   invocation, shape, dependency, omission, and body-ownership intent. Its authoring guidance lives
   in [`curation/SCHEMA.md`](../../curation/SCHEMA.md), beside the manifests.
3. **Body edits live in overlays**, either a surgical `body: patch` or owned replacement files under
   `body: overlay`. Both modes record content hashes in `overlays/overlays.lock.json`; changes to
   stamped primary files stop the build. Patch applicability alone is not a staleness guard because
   a hunk can relocate while still applying; the hash carries the review boundary. A body that
   incorporates other upstream items declares them with `merged_from`, and their stamped inputs
   drift under the same hard-failure policy. The guard follows the files the curation actually used,
   including explicitly named files when a same-filename rule cannot express the merge.
4. **Generated output is committed** under `plugins/`, `opencode/`, and
   `.claude-plugin/marketplace.json`. It contains plain, self-contained files rather than symlinks,
   and CI rejects output that does not match a fresh build.

Direct vendoring with three-way merges was rejected because it loses the durable boundary between
upstream and local intent. A single overlay mode was also rejected: full-file ownership obscures
surgical changes and forgoes later upstream improvements, while patch-only ownership makes broad
rewrites unreadable. The two modes make that trade explicit per item.

## Consequences

- Generated output gains a layer of indirection: every authored change goes through curation or an
  overlay and then a build. In return, upstream pin changes stay separate from local intent.
- Content hashing deliberately blocks even unrelated edits to a stamped file until someone reviews
  them. Merged bodies multiply that review cost by the number of sources they own.
- Patches preserve upstream flow outside the edited regions but can become hard to read as they
  grow. Full overlays make broad ownership clear but permanently forgo automatic improvements to
  the files they replace.
- Every overlay adds future review friction. That is an accepted cost of ADR-0007's preference for
  local control, not a reason to avoid meaningful edits.
