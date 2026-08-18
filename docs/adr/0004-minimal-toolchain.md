# ADR-0004: Minimal toolchain — six commands, one dependency

Date: 2026-08-18
Status: Accepted

## Context

The toolchain exists to serve curation, not the other way around. A build system for markdown files
invites over-engineering: frameworks, plugin systems, invented configuration languages. Every
dependency and abstraction added to `tools/` is something a future session must understand before it
can safely change anything.

## Decision

`tools/` stays a handful of plain-TypeScript commands (`build`, `inventory`, `eject`, `sync`,
`validate`, and `install:opencode`). Authoring and repository-local execution use Node ≥ 24 type
stripping. Exactly one runtime dependency (`yaml`); tests use `node:test`; no CLI framework, no
schema library. If doing a job by hand is cheaper than writing a script for it, it is done by hand.
Growing any of these is a deliberate decision, not a drive-by.

The installer is the one deliberate exception to typecheck-only TypeScript: `npm run build` compiles
its five runtime source files to committed `dist/` JavaScript and formats that emit. The package ships
only `dist/` plus generated OpenCode Module Bundles. It has no `prepare` or `prepack` lifecycle, and a
consumer never compiles TypeScript. Remote delivery is the exact npm tarball attached immutably to a
private GitHub Release and downloaded with `gh`, not an npm registry publication or Git package.

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
