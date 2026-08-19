# ADR-0004: Minimal toolchain

Date: 2026-08-19
Status: Accepted

## Context

The toolchain exists to serve curation, not the other way around. A build system for markdown files
invites over-engineering: frameworks, plugin systems, invented configuration languages. Every
dependency and abstraction added to `tools/` is something a future session must understand before it
can safely change anything.

## Decision

`tools/` stays a minimal set of plain-TypeScript commands. The live command and package-file lists
belong in [`package.json`](../../package.json), not in this ADR. Authoring and repository-local
execution use Node ≥ 24 type stripping. Exactly one runtime dependency (`yaml`) is the ceiling; tests
use `node:test`; there is no CLI framework or schema library. If doing a job by hand is cheaper than
writing a script for it, it is done by hand. Growing the toolchain is a deliberate decision, not a
drive-by.

The installer is the one deliberate exception to typecheck-only TypeScript: `npm run build` compiles
the installer sources to committed `dist/` JavaScript and formats that emit. The package ships the
emitted installer and generated OpenCode Module Bundles. It has no `prepare` or `prepack` lifecycle,
and a consumer never compiles TypeScript. Remote delivery is the exact versioned tarball attached as
a private GitHub Release asset, not an npm registry publication or Git package. Its tag and target
commit identify the intended source point, while the recorded Package SHA-256 detects replacement or
corruption but cannot prevent an authorized re-upload. The exact Package/Release transport and
verification mechanics live in [Distribution and installation](../architecture/distribution-and-installation.md).

## Consequences

- Type stripping constrains the authored code: relative imports carry the `.ts` extension;
  `erasableSyntaxOnly` bans enums, namespaces and parameter properties. The ordinary `typecheck`
  gate remains `--noEmit`; the installer-specific build config rewrites relative import extensions
  into its committed JavaScript emit.
- `node --test` treats a bare directory argument as a module entry point, and a mis-expanded glob
  reports green over zero tests — hence the `pretest` guard that fails when the glob stops finding
  the suite.
- Some conveniences are simply absent (schema validation is hand-rolled, CLI parsing is
  `process.argv`), and that is the intended trade.
- Committing `dist/` adds generated review noise, but makes the packed installer independent of
  consumer toolchains; CI therefore treats stale `dist/` exactly like stale harness output.
