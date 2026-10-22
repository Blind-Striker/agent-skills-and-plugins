---
name: ask-deniz
description: "Use when unsure which skill or ceremony fits, or when asked which
  to use, across installed deniz Modules: process workflows, .NET, Aspire, and
  Akka. Manual ceremonies stay user-started."
---

# ask-deniz

You don't remember every skill, so ask.

These summaries orient you; they are not the skills. Never recommend skipping a step on the strength
of a summary here: say what that step produces and let the user decide.

A **flow** is a path through the skills. Most work travels one **main flow**, two **on-ramps** merge
onto it, and everything else is standalone or a vocabulary layer running underneath.

Two forms below mark who starts the workflow. A **bold skill pointer** is an explicit action for the
user to start. A plain skill fact reaches the model by matching the situation, or another skill
pulls it in. The package renders both in each harness's native syntax. A recommendation is not
permission to launch a ceremony: never load a manual skill from its pointer yourself.

For an authorized task, choose the route that fits, without ceremony the work does not need; for a
"which skill?" question, recommend the route rather than starting it. Several steps do not by
themselves justify a plan, ledger, worktree, or agent fan-out. Honor an approved scope, executor, and working-directory choice without
asking for the same approval again. A plan-only request authorizes a plan, not implementation.

## Optional Modules

The .NET, Aspire, and Akka.NET routes live in [references/dotnet.md](references/dotnet.md): read it
only for .NET work. Each of its sections applies only when its named Module is installed. Before
loading a model route, check that its exact identity is available in the harness's skill catalog.
If it is not available, explain which Module supplies it; do not assume installation or install
anything. Missing catalog information is not proof that a Module is absent. Manual skills may be
hidden even when installed: offer their human pointer conditionally, without declaring them missing
or loading them yourself. The two specialist agents named there are descriptive delegation options,
not skill-load or slash commands.

## The main flow: idea → ship

### 1. Sharpen the idea

- **`$deniz-process:grill-with-docs`** when you are in a working directory. It interviews you and records
  qualifying terms in `GLOSSARY.md` and qualifying decisions in ADRs, creating either lazily.
  Stateful, and the better of the two whenever a repo is there to write into.
- **`$deniz-process:grill-me`** when there is no working directory — same interview, no paper trail.

Both drive the same `$deniz-process:grilling` primitive underneath. It works the design tree in **rounds**: the
whole frontier at once, numbered, each with a recommended answer, and it dispatches subagents to
find environment facts rather than asking you for them.

### 2. Decide, and get the gate

**`$deniz-process:brainstorming`** is where an idea becomes an approved design. It draws out what you
want first, then sizes the work out loud — a quick, clear task it simply does, a small change it
agrees with you in chat, and a project gets a written design document. Past a quick task, **it does
not build until you approve.** That gate is the reason to route through it rather than around it.

Grilling and brainstorming are not rivals. Grilling sharpens what you think; brainstorming decides
what to do about it and makes you say yes. On small work you will often only want one of them.

### 3. Turn the decision into work — two ladders

**The light ladder**, when the work is a feature you can hold in your head:

**`$deniz-process:to-spec`** turns the thread into a spec → **`$deniz-process:to-tickets`** splits it into tracer-bullet tickets
with blocking edges → then either **`$deniz-process:implement`** builds one ticket at a time, clearing context
between them, or **`$deniz-process:implement-spec`** builds the whole spec in one run: it reads the tickets as
a task graph, runs implementer subagents across the ready frontier in parallel, and lands
everything on one integration branch. Its input is the ticket graph, which is also what separates
it from subagent-driven development below, which works a plan. Both drive the test-first loop and
the review themselves; you do not need to invoke those.

**The heavy ladder**, when the work is multi-session or several people's worth:

**`$deniz-process:writing-plans`** turns the approved spec into a plan of checkbox tasks → then either
**`$deniz-process:executing-plans`** to work it in this session, or **`$deniz-process:subagent-driven-development`** to dispatch
each task to its own window. Both executors now **rule rather than stall**: they decide conflicts,
ambiguities and plan defects themselves, record each ruling with its cost-if-wrong, and report every
one at the end. They still stop for anything destructive, security-sensitive, or externally visible.

