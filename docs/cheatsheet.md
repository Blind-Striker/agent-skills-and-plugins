# Skills cheatsheet: which skill, when

Date: 2026-08-24

Convenience routing map for the four curated Modules. This is not an authorship claim or a new
methodology: it describes how upstream work is selected, combined, renamed, and invoked in this
personal collection. Item posture and transformation reasons remain canonical in
[`curation/*.yaml`](../curation/) and the generated [ledger](ledger.json).

## Where the material comes from

| Curated Module | Main upstream sources | What this collection changes |
|---|---|---|
| [`deniz-process`](../curation/deniz-process.yaml) | [Superpowers](https://github.com/obra/superpowers) by Jesse Vincent; [Matt Pocock's skills](https://github.com/mattpocock/skills); [ASD-STE100 skill](https://github.com/danyuchn/asd-ste100-skill) by Dustin Yuchen Teng | Combines the workflow sets, merges overlapping TDD/debug/review material, chooses invocation per item, replaces the upstream router with `ask-deniz`, omits upstream hooks and dead harness metadata |
| [`deniz-dotnet-general`](../curation/deniz-dotnet-general.yaml) | [dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) by Aaron Stannard; [dotnet/skills](https://github.com/dotnet/skills) by .NET Foundation contributors | Selects the .NET areas used here, merges a few overlapping skills, adapts manual ceremonies, and adds the original `writing-tunit-tests` skill |
| [`deniz-dotnet-akka`](../curation/deniz-dotnet-akka.yaml) | [dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) | Keeps the Akka specialist and reference set together, repairs/localizes handoffs, and links the cross-Module Aspire and General dependencies |
| [`deniz-dotnet-aspire`](../curation/deniz-dotnet-aspire.yaml) | [Microsoft Aspire skills](https://github.com/microsoft/aspire-skills); selected Aspire material from [dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) | Packages the official Aspire workflow closure, localizes its internal routes, and adds the selected testing and ServiceDefaults specialists |

The root [third-party notices](../THIRD_PARTY_NOTICES.md) carry the copyright holders and exact
license route. `writing-tunit-tests` was authored from the official [TUnit documentation](https://tunit.dev/docs),
with topic-outline inspiration credited in [`skills/deniz-dotnet-general/NOTICE.md`](../skills/deniz-dotnet-general/NOTICE.md).

## The process distinction that matters

> **Brainstorm when you do not yet know what you want; grill when you think you know and it needs to
> survive interrogation.** Brainstorming generates and narrows options in an open design space.
> Grilling pressure-tests an existing plan, design, document set, or accumulated decision set. A
> brainstorm often feeds a grill; a grill produces sealed decisions that can feed a plan.

## `deniz-process`: the lifecycle line

| Phase | Skill | Reach for it when |
|---|---|---|
| Route | `ask-deniz` | You know the situation but not which ceremony or skill fits it |
| Know | `research` | Facts are missing; primary-source legwork should land as a repository document |
| Explore | `brainstorming` | The solution space is open: shapes, models, naming, or competing approaches |
| Probe | `prototype` | One narrow design question needs runnable throwaway code |
| Re-pitch | `wait-what` | The previous explanation did not land and should be restated using project vocabulary and controlled English |
| Decide | `grilling` / `grill-with-docs` | An artifact exists and needs hole-finding plus sealed decisions; `grill-with-docs` also maintains qualifying vocabulary and ADRs through `domain-modeling` |
| Delegate a decision | `to-questionnaire` | The right answer belongs to another person; turn the gap into focused questions for them |
| Specify | `to-spec` | The conversation is settled enough to become a specification without another interview |
| Plan | `writing-plans` / `to-tickets` | Turn sealed decisions into executable steps or tracer-bullet issue tickets |
| Map a long effort | `wayfinder` | The work is larger than one agent session and needs a decision/ticket map |
| Build | `implement`, `executing-plans`, `subagent-driven-development`, `test-driven-development` | A plan or tickets exist; choose a single-session executor, plan executor, parallel task loop, and test-first discipline as appropriate |
| Verify | `verification-before-completion` | Before saying done, fixed, passing, or complete |
| Review | `requesting-code-review` then `receiving-code-review` | At task/branch close and when acting on review feedback |
| Close | `finishing-a-development-branch` | Decide integration and perform merge/cleanup checks |
| Suspend | `handoff` | A session ends with unfinished work that another session must resume |

Cross-cutting routes:

- `domain-modeling`: terminology is drifting or a durable decision has emerged.
- `codebase-design`: use deep-module/interface vocabulary and design-it-twice at a real seam.
- `improve-codebase-architecture`: periodically inspect existing code for deepening opportunities.
- `systematic-debugging`: a bug, test failure, regression, or unexpected behavior needs isolation.
- `dispatching-parallel-agents` / `using-git-worktrees`: independent tasks or branches can proceed in parallel.
- `triage`: move issues and external PRs through the repository's intake state machine.
- `wizard`: generate a human-operated setup script for credentials, infrastructure, or CI steps; it is deliberately manual.
- `writing-for-agents`: design any document consumed by an agent, including `AGENTS.md` and skill prose.
- `writing-skills`: develop and pressure-test a skill as behavior-shaping software.
- `teach`: create and maintain a stateful learning workspace for a concept.
- `resolving-merge-conflicts`: an actual merge or rebase conflict is in progress.
- `setup-matt-pocock-skills`: configure issue-tracker routing, triage vocabulary, and domain-document conventions in a repository.
- `asd-ste100`: passive controlled-English knowledge used by `wait-what`; normally reached through the handoff rather than invoked directly.
- `using-superpowers`: optional manual switch into the stricter skill-driven session discipline; no SessionStart hook is shipped.

## `deniz-dotnet-general`: mostly self-triggering reference

Most reference skills activate when their topic appears. These are the useful deliberate on-ramps:

| Cluster | Deliberate entry points | Supporting auto-routes |
|---|---|---|
| API and public surface | `api-design`, `snapshot-testing` | modern C#, DI, Options, serialization, concurrency, R3 |
| Test generation and authoring | `code-testing-agent`, `writing-tunit-tests` | framework/platform detection and test-analysis extensions |
| Test execution and quality | `run-tests`, `mtp-hot-reload`, `coverage-analysis` | anti-patterns, pseudo-mutation gap analysis, Testcontainers, Playwright, filter syntax |
| Nullable adoption | `migrate-nullable-references` for project migration | `csharp-nullable-reference-types` for ordinary nullable-enabled code |
| Testability migrations | `generate-testability-wrappers`, then `migrate-static-to-wrapper` when approved | `detect-static-dependencies` identifies the obstacles without mutating code |
| Build and MSBuild | `binlog-generation` then `binlog-failure-analysis`; `build-perf-baseline` first for slow builds | antipatterns, incrementality, parallelism, evaluation, props, items, targets, extensions |
| Performance | `microbenchmarking`, `analyzing-dotnet-performance` | vectorization, database performance, EF Core, serialization and allocation guidance |
| Diagnostics and upgrade | `dotnet-trace-collect`, `dump-collect`, `dotnet-aot-compat`, `thread-abort-migration` | ILSpy and focused framework references |
| Guardrail | `dotnet-slopwatch` after code changes | static-dependency detection and other passive review knowledge |

## Specialized Modules

| Module | Start here | What follows |
|---|---|---|
| `deniz-dotnet-akka` | `akka-net-specialist` for delegated diagnosis; `akka-net-best-practices` for passive rules | Hosting, Management, testing, and Aspire-configuration specialists |
| `deniz-dotnet-aspire` | `aspire` when an AppHost or Aspire workflow is detected | `aspire-init`, `aspireify`, orchestration, deployment, monitoring, integration testing, and ServiceDefaults |

The Aspire bodies remain mostly upstream-owned. Their presence and linkage are verified; this
repository does not claim every upstream CLI, TypeScript, testing, or package example was executed in
every consumer environment.

## The arc a feature often follows

`research` -> `brainstorming` -> optional `prototype` -> `grilling` -> `writing-plans` or
`to-tickets` -> execution with TDD/SDD -> `dotnet-slopwatch` plus verification -> review -> finish
branch. This is a route through the selected upstream methods, not a claim that every task needs every
ceremony.
