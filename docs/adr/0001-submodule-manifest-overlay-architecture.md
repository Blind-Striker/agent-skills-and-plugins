# ADR-0001: Submodule + manifest + overlay architecture

Date: 2026-08-19
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
2. **One curation manifest per distribution pair** records inclusion, deliberate rejection,
   metadata, naming, invocation, shape, dependency, omission, and body-ownership intent. Each
   manifest produces one Claude Code Plugin and one same-named OpenCode Module. Its authoring
   guidance lives in [`curation/SCHEMA.md`](../../curation/SCHEMA.md), beside the manifests.
3. **Body edits live in overlays**, either a surgical `body: patch` or owned replacement files under
   `body: overlay`. Both modes record content hashes in `overlays/overlays.lock.json`; changes to
   stamped inputs stop the build. Patch applicability alone is not a staleness guard because a hunk
   can relocate while still applying; the hash carries the review boundary. A body that incorporates
   other upstream items declares them with `merged_from`, and those inputs receive the same review
   protection. The current stamping and body-assembly mechanics are owned by
   [Transformation and emission](../architecture/transformation-and-emission.md).
4. **Generated output is committed** under `plugins/`, `opencode/`, `dist/`,
   `.claude-plugin/marketplace.json`, `docs/inventory.md`, and `docs/ledger.json`. Generated
   Plugin/Module trees, marketplace output, and the ledger are deterministic and CI freshness-checked
   against clean regeneration. Each `opencode/<module>/` is a self-contained Bundle with deterministic
   identity and a manifest-backed byte-level review boundary. `dist/` is the build-emitted JavaScript
   installer and is formatted by the build; generated trees contain ordinary files rather than
   symlinks.

   The current emission, Bundle-manifest, packaging, and install handoff mechanics live in
   [Transformation and emission](../architecture/transformation-and-emission.md) and
   [Distribution and installation](../architecture/distribution-and-installation.md); this decision
   records the authored/generated boundary rather than cataloging those mechanics twice.

Direct vendoring with three-way merges was rejected because it loses the durable boundary between
upstream and local intent. A single overlay mode was also rejected: full-file ownership obscures
surgical changes and forgoes later upstream improvements, while patch-only ownership makes broad
rewrites unreadable. The two modes make that trade explicit per item.

## Consequences

- Generated output gains a layer of indirection: every authored change goes through curation or an
  overlay and then a build. In return, upstream pin changes stay separate from local intent.
- The committed Module manifests and installer emit make package review and installation independent
  of consumer-side compilation, at the cost of reviewing generated JavaScript and manifest diffs.
- Content hashing deliberately blocks even unrelated edits to a stamped file until someone reviews
  them. Merged bodies multiply that review cost by the number of sources they own.
- Patches preserve upstream flow outside the edited regions but can become hard to read as they
  grow. Full overlays make broad ownership clear but permanently forgo automatic improvements to
  the files they replace.
- Every overlay adds future review friction. That is an accepted cost of ADR-0007's preference for
  local control, not a reason to avoid meaningful edits.
