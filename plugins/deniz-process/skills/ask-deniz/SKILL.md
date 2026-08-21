---
name: ask-deniz
description: Ask which skill or flow fits your situation. A router over the
  skills in this module.
disable-model-invocation: true
---

# ask-deniz

You don't remember every skill, so ask.

A **flow** is a path through the skills. Most work travels one **main flow**, two **on-ramps** merge
onto it, and everything else is standalone or a vocabulary layer running underneath.

Two spellings below, and the difference decides what you can type. **`/thing`** you invoke yourself.
**`thing`** in plain backticks you cannot: it reaches you by matching the situation, or another
skill pulls it in.

## The main flow: idea → ship

### 1. Sharpen the idea

- **`/grill-with-docs`** when you are in a working directory. It interviews you and leaves a paper
  trail: `CONTEXT.md` and ADRs. Stateful, and the better of the two whenever a repo is there to
  write into.
- **`/grill-me`** when there is no working directory — same interview, no paper trail.

Both drive the same `grilling` primitive underneath. It works the design tree in **rounds**: the
whole frontier at once, numbered, each with a recommended answer, and it dispatches subagents to
find environment facts rather than asking you for them.

### 2. Decide, and get the gate

**`/brainstorming`** is where an idea becomes an approved design. It classifies the request out loud
first and scales the ceremony to it — a feasibility spike, a bounded change designed in chat, or an
architectural piece that earns a written spec. All three end the same way: **it does not implement
until you approve.** That gate is the reason to route through it rather than around it.

Grilling and brainstorming are not rivals. Grilling sharpens what you think; brainstorming decides
what to do about it and makes you say yes. On small work you will often only want one of them.

### 3. Turn the decision into work — two ladders

**The light ladder**, when the work is a feature you can hold in your head:

**`/to-spec`** turns the thread into a spec → **`/to-tickets`** splits it into tracer-bullet tickets
with blocking edges → **`/implement`** builds one ticket at a time, clearing context between them.
`/implement` drives the test-first loop and the review itself; you do not need to invoke those.

**The heavy ladder**, when the work is multi-session or several people's worth:

**`/writing-plans`** turns the approved spec into a plan of checkbox tasks → then either
**`/executing-plans`** to work it in this session, or **`/subagent-driven-development`** to dispatch
each task to its own window. SDD now **rules rather than stalls**: it decides conflicts, ambiguities
and plan defects itself, records each ruling with its cost-if-wrong, and reports every one at the
end. It still stops for anything destructive, security-sensitive, or externally visible.

Pick by whether the plan needs to survive a context window. One feature, one sitting: light ladder.
Something you will still be building next week: heavy ladder.

### 4. Build

Inside either ladder, the test-first loop is `test-driven-development` — model-reachable, so it
arrives when you are writing a behaviour rather than because you typed it. Reach for
`using-git-worktrees` when a piece of work wants isolation from what is already in your tree.

### 5. Close out

- **`/requesting-code-review`** reviews the diff on two axes, Standards and Spec, against a fixed
  point. Ask for it on a branch or a PR whenever you want the two-axis read.
- `receiving-code-review` is the other half: how to take findings without capitulating or getting
  defensive.
- `verification-before-completion` is the discipline that stops "it works" from being a claim you
  did not run. It fires on its own when you are about to say done.
- **`/finishing-a-development-branch`** decides how the work integrates, and refuses to remove a
  worktree with uncommitted files behind your back.

## On-ramps

A starting situation that generates work, then merges onto the main flow.

- **Bugs and requests piling up** → **`/triage`**. It moves incoming issues through triage roles and
  produces agent-ready ones. Only for issues **you didn't create**: what `/to-tickets` produced is
  already agent-ready, so don't triage it.

- **Something's broken** → `systematic-debugging`. Model-reachable, because the trigger is the
  situation rather than a wish. It refuses to theorise until it has a tight feedback loop — one
  command that already goes red on *this* bug — then fixes with a regression test. It redacts
  secrets from everything it shows you along the way.

