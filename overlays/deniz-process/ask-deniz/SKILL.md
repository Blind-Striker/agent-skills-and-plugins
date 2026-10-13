---
name: ask-deniz
description: "Choose the skill or ceremony that fits your situation across installed deniz Modules: process workflows, .NET, Aspire, and Akka. Manual ceremonies remain user-started."
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

For an authorized task, choose the smallest fitting route; for a "which skill?" question, recommend
the route rather than starting it. Several steps do not by themselves justify a plan, ledger,
worktree, or agent fan-out. Honor an approved scope, executor, and working-directory choice without
asking for the same approval again. A plan-only request authorizes a plan, not implementation.

## Optional Modules

The .NET sections apply only when their named Module is installed. Before loading a model route,
check that its exact identity is available in the harness's skill catalog. If it is not available,
explain which Module supplies it; do not assume installation or install anything. Missing catalog
information is not proof that a Module is absent. Manual skills may be hidden even when installed:
offer their human pointer conditionally, without declaring them missing or loading them yourself.
The two specialist agents below are descriptive delegation options, not skill-load or slash commands.

## The main flow: idea → ship

### 1. Sharpen the idea

- **`/mattpocock-skills:grill-with-docs`** when you are in a working directory. It interviews you and records
  qualifying terms in `GLOSSARY.md` and qualifying decisions in ADRs, creating either lazily.
  Stateful, and the better of the two whenever a repo is there to write into.
- **`/mattpocock-skills:grill-me`** when there is no working directory — same interview, no paper trail.

Both drive the same `mattpocock-skills:grilling` primitive underneath. It works the design tree in **rounds**: the
whole frontier at once, numbered, each with a recommended answer, and it dispatches subagents to
find environment facts rather than asking you for them.

### 2. Decide, and get the gate

**`/superpowers:brainstorming`** is where an idea becomes an approved design. It draws out what you
want first, then sizes the work out loud — a quick, clear task it simply does, a small change it
agrees with you in chat, and a project gets a written design document. Past a quick task, **it does
not build until you approve.** That gate is the reason to route through it rather than around it.

Grilling and brainstorming are not rivals. Grilling sharpens what you think; brainstorming decides
what to do about it and makes you say yes. On small work you will often only want one of them.

### 3. Turn the decision into work — two ladders

**The light ladder**, when the work is a feature you can hold in your head:

**`/mattpocock-skills:to-spec`** turns the thread into a spec → **`/mattpocock-skills:to-tickets`** splits it into tracer-bullet tickets
with blocking edges → then either **`/mattpocock-skills:implement`** builds one ticket at a time, clearing context
between them, or **`/mattpocock-skills:implement-spec`** builds the whole spec in one run: it reads the tickets as
a task graph, runs implementer subagents across the ready frontier in parallel, and lands
everything on one integration branch. Its input is the ticket graph, which is also what separates
it from subagent-driven development below, which works a plan. Both drive the test-first loop and
the review themselves; you do not need to invoke those.

**The heavy ladder**, when the work is multi-session or several people's worth:

**`/superpowers:writing-plans`** turns the approved spec into a plan of checkbox tasks → then either
**`/superpowers:executing-plans`** to work it in this session, or **`/superpowers:subagent-driven-development`** to dispatch
each task to its own window. Both executors now **rule rather than stall**: they decide conflicts,
ambiguities and plan defects themselves, record each ruling with its cost-if-wrong, and report every
one at the end. They still stop for anything destructive, security-sensitive, or externally visible.

Neither ladder is mandatory for ordinary one-sitting work: an agreed change can be implemented
directly with focused checks, the repository's completion gate, and one final review when required.
Use the light ticket ladder when tickets/specs are useful; use the heavy ladder when a written plan
needs to survive a context window. A new plan does not reopen approval for execution already requested.

### 4. Build

Inside either ladder, the test-first loop is `superpowers:test-driven-development` — model-reachable,
so it arrives when you are writing a behaviour rather than because you typed it. Reach for
`superpowers:using-git-worktrees` when a piece of work wants isolation from what is already in your
tree.

### 5. Close out

- **`/superpowers:requesting-code-review`** reviews the diff on two axes, Standards and Spec, against a fixed
  point. Ask for it on a branch or a PR whenever you want the two-axis read.
- `superpowers:receiving-code-review` is the other half: how to take findings without capitulating or getting
  defensive.
- `superpowers:verification-before-completion` is the discipline that stops "it works" from being a claim you
  did not run. It fires on its own when you are about to say done.
- **`/superpowers:finishing-a-development-branch`** decides how the work integrates, and refuses to remove a
  worktree with uncommitted files behind your back.
- **`/mattpocock-skills:pr`** shapes the body when the work goes up as a pull request: the smallest
  visual that shows the change, before/after evidence that it works, and a one-way or two-way door
  call. A pull request template the repo already has wins; it fills only that template's sections.

