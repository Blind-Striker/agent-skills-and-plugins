# Skill invocation across harnesses

Date: 2026-07-30

Who decides that a skill runs — the model, or the person at the keyboard — is a per-harness
mechanism, and the two harnesses this repo targets disagree about it deeply enough that the same
curation intent needs two different output shapes. This file records what each harness supports,
what the vendored upstreams already use, and the prior art.

`docs/inventory.md` lists what upstream offers; `upstream-repo-layouts.md` records how those repos
sit on disk. This file records invocation. The decision built on top of it is
[ADR-0005](../adr/0005-invocation-intent-in-the-manifest.md).

## Claude Code

Skills and custom commands are one mechanism: `.claude/commands/deploy.md` and
`.claude/skills/deploy/SKILL.md` both produce `/deploy` and behave identically. Frontmatter decides
who may pull the trigger:

| Field | Effect |
|---|---|
| *(neither set)* | Both: the user can type `/name`, and the model can load it when the description matches |
| `disable-model-invocation: true` | **User only.** Also stops the skill being preloaded into subagents, and stops it firing when a scheduled task names it |
| `user-invocable: false` | **Model only.** Hides the skill from the `/` menu |

Other dials that bear on curation: `when_to_use` (trigger phrases appended to `description`;
the pair is truncated at 1,536 characters in the skill listing), `paths` (globs that gate automatic
loading), `context: fork` + `agent` + `background` (run the skill in a subagent), `model` and
`effort` (per-skill overrides), `allowed-tools` and `disallowed-tools`.

The harness ships bundled skills of its own — `/debug`, `/code-review`, `/verify`, `/loop`,
`/batch`, `/doctor`, `/claude-api`. `/verify` and `/code-review` are user-invoked only, which is
the harness making the same trade this repo cares about: keep long, expensive checks under the
user's control.

## OpenCode

Skills are model-only. There is no way for a user to invoke one — no slash form, no menu entry.
Agents see the available skills and load them through a native `skill` tool. Recognised skill
frontmatter is `name`, `description`, `license`, `compatibility` and `metadata`; the documentation
states that unknown fields are ignored, so Claude Code's invocation keys travel into the OpenCode
tree and do nothing there.

Commands are therefore the *only* user-invocable surface. A command is a markdown file whose
frontmatter carries `description`, `agent` (which agent runs it), `model`, `subtask` (force it into
a subagent, so it does not pollute the primary context) and `template`; the body supports
`$ARGUMENTS`, positional `$1`, shell injection and `@file` references.

Discovery walks up from the working directory to the git worktree root, reading
`.opencode/skills/<name>/SKILL.md` and also `.claude/skills/` and `.agents/skills/`; globally it
reads `~/.config/opencode/skills/`. Commands and agents live alongside, in `commands/` and
`agents/`, under either `.opencode/` or `~/.config/opencode/`. All three directory names are plural,
and this repo's `opencode/` tree uses the same spelling.

Access control is config-side — `opencode.json` carries allow/deny/ask patterns over skill names,
and an agent can drop skills entirely with `skill: false`. None of that travels inside a
distributed artifact, so it cannot substitute for getting the output shape right.

## The intent matrix

The same curation intent, expressed natively per harness:

| Intent | Claude Code | OpenCode |
|---|---|---|
| The model decides (passive knowledge) | skill, `user-invocable: false` | skill — the only mode it has |
| The user decides | skill, `disable-model-invocation: true` | **command**; a skill cannot express this |
| Either may | skill, neither key set | skill *and* command |

The asymmetry is the whole point: in Claude Code the dial is a frontmatter flag on one artifact, in
OpenCode it is a choice of artifact.

## What the vendored upstreams use

Re-derive with:

```
rg -n '^(invocable|user-invocable|disable-model-invocation):' external
```

- **mattpocock-skills** sets `disable-model-invocation: true` on `grill-me`, `teach`, `handoff`,
  `edit-article` and `writing-great-skills` — its best-known skills are user-invoked by design.
- **superpowers** sets no invocation frontmatter anywhere; every skill carries `name` and
  `description` only. Its pressure to fire lives in description prose (`brainstorming` opens with
  "You MUST use this before any creative work") and in a SessionStart hook this repo does not
  package. Because that pressure sits in the *description*, which is injected into the system
  prompt, an overlay of the body cannot remove it — only a manifest `frontmatter.description`
  override can.
- **dotnet-skills** sets `invocable: true|false` on most of its skills. That is not a field in
  either harness's frontmatter reference; it is upstream's own convention and reaches our output as
  dead metadata.

## Prior art: wshobson/agents

The multi-harness marketplace ADR-0002 took its model from. One source-of-truth tree, and a
per-harness adapter that emits idiomatic artifacts rather than a shared subset. Its OpenCode adapter
emits agents, commands and skills, rewriting slash commands into the commands directory and
converting `tools:` allowlists into `permission:` deny blocks. Its Codex adapter runs the same
conversion in reverse — commands become skills, because Codex has no command concept.

The governing sentence, which is the design target for our manifest:

> Each adapter handles incompatibilities mechanically — authors don't need to know the per-harness
> rules to write portable content.

## Sources

- [Claude Code — Extend Claude with skills](https://code.claude.com/docs/en/skills)
- [Anthropic — Skill authoring best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- [OpenCode — Agent Skills](https://opencode.ai/docs/skills/)
- [OpenCode — Commands](https://opencode.ai/docs/commands/)
- [wshobson/agents — docs/harnesses.md](https://github.com/wshobson/agents/blob/main/docs/harnesses.md)
- [claude-code#26251](https://github.com/anthropics/claude-code/issues/26251) — `disable-model-invocation`
  reported as blocking user slash invocation too; closed as duplicate, unverified
