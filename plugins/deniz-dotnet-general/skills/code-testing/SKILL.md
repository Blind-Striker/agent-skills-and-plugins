---
name: code-testing
description: Write or strengthen unit tests for existing code. Covers one
  function, class, or missing regression case up to a project-wide suite, in
  TUnit, xUnit, MSTest, NUnit, pytest, Vitest/Jest, Go, or another framework,
  including coverage-driven additions and strengthening tests an audit found
  weak. Focused work stays proportional; broad work runs a Research, Plan,
  Implement pipeline inline. DO NOT USE for repairing failing or flaky tests
  (systematic debugging owns that), only running tests, analysis-only audits,
  framework or platform migrations, or a behavior test blocked on a production
  seam that has not been selected.
license: MIT
disable-model-invocation: true
---

# Code Testing Skill

The entry point for writing and strengthening tests for existing code. It
handles focused work directly and runs a Research → Plan → Implement pipeline in
its own context for broad requests.

This skill does not repair failing or flaky tests. When the request is to fix a
failing, erroring, or flaky test, say that it is out of scope here, then load the
`deniz-process:systematic-debugging` skill and follow it instead. A failure in a
test this run generated is not such a request: fix that test as
[Troubleshooting](#troubleshooting) describes.

## Non-negotiable execution contract

Classify scope **before editing**:

- **Broad** (a project/package-wide suite, or multiple production
  files/modules): run the pipeline in [Step 3](#step-3-run-the-broad-pipeline-inline).
  Create `research.md` and `plan.md` in a resolved non-stageable
  `<TESTAGENT_DIR>` before implementation, then `status.md` there after the
  final test-quality review. If the state files are absent, the broad workflow
  is incomplete.
- **Focused** (the user explicitly limits work to one function/class/file or one
  missing method): do not create intermediate state files. A sparse
  project-wide request remains broad even when only one source module is
  present.

For either scope, run the narrowest relevant test command to a clean exit.
Always apply [Report-safe test names and result validation](unit-test-generation.prompt.md#report-safe-test-names-and-result-validation),
including when the caller supplies conventions. Preserve edge-case data and
validate configured reports, not just console output.
Keep the handoff proportional: for one to three focused requirements, use a
compact bullet list under a **Requirement coverage** label that names the tests
and successful command; for broader or multi-requirement work, use a
`Requirement | Evidence` table. Each requested behavior must cite an exact test
name.

Before sending a broad-scope final response, check that the response itself
contains `| Requirement | Evidence |` and exact test names for every behavioral
row. A table in an internal plan is not enough. Do not summarize
away those names into module-level bullets or an `Area | Tests` table.

Intermediate state files are internal working data, never deliverables. Keep
`<TESTAGENT_DIR>` non-stageable, never place it or its files in
version-controlled workspace content, and never modify `.gitignore` to hide
them.

Treat completeness as a requirement matrix, not a test-count target. Give every
independently requested state, boundary, error path, or interaction its own
concrete assertion. Combine cases only when one execution genuinely proves the
whole requested combination; do not let a parameterized happy-path case stand in
for an empty state, invalid discriminator, or before/at/after boundary.
For broad requests that name several production modules or layers, give each
named module direct tests for its non-trivial public behavior. Cross-module tests
prove composition, but do not substitute for the requested module-level
coverage. Judge breadth by the behavior matrix, never by matching or exceeding a
raw test count.

For broad work, research, plan, implementation, and review remain required.
They are phases of this one run, not separate agent calls.

Use only capabilities available in the current runtime. Do not retry a missing
skill under aliases or retry a policy-denied operation another way. Derive
language guidance from the repository's manifests and representative tests; do
not search installation directories for it.
If scratch storage is denied, keep the research and plan in context, continue
permitted test edits, and report the missing state artifacts. If execution is
denied, continue permitted static review and report tests as unrun, never passed.
Neither blocker authorizes modifying production code or weakening requirements.

For a **broad or comprehensive** request, the explicit matrix is the floor, not
the ceiling. Treat each requested module or layer as an inventory heading, not
one behavior: expand it into the bounded public operations and their distinct
validation paths, branches, boundaries, interactions, and state transitions.
After satisfying the explicit matrix, inspect each target API for observable
equivalence partitions and invariants that the prompt did not name: identity,
empty, singleton and representative interior inputs; exact boundaries plus an
immediately adjacent value; invalid partitions; and ordering, monotonicity,
rollover, capacity, truncation, or state invariants implied by the implementation.
Add one mutation-relevant case per distinct partition not already proved, using
parameterized or table-driven cases only for siblings that prove the same
behavior. A passing coverage threshold is validation, not a breadth stop
condition. Stop when remaining inputs exercise the same branch and invariant,
not merely when the explicit checklist is complete; never add cases only to
raise the count.

## When to Use This Skill

Use this skill when you need to:

- Generate unit tests for an entire project or specific files
- Improve test coverage for existing codebases
- Create test files that follow project conventions
- Write tests that actually compile and pass
- Add tests for new features or untested code
- Generate or extend a C# suite. This estate is TUnit-first: when the test
  project uses TUnit, or a new C# test project is needed, load the
  `deniz-dotnet-general:writing-tunit-tests` skill after this entry skill has
  established scope and project conventions. An existing test project on
  another framework keeps its framework.

## When Not to Use

- Repairing a failing, erroring, or flaky test (load the
  `deniz-process:systematic-debugging` skill)
- Running or executing existing tests (use the `deniz-dotnet-general:run-tests` skill)
- Migrating between test frameworks (use migration skills)
- Answering a framework API or modernization question that does not ask to
  generate tests
- Analysis-only diagnosis of failing tests when no code or test changes are
  requested

## How It Works

This skill runs a **Research → Plan → Implement** pipeline in one context.
Research inventories the bounded targets and their conventions, Plan maps every
requirement to a concrete test, and Implement writes the tests, builds them,
runs them, fixes them, and formats them. A focused request runs the same
reasoning inline, without intermediate state files.

## Step-by-Step Instructions

### Step 1: Determine the user request

Classify both intent and scope before editing:

- **Generate or extend**: follow the generation workflow below.
- **Audit then strengthen**: load the `deniz-dotnet-general:test-anti-patterns` skill
  and the `deniz-dotnet-general:test-gap-analysis` skill for the existing tests in scope,
  then add or strengthen tests only for the weaknesses and gaps they report.
- **Repair**: out of scope. Load the `deniz-process:systematic-debugging` skill
  and follow it; do not edit tests or production code under this skill.

Make sure you understand what user is asking and for what scope.
When the user does not express strong requirements for test style, coverage goals, or conventions, source the guidelines from [unit-test-generation.prompt.md](unit-test-generation.prompt.md). This prompt provides best practices for discovering conventions, parameterization strategies, behavior-focused coverage, and language-specific patterns.

### Step 2: Size the request before invoking anything

Match the machinery to the scope. Running the full pipeline on a one-file
request costs turns and tool calls without improving the tests.

| Scope | What it looks like | How to run it |
| --- | --- | --- |
| **Focused** | One function, class, or file; "tests for X only"; extending an existing suite with the missing cases | Skip intermediate state files. Keep the requirement checklist in your head (or in the final table), read only the target and one neighbouring test for conventions, write the tests, run the narrowest test command, review your own assertions inline. |
| **Broad** | A project, package, or module set; "comprehensive suite"; a coverage threshold to clear across several files | Run the full Research → Plan → Implement pipeline in Step 3, with intermediate state files under `<TESTAGENT_DIR>` and the completion contract below. |

When in doubt, start focused and escalate only if the request turns out to span
several files. Escalating costs one extra pass; running the broad pipeline on a
focused request costs several. A broad scope that fits one context runs each
phase once. For a scope too large for one pass, extend the existing inventory
and plan only for concrete remaining gaps; do not restart discovery.

Before ending a focused request, check all three conditions together:

1. every named behavior has a concrete assertion, including each requested
   boundary or error path;
2. the narrow test command exited successfully;
3. the final handoff maps those behaviors to exact test names and cites that
   successful command.

Do not replace requirement-level evidence with a generic list of covered areas.

### Step 3: Run the broad pipeline inline

Execute the Research → Plan → Implement sequence in this context, following the
[unit-test-generation.prompt.md](unit-test-generation.prompt.md) guidelines.
Treat the current workspace as authoritative even when it is sparse,
gutted-looking, synthetic, or missing tracked files. Never restore or
reconstruct it, including with `git checkout`, `git restore`, `git reset`, or
`git clean`. If the workspace genuinely contains nothing testable, say so and
stop.

For broad scope, resolve one absolute `<TESTAGENT_DIR>` before creating
intermediate state files:

1. Prefer a host-provided session artifact or scratch directory.
2. Otherwise, in a Git worktree run
   `git rev-parse --path-format=absolute --git-path testagent`; this returns a
   path in worktree-specific Git metadata that cannot be staged.
3. Outside Git, create a unique directory under the operating system's
   temporary directory.

Use that absolute directory in every phase. The path may be inside the
repository's `.git` metadata directory, but it must not be version-controlled
workspace content, appear in `git status`, or be stageable.

### Step 4: Execute with bounded context

For multi-file requests:

1. Turn every explicit user requirement into a checklist before implementation. Include requested layers, collaborators to mock, boundary cases, integrations, coverage thresholds, and report artifacts. Copy multi-condition requirements verbatim — they must each map to one test that exercises the whole combination.
2. Research only the requested module or project and write the checklist plus a compact target inventory to `<TESTAGENT_DIR>/research.md`.
3. Reuse manifests, symbol references, and deterministic pairing tools instead of reading every source and test file.
4. For a substantial multi-file inventory, pair production sources with their existing tests once and record the suggested test paths in `<TESTAGENT_DIR>/research.md`; do not repeat that discovery.
5. Plan each target file once, then implement phases sequentially. Map every checklist item to at least one concrete test or explain why it is blocked.
6. Build and test the narrow target during fix cycles. Run workspace-level
   validation once at the end only for broad work, when the repository contract
   requires that entry point, or when the changes can affect other projects.
   Rebuild only after a concrete fix, at most three times for one diagnostic.
   Stop when a diagnostic repeats without progress, a permission or toolchain
   blocker is concrete, or the fix would leave the requested scope, and report
   the work as `PARTIAL` with the exact command and diagnostic.
7. Before reporting success, re-open the generated tests and verify every checklist item against concrete test names and assertions. Coverage alone is not evidence that a requested mock seam, boundary, state transition, or property combination was tested.
8. When the repository has no representative tests, inspect the installed framework version, the project manifests, and the framework's primary documentation rather than inventing conventions.
9. For .NET, classify SDK-style vs. classic non-SDK before choosing commands or creating files. In classic projects, preserve `packages.config`, existing framework/mock versions and custom base fixtures, add every new test file to the project's explicit `<Compile Include>` items, and use the repository's MSBuild/test-runner commands. Never modernize the project or dependency stack merely to generate tests. Load the `deniz-dotnet-general:run-tests` skill for the repository-compatible test command.
10. Confirm the run discovered the new tests. A zero-test run, or one whose
    discovered count did not grow by the tests you added, validates nothing; in a
    classic project, check the `<Compile Include>` registration as well as the
    test output.

### Completion contract

Every scope must satisfy points 3–5 below. Points 1 and 2 are the **broad-scope**
artifacts: on a focused request the same reasoning happens inline and no
intermediate state files are written.

Do not report completion until all of these are true:

1. *(broad scope)* `<TESTAGENT_DIR>/research.md` records the bounded target
   inventory, existing test conventions, and the acceptance checklist.
2. *(broad scope)* `<TESTAGENT_DIR>/plan.md` maps each checklist item to a planned
   test or an explicit blocker.
3. Generated tests compile and pass with the narrowest relevant test command,
   satisfying the shared report-safe naming and result-validation contract.
   Report every skipped test and every setup or fixture failure; neither is a
   pass.
4. Every explicit user requirement is backed by a concrete test and assertion.
   Fix missing mock seams, boundary cases, state transitions, and property
   combinations even when coverage already passes. In the final summary, cite
   at least one generated test name for every checklist item so completion is
   auditable; if an item has no test to cite, keep implementing or report it as
   blocked. For non-behavioral requirements such as scaffolding, scope limits,
   commands, or coverage artifacts, cite the relevant file, command, or report
   instead of forcing a test-name mapping.
   A passing suite with fewer tests is not automatically weaker: judge
   completeness by whether every independently requested behavior has direct,
   nonredundant evidence, not by raw test volume.
   For broad/comprehensive scope, also verify that every observable equivalence
   partition and invariant discovered in the bounded target APIs has one
   mutation-relevant case, even when the prompt did not name it.
   When the request names multiple modules, verify that each module's own
   non-trivial public behavior has direct test evidence in addition to any
   end-to-end composition test.
5. Review the generated tests for behavior gaps and weak assertions. On a broad
   scope, load the `deniz-dotnet-general:test-gap-analysis` skill and the
   `deniz-dotnet-general:test-anti-patterns` skill, review the generated tests with them,
   and record the findings and fixes in `<TESTAGENT_DIR>/status.md`. On a focused scope,
   do the equivalent review inline — re-read each generated assertion against
   the source — without spawning extra passes.

Lead the final response with `SUCCESS` only when every required validation
passed; otherwise lead with `PARTIAL` or `BLOCKED` and give the exact command
and diagnostic for each unrun or failed validation.

The final response must provide requirement-by-requirement evidence. Use compact
bullets under a **Requirement coverage** label for one to three focused
requirements; use a `Requirement | Evidence` table for broader scopes.
Behavioral evidence cites exact generated test names. Non-behavioral evidence
cites the relevant project file, validation command, or coverage report. A
generic list of tested areas is not a substitute.

Preserve the user's exact meaning in each evidence item; quote verbatim only
when wording distinguishes a required combination. A test that merely exercises
the same collaborators does not satisfy a requirement about their interaction,
and per-class requirements need a citation per class.

**Cite a clean run, not an attempt.** The commands behind the final evidence must
have finished successfully: quote the final passing test summary and, when
thresholds were requested, the per-module coverage table from a run that exited
0. If the last coverage run exited non-zero, fix it and re-run before reporting;
never infer threshold clearance from a failed or partial run.

Before reporting, inspect the final working-tree changes and confirm that
`research.md`, `plan.md`, `status.md`, and any other intermediate state files are
not among the changes intended for commit.

## State Management

Broad-scope runs store intermediate state files in a non-stageable
`<TESTAGENT_DIR>` backed by host scratch storage, Git metadata, or OS temp. A
focused request does not create these files:

| File                     | Purpose                      |
| ------------------------ | ---------------------------- |
| `<TESTAGENT_DIR>/research.md` | Codebase analysis results    |
| `<TESTAGENT_DIR>/plan.md`     | Phased implementation plan   |
| `<TESTAGENT_DIR>/status.md`   | Final quality review and fixes |

## Requirements

- Project must have a build/test system configured
- Testing framework should be installed (or installable)

Classic non-SDK .NET projects are supported when their existing build/test
toolchain is available. When it is not available on the current machine, the
agent can still add and register version-compatible tests, but must report
execution as blocked rather than substituting `dotnet test`.

## Troubleshooting

### Tests don't compile

Read the exact compiler error, the project manifest, and one working neighboring
test before changing generated code. Check `<TESTAGENT_DIR>/plan.md` for the
expected test structure, make the smallest version-compatible correction, then
rebuild the narrow target. Do not modernize packages or the project shape merely
to make the generated tests compile.

### Generated tests fail

Most failures in generated tests are caused by **wrong expected values in assertions**, not production code bugs:

1. Read the actual test output
2. Read the production code to understand correct behavior
3. Fix the assertion, not the production code
4. Never mark tests `[Ignore]` or `[Skip]` just to make them pass

### Wrong testing framework detected

Specify your preferred framework in the initial request: "Generate Jest tests for..."

### Environment-dependent tests fail

Tests that depend on external services, network endpoints, specific ports, or precise timing will fail in CI environments. Focus on unit tests with mocked dependencies instead.

### Broader validation fails

During implementation, build and test the narrow target. Run a solution or
workspace-level command only for broad work, when the repository contract uses
that entry point, or when the targeted change can affect other projects. Do not
turn a focused test request into an unconditional full non-incremental build.
