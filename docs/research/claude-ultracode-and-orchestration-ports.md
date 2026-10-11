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

### Prior-art approaches to adopt

Pending: a separate extraction pass is reading the prior-art repositories above for approaches the
orchestration skill can adopt where they fit, restated in this repository's own words. Its findings
belong in this section.

### Open authoring points

- Pin the Codex notes to the V1 tool names, which are the 0.162.1 default.
- Say what the Claude Code notes do when the Workflow tool is unavailable: workflows turned off, or
  a Pro plan that has not turned them on.
- One menu label per harness, and wording that does not trigger Claude's keyword.
- Measure the listing after the skill ships rather than assuming zero catalog cost, and run each
  harness before claiming the skill works there.
