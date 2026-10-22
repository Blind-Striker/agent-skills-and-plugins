# Notices for original skills in this module

`assembleItems` copies only declared directories out of `skills/<module>/`, so this file stays in
the repository and never ships. It records where an original skill's material came from when that
is worth knowing.

## verified-waves

The method follows the curator-approved approaches in this repository's
[orchestration research](../../docs/research/claude-ultracode-and-orchestration-ports.md#adopted-approaches-curator-approved).
That note's extraction pass read the sources below at the named commits, read-only, and ran none of
their code. No text was taken from any of them: the skill is written in this repository's own
words, so no `THIRD_PARTY_NOTICES.md` entry follows.

Idea sources:

- `YuanpingSong/ultracodex@ac349f8` (Apache-2.0): self-contained briefs, coverage reported as N of
  M with results keyed by item id, fresh skeptics, writer waves with a gate after each, and the
  catalog of shapes.
- `norandom/OpenUltraCode@2638f03` (MIT): the fixed report format and no silent drops.
- `PabloNAX/ultracode-skill@bfa2d92` (MIT): keeping the critical path with the parent.
- `just-every/plugin-ultracode@9dde008` (MIT declared in its manifest, with no license file): the
  skeptic, completeness-critic, and loop-bound patterns, as ideas only.
- `code-yeongyu/oh-my-openagent@a103d40` (Sustainable Use License): the brief sections, the
  restart from a ledger, and the loop bounds, as ideas only.
- `Yeachan-Heo/oh-my-codex@2db7d51` (MIT): one orchestration authority, the reason it removed its
  prompt-only parallel mode.

The tool facts in the per-harness notes come from each harness's own source or documentation at
the research note's pins: `openai/codex@092d3ac` (Apache-2.0), `anomalyco/opencode@9b4ec57` (MIT),
and the Claude Code documentation and 2.1.296 build.
