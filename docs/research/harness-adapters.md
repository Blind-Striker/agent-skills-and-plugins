# Harness adapters

Date: 2026-08-18

`AGENTS.md` is the canonical always-on repository contract. Each harness reaches that contract and
the curated output through its own native mechanism; install instructions remain in the root
`README.md`.

## Claude Code

Claude Code consumes this repository as a plugin marketplace (`.claude-plugin/marketplace.json` to
`plugins/deniz-*`). A plugin skill is addressed by its directory name, namespaced as
`<plugin>:<skill-dir>`. The build forces the emitted directory name to equal frontmatter `name`, so
those identities do not diverge in this repository's output.

Claude Code does not read `AGENTS.md` natively. Root `CLAUDE.md` imports it with `@AGENTS.md`, which
inlines the contract at session start without relying on a later read. Import parsing also applies
inside the imported file, so a literal at-sign token in `AGENTS.md` must stay in code formatting.
The built-in Explore and Plan subagents skip `CLAUDE.md`; the relay does not reach those contexts.

## OpenCode

OpenCode reads `AGENTS.md` natively. The build emits one `opencode/<module>/` Bundle per curation
manifest; a Bundle is distribution output, not a directory OpenCode discovers. The `deniz-skills`
installer composes an explicit Selection into the normal global Native tree at the XDG config root,
where OpenCode discovers `skills/`, `commands/`, and `agents/` through its built-in scanners. Its
`SKILL.md` files use the open agent-skills standard.

There is deliberately no runtime OpenCode package adapter: the package exports a CLI, not a plugin
entrypoint; installation does not add a `plugin` row or synthesize config objects at startup. The
same npm-format tarball is run from a checkout or downloaded as a pinned private GitHub Release
asset through `gh`; the tag/target pin and recorded SHA-256 identify the intended bytes, and the
Release is not immutable. Selection, Ownership, Update, Remove, and Recovery belong to the
installer; OpenCode sees only ordinary native files. The output trees remain independent: installing
a Claude Code marketplace Plugin does not install its same-named OpenCode Module.

For measured discovery and invocation behavior, see
[Skill invocation across harnesses](skill-invocation-across-harnesses.md). Before adding or changing
a harness claim, follow the [harness invocation protocol](../../experiments/harness-invocation/protocol.md).
The local packed installer, private Release download equivalence, and Native-tree discovery
measurement is record
[`opencode-module-installer-local-pack-2026-08-18`](../../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md);
its human permission observation is intentionally still open.
