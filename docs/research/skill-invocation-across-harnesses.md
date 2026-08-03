# Skill invocation across harnesses

Date: 2026-08-03

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

All three rows were observed on a live install (Claude Code 2.1.220), against skills isolated in
their own `CLAUDE_CONFIG_DIR` so nothing else was loaded. Two results are worth carrying:

- **`disable-model-invocation: true` does not block the user.** The report below said it does; it
  did not reproduce. The flagged skill appeared in the `/` menu and ran when invoked.
- **Model suppression is structural, not instructional.** Asked to act on the flagged skill's
  trigger phrase, the model reported that the skill was *absent from the available-skills list in
  its context* — "there's no name for me to pass to the Skill tool". It is filtered out of the
  listing rather than told to refrain.

The second point has a consequence for curation: a `manual` item is unreachable by the model no
matter what its description says. Stripping coercive trigger prose from an upstream skill is
therefore a matter of taste, not a safety measure — the description never gets the chance to
persuade anything.

Both directions were later re-measured against this repo's own closed module rather than fixtures,
on the isolated lab of [protocol.md](../../experiments/harness-invocation/protocol.md). The two surfaces are read
from different places and they agree with the manifest exactly:

| Surface | Read from | Contains | Count |
|---|---|---|---|
| user | `init.slash_commands` | `manual` + `both` | 22 |
| model | a `-p` enumeration, with an unmounted control | `auto` + `both` | 18 |

Their intersection is the eight `both` items and their union is all thirty-two taken ones: no
`auto` item is slash-addressable, no `manual` item is in the model's listing. `init.skills` tracks
the *user* surface, not the model's — the two names invite the opposite reading.

The first shipped user-pointer was exercised in the same round. Reaching the spec-discovery branch
of `requesting-code-review`, the model relayed it verbatim as the human's move — "open
`/deniz-process:setup-matt-pocock-skills`" — attempted no invocation, and volunteered a refusal to
guess at a tracker. Notably it did so **without** the explicit guard clause the OpenCode probe below
found load-bearing; on Claude Code the mechanism does not depend on wording anyway, since a `manual`
target is filtered out of the model's listing entirely.

Other dials that bear on curation: `when_to_use` (trigger phrases appended to `description`;
the pair is truncated at 1,536 characters in the skill listing), `paths` (globs that gate automatic
loading), `context: fork` + `agent` + `background` (run the skill in a subagent), `model` and
`effort` (per-skill overrides), `allowed-tools` and `disallowed-tools`.

The harness ships bundled skills of its own — `/debug`, `/code-review`, `/verify`, `/loop`,
`/batch`, `/doctor`, `/claude-api`. `/verify` and `/code-review` are user-invoked only, which is
the harness making the same trade this repo cares about: keep long, expensive checks under the
user's control.

### A duplicate skill name across plugins is safe for the user and unsafe for the model

Measured with two locally installed plugins shipping the same skill name, plus a unique skill in
each as a control:

- **User invocation is unambiguous by construction.** The `/` menu namespaces *every* plugin skill —
  `/lab-alpha:dup-skill (dup-skill)` and `/lab-beta:dup-skill (dup-skill)` appear as two separate
  entries, and the bare name cannot be typed at all: the picker forces a choice. Each qualified form
  invokes its own copy correctly.
- **Model invocation silently picks one.** Given the shared trigger phrase, the model called its
  Skill tool with the *unqualified* `dup-skill`. That resolved without error to `lab-alpha` — first
  in listing order — even though `lab-beta`'s copy had been the one invoked most recently. There is
  no ambiguity signal at all.

So a name collision is not one hazard but two different situations, and `invocation` is exactly the
axis that separates them: a `manual` item is reached through the namespaced menu and can safely share
a name, while anything the model can reach (`auto`, `both`) is resolved by bare name and cannot.

(Unrelated but noted while measuring: `/reload-plugins` reported `2 plugins · 0 skills · 6 agents`
for two plugins carrying four working skills. The skill counter is wrong; do not use it to check
whether an install took.)

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
`.opencode/skills/<name>/SKILL.md`, `.claude/skills/<name>/SKILL.md` and
`.agents/skills/<name>/SKILL.md`; globally it reads the same three under `~/.config/opencode/`,
`~/.claude/` and `~/.agents/`. Commands and agents live alongside, in `commands/` and `agents/`,
under either `.opencode/` or `~/.config/opencode/`. All three directory names are plural, and this
repo's `opencode/` tree uses the same spelling.

