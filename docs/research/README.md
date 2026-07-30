# Research

Date: 2026-07-30

Harness and upstream research notes: how a harness discovers and loads what this repo builds, what
it cannot express, what the vendored upstream repos actually look like, and what a real setup took.
Examples: wiring `opencode/` into an OpenCode config; investigating whether Codex or Cursor are
worth an emitter; the layout traps of the five submodules.

Conventions:

- One topic per file, named after the topic (`opencode-consumption.md`).
- Evergreen: record findings and how things work, not the session that produced them. Open work
  belongs in [ROADMAP.md](../ROADMAP.md).
- Every file carries a `Date:` line.
