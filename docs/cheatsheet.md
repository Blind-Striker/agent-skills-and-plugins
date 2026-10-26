# Skills cheatsheet: which skill, when

Date: 2026-10-13

Convenience routing map for the four curated Modules. This is not an authorship claim or a new
methodology: it describes how upstream work is selected, combined, renamed, and invoked in this
personal collection. Item posture and transformation reasons remain canonical in
[`curation/*.yaml`](../curation/) and the generated [ledger](ledger.json).

The tables use bare output names. Each harness addresses a name inside its Module: Claude Code as
`/deniz-process:brainstorming`, Codex as `$deniz-process:brainstorming`, and OpenCode as
`@deniz-process.brainstorming` for a skill or agent, with `/<plugin>.<name>` only for an item
emitted as a command. Each target's identity rules live in
[Harness emission](architecture/transformation-and-emission.md#harness-emission).

## Where the material comes from

| Curated Module | Main upstream sources | What this collection changes |
|---|---|---|
| [`deniz-process`](../curation/deniz-process.yaml) | [Superpowers](https://github.com/obra/superpowers) by Jesse Vincent; [Matt Pocock's skills](https://github.com/mattpocock/skills); [ASD-STE100 skill](https://github.com/danyuchn/asd-ste100-skill) by Dustin Yuchen Teng; [Ponytail](https://github.com/DietrichGebert/ponytail) by Dietrich Gebert; `pr`'s Summary guidance from [HumanLayer's `show-me`](https://github.com/humanlayer/skills) | Combines the workflow sets, merges overlapping TDD/debug/review material, chooses invocation per item, replaces the upstream router with `ask-deniz`, omits upstream hooks and dead harness metadata |
| [`deniz-dotnet-general`](../curation/deniz-dotnet-general.yaml) | [dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) by Aaron Stannard; [dotnet/skills](https://github.com/dotnet/skills) by .NET Foundation contributors; [Blazing Fast .NET](https://github.com/EgorBo/BlazingFastDotnet) by Egor Bogatov | Selects the .NET areas used here, merges a few overlapping skills, adapts manual ceremonies, and adds the original `writing-tunit-tests` and `dotnet-walls` skills; requires the Aspire and Process Modules, Process for two reasons: `code-testing` hands a repair or flaky-test request to `systematic-debugging`, and `writing-tunit-tests` hands what to test, and in what order, to `test-driven-development` |
| [`deniz-dotnet-akka`](../curation/deniz-dotnet-akka.yaml) | [dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) | Keeps the Akka specialist and reference set together, repairs/localizes handoffs, and links the cross-Module Aspire and General dependencies |
| [`deniz-dotnet-aspire`](../curation/deniz-dotnet-aspire.yaml) | [Microsoft Aspire skills](https://github.com/microsoft/aspire-skills); selected Aspire material from [dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) | Packages the official Aspire workflow closure, localizes its internal routes, and adds the selected testing and ServiceDefaults specialists |
| [`deniz-coordination`](../curation/deniz-coordination.yaml) | none; original work | Holds coordination ([ADR-0009](adr/0009-coordination-ships-in-its-own-module.md)): the original `coordinator` mode, the original `delegating-to-worker-models` craft, the `harness` dispatcher shipped as .NET source for Claude Code and Codex, and the original `verified-waves` orchestration skill for OpenCode and Codex ([ADR-0010](adr/0010-per-harness-item-emission.md)); requires Process, whose `requesting-code-review` is the coordinator's review read |

Install a Module's required Modules beside it. OpenCode's installer refuses a Selection that has
General without Aspire and Process, Akka without Aspire and General, or Coordination without Process; Claude Code and Codex
install each Plugin on its own, so add the required ones yourself. The README's
[requirement table](../README.md#opencode-from-this-checkout) lists every Module's requirements.

The root [third-party notices](../THIRD_PARTY_NOTICES.md) carry the copyright holders and exact
license route. `writing-tunit-tests` was authored from the official [TUnit documentation](https://tunit.dev/docs),
with topic-outline inspiration credited in [`skills/deniz-dotnet-general/NOTICE.md`](../skills/deniz-dotnet-general/NOTICE.md).
`verified-waves` was written here from the approaches in the
[orchestration research](research/claude-ultracode-and-orchestration-ports.md#adopted-approaches-curator-approved),
and its idea-only sources are in [`skills/deniz-coordination/NOTICE.md`](../skills/deniz-coordination/NOTICE.md).
`delegating-to-worker-models` was written here from the evidence in the
[worker-model delegation research](research/worker-model-delegation.md).

## The process distinction that matters

> **Brainstorm when you do not yet know what you want; grill when you think you know and it needs to
> survive interrogation.** Brainstorming generates and narrows options in an open design space.
> Grilling pressure-tests an existing plan, design, document set, or accumulated decision set. A
> brainstorm often feeds a grill; a grill produces sealed decisions that can feed a plan.

## `deniz-process`: the lifecycle line

| Phase | Skill | Reach for it when |
|---|---|---|
| Route | `ask-deniz` | You know the situation but not which ceremony or skill fits; available to user and model across installed Modules, with manual ceremonies left for the user to start |
| Know | `research` | Facts are missing; primary-source legwork should land as a repository document |
| Explore | `brainstorming` | What you want is not settled yet; it draws that out, then sizes the work: a quick task is just done, a small change is agreed in chat, a project gets a written design |
| Probe | `prototype` | One narrow design question needs runnable throwaway code |
| Re-pitch | `wait-what` | The previous explanation did not land and should be restated using project vocabulary and controlled English |
| Decide | `grilling` / `grill-me` / `grill-with-docs` | An artifact exists and needs hole-finding plus sealed decisions; `grill-me` is the stateless entry, while `grill-with-docs` also maintains qualifying vocabulary and ADRs through `domain-modeling` |
| Delegate a decision | `to-questionnaire` | The right answer belongs to another person; turn the gap into focused questions for them |
| Specify | `to-spec` | The conversation is settled enough to become a specification without another interview |
| Plan | `writing-plans` / `to-tickets` | Turn sealed decisions into executable steps or tracer-bullet issue tickets |
| Map a long effort | `wayfinder` | The work is larger than one agent session and needs a decision/ticket map |
| Build | `implement`, `implement-spec`, `executing-plans`, `subagent-driven-development`, `test-driven-development` | A plan or tickets exist: `implement` works tickets one at a time, `implement-spec` builds a spec's whole ticket graph with parallel subagents on one integration branch, `executing-plans` runs a written plan inline in this session, `subagent-driven-development` runs it with a fresh subagent per task, and `test-driven-development` is the test-first discipline under each |
| Verify | `verification-before-completion` | Before saying done, fixed, passing, or complete |
| Review | `requesting-code-review` then `receiving-code-review` | At task/branch close and when acting on review feedback |
| Close | `finishing-a-development-branch` | Decide integration and perform merge/cleanup checks |
| Publish | `pr` | The work goes up as a pull request: shape its body with the smallest visual, before/after evidence, and a one-way or two-way door call, inside any template the repository already has |
| Look back | `retro` | A build is done, especially one that went sideways: propose changes to the agent's environment (checks, standards, steering files, tooling); it edits nothing |
| Suspend | `handoff` | A session ends with unfinished work that another session must resume |

Cross-cutting routes:

- `domain-modeling`: terminology is drifting or a durable decision has emerged.
- `codebase-design`: use deep-module/interface vocabulary and design-it-twice at a real seam.
- `improve-codebase-architecture`: periodically inspect existing code for deepening opportunities.
- `systematic-debugging`: a bug, test failure, regression, or unexpected behavior needs isolation.
- `diagnosing-superpowers`: a session went wrong (repeated work, an ignored plan, a skill that did not fire, too slow or too costly) and you want the evidence; it drafts an upstream issue only when you approve the text. It gives no advice; `retro` is the one that proposes environment changes.
- `dispatching-parallel-agents` / `using-git-worktrees`: independent tasks or branches can proceed in parallel.
- `verified-waves` (Coordination; OpenCode and Codex only): run one task as waves of sub-agents with every finding checked by a fresh skeptic; `manual`, so you start it.
- `coordinator` (Coordination): make the agent own a task end to end, plan it, hand the work to other agents, review what comes back, and finish with as few questions as possible; `manual`, so you start it per task.
- `delegating-to-worker-models`: hand one piece of work to another agent through any subagent mechanism, with what stays with you, the worker contract and model profiles, evidence from your own run, and review.
- `triage`: move issues and external PRs through the repository's intake state machine.
- `wizard`: generate a human-operated setup script for credentials, infrastructure, or CI steps; it is deliberately manual.
- `writing-for-agents`: design any document consumed by an agent, including `AGENTS.md` and skill prose.
- `writing-skills`: develop and pressure-test a skill as behavior-shaping software.
- `teach`: create and maintain a stateful learning workspace for a concept.
- `setup-matt-pocock-skills`: configure issue-tracker routing, triage vocabulary, and domain-document conventions in a repository; it writes them to `AGENTS.md` and proposes importing that file into `CLAUDE.md`.
- `asd-ste100`: passive controlled-English knowledge used by `wait-what`; normally reached through the handoff rather than invoked directly.
- `ponytail`: the smallest complete change, when you ask for the simplest solution or complain about bloat; it never stays on for a whole session, and its lite and ultra levels are not shipped. `ponytail-review`, `ponytail-audit`, and `ponytail-debt` are its manual reports: one change, a whole repo, and the `shortcut:` debt ledger.
- `using-superpowers`: optional manual switch into the stricter skill-driven session discipline; no SessionStart hook is shipped, and each harness gets only its own tool notes.

Ordinary one-sitting changes do not need a spec/ticket ladder or a written plan merely because they
have several steps. The planning/execution skills first check task size and preserve approvals for
scope, executor, and current checkout; an explicit plan-only request still authorizes no implementation.
The agent picks the process weight (inline, one delegated subagent, or a full executor) by judgment
and asks once only when genuinely torn. An explicit plan request or an approved design is not sized
again, a resumed plan keeps the executor its ledger records, and the default branch needs your
earlier answer or a standing instruction before the first commit there.

## `deniz-dotnet-general`: mostly self-triggering reference

Most reference skills activate when their topic appears. These are the useful deliberate on-ramps:

| Cluster | Deliberate entry points | Supporting auto-routes |
|---|---|---|
| API and public surface | `api-design`, `snapshot-testing` | `modern-csharp-coding-standards`, `dependency-injection-patterns`, `microsoft-extensions-configuration`, `serialization`, `csharp-concurrency-patterns`, `r3-reactive-extensions` |
| Existing package setup | `convert-to-cpm` is the user-started conversion of existing projects to CPM | `dotnet-project-structure`, `package-management`, `dotnet-local-tools`; package updates alone do not authorize conversion, while new solutions may begin with CPM |
| Refactoring | `csharp-refactoring` for a behavior-preserving rename, move, extract, or consolidation | `modern-csharp-coding-standards` for what new code should look like, without silently changing behavior |
| HTTP and instrumentation | `dotnet-webapi` for HTTP/OpenAPI endpoints, `opentelemetry-net-instrumentation` for .NET telemetry | an existing composition-root exporter, including Aspire ServiceDefaults, remains the single exporter registration |
| Test generation and authoring | `code-testing` writes and strengthens tests (a failing or flaky test goes to `systematic-debugging`), `writing-tunit-tests` | `platform-detection`, `test-analysis-extensions`, `cscheck` for framework/platform lookup and property/model/Spec tests |
| Test execution and quality | `run-tests`, `mtp-hot-reload`, `coverage-analysis` | `test-anti-patterns`, `test-gap-analysis`, `testcontainers-integration-tests`, `playwright-blazor-testing`, `filter-syntax` as a consumer-only reference |
| Nullable adoption | `migrate-nullable-references` for project migration | `csharp-nullable-reference-types` for ordinary nullable-enabled code |
| Trim/AOT adoption | `dotnet-aot-compat` for existing-library adoption and project-wide IL-warning migration, not publishing binaries | `aot-trimming` turns on an app's PublishAot/PublishTrimmed and handles trim/AOT-safe code and its in-scope warnings |
| Testability migrations | `generate-testability-wrappers`, then `migrate-static-to-wrapper` when approved | `detect-static-dependencies` identifies the obstacles without mutating code |
| Build and MSBuild | `msbuild` for a failing build, a slow build, or a build-file review; it captures and replays binlogs and routes to the specialists | `check-bin-obj-clash`, `build-parallelism`, `property-patterns`, `item-management`, `target-authoring`, `including-generated-files`, `directory-build-organization`, `msbuild-modernization`, `sdk-container-publishing` |
| Performance | `microbenchmarking`, `analyzing-dotnet-performance`, `blazing-fast-dotnet` for profiling and runtime, GC, ThreadPool, ASP.NET Core, and NativeAOT tuning of a running workload | `vectorization`, `database-performance`, `efcore-patterns`, `serialization` and allocation guidance |
| Diagnostics and upgrade | `dotnet-trace-collect`, `dump-collect`, `thread-abort-migration` | `ilspy-decompile` and focused framework references; collection is not analysis |
| Language experiments and native interop | `csharp-scripts` only for an explicit project-free C# request, `dotnet-pinvoke` for native interop | neither is a generic shell-script or pure-managed debugging route |
| HTTPS dev trust | `dotnet-devcert-trust` for the exact Linux/WSL certificate-trust error | generation, trust and system CA bundle guidance, not a general machine-configuration rollout |
| Guardrail | `dotnet-slopwatch` after code changes | `dotnet-walls` for a worker's verify gates in a repository without its own, static-dependency detection and other passive review knowledge |

The delegated `roslyn-incremental-generator-specialist` is an agent in Claude Code/OpenCode and a
skill in Codex. Use the harness's actual specialist surface, not a fabricated slash command.

## Specialized Modules

| Module | Start here | What follows |
|---|---|---|
| `deniz-dotnet-akka` | `akka-net-specialist` for delegated diagnosis; `akka-net-best-practices` for passive rules | Hosting, Management, testing, and Aspire-configuration specialists |
| `deniz-dotnet-aspire` | `aspire` when an AppHost or Aspire workflow is detected | `aspire-init`, `aspireify`, orchestration, deployment, monitoring, integration testing, and ServiceDefaults; the manual `aspire-project-v2-migration` runs only when you invoke it to move existing project resources to Project v2 |

The Aspire bodies remain mostly upstream-owned. Their presence and linkage are verified; this
repository does not claim every upstream CLI, TypeScript, testing, or package example was executed in
every consumer environment.

## The arc a feature often follows

`research` -> `brainstorming` -> optional `prototype` -> `grilling` -> `writing-plans` or
`to-tickets` -> execution with TDD/SDD -> `dotnet-slopwatch` plus verification -> review -> finish
branch -> `retro`. This is a route through the selected upstream methods, not a claim that every
task needs every ceremony.