- **A huge, foggy effort** — greenfield, or a build too big to see the end of → **`/wayfinder`**.
  The most demanding flow here. It charts a shared map of decision tickets and resolves them one at
  a time, producing **decisions, not deliverables**, until the way is clear. Then it hands off: join
  the main flow at `/to-spec` or `/brainstorming`, depending on which ladder the cleared work wants.
  Never reach for it on a well-scoped feature.

- **A merge went sideways** → `resolving-merge-conflicts`. Model-reachable; the conflict is the
  trigger.

## Codebase health

Not feature work, just upkeep. **`/improve-codebase-architecture`** runs whenever you have a spare
moment to keep the codebase good for agents to operate in. It surfaces deepening opportunities;
picking one *generates an idea* you take back to step 1.

## Vocabulary underneath

Two model-reachable references that run *beneath* the other skills, each the single source of truth
for its words. You get them by naming the confusion, not by typing a command.

- `domain-modeling` sharpens the project's **domain** language: challenge a fuzzy term, resolve one
  word doing three jobs, record a hard-to-reverse decision as an ADR. It is what `/grill-with-docs`
  drives to keep `CONTEXT.md` a clean glossary.
- `codebase-design` is the deep-module vocabulary — module, interface, depth, seam, adapter,
  leverage, locality — for designing a module's **shape**: a lot of behaviour behind a small
  interface at a clean seam.

## Standalone

Reach for these directly; they are not on any flow.

| Skill | For |
|---|---|
| **`/wait-what`** | That last message did not land. Ask for it again, in plainer words, in this project's vocabulary. |
| **`/handoff`** | Write a portable file so the work can travel — new harness, new directory, a colleague. |
| **`/prototype`** | Answer a design question with throwaway code: a self-contained HTML demo for logic, a real screen for UI. |
| **`/research`** | Send a background agent at primary sources and get a cited Markdown file back. |
| **`/to-questionnaire`** | The decision needs someone else's knowledge. Turn it into questions aimed at the gap. |
| **`/teach`** | A stateful learning workspace for something you are trying to actually understand. |
| **`/wizard`** | Generate a bash wizard for steps only a human can do — dashboards, credentials, CI secrets. It writes real `.env` values and can set real repository secrets, so read what it generated before running it. |
| **`/writing-for-agents`** | The design vocabulary for any document an agent reads: a skill, an `AGENTS.md`, a doc behind a pointer. |
| **`/writing-skills`** | The testing methodology for skills: pressure scenarios, baselines, closing loopholes. Pairs with the one above rather than competing with it. |
| **`/using-superpowers`** | Switch this session to skill-driven working. Flipped by hand, never automatically. |
| **`/setup-matt-pocock-skills`** | One-time setup for the issue-tracker conventions the ticket flow assumes. |
| `dispatching-parallel-agents` | How to brief a subagent so it comes back with something usable. Model-reachable; it arrives when you are about to fan work out. |

## Phase boundaries

A **phase** is a chunk of work inside a session: the grilling, the implementation, the QA. At the
**boundary** between two of them you have five options, and picking between them is the fuzziest
decision in this whole map:

- **Continue**: stay put. Costs nothing, loses nothing.
- **Clear the window** when nothing here matters to what's next — `/clear` in Claude Code, `/new` in
  OpenCode.
- **`/handoff`** writes a portable markdown file. Narrow: a new harness, a new directory, a
  colleague, or forking a side task mid-phase. What it buys is portability.
- **Subagent**: send a tightly-scoped task to its own window and get a report back.
- **`/compact`** compresses this context and seeds a fresh session with it. The **default**, at the
  bottom of the tree rather than the first reach.

The whole tree, in order, with what each choice costs: [PHASE-BOUNDARIES.md](PHASE-BOUNDARIES.md).
