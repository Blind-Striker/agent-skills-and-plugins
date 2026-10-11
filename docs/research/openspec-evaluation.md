# OpenSpec evaluation

Date: 2026-10-12

> **Dated evidence and advisory synthesis, not current local policy.** This note records whether
> OpenSpec should join the estate and in what shape. The curator's decision to park it, and the
> triggers that reopen it, are operational state in the [roadmap](../ROADMAP.md#deferred). Current
> invocation and emission mechanics live in
> [Transformation and emission](../architecture/transformation-and-emission.md), and the lifecycle of
> plans and specifications lives in
> [Documentation](../engineering/documentation.md#handovers-and-plans).

## Question and evidence boundary

Should OpenSpec be added to the estate, and if so, through which integration shape?

The pins are Fission-AI/OpenSpec v1.14.1 (`87c3595`), OpenCode v2.0.26 (`9b4ec57`), and this
repository at `29d6bde`. All findings are source reading of those trees plus GitHub issues,
releases, and community posts fetched on 2026-10-12. Nothing was executed: no `openspec init` ran,
so every generated tree and frontmatter field below is inferred from source. Three independent
reports were reconciled into one synthesis, and an adversarial verification pass then rechecked each
load-bearing claim against source; its corrections are applied below.

## What OpenSpec is

- A spec-driven development tool: an MIT-licensed Node CLI (Node 20.19 or later; `LICENSE`,
  `package.json:17,59-60`) plus 12 committed agent skills. It ships about one release a week:
  v1.7.0 on 2026-07-29 through v1.14.1 on 2026-10-06 is eleven releases.
- Each change is a folder holding `proposal.md`, `design.md`, `tasks.md`, and delta specs. Delta
  specs use ADDED, MODIFIED, REMOVED, and RENAMED sections with SHALL requirements and WHEN/THEN
  scenarios.
- Archiving a change merges its deltas into the long-lived `openspec/specs/<capability>/spec.md`
  files (`docs/concepts.md:46-50,395-398,547`).
- The skills are thin clients. Their real instructions come from `openspec instructions --json`,
  which the propose skill calls the authoritative guidance (`skills/openspec-propose/SKILL.md:158`).
  That runtime contract and `schemas/` were not read, so the skill bytes understate what any
  integration would have to track.
- Telemetry is on unless `OPENSPEC_TELEMETRY=0` or `telemetry.enabled false` turns it off; events go
  to PostHog through `edge.openspec.dev` (`src/telemetry/index.ts:7-8,31-32,73`). The separate
  npm-registry update check is skipped by `OPENSPEC_TELEMETRY=0`, `DO_NOT_TRACK=1`, or
  `OPENSPEC_NO_UPDATE_CHECK` (`docs/cli.md:215`).
- The core profile is six workflows: propose, explore, apply, update, sync, and archive
  (`src/core/profiles.ts:14`); verify belongs only to the expanded profile. The twelve skill
  descriptions run 176 to 433 characters: the core six total 1,694 characters and all twelve 3,063.
  The core six `SKILL.md` files total 93,514 bytes, frontmatter included.
- An upstream test keeps the committed `skills/` byte-identical to generator output
  (`test/core/templates/skillssh-parity.test.ts:20-29`), and the committed tree is the
  all-workflows variant. Its bodies carry 49 `/openspec-*` cross-references on 48 lines. The core
  six alone carry 24, and 7 of those point to non-core skills: `/openspec-continue-change` six times
  and `/openspec-new-change` once.

### What `openspec init` writes per harness

With the default delivery, `both` (`src/core/init.ts:974`):

- **Claude Code** gets `.claude/skills/` plus `.claude/commands/opsx/`. The Claude command adapter
  writes only `name`, `description`, `allowed-tools`, `category`, and `tags`
  (`src/core/command-generation/adapters/claude.ts:14-31`), and skill frontmatter is only `name`,
  `description`, `allowed-tools`, `license`, `compatibility`, and `metadata`
  (`src/core/shared/skill-generation.ts:182-190`). No file in `src`, `skills`, `schemas`, or `docs`
  emits `disable-model-invocation`, `user-invocable`, `allow_implicit_invocation`, or an autoinvoke
  key, so the model can reach every `/opsx:*` command and every skill.
- **Codex** gets `.agents/skills/` and no commands, because Codex is a skills-invocable tool
  (`src/core/command-surface.ts:17-39`); its skills are invoked as `$openspec-*`
  (`docs/supported-tools.md:192`). Codex's own `.agents/skills` loader was not located in the
  Codex source, so this rests on OpenSpec source and the issue #2022 audit.
- **OpenCode** gets `.opencode/skills/` plus `.opencode/commands/opsx-<id>.md`
  (`src/core/command-generation/adapters/opencode.ts:37`). OpenCode 2.0.26 also discovers
  `.claude` and `.agents` (`packages/core/src/config/discovery.ts:41`), but it keys skills by
  directory-name ID in a map (`packages/core/src/config/plugin/skill-file.ts:45-48`,
  `compatibility.ts:41,53`, `skill.ts:155`; `packages/core/src/skill.ts:100-101`). Three copies of
  `openspec-*` therefore yield one catalog entry per name, and which copy wins is unclear. The costs
  are the ambiguous winner and two command spellings, `.claude/commands/opsx/*` and
  `.opencode/commands/opsx-*`; it is not a triple listing. Whether OpenCode also surfaces
  `.claude/commands/opsx/*` as commands was not traced.

`openspec init --tools none` creates the `openspec/` root (`specs/`, `changes/`,
`changes/archive/`, and `config.yaml`) without writing any harness file (`docs/cli.md:105`;
`src/core/init.ts:306-309,767-768,899-904,982,1167`). Not every side path, such as legacy cleanup,
was traced.

With delivery `skills` or `both`, `openspec update` compares only the recorded version stamp, so a
hand edit survives until the next CLI version or `--force` (`docs/cli.md:217-224`). The docs still
say edits are replaced on the next update and that generated files are OpenSpec's to own
(`docs/supported-tools.md:189`). At one release a week, patching generated files is a workaround,
not a supported shape.

A skill the model picked on its own steps back only when the repository has no `openspec/` root
(`skills/openspec-propose/SKILL.md:30-39`). In a repository that uses OpenSpec, auto-triggering is
fully active.

## Overlap and gaps

| Job | OpenSpec | This estate | Verdict |
|---|---|---|---|
| Clarify intent | explore | brainstorming, grilling, grill-with-docs, wayfinder | Covered |
| Proposal and design | propose writes `proposal.md` and `design.md` | brainstorming's written design, to-spec | Covered, with a risk of two competing specs |
| Task breakdown | `tasks.md` checklist | writing-plans (TDD steps, exact paths), to-tickets | Covered; ours is finer-grained |
| Implementation | apply | executing-plans, subagent-driven-development, implement, implement-spec, test-driven-development | Covered; ours is stricter. One hands-on review saw apply write a whole implementation with no tests |
| Check against the spec | verify (expanded profile only) | requesting-code-review (Spec axis), verification-before-completion | Covered |
| Record decisions | `design.md` decisions | ADRs through domain-modeling | Covered |
| Lasting per-capability behaviour spec | `specs/<capability>/spec.md` | None | Real gap; see below |
| Delta merge for brownfield changes | ADDED, MODIFIED, REMOVED, RENAMED, merged on archive | None | Gap; useful only together with the row above |
| Spec structure validation | `openspec validate` | None | Small gap |
| Change audit trail | `changes/archive/YYYY-MM-DD-*` | Git history and pull requests | Mostly covered |
| Planning across repositories | Stores (beta) | None | Speculative |

The lasting-spec gap exists by policy only in this repository: plans and specifications under
`docs/superpowers/` are deleted when the work merges
([Documentation](../engineering/documentation.md#handovers-and-plans)). The curated Process skills
carry no delete-on-merge rule into other repositories; a grep of the emitted brainstorming,
writing-plans, executing-plans, and finishing-a-development-branch bodies found none (a limited
grep, so an inference). For the curator's other repositories the gap is unrecorded rather than a
policy.

## Integration options

| Option | Feasible | Cost | Claude Code, OpenCode, Codex | Skill-catalog impact |
|---|---|---|---|---|
| **(1) Submodule; curate the committed `skills/openspec-*`** | Yes: the skills are committed files kept identical to the generator output. | High and ongoing: re-curation about weekly; localizing the `/openspec-*` cross-references ([ADR-0008](../adr/0008-references-are-symbols.md)), with an overlay decision for the 7 core references that dangle if only the core six are taken; the pinned submodule and the floating npm CLI can drift apart, so the exact CLI version would go in the item comment (inference); Node, the CLI, and a telemetry opt-out on both curator machines; `init --tools none` once per repository. | Claude Code: `manual` writes `disable-model-invocation: true`, invoked by slash command. Codex: `policy.allow_implicit_invocation: false`, invoked as `$plugin:skill`. OpenCode: `opencode/autoinvoke: false`; the skill tool can still load an ID that text names. | About zero with `manual`, since the skills leave all three listings; with `auto`, six entries and about 1,694 characters. |
| **(2) An original skill that drives the CLI** | Not to this repository's standard yet: an original skill is a byte copy with no invocation declaration until Iteration 2 (roadmap). | High: it would rewrite upstream's 93 KB contract against a CLI that changes weekly. | It cannot be `manual` the same way in all three harnesses until then. | Depends on posture; no clean `manual` until Iteration 2. |
| **(3) Borrow WHEN/THEN scenarios through an overlay on to-spec or writing-plans** | Yes. | Low. | The same in all three, through the existing emitters. | No new entries. |
| **(4) Outside the estate; run `openspec init` per repository** | Yes, today. | No repository work, but no manual setting exists and hand-patching generated files is a workaround. | Claude Code: `.claude/skills` plus `.claude/commands/opsx`, all model-reachable. Codex: `.agents/skills`, implicit invocation, `$openspec-*`. OpenCode: `.opencode/skills` and commands, plus the `.agents` and `.claude` copies, which collapse to one entry per name with an unclear winner. | Six auto entries (about 1,694 characters) per harness inside that repository; on Claude Code possibly about twice that unless delivery is `skills` (inference, unmeasured). Under the measured [listing budgets](../../experiments/harness-invocation/records/2026-10-11-skill-catalog-budgets.md), every added entry shortens every other description. |
| **(5) Do nothing** | Yes. | None. | No change. | None. |

On option (3): a delta only means something when it merges into a spec that lasts. That framing is
an inference from the archive-merge semantics cited above, not an upstream statement. The value of
(3) is low unless something outlives the merge.

On option (4): it breaks two standing rules of [ADR-0007](../adr/0007-control-beats-fidelity.md),
that the curator holds the trigger per item and that every harness is decided at once.

## Is it necessary

No pain has been recorded. The missing living spec follows from this repository's plan lifecycle,
and the curator's working truth today is tests, Git, and the architecture canon.

Living specs are also the most disputed part of OpenSpec:

- A [Hacker News thread](https://news.ycombinator.com/item?id=49734264) on OpenSpec calls
  long-lived specs in the repository useless and says the spec almost immediately goes out of date.
- OpenSpec [issue #1909](https://github.com/Fission-AI/OpenSpec/issues/1909), an open maintainer
  design-review proposal to fix this, says main specs read like a stale copy of the code.
- In [abapify/adt-cli#224](https://github.com/abapify/adt-cli/issues/224), six changes were never
  archived and drift went unseen for weeks.
- OpenSpec's own repository archived 2 changes in 2026-07, 1 in 2026-08, and none in 2026-09 or
  2026-10 to the pin, with 28 unarchived changes open.

It also cuts against the curator's taste:

- One `propose` writes four artifacts, and the vendor itself says the ceremony is probably not worth
  it for a one-character typo (`docs/faq.md:17`).
- Every skill auto-triggers by default.
- A [hands-on report](https://heyuan110.com/posts/ai/2026-06-28-openspec-superpowers-workflow/)
  that ran OpenSpec beside superpowers' brainstorming saw the two spec files disagree within the
  hour. This estate's brainstorming hands software work only to writing-plans, and executing-plans
  calls the spec its binding authority; with both chains present it is unclear which spec that is.
- The Claude Code and Codex listings are already over budget
  ([catalog-budget record](../../experiments/harness-invocation/records/2026-10-11-skill-catalog-budgets.md)).

### Issue #2022

[Issue #2022](https://github.com/Fission-AI/OpenSpec/issues/2022) is an open proposal, opened on
2026-10-01 by OpenSpec's lead maintainer (`MAINTAINERS.md:9`), to make skills-only delivery the
default at the next major version, deprecate commands then, and decide removal later. It also names
the precedence question of which duplicate copy wins. A maintainer comment of 2026-10-07 finds that
the agentskills.io specification has no invocation fields and that, with skills only, on OpenCode
only the model loads a skill, so users lose a way to start a workflow on purpose. Skills-only
delivery alone therefore would not make option (4) compatible with a curator-held trigger on
OpenCode; it would also need an invocation control in OpenSpec's skill generator.

## Prior art not evaluated

Third-party OpenSpec ports and bridges exist and were not assessed as candidates for option (2):
AngDrew/opencode-openspec, chyiiiiiiiiiiii/openspec-skills, the `openspec` skill in
itechmeat/llm-code, the `openspec-workflow` skill in wyattowalsh/agents, and anthemflynn/dwc's
`openspec-brainstorming`, which replaces superpowers' brainstorming when an `openspec/` root exists.

## Not verified at this boundary

- No `openspec init` ran in a sandbox, so generated trees are source-derived.
- Whether `.claude/commands/opsx/*` entries appear in Claude Code's skill listing beside the
  `.claude/skills` copies, and the command description lengths, were not measured.
- Which duplicate copy wins in OpenCode 2.0.26 (compatibility loader against config skill loader
  order) was not traced.
- Whether a core-profile generation, rather than the committed all-workflows tree, drops the 7
  non-core references was not checked.
- That `openspec update` with delivery `skills` deletes command files without an edit check
  (`src/core/update.ts`, `removeCommandFiles`) was reported in the #2022 discussion and not checked
  in source.

## Decision and advice

**Verdict: park (option 5).** The curator decided on 2026-10-12 to document OpenSpec and park it: no
submodule, no original skill, no overlay. Nothing in the curator's recorded work calls for it, and
the one real gap it fills comes from a deliberate lifecycle choice in this repository.

Advice for a reopening, recorded here as advisory synthesis:

- **Evaluate only in a throwaway sandbox repository** with option (4): set delivery to `skills`,
  turn telemetry and the update check off, and use only OpenSpec's own chain there (propose, apply,
  archive) with no brainstorming or writing-plans.
- **If it earns its place, adopt it through option (1)**: its own Module or inside Process, every
  item `manual`, `init --tools none` per repository, and the exact CLI version recorded beside the
  items. Do not keep option (4) running in real repositories.

The reopening triggers live in the [roadmap's Deferred entry](../ROADMAP.md#deferred). If it is
reopened, these questions are the curator's alone:

1. Should any behaviour spec outlive the merge? This drives everything else.
2. Is there a specific repository or pain behind the question? Which one?
3. Is a Node CLI, with a telemetry opt-out, acceptable on both curator machines?
4. In a repository that uses OpenSpec, should it replace brainstorming and writing-plans, or sit
   beside them with a routing rule?
5. If adopted, a new Module or Process? Is a weekly sync review acceptable upkeep?
6. Which OpenSpec workflows are in scope: the core six, or only propose, apply, and archive?
