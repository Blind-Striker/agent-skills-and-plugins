# Skills Cheatsheet — which skill, when

Date: 2026-08-18

Convenience routing relay for the two mature curated sets covered here:
[`deniz-process`](../curation/deniz-process.yaml) (workflow) and
[`deniz-dotnet-general`](../curation/deniz-dotnet-general.yaml) (.NET know-how). Item posture remains
canonical in those manifests and the [generated ledger](ledger.json). The one confusion this exists
to kill:

> **Brainstorm when you don't yet know what you want; grill when you think you know and it
> needs to survive interrogation.** Brainstorming *generates and narrows options* in an open
> design space. Grilling *pressure-tests something that already exists* (a plan, a design, a
> doc set, accumulated decisions) and seals the outcome. A brainstorm's output is often a
> grill's input; a grill's output is decisions → a plan. The order can invert: when decisions
> accumulate from research/spikes first, grill them before brainstorming the next open space.

## deniz-process — the lifecycle line

| Phase | Skill | Reach for it when |
|---|---|---|
| Know | `research` | Facts are missing; primary-source legwork, result lands as a repo doc |
| Explore | `brainstorming` | The solution space is open — shapes, models, naming, approaches |
| Probe | `prototype` | One narrow question only runnable code can answer; output is throwaway |
| Decide | `grilling` / `grill-with-docs` | An artifact exists and needs hole-finding + sealed decisions; `-with-docs` also emits ADRs + glossary as it goes (via `domain-modeling`) |
| Plan | `writing-plans` | Decisions are sealed; turn them into an executable step plan |
| Build | `executing-plans`, `subagent-driven-development`, `test-driven-development` | Plan in hand; SDD when tasks parallelize, TDD for well-specified units |
| Verify | `verification-before-completion` | Before saying "done / fixed / passing" — always |
| Review | `requesting-code-review` → `receiving-code-review` | At branch close / when review feedback arrives |
| Close | `finishing-a-development-branch` | Merge/cleanup checklist |
| Suspend | `handoff` | Session ends with work unfinished — write the handover |

Cross-cutting, any phase:

- `domain-modeling` — terms are drifting, or a decision worth an ADR crystallizes mid-talk
- `codebase-design` — module/interface design vocabulary; design-it-twice for a real seam
- `improve-codebase-architecture` — periodic review of *existing* code, not greenfield design
- `systematic-debugging` — stuck on a bug; stop guessing, start isolating
- `dispatching-parallel-agents` / `using-git-worktrees` — 2+ independent tasks / parallel branches
- `to-spec` → `to-tickets`, `triage` — brainstorm → spec → tickets; issue intake
- `setup-matt-pocock-skills` — bootstrap `docs/agents/` in a new repo

## deniz-dotnet-general — mostly self-triggering reference

These activate when their topic shows up; you rarely invoke them by name. The ones worth
calling deliberately:

| Cluster | Deliberate entry points | The rest (auto-routes) |
|---|---|---|
| API / public surface | `api-design` (extend-only lock), `snapshot-testing` (Verify baselines) | coding standards, DI, options, serialization, concurrency, R3 |
| Testing | `run-tests`, `mtp-hot-reload` (fast fix loop) | anti-patterns, gap analysis, coverage/CRAP, Testcontainers, Playwright |
| Build / MSBuild | `binlog-generation` → `binlog-failure-analysis`; `build-perf-baseline` first when slow | antipatterns, incremental, parallelism, eval, props/targets authoring, … |
| Performance | `microbenchmarking` (BDN), `analyzing-dotnet-performance` (pattern scan) | database-performance, efcore-patterns |
| Guardrail | `dotnet-slopwatch` — after every code change; catches shortcut-masking | detect-static-dependencies, ilspy-decompile, csharp-scripts |

## The arc a feature actually follows

research → brainstorm → (prototype) → grill → write plan → execute (TDD/SDD) → slopwatch +
verify → review → finish branch. Jumping from idea straight to execute is the anti-pattern
this whole toolbox exists to prevent.
