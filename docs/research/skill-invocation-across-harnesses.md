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

Re-derive per repo with:

```
for r in external/*/; do
  printf '%-24s %s\n' "$(basename $r)" \
    "$(grep -rlE '^(invocable|user-invocable|disable-model-invocation):' "$r" 2>/dev/null | wc -l)"
done
```

Three of the five vendored repos set invocation frontmatter; `superpowers` and `aspire-skills` set
none at all.

- **mattpocock-skills** uses `disable-model-invocation: true` as its *default posture*, not as a
  mark on a few flagship skills — it carries the flag across most of both promoted buckets, and
  through `in-progress`, `personal` and `deprecated` as well. The interesting artefact is which
  skills **lack** it, because that set is the composition pattern below made mechanical:

  ```
  for f in external/mattpocock-skills/skills/{engineering,productivity}/*/SKILL.md; do
    grep -qE '^disable-model-invocation:' "$f" || echo "${f#*/skills/}"
  done
  ```

  Everything that survives that filter is a *knowledge* skill — `grilling`, `tdd`,
  `diagnosing-bugs`, `code-review`, `domain-modeling`, `codebase-design`, `research`, `prototype`,
  `resolving-merge-conflicts`. Everything flagged is a *trigger*: `grill-me` and `grill-with-docs`
  are user-only, while the `grilling` discipline they invoke is model-only. That is the ADR-0005
  matrix already implemented upstream, and it maps to our field directly — flagged means
  `invocation: manual`, unflagged means `invocation: auto`.
- **superpowers** sets no invocation frontmatter anywhere; every skill carries `name` and
  `description` only. It reaches for automatic invocation two other ways. First, description prose:
  `brainstorming` opens with "You MUST use this before any creative work" — pressure written into
  the field that gets injected into the system prompt, so an overlay of the body cannot remove it;
  only a manifest `frontmatter.description` override can. Second, a **SessionStart bootstrap**:
  `hooks/hooks.json` registers a `startup|clear|compact` hook whose script reads
  `skills/using-superpowers/SKILL.md` and injects its full text wrapped in `<EXTREMELY_IMPORTANT>`.
  The bootstrap is not a separate skill — it is the delivery mechanism, and `using-superpowers` is
  its payload. This repo packages no hooks, so that amplification is absent from our output.

  The coercive language is narrower than it looks, and worth locating precisely before deciding
  what to strip. `1%` appears in exactly one place: the `<EXTREMELY-IMPORTANT>` block of
  `using-superpowers` ("even a 1% chance … YOU DO NOT HAVE A CHOICE"). `brainstorming` carries the
  only other trigger pressure, in its `description` and one body line. Two further matches for
  "You MUST" are different things and should not be swept up with them:
  `systematic-debugging` uses it for *procedural* discipline inside the skill ("complete each phase
  before proceeding"), and `writing-skills` uses it to declare a dependency ("REQUIRED BACKGROUND").
  Re-derive with `rg -n '\b1%|You MUST|DO NOT HAVE A CHOICE' external/superpowers/skills --glob '**/SKILL.md'`.
- **dotnet-skills** sets `invocable: true|false` on most of its skills. That is not a field in
  either harness's frontmatter reference; it is upstream's own convention and reaches our output as
  dead metadata.
- **dotnet-agent-skills** uses both real keys, and mostly on agents rather than skills. The
  dominant use is `user-invocable: false` on the `code-testing-*` sub-agents — pipeline stages
  meant to be dispatched by an orchestrator, kept out of the user's slash menu. A few skills
  (`filter-syntax`, `platform-detection`, and the two `*-extensions`) set `user-invocable: false`
  **and** `disable-model-invocation: true` together, which in Claude Code leaves nothing able to
  invoke them directly: they are reference material for another skill to read, not entry points.
  Worth knowing before curating any of them, since our `invocation` field has no value that means
  "neither".

## The composition pattern

mattpocock-skills documents an architecture in `.agents/invocation.md` that is the intent matrix
applied consistently, and it is worth borrowing rather than reinventing:

- **A user-invoked skill is a trigger; a model-invoked skill is the knowledge.** `grill-me` is seven
  lines and its whole body is "Run a `/grilling` session"; `grill-with-docs` is the same trigger
  plus `/domain-modeling`. The interview discipline itself lives in `grilling`, which is
  model-invoked. Two entry points, one body of knowledge.
- **A user-invoked skill may invoke model-invoked skills, but never another user-invoked skill.**
  That keeps the human the only entry point to a ceremony.
- **Dependencies are prose `/skill` invocations, not file cross-references.** Shared material lives
  inside the skill that owns it and is reached by invoking that skill.
- **The description's audience follows the invocation.** User-invoked: human-facing, one line, no
  trigger lists — it is read by a person browsing slash commands. Model-invoked: model-facing, rich
  trigger phrasing, because auto-invocation depends on it.

The contrast with superpowers is structural, not cosmetic. Superpowers expresses dependencies as
`superpowers:<name>` references and deep links into sibling skill directories, which is what
produces its dense cross-reference graph — and, for us, the dangling references that every partial
curation leaves behind.

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