**OpenCode reads Claude Code's skill tree, and its own wins.** Both measured on 1.18.7: a skill
placed only in `.claude/skills/` is discovered, and when the same name exists in `.opencode/`,
`.claude/` and `.agents/` at once, exactly one entry survives — the `.opencode/` copy. Reading the
Claude tree can be switched off with `OPENCODE_DISABLE_CLAUDE_CODE_SKILLS=1`.

**It does not, however, reach a plugin.** Claude Code installs marketplace plugins under
`~/.claude/plugins/cache/<marketplace>/<plugin>/skills/…`, and only `~/.claude/skills/` is on
OpenCode's search path. Measured both ways at once: a skill placed the way a plugin actually lands
is invisible to OpenCode, while a hand-placed one beside it under `~/.claude/skills/` is found.

So the two output trees **are** independent for the distribution path this repo uses. Installing the
`deniz-*` plugins for Claude Code does not leak them into OpenCode, and `opencode/` is the only way
they arrive there — which is what ADR-0002 assumes. The Claude-tree reading only becomes relevant if
someone hand-copies skills into `.claude/skills/`, and it can be switched off outright with
`OPENCODE_DISABLE_CLAUDE_CODE_SKILLS=1`.

Three behaviours were measured on OpenCode 1.18.7, and each corrects or extends the documentation:

- **`OPENCODE_CONFIG_DIR` is searched for skills**, though the documentation lists only agents,
  commands, modes and plugins for it. So the built `opencode/` tree has two viable mount points, not
  one: that variable, or a project-local `.opencode/`.
- **It adds a location rather than replacing the default.** Setting it does not hide
  `~/.config/opencode/` or the package cache — unlike Claude Code's `CLAUDE_CONFIG_DIR`, which does
  replace. Relevant to anyone trying to get a clean listing.
- **Unrecognised frontmatter really is ignored.** A skill carrying `disable-model-invocation`,
  `user-invocable` and `allowed-tools` loaded and ran without complaint. Claude-only keys reaching
  the OpenCode tree are therefore dead weight rather than breakage — still worth dropping and
  reporting (ADR-0006 axis 3), but a tidiness problem rather than a correctness one.
- **A directory under `skills/` with no `SKILL.md` is ignored silently.** This is the parking spot a
  skill→command conversion needs: an item emitted as an OpenCode command can keep its bundled files
  at `skills/<name>/` and reference them from the command body, instead of losing them.
- **`@file` in a command body resolves against the project root** — the directory OpenCode was
  started in. Not the command file's own directory, and not `.opencode/`. Measured by probing all
  three bases at once: `@h5-root.md` and `@.opencode/skills/…` substituted, a sibling of the command
  file and a `.opencode/`-relative spelling did not.

Together those two give the full recipe for a skill→command conversion: park the bundled files at
`skills/<name>/`, and spell the body's references from the project root as
`@.opencode/skills/<name>/…`. The catch is that project-root resolution ties the recipe to a
**project-local mount**; for a global install under `~/.config/opencode/` the project root is
unrelated to where the assets sit, so `@` cannot reach them and the body must name the path in prose
for the agent's own read tool instead.

`opencode run` does not expand a slash in the message text: `/name …` reaches the model as literal
prose, and a name that exists nowhere produces no error at all. What follows from that is *not*
that commands are TUI-only — **`opencode run --command <name> [args]` invokes one**, and the CLI
resolves the name before any model call, so an unknown one fails without spending a token. With
`--format json` the whole event stream is readable, `tool_use` included, which makes "did the model
invoke this skill" an observation rather than a question put to the model.

The distinction is not academic, because the wrong path *looks* like it works. Sending
`/grill-me …` as text made the model invoke the `grilling` skill on its first turn — indistinguishable
from command expansion until a nonexistent name is sent as the control, which passes straight
through to a model that simply answers. The model had inferred from the word, not read the command.

