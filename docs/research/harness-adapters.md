# Harness adapters

Date: 2026-08-02

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

OpenCode reads `AGENTS.md` natively and consumes the `opencode/` tree. Its `SKILL.md` files use the
open agent-skills standard. The output trees remain independent: installing a Claude Code
marketplace plugin does not install the corresponding OpenCode artifacts.

For measured discovery and invocation behavior, see
[Skill invocation across harnesses](skill-invocation-across-harnesses.md). Before adding or changing
a harness claim, follow the [harness invocation protocol](../../experiments/harness-invocation/protocol.md).