### 6. Look back

**`/mattpocock-skills:retro`** closes the loop. After a build, and especially one that went sideways, it
looks back over the session and proposes changes to the agent's **environment**, not the code:
navigation pointers, automated checks, coding standards, steering files, tooling. It edits nothing
itself. Run it in the session it looks back on, before you clear; after clearing, point it at that
session's log.

When a session went wrong, retro pairs with **`/superpowers:diagnosing-superpowers`**, which
gathers the evidence, cited line by line, and gives no advice; retro proposes the environment
changes.

## On-ramps

A starting situation that generates work, then merges onto the main flow.

- **Bugs and requests piling up** → **`/mattpocock-skills:triage`**. It moves incoming issues through triage roles and
  produces agent-ready ones. Only for issues **you didn't create**: what `to-tickets` produced is
  already agent-ready, so don't triage it.

- **Something's broken** → `superpowers:systematic-debugging`. Model-reachable, because the trigger is the
  situation rather than a wish. It refuses to theorise until it has a tight feedback loop — one
  command that already goes red on *this* bug — then fixes with a regression test. It redacts
  secrets from everything it shows you along the way. Once the fix is in, run
  **`/mattpocock-skills:retro`** in the same session to ask what would have prevented the bug.

- **A huge, foggy effort** — greenfield, or a build too big to see the end of → **`/mattpocock-skills:wayfinder`**.
  The most demanding flow here. It charts a shared map of decision tickets and resolves them one at
  a time, producing **decisions, not deliverables**, until the way is clear. Then it hands off: join
  the main flow at `to-spec` or `superpowers:brainstorming`, depending on which ladder the cleared
  work wants. Never reach for it on a well-scoped feature.

## Codebase health

Not feature work, just upkeep. **`/mattpocock-skills:improve-codebase-architecture`** runs whenever you have a spare
moment to keep the codebase good for agents to operate in. It surfaces deepening opportunities;
picking one *generates an idea* you take back to step 1.

## Vocabulary underneath

Two model-reachable references that run *beneath* the other skills, each the single source of truth
for its words. You get them by naming the confusion, not by typing a command.

- `mattpocock-skills:domain-modeling` sharpens the project's **domain** language: challenge a fuzzy term, resolve one
  word doing three jobs, record a hard-to-reverse decision as an ADR. It is what `grill-with-docs`
  drives to keep `GLOSSARY.md` a clean glossary.
- `mattpocock-skills:codebase-design` is the deep-module vocabulary — module, interface, depth, seam, adapter,
  leverage, locality — for designing a module's **shape**: a lot of behaviour behind a small
  interface at a clean seam.

## Standalone

Reach for these directly; they are not on any flow.

| Skill | For |
|---|---|
| **`/mattpocock-skills:wait-what`** | That last message did not land. Ask for it again, in plainer words, in this project's vocabulary. |
| **`/mattpocock-skills:handoff`** | Write a portable file so the work can travel — new harness, new directory, a colleague. |
| **`/mattpocock-skills:prototype`** | Answer a design question with throwaway code: a self-contained HTML demo for logic, a real screen for UI. |
| **`/mattpocock-skills:research`** | Send a background agent at primary sources and get a cited Markdown file back. |
| **`/mattpocock-skills:to-questionnaire`** | The decision needs someone else's knowledge. Turn it into questions aimed at the gap. |
| **`/mattpocock-skills:teach`** | A stateful learning workspace for something you are trying to actually understand. |
| **`/mattpocock-skills:wizard`** | Generate a bash wizard for steps only a human can do — dashboards, credentials, CI secrets. It writes real `.env` values and can set real repository secrets, so read what it generated before running it. |
| **`/mattpocock-skills:writing-for-agents`** | The design vocabulary for any document an agent reads: a skill, an `AGENTS.md`, a doc behind a pointer. |
| **`/superpowers:writing-skills`** | The testing methodology for skills: pressure scenarios, baselines, closing loopholes. Pairs with the one above rather than competing with it. |
| **`/superpowers:using-superpowers`** | Switch this session to skill-driven working. Flipped by hand, never automatically. |
| **`/superpowers:diagnosing-superpowers`** | A session went wrong — repeated work, an ignored plan, a skill that did not fire, too slow or too costly. It finds out why, with cited evidence, and drafts an issue for the superpowers maintainers only when you approve the text. |
| **`/mattpocock-skills:setup-matt-pocock-skills`** | One-time setup for the issue-tracker conventions the ticket flow assumes. |
| **`/deniz-process:verified-waves`** | A task big enough to split: it plans the fan-out in chat, runs it as waves of sub-agents, sends every finding to a fresh skeptic, and reports N of M done. You start it; it never starts itself. |
| `superpowers:dispatching-parallel-agents` | How to brief a subagent so it comes back with something usable. Model-reachable; it arrives when you are about to fan work out. To run a whole verified job, start **`/deniz-process:verified-waves`**. |
| `asd-ste100:asd-ste100` | Rewrite ambiguous machine- or agent-facing text in controlled English; not creative copy or a new execution ceremony. |