Discovery stays free and deterministic: **`opencode debug skill` prints every resolved skill as JSON
with its `location`**, `opencode debug config` prints the resolved commands and agents, and
`opencode debug paths` prints the home/data/config/cache/state roots — all better than asking a
model what it can see, which is how the earliest rounds were run.

Read that way, discovery resolves as:

| Mount point | Discovered? |
|---|---|
| project-local `.opencode/skills/` | yes, walking up from the working directory |
| global `<home>/.config/opencode/skills/` | yes, from any working directory |
| `OPENCODE_CONFIG_DIR/skills/` | yes — and on a name collision it **shadows** the global copy |

`OPENCODE_CONFIG_DIR` is additive: it does not hide the global config dir, and `opencode debug paths`
still reports the standard `config` root when it is set. A directory under `skills/` with no
`SKILL.md` appears in no listing at all — the parking-spot finding, now from a deterministic source
rather than the absence of a complaint.

Three more behaviours that bear directly on the emitters:

- **The `name:` field wins over the directory name.** A skill in `skills/e5-dirname-differs/` whose
  frontmatter says `name: e6-name-field-wins` is listed under the frontmatter name. The addressable
  identity is the field, not the folder — the opposite of Claude Code, which addresses a plugin
  skill by its directory. So a per-harness renaming can be done in frontmatter alone.
- **A skill and a command may share a name.** Both resolve, from the same project, with no
  complaint: the skill appears in `debug skill`, the command in `debug config`. That is
  `invocation: both` shown to be expressible rather than assumed — the collision class ADR-0005
  worried about is not a collision here.
- **Every `.md` under `commands/` becomes a command**, frontmatter or not: a bare file with no
  frontmatter was registered with an empty description. So bundled files must never be parked beside
  a command — only under `skills/<name>/`, which is ignored. Parking them in `commands/` would
  publish each one as a phantom command.

That the global mount works has a consequence for the conversion recipe above. Assets for a globally
installed item sit under `<home>/.config/opencode/skills/<name>/`, which no project-root-relative
`@` reference can reach. So the recipe splits by mount point: `@.opencode/skills/<name>/…` for a
project-local install, and a prose instruction naming the path for a global one. Which mount points
this repo intends to support is therefore a decision the emitter needs before it can write the
reference.

### Does a model read a pointer as the user's move? (1.18.7, model grok-4.5, `opencode run`)

[ADR-0008](../adr/0008-references-are-symbols.md) spells a reference two ways: `ns:name` means the
model invokes the target, `/ns:name` means the human is told what to open. The linker proves only
that the target is *reachable* by the audience named; whether a model actually declines to invoke
what it was told to point at is runtime behaviour, and it was probed rather than assumed.

Fixtures were project-local under `.opencode/`, discovery confirmed with `opencode debug skill` and
`debug config` before any prompt: an `auto` knowledge skill carrying both reference lines, a plain
skill as the model-edge target, and a command with no skill of its own as the pointer target.

- **Control (model-edge).** "use the `zx-target` skill now. Do not describe it — invoke it."  → the
  knowledge skill fired, then the target skill fired. Model-side composition works.
- **Pointer.** "suggest opening `/zx-ceremony` to the user — do not run it yourself." → the
  knowledge skill fired and **no invocation of the command was attempted**; the model replied "Open
  `/zx-ceremony` when you're ready."

So the measured-safe pointer template is the slash form **plus the explicit guard clause**. The
clause is load-bearing as far as this probe goes: a bare slash on its own was never tried, so
nothing here says it would behave the same. On Claude Code the question is narrower, because the
mechanism does not depend on wording at all — a `manual` item is filtered out of the model's skill
listing entirely (measured above), so the worst a bad pointer sentence can cost there is a confusing
message, never a wrong invocation.

### Observed on this repo's real output (1.18.7, TUI, model GPT-5.6 Terra)

The fixture rounds above were confirmed against the built `opencode/` tree itself: three mounts
(`OPENCODE_CONFIG_DIR`, project-local `.opencode/`, global config dir) each resolve the full set —
every skill with a `SKILL.md`, every command, no parked directory in any listing — and a TUI
session exercised the artifacts end to end:

