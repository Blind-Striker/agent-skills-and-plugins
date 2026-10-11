# Claude ultracode and orchestration ports

Date: 2026-10-12

> **Dated evidence and decision history, not current local policy.** This note records what Claude
> Code's ultracode is, what OpenCode and Codex can offer in its place, and the curator's 2026-10-12
> decisions about carrying multi-agent orchestration in this estate. The queued work, the deferred
> design, and the open questions are operational state in the [roadmap](../ROADMAP.md). Current
> invocation and per-harness file mechanics live in
> [Transformation and emission](../architecture/transformation-and-emission.md) and
> [`curation/SCHEMA.md`](../../curation/SCHEMA.md#harness_files); installer ownership lives in
> [Distribution and installation](../architecture/distribution-and-installation.md#destination-selection-and-ownership).

## Question and evidence boundary

What is ultracode, does a faithful port to OpenCode or Codex already exist, and how should this
estate carry the same kind of orchestration in all three harnesses?

The pins are:

- **Claude Code 2.1.296:** the Windows `claude.exe` from npm `@anthropic-ai/claude-code` 2.1.296
  (SHA-256 `76de74f7694a5bdc03378a5f837c9e132827bf004c98c276bc4e9094ff06769a`), read by a
  read-only byte scan and never executed. `@` numbers below are byte offsets into that file;
  identifiers are minified names from that build.
- **Claude docs:** [dynamic workflows](https://code.claude.com/docs/en/workflows) and
  [model configuration](https://code.claude.com/docs/en/model-config#when-ultracode-is-available),
  fetched 2026-10-12, and the Claude Code changelog at
  [`3deb873`](https://github.com/anthropics/claude-code/blob/3deb873687379f0c199a69fdaccc65f0865f7a29/CHANGELOG.md).
- **OpenCode:** `anomalyco/opencode` v2.0.26 (`9b4ec57`), paths under `packages/`.
- **Codex:** `openai/codex` rust-v0.162.1 (`092d3ac`), paths under `codex-rs/`.
- **This repository:** `29d6bde`.

Nothing ran in any harness: no session confirmed the keyword or the skill autoload at runtime, no
OpenCode 2 load of a third-party plugin was attempted, and no Codex hook trust or spill flow was
exercised. An adversarial verification pass rechecked each load-bearing claim against source; its
corrections are applied below.

**Text ownership.** The prompt texts behind ultracode are Anthropic's; the module that holds them
carries `(c) Anthropic PBC. All rights reserved.` This note paraphrases them and quotes at most one
short line per piece as evidence. They are not vendored, and the MIT license of a third-party mirror
of Claude Code prompts does not plausibly license them (inference).

## The five-piece mechanism

Ultracode is not a skill, not a fixed system-prompt section, and not a single tool. Five pieces work
together:

1. **Keyword detector (harness code, `@219244675`, `@223931226`).** A case-insensitive whole-word
   match for "ultracode" in the raw text a human typed. It skips text inside quotes, backticks,
   brackets, braces, parentheses, and tags, and any match next to `/`, `\`, or `-`. It returns
   nothing when the prompt starts with `/`, so invoking any skill or command by slash never fires
   it; only a prose mention of the word does. Since 2.1.210 a prompt passed with `-p`, an
   unstamped SDK prompt, a scheduled task, or a relayed webhook or comment does not fire it either
   (docs). Evidence: `if(e.startsWith("/"))return[]`.
2. **Session flag (harness state, `@215822666`, `@213561323`, `@218686519`).** `/effort ultracode`
   turns it on for the session, `claude --effort ultracode` and the Agent SDK `effortLevel`
   value turn it on and also set effort to `xhigh` (`@215821775`), the `ultracode` setting turns it
   on from settings, and the SDK `apply_flag_settings` request may change only `effortLevel` and
   `ultracode`. Since 2.1.284 it is a toggle at any effort level rather than an effort level that
   forces `xhigh` (changelog). It is available only when workflows are enabled and the main model
   supports `xhigh` effort (`@215819354`, `@215818717`); otherwise `--effort ultracode` starts the
   session with ultracode off at the highest allowed effort up to `xhigh` (model-config docs).
   Evidence, from the settings schema: "standing dynamic-workflow orchestration at any effort level".
3. **Injected reminders (meta messages, not the system prompt; `@224752630`, `@223925284`,
   `@223946638`).** On a turn whose prompt matched the keyword, a reminder tells the model the turn
   opted into multi-agent orchestration and to use the Workflow tool. For the session flag, a full
   reminder arrives when ultracode turns on, a short "still on" reminder after 10 or more user turns
   by default (an environment variable or server flag can change the cadence), and an exit notice
   that restores the tool's ordinary opt-in rule when it turns off. Evidence: "opting this turn into
   multi-agent orchestration".
4. **Workflow tool and runtime (`@246765155`, `@244805331`, `@233304741`).** The tool description
   holds the opt-in rule: call the tool only when the user explicitly opted in, by the keyword, the
   session flag, a direct request in the user's own words, a named or saved workflow, or a skill or
   slash command whose instructions say to call Workflow; otherwise describe what a workflow would
   do and ask. Behind it, a background runtime executes a deterministic JavaScript script: no
   filesystem, shell, or `import()`; `Date.now()`, `Math.random()`, and an argument-less
   `new Date()` throw; concurrency is `min(16, max(2, CPUs - 2))` agents by default, settable from 1
   to 256 through `CLAUDE_CODE_WORKFLOW_MAX_CONCURRENT_AGENTS` since 2.1.269; at most 4,096 items
   per `parallel()` or `pipeline()` call and 1,000 agents per run; `schema` results get five
   validation attempts; a run resumes within its session from a journal (`resumeFromRunId`).
   Completion arrives later as a task notification. Evidence: "The user invoked a skill or slash
   command whose instructions tell you to call Workflow."
5. **Bundled `workflow-authoring` skill (`@244827173`, `@244841569`, `@244809895`).** A
   user-invocable skill, enabled when workflows are enabled. When the keyword reminder or the full
   ultracode reminder fires, the harness injects its body as meta messages with no Skill call. Its
   ultracode paragraph makes the opt-in standing: a workflow for every substantive task, several
   workflows in sequence for multi-phase work, the quality patterns (adversarial verification,
   multi-modal sweep, completeness critic, loop until a round finds nothing new), and solo work only
   on conversational or trivial turns. Evidence: "that opt-in is standing".

While ultracode is on, three safeguards are off: the Agent tool's concurrent-subagent cap
(`@228223050`), the "Large workflow" warning (`@243231288`), and, in auto permission mode, the
approval of the first workflow launch (docs).

**How the pieces interact.** The detector and the session flag produce reminders; the reminders
trigger the skill autoload; the tool description makes the model treat the reminder as the opt-in;
the runtime runs the script in the background and reports back through a task notification. The
behavior is all text, and the capability is harness code.

**Availability.** The Workflow tool needs only workflows to be enabled: they are available on paid
plans and through the API and cloud providers, Pro turns them on in `/config`, and a setting, an
environment variable, or managed settings turn them off. Ultracode additionally needs a model that
supports `xhigh`.

**Plugin-shipped workflows.** Besides workflows Claude writes per task, Claude Code runs saved
scripts by name: project `.claude/workflows/`, personal `~/.claude/workflows/`, and
[a plugin's `workflows/` directory](https://code.claude.com/docs/en/workflows#distribute-a-workflow-in-a-plugin)
at the plugin root, or another location named by the plugin manifest's `workflows` field. A plugin
workflow runs as `/<plugin>:<meta.name>`, and its launch approval offers a per-project "don't ask
again". This surface is Claude Code only.

## Is there already a port

No one ships a faithful port that works in all three harnesses.

| Candidate | License | What it is | Verdict |
|---|---|---|---|
| [PabloNAX/ultracode-skill](https://github.com/PabloNAX/ultracode-skill) `bfa2d92` | MIT (`LICENSE`) | A 421-line `SKILL.md` of prompt discipline using Codex `spawn_agent`, `wait_agent`, and `close_agent`; its description is a long trigger list; no OpenCode row. Only the frontmatter and the host table were read. | Ideas only; restate in our own words. |
| [YuanpingSong/ultracodex](https://github.com/YuanpingSong/ultracodex) `ac349f8` | Apache-2.0 (`LICENSE`, `NOTICE`) | Runs Claude workflow scripts unmodified on Codex or OpenCode (README:13); tested only against OpenCode 1.17.18 (README:21). | Not a license barrier: it is a runtime CLI, not a skill to package (inference). A personal install only after checking it on OpenCode 2. |
| [just-every/plugin-ultracode](https://github.com/just-every/plugin-ultracode) `9dde008` | MIT declared, no license file: `.codex-plugin/plugin.json` says `"license": "MIT"`, but the repository has no `LICENSE` file or copyright notice, and its `package.json` is private with no license field | A Codex skill plus a Node engine that drives `codex` subprocesses. | Legally ambiguous; do not vendor. |
| [norandom/OpenUltraCode](https://github.com/norandom/OpenUltraCode) `2638f03` | MIT (`LICENSE`) | OpenCode commands plus a plugin whose default export is an OpenCode 1 function (`src/plugin.ts:10-22`). | Dead on OpenCode 2 at source level: OpenCode 2 needs a default export with an `id` and an `effect` or `setup` function and raises a load error otherwise (`packages/core/src/plugin/module.ts:78-91,125-132`). No runtime load was attempted. |
| [code-yeongyu/oh-my-openagent](https://github.com/code-yeongyu/oh-my-openagent) (OmO) `a103d40` | Sustainable Use License for its own code (`LICENSE.md`), a source-available license | A heavy ecosystem that injects orchestration automatically. | Rejected. |
| LazyCodex | MIT (`LICENSE`) | A Codex counterpart from the OmO author. | Rejected with the ecosystems. |
| [Yeachan-Heo/oh-my-codex](https://github.com/Yeachan-Heo/oh-my-codex) `2db7d51` | MIT (`LICENSE`) | A Codex orchestration ecosystem. | Rejected with the ecosystems. |
| ruflo | MIT (README badge only) | An agent meta-harness for Claude Code and Codex. | Rejected with the ecosystems. |
| oh-my-opencode-slim | MIT (`package.json`) | A slimmer OmO for OpenCode. | Rejected with the ecosystems. |
| open-dynamic-workflows | MIT (`LICENSE`) | A script-as-orchestrator runtime. | Rejected with the ecosystems. |
| superpowers `dispatching-parallel-agents` (`auto`) and `subagent-driven-development` (`both`) | Already curated in Process | Model-driven fan-out with no verification patterns. | The closest thing in the estate today. |

The ecosystems are rejected as a group: they inject automatically, which works against a
curator-held trigger, and they install files and plugins that conflict with this installer's file
ownership; only OmO also has a license barrier. Licenses were read from the cloned `LICENSE` file,
manifest field, or README as stated; the per-ecosystem reasons and the claim that OmO's OpenCode
edition and similar plugins also fail on OpenCode 2 were not independently verified.

## Capability matrix

Codex and OpenCode limits below are configurable defaults, not hard limits.

| Requirement | Claude Code 2.1.296 | OpenCode 2.0.26 | Codex 0.162.1 |
|---|---|---|---|
| Keyword injection | Built in | Only through a plugin: `session.hook("prompt")` can rewrite the text or push into `prompt.skills`, and a skill pushed there loads with no autoinvoke check (`core/src/session/prompt.ts:40-75`). A file under `<config>/plugins/` loads with no `opencode.json` edit (`core/src/plugin/source-directory.ts:7-33`). | Only through a hook: `UserPromptSubmit` appends `additionalContext` (`hooks/src/events/user_prompt_submit.rs:170-179`). The limit is a per-handler default of 2,500 tokens (`hooks/src/output_spill.rs:12,19-24`; `hooks/src/engine/discovery.rs:728-730`); oversized text is written in full to a temporary file and replaced by a head-and-tail preview plus the path, not truncated (`output_spill.rs:53-90`). Each hook runs only once trusted (`discovery.rs:713-720`). A plugin's `hooks/hooks.json` loads with no feature gate, but not for the Agent Plugins manifest format (`core-plugins/src/loader.rs:70,953-961`). |
| Orchestration guidance | Tool description plus bundled skill | None; needs a skill | None; needs a skill |
| Deterministic engine | Workflow runtime | None stock. Code Mode cannot call `subagent` (`codemode: false`, `core/src/tool/plugin/subagent.ts:104`), so this needs a plugin tool. | None. Only an external `exec` or SDK driver; MCP tool calls time out after a per-server default of 300 s (`tool_timeout_sec`; `codex-mcp/src/rmcp_client.rs:107`, `connection_manager.rs:365-366`). |
| Subagent fan-out | Up to 16 concurrent agents per workflow by default, fewer on fewer CPUs | `subagent` with `background: true` (`subagent.ts:44-46`); no concurrency limiter; depth `experimental.subagent_depth`, default 1 (`:129-133`) | `spawn_agent`: 6 threads per session by default, set by `[agents] max_concurrent_threads_per_session` (alias `max_threads`), and 4 under `multi_agent_v2`; depth `[agents] max_depth`, default 1 (`core/src/config/mod.rs:257-258,267,1633-1646,3898-3912`; `spawn.rs:74-80`) |
| Resume | Run journal (`resumeFromRunId`) | Per child through its `sessionID` | Per child through `resume_agent` |
| Structured results | `schema` with five validation attempts | Plain string | None for `spawn_agent` (unverified) |
| Progress view | `/workflows` view and the task panel | Child sessions; a plugin tool's `progress()` (`schema/src/tool.ts:19`) | Native agent threads (inference) |

`multi_agent_v2` is stable but off by default in 0.162.1 (`features/src/lib.rs:1421-1424`), so the
V1 tool names `spawn_agent`, `wait_agent`, and `close_agent` are the right default to name; its own
names, such as `followup_task` and `send_message`, were only partly checked.

## Port designs

**(A) One `manual` orchestration skill with per-harness notes.** A neutral body covers the opt-in,
scaling the fan-out to the request, adversarial verification, a completeness critic, looping until a
round finds nothing new, and no silent caps. `harness_files` gives each harness its own tool notes,
the mechanism `using-superpowers` already uses:

- **Claude Code:** call the Workflow tool. A skill whose instructions say to call Workflow is a
  sanctioned opt-in under the tool's own rule, so the notes route into the native runtime rather
  than re-implementing it.
- **OpenCode:** `subagent` with `background: true`, wait for the completion messages, depth 1 by
  default.
- **Codex:** `spawn_agent`, `wait_agent`, and `close_agent` in waves sized to the configured thread
  limit (default 6), closing finished agents.

Per-harness notes are needed because localization's closed harness phrasing renders only the
skill-tool sentence and the subagent-dispatch words, and the Codex dispatch wording names no tool
([References and linking](../architecture/references-and-linking.md#harness-phrasing)).
`manual` keeps the skill out of the model's listing in all three harnesses
(`disable-model-invocation`, `opencode/autoinvoke: false`, `allow_implicit_invocation: false`), so
its catalog cost should be zero; that is asserted from the mechanics, not measured after adding a
skill. Outside Claude Code it loses script-held state, deterministic control flow, run resume,
schemas, a concurrency gate, the progress view, and a standing session mode. Estimated effort: one
to two days plus a runtime panel per harness (inference). Canon fit: full.

**(B) A plus keyword injection.** OpenCode needs about 30 lines of plugin, but the installer owns
only `skills/`, `commands/`, and `agents/` and adds no plugin entrypoint, so this is an ADR-level
change to installer ownership. Codex needs an emitter change for `hooks/hooks.json`, a hook script,
every user's trust in the hook, and continued use of the Legacy `.codex-plugin/plugin.json`
manifest format, since the Agent Plugins format skips plugin hooks. The value is small: a `manual`
skill is already a one-token trigger in each harness (`/deniz-process:<name>`,
`@deniz-process.<name>`, `$deniz-process:<name>`), and the only real gain, a standing session mode,
needs per-session state. Estimated effort: about a week. Canon fit: poor.

**(C) A deterministic engine.** OpenCode: a plugin `agent` tool called from Code Mode plus a journal
in plugin storage, about one to two weeks; it bypasses the stock depth and permission checks, which
would have to be re-implemented. Codex: an `exec` or SDK backend behind MCP, a large and risky
effort whose children run as separate top-level threads. A portable MCP server adds both costs.
Canon fit: none; it needs installer changes and one runtime per harness, and it is a product rather
than a skill.

## Decisions

The curator decided on 2026-10-12:

1. **Build design A** as one original `manual` skill with a shared body and per-harness tool notes
   through `harness_files`. Claude Code routes into the native Workflow tool, OpenCode runs
   background `subagent` waves, and Codex runs `spawn_agent` waves. It is not named "ultracode" and
   is written in this repository's own words. Slash invocation would not fire Claude's keyword, as
   the detector ignores prompts that start with `/`, but the name would still blur the skill with
   Claude's built-in keyword and setting, and a prose mention of it would fire that keyword.
2. **Defer design B.** Revisit only if a standing session mode is wanted in OpenCode or Codex; that
   is the only gain that would justify an ADR widening installer ownership.
3. **Reject design C** for this estate.
4. **Plugin-shipped workflows** in the Claude Plugin's `workflows/` directory are wanted as an
   idea, for example the curation sanity panel or an independent review as
   `/deniz-process:<name>`. The surface is Claude-only, so how it meets the rule that every harness
   is decided at once ([ADR-0007](../adr/0007-control-beats-fidelity.md)) is an open question. One
   candidate answer is that Claude Code gets the scripted form while OpenCode and Codex get the
   orchestration skill's model-driven form of the same playbook;
   [ADR-0002](../adr/0002-multi-harness-output.md) already allows a target to expose a capability
   the neutral manifest leaves unspecified as a native realization.

The queue position, the prerequisite, and the open questions are in the [roadmap](../ROADMAP.md).

### Adopted approaches (curator-approved)

On 2026-10-12 the curator approved the approaches below for the orchestration skill, the rejections
after them, and the skill outline at the end. An extraction pass read the prior-art clones at the
commits in [the port table](#is-there-already-a-port), read-only, and ran none of their code. It
ranked 16 adoptable approaches and 15 rejects; the approval adopts 12 of those approaches and rejects
11 patterns. The cited files were checked to exist at the pinned commits, and the Codex and OpenCode
tool facts used below were re-read at their pins; the extraction's other line-level readings were
not independently re-verified.

Everything here is restated in this repository's own words, and the skill will copy no prior-art
text. That rule is binding for two sources: OmO's own code is under the Sustainable Use License, and
just-every declares MIT in a manifest but has no license file, so both supply ideas only. With
nothing copied, no `THIRD_PARTY_NOTICES.md` entry follows. Each source line names the repository at
its commit, the file, and the license; Codex paths are under `codex-rs/` and OpenCode paths under
`packages/`, at the pins in [the evidence boundary](#question-and-evidence-boundary).

#### Adopted

1. **A self-contained brief per subagent.** A child starts without the parent's context, so the
   parent gives each child one brief with the same sections in the same order: the goal and what the
   parent will do with the result; the exact context, meaning paths, earlier findings, and any frozen
   shared contract pasted unchanged into every brief; the scope, either read-only or a named write
   set; what the child must and must not do, always including no commit or push and no edit outside
   the scope; a binary done-when check; and the report to return. A child that cannot go on returns
   a blocked report with its exact question instead of asking the user or guessing.
   - Source: `code-yeongyu/oh-my-openagent@a103d40`
     `packages/omo-opencode/src/agents/sisyphus-dynamic-prompt-execution.ts`, Sustainable Use
     License, idea only; `YuanpingSong/ultracodex@ac349f8` `skills/agent-script-authoring/SKILL.md`,
     Apache-2.0, restated.
2. **A fixed markdown report, with long output in a results file.** Every child ends with the same
   short report: a status of DONE, PARTIAL, BLOCKED, or FAILED; a summary of a few lines; findings,
   each with a severity, file-and-line or command evidence, and a confidence; the files it changed;
   the checks it ran, each PASS, FAIL, or NOT RUN with a reason; open questions; and the path of its
   results file. Anything longer than about 40 lines goes into that file. This replaces schemas
   outside Claude Code: neither OpenCode nor Codex validates a child's output, and OpenCode delivers
   the child's whole final text into the parent session (`core/src/session/subagent-completion.ts`),
   so a short report is what keeps the parent's context small. In Claude Code the same fields become
   the Workflow `schema`, so the standard stays one standard.
   - Source: `norandom/OpenUltraCode@2638f03` `.opencode/agents/open-ultracode-verifier.md`, MIT,
     restated; `anomalyco/opencode@9b4ec57` `packages/core/src/session/subagent-completion.ts`, MIT,
     tool fact.
3. **Fresh skeptics that cite evidence.** A finding that matters goes to a new child, never a
   continuation of the child that produced it, and that child is asked to refute it. A refutation
   must cite evidence; doubt alone does not refute. A finding is confirmed only when no skeptic
   refutes it, and when a skeptic fails, the finding is reported as UNVERIFIED rather than confirmed
   or dropped. One skeptic is the default; a high-severity finding, or one an edit will act on, gets
   two with different lenses: reproduce it, and check it against the documented contract. Verifiers
   run the checks themselves instead of trusting a worker's report, and refuted findings stay in the
   final report with their reasons.
   - Source: `YuanpingSong/ultracodex@ac349f8` `examples/review-verify-fix/workflow.js`, Apache-2.0,
     restated; `just-every/plugin-ultracode@9dde008`
     `skills/ultracode/references/quality-patterns.md`, no license file, idea only.
4. **No silent drops.** A child that failed, timed out, or returned nothing is a visible gap. The
   parent reports coverage as N of M items done and names every item that is not done with its
   status. It never writes its own result for an item whose child failed; it re-dispatches the item
   or reports it. It keys results by item id, not position, and states every cap it applied, such as
   a top-N cut, sampling, or a skipped area.
   - Source: `YuanpingSong/ultracodex@ac349f8` `skills/agent-script-authoring/SKILL.md`,
     Apache-2.0, restated; `norandom/OpenUltraCode@2638f03` `.opencode/skills/open-ultracode/SKILL.md`,
     MIT, restated.
5. **One ledger file and a manual restart in place of resume.** One small file holds the goal, the
   done-when checks, the planned waves and agent total, one row per item (id, kind, write set,
   status, child handle, results path), and an append-only log. It lives in a scratch location
   outside the working tree, the user is told its path, and it is never committed. After a crash or
   a lost context, the parent re-reads it, keeps the DONE items, tries the harness's own continuation
   for an item still running, inspects `git status` and `git diff` on a write item's write set before
   re-dispatching it because its work may already be on disk, re-dispatches the rest, and tells the
   user what it reused and what it re-ran. It never restarts from scratch silently, and it treats a
   restart as at-least-once, not exactly-once. The native handles are Claude Code's
   `resumeFromRunId`, which covers one run in one session, OpenCode's child `sessionID`, and Codex's
   `resume_agent`.
   - Source: `code-yeongyu/oh-my-openagent@a103d40` `packages/prompts-core/prompts/ultrawork/default.md`,
     Sustainable Use License, idea only; `openai/codex@092d3ac`
     `codex-rs/core/src/tools/handlers/multi_agents_spec.rs`, Apache-2.0, tool fact.
6. **At most 6 live children, everywhere.** Before dispatching, the parent tells the user the number
   of waves and the agent total, and it asks before going over that total. It sizes each wave to the
   independent items it has, not to the ceiling, and never runs more than 6 children at once in any
   harness. Six is Codex's default thread limit, so one plan behaves the same in all three. In Codex
   a finished child keeps its slot until it is closed, so the parent closes each child once its
   report is harvested. In OpenCode a background child returns at once and announces its own
   completion, so the parent never sleeps, polls, or asks a child for status; it does
   non-overlapping work or ends the turn.
   - Source: `openai/codex@092d3ac` `codex-rs/core/src/config/mod.rs` and
     `codex-rs/core/src/tools/handlers/multi_agents_spec.rs`, Apache-2.0, tool facts;
     `anomalyco/opencode@9b4ec57` `packages/core/src/tool/plugin/subagent.ts`, MIT, tool fact.
7. **The critical path stays local.** The parent scouts only until it can list the items, then
   delegates the per-item work. While children run, it does only work that does not overlap theirs,
   and it never repeats a search it delegated.
   - Source: `openai/codex@092d3ac` `codex-rs/core/src/tools/handlers/multi_agents_spec.rs`,
     Apache-2.0, restated tool guidance; `PabloNAX/ultracode-skill@bfa2d92` `ultracode/SKILL.md`,
     MIT, restated.
8. **Parallel writers only on disjoint write sets, and a gate child after each write wave.**
   Writers run at the same time only when their write sets do not overlap; otherwise they run in
   sequence, except that Claude Code can give each writer its own worktree. Each writer's brief says
   that other agents are editing other files, that it edits only its own write set, and that it
   neither reverts nor reformats changes it did not make. During a wave a writer checks only its own
   files; after the wave one gate child runs the project's checks and reports PASS or FAIL for each.
   A frozen shared contract decides which side of a seam is wrong, and no test is weakened or deleted
   to make a check pass.
   - Source: `YuanpingSong/ultracodex@ac349f8` `examples/staged-build-gates/README.md`, Apache-2.0,
     restated; `openai/codex@092d3ac` `codex-rs/core/src/agent/role.rs` (worker role), Apache-2.0,
     restated.
9. **Bounded loops.** A discovery loop stops after 2 consecutive rounds that find nothing new against
   everything already seen, not only what survived, or after 3 rounds. A fix-and-review cycle allows
   at most 2 re-reviews, each by a fresh reviewer given only the change and the cited blockers; a
   concern blocks only when it names a done-when check that fails, and what remains after the second
   re-review goes to the user. An item that fails twice is BLOCKED and reported. Zero findings skip
   the downstream waves. The parent invents no budget or deadline the user did not give; the
   announced agent total is the budget.
   - Source: `just-every/plugin-ultracode@9dde008` `skills/ultracode/references/quality-patterns.md`,
     no license file, idea only; `code-yeongyu/oh-my-openagent@a103d40`
     `packages/prompts-core/prompts/ultrawork/default.md`, Sustainable Use License, idea only.
10. **A completeness critic for audits.** An audit or a find-every-instance task ends with one fresh
    critic that receives the scope and the ledger and names missed areas, unverified claims, and
    unread files. Its gaps become one more targeted wave, at most once, under the loop bounds of
    approach 9. A single fix does not use it. Claude Code's bundled skill carries the same pattern
    (piece 5 above).
    - Source: `just-every/plugin-ultracode@9dde008`
      `skills/ultracode/references/quality-patterns.md`, no license file, idea only.
11. **A small catalog of shapes.** Before writing briefs, the parent picks the dominant shape from
    five or six: a read-only sweep, fan-out then synthesis, review then verify then fix, build waves
    with gates, a draft-and-critic loop, and a judge panel. A fan-out whose raw results are the
    deliverable ends at the fan-out, with no synthesizer. When candidates must be ranked or merged,
    one agent sees all of them. Each item is verified as soon as its producer reports, rather than
    after a barrier for the whole wave.
    - Source: `YuanpingSong/ultracodex@ac349f8` `skills/agent-script-authoring/SKILL.md`,
      Apache-2.0, restated and cut down from ten shapes.
12. **The Codex spawn-authorization sentence.** Codex's spawn tool forbids sub-agents unless the
    user or an applicable skill or `AGENTS.md` explicitly asks for them, and it says a request for
    depth or thoroughness does not count. The skill therefore states that the user's invocation of
    it is an explicit request for parallel sub-agents on this task. Codex's built-in explorer role
    also tells the parent to trust explorer results without further checks; the Codex notes override
    that, so an explorer report is input and any claim in the final report still goes through
    verification.
    - Source: `openai/codex@092d3ac` `codex-rs/core/src/tools/handlers/multi_agents_spec.rs` and
      `codex-rs/core/src/agent/role.rs`, Apache-2.0, restated tool facts.

The extraction's other four approaches are outside this approval and stay undecided: a written
done-when and stop condition stated before any dispatch, a visible and labeled sequential fallback
when children cannot start, an approval question before a destructive step, and a fixed set of
forward-test prompts kept under `experiments/`.

#### Rejected

- **Keyword hooks and long trigger lists**, such as OmO's prompt-submit keyword hook and the
  trigger-list descriptions of PabloNAX and just-every. They pull the skill toward triggering itself
  against a curator-held manual trigger, and a hook needs plugin files the installer does not own
  (design B, deferred above). The description is written for a person reading a menu.
- **Scale-up by default**, as in just-every's and OmO's always-delegate, never-ration framing and
  Claude's own standing ultracode text. It costs without bound and works against lean execution and
  against Codex's own critical-path guidance; scale is announced and bounded per run instead
  (approach 6).
- **Engines, dashboards, and tmux teams**: ultracodex's executors, just-every's Node engine and
  dashboard, and oh-my-codex's tmux team. This is design C, a product rather than a skill, with an
  install and a runtime per harness.
- **A `.workflow/` artifact directory in the repository**, PabloNAX's in-repo run root of plan,
  state, packet, contract, handoff, and audit files. It is ceremony and dirties the working tree; one
  ledger and its results files in scratch cover the need (approach 5).
- **Codex goals** as a continuity engine, as just-every and OmO use them. Goals are stable and on by
  default in Codex 0.162.1 (`codex-rs/features/src/lib.rs`) and continue on their own, so they would
  add a hidden standing mode in one harness only and break the same-standard rule. The skill creates
  none unless the user asks for unattended continuation.
- **Per-worker model choice**: just-every's model tiers, OpenUltraCode's agents pinned to a model,
  and OmO's categories. The OpenCode and Codex spawn tools both say to leave the model unset unless
  the user asks, and model routing is not this skill's job; a model is set only on the user's
  request.
- **JSON schemas with repair loops outside Claude Code**, as in ultracodex's executor. Neither
  OpenCode nor Codex validates child output, so prompt-level JSON with a manual repair loop is
  fragile and costly; the markdown report of approach 2 is used, and `schema` only in Claude Code.
- **Installer-shipped role agents**, OpenUltraCode's role agent files. They add installer-owned
  `agents/` files for no gain, since the role briefs fit inline in the skill; revisit only if a
  read-only agent profile becomes necessary.
- **Mandated commits and auto-revert**, OmO's commit-often rule and its revert to the last working
  state after repeated failures. Commits conflict with committing only when asked, and a revert is
  destructive.
- **Agent fan-outs running next to a Workflow run** in Claude Code. In Claude Code the Workflow
  runtime is the one orchestration authority; oh-my-codex removed its prompt-only parallel mode for
  adding a second authority without a runtime of its own (`skills/ultrawork/SKILL.md`).
- **The names "ultracode" and "ultrawork".** They collide with Claude's keyword and setting and with
  OmO's branding (decision 1).

#### Skill outline

**`SKILL.md`, the shared body.** It names no harness tool: tool names live only in the per-harness
notes, which `harness_files` ships at one output path in each tree
([`curation/SCHEMA.md`](../../curation/SCHEMA.md#harness_files)). The frontmatter has a name the
curator picks, not "ultracode", `manual` invocation, and a one-line menu description. The sections:

1. What invoking it means: the explicit request for parallel sub-agents on this task (approach 12).
   Read the harness notes first; do a small or tightly coupled task directly and say so (approach 7).
2. The plan, in chat, before any dispatch: goal, done-when checks, shape (approach 11), items with
   their kind and write sets, waves and agent total under the ceiling (approach 6), and the
   verification plan.
3. The ledger (approach 5).
4. The brief and report templates and the results-file rule (approaches 1 and 2).
5. Running waves: scout then delegate, non-overlapping parent work, disjoint writers with the
   coordination line and a gate, and per-item verification (approaches 7, 8, and 11).
6. Collecting: record each report, deduplicate against the seen set, key by item id, report N of M,
   and substitute no guess (approaches 4 and 9).
7. Verification: fresh skeptics and, for audits, the completeness critic (approaches 3 and 10).
8. Loops and stop conditions (approach 9).
9. Failures: retry once with a corrected brief, continuing the same child only when the failure was
   a misunderstanding, and otherwise mark the item BLOCKED (approach 9).
10. Restart from the ledger, inspecting a write item's write set before re-dispatching it
    (approach 5).
11. The final report: outcome, counts of DONE, PARTIAL, BLOCKED, FAILED, and UNVERIFIED with names,
    files changed, checks run with PASS, FAIL, or NOT RUN and reasons, refuted findings with
    reasons, caps applied, remaining risk, and the ledger path, with every child closed or finished
    first.

**`references/claude-code-tools.md`, Claude Code's notes.**

- Invoking the skill is the Workflow tool's own sanctioned opt-in, a skill whose instructions say to
  call it; load the bundled `workflow-authoring` skill before writing the script, and use one run
  per phase when the parent must stay involved between phases.
- Mapping: a wave is `parallel()`, per-item verification is `pipeline()`, a brief is an `agent()`
  prompt, the report is a `schema` with the same fields, a read-only child uses a read-only
  `agentType`, and overlapping writers use `isolation: 'worktree'`.
- The runtime queues items under its own concurrency limit (piece 4 above); the announced agent
  total still holds. Completion arrives as a task notification, so do not poll.
- Restart with `resumeFromRunId` within the same session; otherwise read the run's journal or agent
  transcripts and write a continuation that skips DONE items. The run journal covers one run, so the
  ledger matters across several runs.
- No parallel Agent-tool fan-out beside a running Workflow.

**`references/opencode-tools.md`, OpenCode's notes.**

- `subagent` with `background: true`, `agent` set to `explore` for read-only work or `general` for
  writes, a short `description`, and the brief as `prompt`; never `model` unless the user asks.
- OpenCode has no concurrency limiter, so the skill holds itself to 6 live children. Depth is 1 by
  default (`experimental.subagent_depth`), so children cannot start children.
- After launching, do non-overlapping work or tell the user what was launched and end the turn;
  never sleep, poll, or ask a child for status. Completion arrives as a `<subagent>` message holding
  the child's whole final text.
- Record each child's `sessionID` in the ledger. A fix continues the same child through its
  `sessionID`; a verifier is always a new call without one.

**`references/codex-tools.md`, Codex's notes.**

- The authorization sentence first: the user invoked this skill to request sub-agents for this task.
- `spawn_agent` with the brief as `message` and `agent_type` set to `explorer` for read-only work
  or `worker` for bounded writes; `fork_context` unset so the brief stands alone; `model` and
  `reasoning_effort` unset unless the user asks.
- A rolling wave: `wait_agent` on all live ids with a timeout of minutes rather than the short
  default, harvest the first finisher's final message, record it in the ledger, `close_agent` it,
  then spawn the next pending item.
- Defaults of 6 threads and depth 1, both configurable under `[agents]`; a finished agent holds its
  slot until it is closed.
- `send_input` sends a correction to the same child; `resume_agent` reopens a closed one.
- Explorer reports are input, not verified facts, and no goal is created.
- The V1 tool names, the 0.162.1 default, with one line noting that `multi_agent_v2` uses other
  names and a default of 4 threads.

### Open authoring points

- Pin the Codex notes to the V1 tool names, which are the 0.162.1 default.
- Say what the Claude Code notes do when the Workflow tool is unavailable: workflows turned off, or
  a Pro plan that has not turned them on.
- One menu label per harness, and wording that does not trigger Claude's keyword.
- Measure the listing after the skill ships rather than assuming zero catalog cost, and run each
  harness before claiming the skill works there.