## .NET work: General

Applies only when the `deniz-dotnet-general` Module is installed.

Pick by the requested outcome, not by how many files exist. These clusters map the entire Module;
bold pointers are user-started ceremonies, while the reference skills support authorized work.

| Situation | Route and boundary |
|---|---|
| New C# code or a public API | `dotnet-skills:csharp-coding-standards` for idioms; `dotnet-skills:csharp-api-design` for extend-only API and wire compatibility. For behavior-preserving changes use `dotnet:csharp-refactoring`, not idioms that change equality or copying. |
| Concurrency or reactive events | `dotnet-skills:csharp-concurrency-patterns` chooses the abstraction; `dotnet-skills:r3-reactive-extensions` covers R3 event streams rather than ordinary request/response I/O. |
| Serialization | `dotnet-skills:serialization` chooses formats and wire evolution; use `dotnet-skills:snapshot-testing` for human-reviewed output baselines, not email authoring. |
| Service composition | `dotnet-skills:microsoft-extensions-dependency-injection` groups registrations; `dotnet-skills:microsoft-extensions-configuration` handles Options and validation. |
| Database access | `dotnet-skills:efcore-patterns` owns EF usage; `dotnet-skills:database-performance` spans EF/Dapper data-access and SQL performance. |
| Web endpoints or telemetry | `dotnet-aspnetcore:dotnet-webapi` handles HTTP/OpenAPI endpoints, not Blazor or EF queries; `dotnet-skills:opentelementry-dotnet-instrumentation` instruments .NET. Keep an existing composition-root exporter, including Aspire ServiceDefaults, rather than adding a duplicate. |
| Nullable code or adoption | `dotnet-skills:csharp-nullable-reference-types` applies when NRT is already enabled. **`/dotnet-upgrade:migrate-nullable-references`** enables it or migrates a systematic warning set. |
| Publish AOT/trimmed, or adopt library compatibility | `dotnet-skills:aot-trimming` turns on an app's PublishAot/PublishTrimmed and handles code-in-scope warnings. **`/dotnet-upgrade:dotnet-aot-compat`** is the separate existing-library adoption/project-wide IL-warning ceremony; it does not publish binaries. |
| Repo layout, packages, or tools | `dotnet-skills:project-structure`, `dotnet-skills:package-management`, and `dotnet-skills:local-tools` cover daily conventions. New solutions may start with CPM; **`/dotnet-nuget:convert-to-cpm`** converts existing projects. A package update alone does not authorize conversion. |
| SDK container images | `dotnet-skills:sdk-container-publishing` covers PublishContainer/CI, not Dockerfiles, Aspire AppHost deploys, or containers used in tests. |
| Failed/slow build or build XML review | `dotnet:msbuild` is the entry: diagnose, capture/replay a binlog, or report review findings. Working legacy-project conversion is a different job: `dotnet-msbuild:msbuild-modernization`. |
| MSBuild specialists | `dotnet-msbuild:directory-build-organization` for shared-file layering; `dotnet-msbuild:property-patterns` for concrete property defects; `dotnet-msbuild:item-management` for item semantics; `dotnet-msbuild:including-generated-files` for physical artifacts missing from compilation/output. |
| Targets, scheduling, or output clashes | `dotnet-msbuild:target-authoring` writes/fixes target chains and Inputs/Outputs; `dotnet-msbuild:build-parallelism` needs multiple project files; `dotnet-msbuild:check-bin-obj-clash` diagnoses shared paths, not locks held by a running app. |
| Write or strengthen tests | **`/dotnet-test:code-testing`** scopes the test-writing ceremony; it does not repair flaky/failing tests. `deniz-dotnet-general:writing-tunit-tests` supplies TUnit API/syntax, and `dotnet-skills:cscheck` supplies property/model/Spec tests. Repair goes to the Process debugging route above. |
| Run tests, identify a runner, or rerun on edit | `dotnet-test:run-tests` owns exact commands and filters; `dotnet-test:platform-detection` identifies only; `dotnet-test:mtp-hot-reload` handles explicitly requested MTP console hot reload, not IDE Test Explorer. `dotnet-test:filter-syntax` is their consumer-only filter reference, not a separate ceremony. |
| Review test quality or behavioral gaps | `dotnet-test:test-anti-patterns` reports pragmatic test problems; `dotnet-test:test-gap-analysis` reports production changes the assertions would miss. Both are report-only and consume `dotnet-test:test-analysis-extensions`; do not turn the helper into a standalone audit. |
| Coverage evidence or CRAP | `dotnet-test:coverage-analysis` needs supplied coverage, an explicit collection request, or an explicit CRAP request. It is not a substitute for behavioral gap analysis. |
| Integration or UI tests | `dotnet-skills:testcontainers` provides real infrastructure without an Aspire AppHost; `dotnet-skills:playwright-blazor` covers Blazor UI testing, not Blazor authoring. Existing TUnit projects stay on TUnit. |
| Hard-to-test static dependencies | `dotnet-test:detect-static-dependencies` reports C#/.NET obstacles without edits. **`/dotnet-test:generate-testability-wrappers`** creates a missing seam; **`/dotnet-test:migrate-static-to-wrapper`** is a separately started bounded migration to that abstraction. Never automatically chain the two ceremonies. |
| Performance code review or measurement | `dotnet-diag:analyzing-dotnet-performance` scans .NET code on a perf-audit request; `dotnet-diag:microbenchmarking` measures with BenchmarkDotNet; `dotnet-advanced:vectorization` authors/reviews SIMD specifically. |
| Production diagnostic artifacts | **`/dotnet-diag:dotnet-trace-collect`** selects collection tools and captures performance evidence; **`/dotnet-diag:dump-collect`** collects modern .NET crash dumps. Neither performs analysis of the collected files. |
| Focused language/runtime work | `dotnet-advanced:csharp-scripts` needs an explicit request for C# without a project; `dotnet-advanced:dotnet-pinvoke` handles native interop; `dotnet-skills:ilspy-decompile` inspects compiled assemblies. |
| Linux/WSL HTTPS trust or old thread aborts | `dotnet-skills:dotnet-devcert-trust` repairs the exact dev-cert trust error. **`/dotnet-upgrade:thread-abort-migration`** replaces owned Thread.Abort patterns when moving to modern .NET, not every Thread API. |
| LLM change guardrail | `dotnet-skills:slopwatch` runs after .NET code changes. Baseline/tool installation still needs the skill's own approval; the audit does not authorize approving its own baseline. |