- **A typed command pastes its whole body into the chat as the message.** A command is a
  template, so a seven-line body (grill-me) reads clean while a 150-line one (brainstorming,
  writing-plans) dumps a wall of text on every invocation. Correctness is unaffected — the model
  follows the pasted body — but long-body `manual` conversions are noisy; the candidate emitter
  fix is tracked in `docs/ROADMAP.md`.
- **Cross-artifact composition is model-mediated, and works.** The TUI does not expand a slash
  command nested inside a command body; the model reads "Run a `/grilling` session" and invokes
  the `grilling` skill through its skill tool. Trigger command → knowledge skill survives the
  crossing, on a non-Claude model.
- **Parked bundles are reachable.** Asked for the visual-companion guide, the model located and
  read the parked file by absolute path — behind a folder-access permission prompt, since a
  config-dir mount puts skill files outside the project tree.
- **All three invocation surfaces behave on real output.** `manual` commands list in the `/`
  menu under their curated descriptions; an `auto` skill fired on a free-form bug sentence (the
  merged systematic-debugging visibly steered the session loop-first); a `both` item served both
  surfaces in one session — `/writing-plans` paste, then a model-side `Skill "writing-plans"`
  invocation.

Two mount facts measured on the way. The **package cache outranks every mount**: `plugin:`
packages > `OPENCODE_CONFIG_DIR` > global config skills, so an installed upstream package shadows
same-named curated output until removed. And the **global config mount follows
`XDG_CONFIG_HOME`**, falling back to the real profile — relocating `USERPROFILE` moves what
`opencode debug paths` *reports* but not what discovery *reads*.

Access control is config-side — `opencode.json` carries allow/deny/ask patterns over skill names,
and an agent can drop skills entirely with `skill: false`. None of that travels inside a
distributed artifact, so it cannot substitute for getting the output shape right.

## What the three words mean per harness

`skill`, `command` and `agent` name different things in each harness, and most confusion about this
repo starts by assuming they travel. They do not.

| | Claude Code | OpenCode |
|---|---|---|
| **skill** | The *same mechanism* as a command: `.claude/skills/x/SKILL.md` and `.claude/commands/x.md` both produce `/x` and behave identically. The only difference is packaging — a command is one file, a skill is a directory that can carry assets | **Model-only, always.** A user cannot reach it at all; agents load it through a native `skill` tool |
| **command** | A skill in one-file packaging | **The user surface, always.** The only user-invocable artifact, carrying `agent`, `model`, `subtask`, `template`, `$ARGUMENTS` |
| **agent** | A subagent with its own context, dispatched rather than invoked | The same, declared `mode: subagent` |
| **who invokes it** | a **frontmatter flag**, independent of packaging | **which artifact it is** — there is no flag |

Two consequences this repo is built around:

- **Shape does not carry intent.** In Claude Code a `manual` item can be either packaging, because the
  flag decides. In OpenCode the choice of artifact *is* the decision. So `invocation` is the word
  that means the same thing on both sides, and `as:` is a separate dial for when a specific artifact
  is wanted regardless (ADR-0006 axis 2).
- **`manual` and `command` are not synonyms.** They coincide in Claude Code (a flagged skill and a
  command behave alike) and coincide by construction in OpenCode (manual can only be a command).
  They come apart in three places: `both` needs two artifacts in OpenCode and one flagless skill in
  Claude Code, which `as: command` cannot express; a `manual` item carrying assets wants a directory
  on one side and a single file on the other, so only the intent survives the crossing; and
  `as: agent` is a shape for which invocation is not the question at all.

## The intent matrix

The same curation intent, expressed natively per harness:

| Intent | Claude Code | OpenCode |
|---|---|---|
| The model decides (passive knowledge) | skill, `user-invocable: false` | skill — the only mode it has |
| The user decides | skill, `disable-model-invocation: true` | **command**; a skill cannot express this |
| Either may | skill, neither key set | skill *and* command |

The asymmetry is the whole point: in Claude Code the dial is a frontmatter flag on one artifact, in
OpenCode it is a choice of artifact.

## Reachability is not propensity

`invocation` decides who *may* pull the trigger. Whether the model actually does is a separate
property, and the two are measured differently: reachability is mechanical and exact — a flag, a
listing, an artifact — while propensity is a selection the model makes from names and descriptions,
and nothing in either harness forces it.

