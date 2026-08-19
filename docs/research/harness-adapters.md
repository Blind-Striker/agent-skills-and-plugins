# Harness adapters

Date: 2026-08-18

> **Dated evidence and relay, not current policy.** This note preserves harness-specific discovery
> and instruction-loading findings that shaped the adapters. Current local emission mechanics live
> in [Transformation and emission](../architecture/transformation-and-emission.md), current OpenCode
> composition mechanics live in
> [Distribution and installation](../architecture/distribution-and-installation.md), and runnable
> consumption instructions live in the root [README](../../README.md).

## Claude Code

The adapter evidence showed Claude Code consuming a plugin marketplace and addressing a plugin skill
by directory name as `<plugin>:<skill-dir>`. The repository's current
marketplace, Plugin, identity, and invocation mapping are owned by
[Transformation and emission](../architecture/transformation-and-emission.md#claude-code), rather
than by this research note.

At the evidence boundary, Claude Code did not read `AGENTS.md` natively. Root `CLAUDE.md` imported it
with `@AGENTS.md`, which inlined the contract at session start without relying on a later read.
Import parsing also applied inside the imported file, so the relay required a literal at-sign token
in `AGENTS.md` to stay in code formatting. The built-in Explore and Plan subagents skipped
`CLAUDE.md`; the relay did not reach those contexts.

## OpenCode

At the evidence boundary, OpenCode read `AGENTS.md` natively. Discovery evidence showed it reading
ordinary `skills/`, `commands/`, and `agents/` below its config roots; its `SKILL.md` files used the
open agent-skills convention. A generated `opencode/<module>/` tree was a Bundle source, not a
directory OpenCode discovered as this product's route. The current OpenCode distribution and
installation contract is owned by
[Distribution and installation](../architecture/distribution-and-installation.md).

For measured discovery and invocation behavior, see
[Skill invocation across harnesses](skill-invocation-across-harnesses.md). Before adding or changing
a harness claim, follow the [harness invocation protocol](../../experiments/harness-invocation/protocol.md).
The local packed installer, private Release download equivalence, and Native-tree discovery
measurement is record
[`opencode-module-installer-local-pack-2026-08-18`](../../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md);
that record explicitly leaves human permission behavior and model-driven parked-body reads
unmeasured. During the related 2026-08-18 real-profile migration, `~/.agents/skills/` was measured
absent and needed no cleanup. That is dated evidence about that profile, not a current guarantee; the
same migration's installer Plan/Apply and preserved-control-plane observations remain in the linked
record. The source analysis explaining why package-cache placement alone does not register artifacts
is [OpenCode plugin packages, cached artifacts, and per-module
distribution](2026-08-07-opencode-plugin-package-artifacts.md).