For delegated Roslyn incremental-generator diagnosis/design, the Module also supplies
`roslyn-incremental-generator-specialist`: an agent in Claude Code/OpenCode, a skill in Codex.
Use the specialist surface the harness actually offers, not a fabricated slash command.

## Aspire work

Applies only when the `deniz-dotnet-aspire` Module is installed.

Start AppHost/CLI work through `aspire:aspire`. Its downstream workflows are `aspire-init`
(no AppHost, skeleton), `aspireify` (resource wiring and TS toolchains), `aspire-orchestration`
(lifecycle and CLI migration), `aspire-deployment`, and `aspire-monitoring`. Let that router choose
the workflow; these descriptive names are not direct-load instructions. Ordinary build/test work
is not an Aspire lifecycle request.

**`/aspire:aspire-project-v2-migration`** is the user-started Project v2 resource rewrite, not CLI
entry-point migration. The additional specialists are `dotnet-skills:aspire-integration-testing`
for Aspire.Hosting.Testing and `dotnet-skills:aspire-service-defaults` for customizing the shared
project; a missing ServiceDefaults project comes from wiring or the installed template, not by hand.

## Akka.NET work

Applies only when the `deniz-dotnet-akka` Module is installed.

`dotnet-skills:akka-best-practices` supplies actor-system design rules;
`dotnet-skills:akka-hosting-actor-patterns` covers Hosting/entity actors and local/cluster seams;
`dotnet-skills:akka-management` covers discovery/bootstrap/health;
`dotnet-skills:akka-testing-patterns` covers actor tests; and
`dotnet-skills:akka-aspire-configuration` owns the Akka/Aspire cookbook. For delegated diagnosis,
`akka-net-specialist` is the Module's agent in Claude Code/OpenCode and a skill in Codex: use the
actual specialist surface rather than a fake slash command.

## Phase boundaries

A **phase** is a chunk of work inside a session: the grilling, the implementation, the QA. At the
**boundary** between two of them you have five options, and picking between them is the fuzziest
decision in this whole map:

- **Continue**: stay put. Costs nothing, loses nothing.
- **Clear the window** when nothing here matters to what's next — `/clear` in Claude Code, `/new` in
  OpenCode, or start a new Codex chat or CLI session.
- **`/mattpocock-skills:handoff`** writes a portable markdown file. Narrow: a new harness, a new directory, a
  colleague, or forking a side task mid-phase. What it buys is portability.
- **Subagent**: send a tightly-scoped task to its own window and get a report back.
- **`/compact`** compresses this context and seeds a fresh session with it. The **default**, at the
  bottom of the tree rather than the first reach.

The whole tree, in order, with what each choice costs: [PHASE-BOUNDARIES.md](PHASE-BOUNDARIES.md).