The paired tier-2 records
[`tdd-intent-fire-claude-2026-08-02`](../../experiments/harness-invocation/records/2026-08-02-tdd-intent-fire-claude.md)
and
[`tdd-intent-fire-opencode-2026-08-02`](../../experiments/harness-invocation/records/2026-08-02-tdd-intent-fire-opencode.md)
measure one bounded JavaScript task: the control prompt was `Implement a formatDuration(seconds)
function in duration.js that turns 5400 into 1h30m.`, and the intent prompt added exactly `Let's go
test-driven. ` before it; neither prompt named a skill. Claude Code 2.1.220 (`claude-opus-5` and
`claude-fable-5`) and OpenCode 1.18.7 (`openai/gpt-5.6-sol@xhigh` and `xai/grok-4.5@high`) each ran
two model configurations three times per condition: six valid attempts in each condition and twelve
per harness.

The [`tdd-intent-fire-claude-2026-08-02`](../../experiments/harness-invocation/records/2026-08-02-tdd-intent-fire-claude.md)
record observed target-skill invocation in 3/6 control attempts and 6/6 intent attempts. The behavior
distribution over six classifiable attempts per condition was control: `followed` 0/6, `partial`
3/6, `not-followed` 3/6; intent: `followed` 0/6, `partial` 6/6, `not-followed` 0/6. Execution policy
denied every attempted JavaScript test or check, so the `partial` outcomes show incomplete test-first
activity rather than a completed red-green cycle; the panel observed none.

Independently, the [`tdd-intent-fire-opencode-2026-08-02`](../../experiments/harness-invocation/records/2026-08-02-tdd-intent-fire-opencode.md)
record observed target-skill invocation in 5/6 control attempts and 6/6 intent attempts. Its behavior
distribution over six valid attempts per condition was control: `followed` 5/6, `partial` 0/6,
`not-followed` 1/6; intent: `followed` 6/6, `partial` 0/6, `not-followed` 0/6.

Separate, earlier exploratory work—not this intent-fire panel—yielded two OpenCode command-surface
notes. A converted command could
reach its parked bundle, and a sibling-item path that `validate` warns about could be read through
the parked skill layout. Tool input showed the model resolving `../<item>/…` against the skill
directory rather than the command file. The filesystem warning remains valid where the target has
no `skills/<name>/` directory — an excluded item, or a `manual` item whose empty bundle caused the
emitter to drop the husk.