Neither ladder is mandatory for ordinary one-sitting work: an agreed change can be implemented
directly with focused checks, the repository's completion gate, and one final review when required.
Use the light ticket ladder when tickets/specs are useful; use the heavy ladder when a written plan
needs to survive a context window. A new plan does not reopen approval for execution already requested.
Pick the weight by judgment — inline work, one delegated subagent, or a full executor — and say which
in one line. Only when you are genuinely torn, ask once, with your recommendation. A plan that already
has a ledger keeps the executor the ledger names.

### 4. Build

Inside either ladder, the test-first loop is `$deniz-process:test-driven-development` — model-reachable,
so it arrives when you are writing a behaviour rather than because you typed it. Reach for
`$deniz-process:using-git-worktrees` when a piece of work wants isolation from what is already in your
tree.

### 5. Close out

- **`$deniz-process:requesting-code-review`** reviews the diff on two axes, Standards and Spec, against a fixed
  point. Ask for it on a branch or a PR whenever you want the two-axis read.
- `$deniz-process:receiving-code-review` is the other half: how to take findings without capitulating or getting
  defensive.
- `$deniz-process:verification-before-completion` is the discipline that stops "it works" from being a claim you
  did not run. It fires on its own when you are about to say done.
- **`$deniz-process:finishing-a-development-branch`** decides how the work integrates, and refuses to remove a
  worktree with uncommitted files behind your back.
- **`$deniz-process:pr`** shapes the body when the work goes up as a pull request: the smallest
  visual that shows the change, before/after evidence that it works, and a one-way or two-way door
  call. A pull request template the repo already has wins; it fills only that template's sections.

### 6. Look back

**`$deniz-process:retro`** closes the loop. After a build, and especially one that went sideways, it
looks back over the session and proposes changes to the agent's **environment**, not the code:
navigation pointers, automated checks, coding standards, steering files, tooling. It edits nothing
itself. Run it in the session it looks back on, before you clear; after clearing, point it at that
session's log.

When a session went wrong, retro pairs with **`$deniz-process:diagnosing-superpowers`**, which
gathers the evidence, cited line by line, and gives no advice; retro proposes the environment
changes.

## On-ramps

A starting situation that generates work, then merges onto the main flow.

- **Bugs and requests piling up** → **`$deniz-process:triage`**. It moves incoming issues through triage roles and
  produces agent-ready ones. Only for issues **you didn't create**: what `to-tickets` produced is
  already agent-ready, so don't triage it.

- **Something's broken** → `$deniz-process:systematic-debugging`. Model-reachable, because the trigger is the
  situation rather than a wish. It refuses to theorise until it has a tight feedback loop — one
  command that already goes red on *this* bug — then fixes with a regression test. It redacts
  secrets from everything it shows you along the way. Once the fix is in, run
  **`$deniz-process:retro`** in the same session to ask what would have prevented the bug.

- **A huge, foggy effort** — greenfield, or a build too big to see the end of → **`$deniz-process:wayfinder`**.
  The most demanding flow here. It charts a shared map of decision tickets and resolves them one at
  a time, producing **decisions, not deliverables**, until the way is clear. Then it hands off: join
  the main flow at `to-spec` or `$deniz-process:brainstorming`, depending on which ladder the cleared
  work wants. Never reach for it on a well-scoped feature.

## Codebase health

Not feature work, just upkeep. **`$deniz-process:improve-codebase-architecture`** runs whenever you have a spare
moment to keep the codebase good for agents to operate in. It surfaces deepening opportunities;
picking one *generates an idea* you take back to step 1.

## Vocabulary underneath

Two model-reachable references that run *beneath* the other skills, each the single source of truth
for its words. You get them by naming the confusion, not by typing a command.

- `$deniz-process:domain-modeling` sharpens the project's **domain** language: challenge a fuzzy term, resolve one
  word doing three jobs, record a hard-to-reverse decision as an ADR. It is what `grill-with-docs`
  drives to keep `GLOSSARY.md` a clean glossary.
- `$deniz-process:codebase-design` is the deep-module vocabulary — module, interface, depth, seam, adapter,
  leverage, locality — for designing a module's **shape**: a lot of behaviour behind a small
  interface at a clean seam.

## Standalone

Reach for these directly; they are not on any flow.

