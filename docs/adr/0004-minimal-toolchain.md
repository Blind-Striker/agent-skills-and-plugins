# ADR-0004: Minimal toolchain — five commands, one dependency

Date: 2026-07-30
Status: Accepted

## Context

The toolchain exists to serve curation, not the other way around. A build system for markdown files
invites over-engineering: frameworks, plugin systems, invented configuration languages. Every
dependency and abstraction added to `tools/` is something a future session must understand before it
can safely change anything.

## Decision

`tools/` stays a handful of plain-TypeScript commands (`build`, `inventory`, `eject`, `sync`,
`validate`) run directly by Node ≥ 24 via type stripping — no bundler, no compile step. Exactly one
runtime dependency (`yaml`); tests use `node:test`; no CLI framework, no schema library. If doing a
job by hand is cheaper than writing a script for it, it is done by hand. Growing any of these is a
deliberate decision, not a drive-by.

## Consequences

- Type stripping constrains the code: relative imports carry the `.ts` extension;
  `erasableSyntaxOnly` bans enums, namespaces and parameter properties; `tsc` (TS7) is a
  typecheck-only gate (`--noEmit`), never a build step.
- `node --test` treats a bare directory argument as a module entry point, and a mis-expanded glob
  reports green over zero tests — hence the `pretest` guard that fails when the glob stops finding
  the suite.
- Some conveniences are simply absent (schema validation is hand-rolled, CLI parsing is
  `process.argv`), and that is the intended trade.