Invocation and TDD discipline remain distinct events: a target `Skill` or `skill` tool input records
the former, while the latter is a review of the session's ordering, outputs, final diff, and text.
These sessions are samples from non-deterministic models, not deterministic rates or causal proof
that the intent phrase caused either outcome. The synthesis does not extend beyond these exact
models, harness versions, prompt wording, one tiny task, and three repeats per condition.

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
  only other trigger pressure, in its `description` and one body line. The remaining matches for
  "You MUST" are different things and should not be swept up with them:
  `systematic-debugging` uses it for *procedural* discipline inside the skill ("complete each phase
  before proceeding"), and `writing-skills` uses it twice — declaring a dependency
  ("REQUIRED BACKGROUND") and again inside an authoring example of that same convention.
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
  The resolution, first applied in `deniz-dotnet-general`: the "neither" class maps to our
  `invocation: auto`. The dependents point at these skills by bare name (candidate-tier prose that
  survives unchanged into both trees), and `auto` keeps them model-reachable in both harnesses
  while users never see them — no "neither" value is needed.

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

Two things were measured about the prose form, and together they set the price of leaving it alone.
It **works**: `grill-me` → `grilling` fired as the first act of the session, a user-only trigger
reaching model-only knowledge across the boundary, exactly as the architecture intends. And it is
**invisible** — a bare name is candidate-tier by [ADR-0008](../adr/0008-references-are-symbols.md),
so that edge is in no `depends_on`, in no ledger, and under no guard. The one composition this repo
has demonstrated at runtime is the one its linker cannot see.

The spelling also disagrees with ours in a way worth counting before deciding anything. Across the
module, twenty-seven bodies name another curated item as a bare `/name`; in eleven of them the
target is `auto`, so the slash claims a user surface that target does not have — a human cannot type
it at all. Under our grammar a slash means the human is the audience, and the same eleven sentences
spelled namespaced would be linker errors. Matt's convention reads `/x` as "the model invokes x",
and models oblige, which is why the incoherence costs nothing today. Re-derive the count before
acting on it; the promotion rule ("any touch promotes it into the convention") means each of these
becomes a curation decision the moment its body is edited for any reason.

## Prior art: wshobson/agents

The multi-harness marketplace ADR-0002 took its model from. One source-of-truth tree
(`plugins/<name>/{agents,commands,skills}/`), a generator (`tools/generate.py`) and a per-harness
adapter (`tools/adapters/{codex,copilot,cursor,gemini,opencode}.py`) that emits idiomatic artifacts
rather than a shared subset.

The governing sentence, which is the design target for our manifest:

> Each adapter handles incompatibilities mechanically — authors don't need to know the per-harness
> rules to write portable content.

**What it does, read from the adapters** — worth knowing precisely, because the differences from
this repo are as instructive as the similarities:

- **Type is inherited, never chosen.** Each source type maps 1:1 to the target's equivalent
  "without intermediate transformations". Conversion happens *only* where the target lacks the
  concept — Codex has no commands, so commands become skills there. Nothing lets an author say "emit
  this skill as a command"; that is [ADR-0006](../adr/0006-output-is-a-transformation.md) axis 2, and
  it is where this repo is more ambitious than its prior art.
- **Skill directories are mirrored whole.** `skill.dir.rglob("*")` copies `references/`, `assets/`,
  `scripts/`, `examples/` verbatim, preserving relative paths and binary content. Sibling references
  inside a body keep working because the directory survives — so the problem of a converted item
  pointing at files that no longer exist never arises for them.
- **Body rewriting is limited to tool names.** `strip_claude_tool_refs()` in `adapters/base.py`
  turns Claude Code's tool vocabulary into the target's — `` `Read` `` → `` `read` ``, "the Read
  tool" → `` `open` ``, `` `Bash` `` → `` `shell` ``. There is no link or file-reference rewriting
  anywhere.

  **This repo does not need that rewrite, and implementing it would cause a bug.** OpenCode's own
  tool identifiers (from its config schema) are `read`, `edit`, `glob`, `grep`, `list`, `bash`,
  `task`, `todowrite`, `question`, `webfetch`, `websearch`, `lsp`, `skill`, `external_directory`,
  `doom_loop` — the same words as Claude Code's, differing only in case, so the transform reduces to
  a case fold. And the vendored corpus does not need even that: of 206 upstream `SKILL.md` files,
  the search for Claude tool names returns four hits, all of them `` `Task` `` inside .NET skills
  where it means `System.Threading.Tasks.Task`. A case-folding rewrite would have silently corrupted
  three .NET skills to fix zero real references.
- **Flat namespaces get a prefix.** OpenCode output is `.opencode/skills/<plugin>-<skill>/SKILL.md`
  and `.opencode/{commands,agents}/<plugin>__<name>.md` — the plugin name is carried into the
  artifact name rather than requiring globally unique names as ADR-0002 does.
- **Agent permissions are mapped, not dropped.** Claude Code's `tools:` allowlist becomes an
  OpenCode `permission:` deny block — the concrete recipe for the mapping ADR-0002 deferred.
- **A hard size cap is absorbed by splitting.** Codex truncates skill bodies over 8 KB at load, so
  the adapter splits the overflow into `references/details.md` and `_overflow.md` rather than
  letting the truncation happen silently.
- **Most generated output is gitignored** and rebuilt by `make generate`; only the small registries
  are committed. This repo commits everything instead, because ADR-0001 wants a clone to work
  immediately — a different goal, not a disagreement.

## Sources

- [Claude Code — Extend Claude with skills](https://code.claude.com/docs/en/skills)
- [Anthropic — Skill authoring best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- [OpenCode — Agent Skills](https://opencode.ai/docs/skills/)
- [OpenCode — Commands](https://opencode.ai/docs/commands/)
- [wshobson/agents — docs/harnesses.md](https://github.com/wshobson/agents/blob/main/docs/harnesses.md)
- [claude-code#26251](https://github.com/anthropics/claude-code/issues/26251) — `disable-model-invocation`
  reported as blocking user slash invocation too; closed as duplicate, and **did not reproduce** on
  Claude Code 2.1.220