| Skill | For |
|---|---|
| **`$deniz-process:wait-what`** | That last message did not land. Ask for it again, in plainer words, in this project's vocabulary. |
| **`$deniz-process:handoff`** | Write a portable file so the work can travel — new harness, new directory, a colleague. |
| **`$deniz-process:prototype`** | Answer a design question with throwaway code: a self-contained HTML demo for logic, a real screen for UI. |
| **`$deniz-process:research`** | Send a background agent at primary sources and get a cited Markdown file back. |
| **`$deniz-process:to-questionnaire`** | The decision needs someone else's knowledge. Turn it into questions aimed at the gap. |
| **`$deniz-process:teach`** | A stateful learning workspace for something you are trying to actually understand. |
| **`$deniz-process:wizard`** | Generate a bash wizard for steps only a human can do — dashboards, credentials, CI secrets. It writes real `.env` values and can set real repository secrets, so read what it generated before running it. |
| **`$deniz-process:writing-for-agents`** | The design vocabulary for any document an agent reads: a skill, an `AGENTS.md`, a doc behind a pointer. |
| **`$deniz-process:writing-skills`** | The testing methodology for skills: pressure scenarios, baselines, closing loopholes. Pairs with the one above rather than competing with it. |
| **`$deniz-process:using-superpowers`** | Switch this session to skill-driven working. Flipped by hand, never automatically. |
| **`$deniz-process:diagnosing-superpowers`** | A session went wrong — repeated work, an ignored plan, a skill that did not fire, too slow or too costly. It finds out why, with cited evidence, and drafts an issue for the superpowers maintainers only when you approve the text. |
| **`$deniz-process:setup-matt-pocock-skills`** | One-time setup for the issue-tracker conventions the ticket flow assumes. |
| `$deniz-process:dispatching-parallel-agents` | How to brief a subagent so it comes back with something usable. Model-reachable; it arrives when you are about to fan work out. |
| `$deniz-process:asd-ste100` | Rewrite ambiguous machine- or agent-facing text in controlled English; not creative copy or a new execution ceremony. |
| `$deniz-process:ponytail` | You want the smallest change that fully solves the task, or the work is getting over-engineered. Model-reachable when you ask for the simplest solution or complain about bloat; it never switches on for a whole session. |
| **`$deniz-process:ponytail-review`** | A plain-English quality review of one change, ranked must/should/nice, that also asks whether every line is needed. A second opinion; plan runs keep their own review contract. |
| **`$deniz-process:ponytail-audit`** | The same quality review for a whole repo or folder, ranked. Expensive; it changes nothing. |
| **`$deniz-process:ponytail-debt`** | List every `shortcut:` comment as a debt ledger, so a deliberate shortcut does not quietly become permanent. |

## Coordination

Applies only when the `deniz-coordination` Module is installed.

| Skill | For |
|---|---|
| **`$deniz-coordination:coordinator`** | You want the agent to own a task or project end to end: plan it, hand the work to other agents, review what comes back, and finish with as few questions as possible. A mode you start per task. |
| `$deniz-coordination:delegating-to-worker-models` | Handing one piece of work to another agent: what stays with you, the prompt, the worker contract, evidence from your own run, review. Model-reachable; it arrives when another agent's prompt is being written. |

This harness's coordination tools are in [references/coordination.md](references/coordination.md).

## Phase boundaries

A **phase** is a chunk of work inside a session: the grilling, the implementation, the QA. At the
**boundary** between two of them you have five options, and picking between them is the fuzziest
decision in this whole map:

- **Continue**: stay put. Costs nothing, loses nothing.
- **Clear the window** when nothing here matters to what's next — `/clear` in Claude Code, `/new` in
  OpenCode, or start a new Codex chat or CLI session.
- **`$deniz-process:handoff`** writes a portable markdown file. Narrow: a new harness, a new directory, a
  colleague, or forking a side task mid-phase. What it buys is portability.
- **Subagent**: send a tightly-scoped task to its own window and get a report back.
- **`/compact`** compresses this context and seeds a fresh session with it. The **default**, at the
  bottom of the tree rather than the first reach.

The whole tree, in order, with what each choice costs: [PHASE-BOUNDARIES.md](PHASE-BOUNDARIES.md).
