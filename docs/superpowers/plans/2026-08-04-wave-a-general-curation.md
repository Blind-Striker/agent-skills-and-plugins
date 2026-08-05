# Wave A — deniz-dotnet-general curation follow-up Implementation Plan

Date: 2026-08-05

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the Wave-A rulings from `docs/superpowers/specs/2026-08-04-t-items.md`: the approved cuts, the completed testcontainers patch, redirect repair with edge promotion, ceremony flips, the corrected header, and the ROADMAP additions.

**Architecture:** Everything is curation-layer work: `curation/deniz-dotnet-general.yaml`, overlay patches under `overlays/deniz-dotnet-general/`, and `docs/ROADMAP.md`. `plugins/` and `opencode/` are build output — never hand-edited; every task that changes curation or overlays regenerates them with `npm run build`.

**Tech Stack:** repo toolchain plus Git and the .NET SDK for the Testcontainers API smoke compile — `npm run build`, `npm run validate`, `npm run eject`, `git`, `dotnet`.

## Global Constraints

- Record `git rev-parse HEAD` as `<wave-start>` before Task 1; all close-out diffs are relative to that commit.
- Hard rules (AGENTS.md): no curator names, no dates in manifest comments/overlays; deliberate rejections stay in the manifest as `exclude: true` + reason.
- A patch and its manifest edit (`body: patch`, `depends_on`) must land in the same commit — `validate` errors on a one-sided model edge.
- Patch ceremony (extend/create): `npm run eject -- deniz-dotnet-general <name> --patch [--force]` lays a working copy in `overlays/deniz-dotnet-general/<name>/`; edit; re-run `npm run eject -- deniz-dotnet-general <name> --patch` to cut+stamp `overlay.patch`.
- Neutral reference spelling in bodies: `dotnet-test:<name>` / `dotnet-skills:<name>` (nearest upstream plugin namespace). The build localizes per tree. Namespace the FIRST live mention of every taken target in each touched body and declare it in `depends_on`; later bare mentions are legal candidate-tier.
- Anchor edits by exact physical strings, not line numbers. For folded YAML descriptions, replace the complete `description: >` block shown by the task; do not search for a whitespace-normalized sentence that is not contiguous upstream.
- Gates after every task: `npm run build` then `npm run validate` → expect `0 error(s)`; warning set unchanged from the previous task unless the task says otherwise.
- Commit output trees (`plugins/`, `opencode/`, `docs/ledger.json`, `.claude-plugin/marketplace.json`) together with the source edit that caused them — CI rejects stale output.
- When a task changes taken/excluded selection, regenerate and commit `docs/inventory.md` in that same task.

---

### Task 1: Apply the approved cuts (T1)

**Files:**
- Modify: `curation/deniz-dotnet-general.yaml`
- Modify: `docs/inventory.md` (generated)
- Modify: `plugins/`, `opencode/`, `docs/ledger.json`, `.claude-plugin/marketplace.json` (generated)

- [ ] **Step 1: Flip the named items to excluded, with reasons**

Replace each item's `invocation:`/`body:` lines with `exclude: true` + comment. Move each entry into the exclusions region (keep the file's section comments intact). Exact replacements:

```yaml
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/grade-tests
    exclude: true # per-test PR grading is not in the curator's flow
  - source: dotnet-agent-skills/plugins/dotnet-experimental/skills/exp-mock-usage-analysis
    exclude: true # experimental shelf; the broad audit keeps the practical over-mocking checks
  - source: dotnet-agent-skills/plugins/dotnet-experimental/skills/exp-test-maintainability
    exclude: true # experimental shelf; the broad audit keeps the practical duplication checks
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/find-untested-sources
    exclude: true # a second where-to-test surface; coverage-analysis is the single prioritization home
  - source: dotnet-agent-skills/plugins/dotnet-test-migration/skills/migrate-vstest-to-mtp
    exclude: true # test-platform migration is not in the flow: old repos are rewritten TUnit-first, not migrated
  - source: dotnet-agent-skills/plugins/dotnet-test-migration/skills/migrate-xunit-to-xunit-v3
    exclude: true # same call — no framework-version migrations in the flow
  - source: dotnet-skills/skills/mjml-email-templates
    exclude: true # email-template authoring is not in the curator's flow; general Verify guidance stays
  - source: dotnet-agent-skills/plugins/dotnet-upgrade/skills/migrate-dotnet8-to-dotnet9
    exclude: true # version-to-version upgrade guide is not in the flow; general .NET knowledge stays
  - source: dotnet-agent-skills/plugins/dotnet-upgrade/skills/migrate-dotnet9-to-dotnet10
    exclude: true # same call; its version-specific reference closure leaves with the parent skill
```

The two .NET-version migration items have no separately curated dependencies: their `references/`
files are bundled children and disappear with the parent. Their only artifact handoffs point to each
other. Do not cut independently useful EF Core, serialization, ASP.NET Core, MSBuild, cryptography,
container, or interop skills merely because the migration bundles discuss those topics.

- [ ] **Step 2: Record the test-writing gap beside the MSTest exclusion**

Extend the existing `writing-mstest-tests` exclusion comment to end with: `; the TUnit-first replacement is a deferred own skill (docs/ROADMAP.md)`.

- [ ] **Step 3: Build and validate**

Run: `npm run inventory`, `npm run build`, then `npm run validate`.
Expected: build succeeds; every item in the Step 1 cut list disappears from both output trees;
inventory marks each named item excluded, validate reports `0 error(s)`, and the warning identities
are unchanged from the pre-wave baseline.

- [ ] **Step 4: Commit**

```bash
git add curation/deniz-dotnet-general.yaml plugins opencode docs/inventory.md docs/ledger.json .claude-plugin/marketplace.json
git commit -m "feat: cut unused general-dotnet workflows"
```

---

### Task 2: Ceremony flips + header rewrite (T4, T5)

**Files:**
- Modify: `curation/deniz-dotnet-general.yaml`

- [ ] **Step 1: Flip the retained named items `both` → `manual`, updating each comment to match**

| Item | New line(s) |
|---|---|
| convert-to-cpm | `invocation: manual # one-shot repo-mutating conversion ceremony — the curator types it` |
| generate-testability-wrappers | `invocation: manual # writes production abstractions + DI registrations — typed deliberately, never model-fired` |
| migrate-static-to-wrapper | `invocation: manual # codemod across a bounded scope — typed deliberately` |
| dotnet-trace-collect | `invocation: manual # production artifact-collection ceremony — the curator decides when` |
| dump-collect | `invocation: manual # collection-only counterpart; the curator decides when` |
| migrate-nullable-references | `invocation: manual # NRT adoption ceremony with readiness script — typed` |
| thread-abort-migration | `invocation: manual # Thread.Abort → cooperative cancellation — typed` |
| dotnet-aot-compat | `invocation: manual # trim/AOT ceremony — typed; its in-ceremony process discipline left as shipped` |

Do not touch `slopwatch`, `analyzing-dotnet-performance`, `dotnet-devcert-trust` — they stay `both`, comments already say why.

- [ ] **Step 2: Rewrite the header invocation paragraph**

Replace the paragraph starting `# Invocation rule applied module-wide (curator's call): honour the upstream` (through `# ...The one agent kept is a genuine specialist.`) with:

```yaml
# Invocation rule applied module-wide (curator's call): knowledge and report-only
# audits are `auto` — their descriptions honestly name the work, so model selection
# is permitted. Repo/system-mutating ceremonies are `manual`:
# the curator starts them by name, the model never does. `both` is reserved for
# report-only items the curator also types by habit (slopwatch, the perf audit) and
# the one repair flow the model should reach on its exact error (dotnet-devcert-trust).
# The one agent kept is a genuine specialist.
```

- [ ] **Step 3: Replace the reference-posture header paragraph** (starts `# Reference posture:`) with:

```yaml
# Reference posture: untouched MS bodies point at sibling skills by bare name ("see the
# `filter-syntax` skill") — candidate-tier by ADR-0008, never build state. Load-bearing
# edges from patched bodies are namespaced and declared (depends_on). The reference husks
# (filter-syntax, platform-detection, test-analysis-extensions) are taken `auto`, which is
# the grammar's answer to upstream's user-invocable:false + disable-model-invocation:true
# "neither" class: model loads them by name, users never see them.
```

- [ ] **Step 4: Build, validate, inspect**

Run: `npm run build && npm run validate`
Expected: validate reports `0 error(s)` and the warning identities are unchanged (no retained
flipped item's bundle links its own `SKILL.md`). The build report gains parked-bundle warnings for
the flipped items that carry files — expected `manual` cost; Wave B replaces these with the stub
shape. Spot-check: `plugins/deniz-dotnet-general/skills/dotnet-aot-compat/SKILL.md` frontmatter now
carries `disable-model-invocation: true`; `opencode/skills/dotnet-aot-compat/` has no `SKILL.md` but
keeps its bundle; `opencode/commands/dotnet-aot-compat.md` exists.

- [ ] **Step 5: Commit**

```bash
git add curation/deniz-dotnet-general.yaml plugins opencode docs/ledger.json
git commit -m "feat: mutating ceremonies become manual; honest header rule"
```

---

### Task 3: Description overrides for the two reference tables (T3)

**Files:**
- Modify: `curation/deniz-dotnet-general.yaml`

- [ ] **Step 1: Add `frontmatter.description` overrides** (their upstream descriptions name the now-cut `migrate-vstest-to-mtp` as a loader):

```yaml
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/filter-syntax
    invocation: auto # reference husk: VSTest/MTP filter syntax incl. TUnit; upstream ships it "neither", we ship it model-reachable
    frontmatter:
      # upstream's loader list names migrate-vstest-to-mtp (excluded); this is the surviving truth
      description: "Reference data for test filter syntax across all platform and framework combinations: VSTest --filter expressions, MTP filters for MSTest/NUnit/xUnit v3/TUnit, and VSTest-to-MTP filter translation. DO NOT USE directly — loaded by run-tests and mtp-hot-reload when they need filter syntax."
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/platform-detection
    invocation: auto # reference husk: VSTest-vs-MTP + framework detection
    frontmatter:
      # same reason as filter-syntax
      description: "Reference data for detecting the test platform (VSTest vs Microsoft.Testing.Platform) and test framework (MSTest, xUnit, NUnit, TUnit) from project files. DO NOT USE directly — loaded by run-tests and mtp-hot-reload when they need detection logic."
```

- [ ] **Step 2: Build, validate, inspect**

Run: `npm run build && npm run validate` → require `0 error(s)` and the same warning identities as the previous task.
Check: `Select-String -Path plugins/deniz-dotnet-general/skills/filter-syntax/SKILL.md -Pattern 'migrate-vstest'` → no matches.

- [ ] **Step 3: Commit**

```bash
git add curation/deniz-dotnet-general.yaml plugins opencode docs/ledger.json
git commit -m "feat: reference-table descriptions stop naming the cut migration skill"
```

---

### Task 4: run-tests patch (T3)

**Files:**
- Create: `overlays/deniz-dotnet-general/run-tests/overlay.patch`
- Modify: `curation/deniz-dotnet-general.yaml` (run-tests item)

- [ ] **Step 1: Eject a working copy**

Run: `npm run eject -- deniz-dotnet-general run-tests --patch`
Expected: `Ejected ... -> overlays/deniz-dotnet-general/run-tests/ (working copy)`

- [ ] **Step 2: Edit `overlays/deniz-dotnet-general/run-tests/SKILL.md`.** Replace the complete folded frontmatter block beginning `description: >` and ending immediately before `license:` with:

```yaml
description: >
  Recommend or run the exact `dotnet test` command. ALWAYS use when the
  user asks to run, filter, or troubleshoot .NET tests or wants the precise
  command, flags, or argument order — the right syntax depends on the test
  platform (VSTest vs Microsoft.Testing.Platform) and SDK version and is
  easy to get wrong from memory. USE FOR: running all tests or a subset (a
  specific class, category, or trait) via filters; a single framework in a
  multi-TFM project (`--framework`); TRX reports; crash or hang dumps;
  whether MTP args need the `--` separator (SDK 8/9) or pass directly
  (SDK 10+); diagnosing why `dotnet test` fails or uses wrong argument
  syntax. Detects the platform (VSTest vs MTP) and framework
  (MSTest/xUnit/NUnit/TUnit), then picks the matching command and filter
  flag (--filter, --filter-class, --filter-trait, --filter-query,
  --treenode-filter). DO NOT USE FOR: writing test code; iterating on
  failing tests without rebuilding (use mtp-hot-reload), CI/CD config, or
  debugging test logic.
```

Apply these body replacements:

| Old (locate by substring) | New |
|---|---|
| `- User needs to write or generate test code (use ` `` `writing-mstest-tests` `` ` for MSTest, or general coding assistance for other frameworks)` | `- User needs to write or generate test code (out of scope — this skill runs tests)` |
| `- User needs to migrate from VSTest to MTP (use ` `` `migrate-vstest-to-mtp` `` `)` | *(delete the whole line)* |
| `- User wants to iterate on failing tests without rebuilding (use ` `` `mtp-hot-reload` `` `)` | ``- User wants to iterate on failing tests without rebuilding (use the `dotnet-test:mtp-hot-reload` skill)`` |
| `see the ` `` `platform-detection` `` ` skill.` (the "For full detection logic" bullet) | ``see the `dotnet-test:platform-detection` skill.`` |
| `See the ` `` `filter-syntax` `` ` skill for the complete filter syntax` (first mention only) | ``See the `dotnet-test:filter-syntax` skill for the complete filter syntax`` |

Leave every later bare `filter-syntax`/`platform-detection` mention untouched.

- [ ] **Step 3: Cut the patch**

Run: `npm run eject -- deniz-dotnet-general run-tests --patch`
Expected: `Cut N patch lines -> overlays/deniz-dotnet-general/run-tests/overlay.patch`

- [ ] **Step 4: Declare in the manifest** — the run-tests item becomes:

```yaml
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/run-tests
    invocation: auto # the operational core: platform/framework detection → exact dotnet test syntax
    body: patch # drops redirects to excluded items (code-testing-agent, writing-mstest-tests,
    # migrate-vstest-to-mtp) and promotes every live target in the touched body
    depends_on: [filter-syntax, mtp-hot-reload, platform-detection]
```

- [ ] **Step 5: Build, validate, inspect**

Run: `npm run build && npm run validate` → require `0 error(s)` and the same warning identities as the previous task.
Check: `plugins/.../run-tests/SKILL.md` contains `deniz-dotnet-general:filter-syntax`, `deniz-dotnet-general:mtp-hot-reload`, and `deniz-dotnet-general:platform-detection`; `opencode/skills/run-tests/SKILL.md` contains the three bare output names and no `deniz-dotnet-general:` spelling; the ledger entry gains all three fact edges.

- [ ] **Step 6: Commit**

```bash
git add curation/deniz-dotnet-general.yaml overlays plugins opencode docs/ledger.json
git commit -m "feat: run-tests drops dead redirects and declares its reference-table edges"
```

---

### Task 5: mtp-hot-reload patch (T3)

**Files:**
- Create: `overlays/deniz-dotnet-general/mtp-hot-reload/overlay.patch`
- Modify: `curation/deniz-dotnet-general.yaml`

- [ ] **Step 1:** `npm run eject -- deniz-dotnet-general mtp-hot-reload --patch`

- [ ] **Step 2: Edit `overlays/deniz-dotnet-general/mtp-hot-reload/SKILL.md`:**

| Old | New |
|---|---|
| `Follow the detection procedure in the ` `` `platform-detection` `` ` skill` | ``Follow the detection procedure in the `dotnet-test:platform-detection` skill`` |
| `If the project uses VSTest, inform the user that MTP hot reload is not available and suggest migrating to MTP first (see ` `` `migrate-vstest-to-mtp` `` `), or using Visual Studio's built-in Test Explorer hot reload feature instead.` | `If the project uses VSTest, inform the user that MTP hot reload is not available there and suggest Visual Studio's built-in Test Explorer hot reload feature instead.` |
| `see the ` `` `filter-syntax` `` ` skill for full details.` | ``see the `dotnet-test:filter-syntax` skill for full details.`` |

- [ ] **Step 3:** `npm run eject -- deniz-dotnet-general mtp-hot-reload --patch`

- [ ] **Step 4: Manifest:**

```yaml
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/mtp-hot-reload
    invocation: auto # TUnit is MTP-native, so MTP iteration speed matters here
    body: patch # drops the migrate-vstest-to-mtp handoff (excluded) and declares the husk edges
    depends_on: [filter-syntax, platform-detection]
```

- [ ] **Step 5:** Run `npm run build && npm run validate`; require `0 error(s)`, no new warning identity, and no `migrate-vstest` match under `plugins/deniz-dotnet-general/skills/mtp-hot-reload/`.

- [ ] **Step 6: Commit**

```bash
git add curation/deniz-dotnet-general.yaml overlays plugins opencode docs/ledger.json
git commit -m "feat: mtp-hot-reload rewires its VSTest handoff and declares husk edges"
```

---

### Task 6: test-anti-patterns patch (T3)

**Files:**
- Create: `overlays/deniz-dotnet-general/test-anti-patterns/overlay.patch`
- Modify: `curation/deniz-dotnet-general.yaml`

- [ ] **Step 1:** `npm run eject -- deniz-dotnet-general test-anti-patterns --patch`

- [ ] **Step 2: Edit `overlays/deniz-dotnet-general/test-anti-patterns/SKILL.md`.** Replace the complete folded frontmatter block beginning `description: >` and ending immediately before `license:`; do not search for a normalized one-line sentence. Use:

```yaml
description: >
  Audits an existing test file or suite in any language for pragmatic
  anti-patterns and quality issues, producing a severity-ranked report.
  Use when asked to audit or review tests, find false-confidence tests,
  investigate flakiness or order dependence, identify duplicated tests or
  magic values, or judge whether assertions are absent, trivial-only, or
  repeatedly verify only one facet of behavior. Polyglot: .NET, Python,
  TypeScript/JavaScript, Java, Go, Ruby, Rust, Swift, Kotlin, PowerShell,
  and C++. DO NOT USE FOR: writing or running tests; coverage/CRAP metrics;
  full assertion-diversity metrics or an academic smell-catalog audit; fixing
  or rewriting the tests it audits (report-only).
```

Body edits:

| Old | New |
|---|---|
| `Call the ` `` `test-analysis-extensions` `` ` skill` (first mention, the blockquote) | ``Call the `dotnet-test:test-analysis-extensions` skill`` |
| `- User wants to write new tests from scratch (use ` `` `code-testing-agent` `` ` for any language, or ` `` `writing-mstest-tests` `` ` for MSTest specifically)` | `- User wants to write new tests from scratch (out of scope — this skill audits existing tests)` |
| `- User asks to fix swapped ` `` `Assert.AreEqual` `` ` argument order in MSTest (use ` `` `writing-mstest-tests` `` `)` and the following `DynamicData` line | Replace both lines with one: `- User asks to fix or modernize test code (this skill reports; it does not edit tests)` |
| `- User wants to run or execute tests (use ` `` `run-tests` `` ` for .NET)` | ``- User wants to run or execute tests (use the `dotnet-test:run-tests` skill for .NET)`` |
| `- User wants a deep formal test smell audit with academic taxonomy and extended catalog (use ` `` `test-smell-detection` `` `)` | `- User requires a full academic smell catalog or citable taxonomy (not provided by this pragmatic audit)` |
| `For a deep mock audit in .NET, use ` `` `exp-mock-usage-analysis` `` `.` | *(delete this final sentence; keep the practical over-mocking detector)* |
| `For a detailed duplication analysis in .NET, use ` `` `exp-test-maintainability` `` `.` | *(delete this sentence; keep the practical duplicate-test detector and its calibration note)* |

Add this row immediately after the existing **No assertions** row in the Critical table:

```markdown
| **Trivial-only assertions** | Tests whose only checks are null/presence/truthiness guards or constants, with no assertion on the value, state, structure, error, or side effect that defines the behavior. A guard followed by a meaningful assertion is not trivial-only. |
```

Add this row immediately after **Duplicate tests** in the Medium table:

```markdown
| **Single-facet assertion pattern** | Across a related test group, every test repeatedly verifies only one facet while relevant state transitions, structure, errors, or side effects remain unchecked. Report qualitatively with concrete missing facets; do not compute an assertion-diversity score. |
```

Replace the complete Step 5 depth-bar block, from `**Depth bar` through the current numbered item 4, with:

```markdown
**Depth bar — a tidy report that is shallower than an unassisted review is a failure.** Before writing, satisfy all five:

1. **Account for every test in scope.** Walk the full list of test methods and fields; a finding table that silently skips tests (or fixtures like an unused `static HttpClient` field) is incomplete. State the number of tests reviewed.
2. **Calibrate assertion depth.** Distinguish assertion-free, trivial-only, and meaningful assertions. Name a missing value, state, structure, error, or side-effect facet only when the production behavior makes that facet relevant.
3. **Make every Critical/High fix complete and specific.** Give the replacement assertion with the *exact expected value* (the computed discount, the exact CSV line, the full expected object), not a `// assert something here` placeholder.
4. **Name the adjacent gaps the tests should also cover** — untested error paths, boundary values, and round-trip/culture-sensitivity risks in the same class. These are part of "what's wrong with my tests", and omitting them is the most common way this review loses to an unassisted one.
5. **Keep the report internally consistent.** Summary counts must equal the enumerated findings. Publish a settled conclusion: do all reconsidering before you write, and never leave "wait, that's wrong" / "this should fail but doesn't" reasoning in the output.
```

Leave the second bare `test-analysis-extensions` mention untouched.

- [ ] **Step 3:** `npm run eject -- deniz-dotnet-general test-anti-patterns --patch`

- [ ] **Step 4: Manifest:**

```yaml
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/test-anti-patterns
    invocation: auto # the broad pragmatic audit, including qualitative assertion-depth review
    body: patch # dead redirects dropped; practical assertion-depth checks added; every live body
    # edge promoted
    depends_on: [run-tests, test-analysis-extensions]
```

- [ ] **Step 5:** Run `npm run build && npm run validate`; require `0 error(s)` and no new warning identity. Confirm no `exp-mock-usage-analysis|exp-test-maintainability|assertion-quality|test-smell-detection|writing-mstest-tests|code-testing-agent` artifact reference remains under the emitted skill, and confirm the ledger records model edges to `run-tests` and `test-analysis-extensions`.

- [ ] **Step 6: Commit**

```bash
git add curation/deniz-dotnet-general.yaml overlays plugins opencode docs/ledger.json
git commit -m "feat: test-anti-patterns adds practical assertion-depth review"
```

---

### Task 7: test-gap-analysis patch (T3)

**Files:**
- Create: `overlays/deniz-dotnet-general/test-gap-analysis/overlay.patch`
- Modify: `curation/deniz-dotnet-general.yaml`

- [ ] **Step 1:** `npm run eject -- deniz-dotnet-general test-gap-analysis --patch`

- [ ] **Step 2: Edit `overlays/deniz-dotnet-general/test-gap-analysis/SKILL.md`.** Replace the complete one-line frontmatter description with:

```yaml
description: "Performs pseudo-mutation analysis on production code in any language to find gaps in existing tests. Use when the user asks to find weak or shallow tests, discover untested edge cases, or check whether tests would catch a bug — for example, whether a boundary, boolean, null, exception, or arithmetic change would slip through. Confirms candidate survivors by applying them and running the covering tests when execution is available. Polyglot: .NET, Python, TypeScript/JavaScript, Java, Go, Ruby, Rust, Swift, Kotlin, PowerShell, and C++. DO NOT USE FOR: writing new tests; pragmatic anti-pattern or qualitative assertion-depth audits; quantitative assertion-diversity metrics; or running an actual mutation-testing framework."
```

Body:

| Old | New |
|---|---|
| `Call the ` `` `test-analysis-extensions` `` ` skill` (first mention, the blockquote) | ``Call the `dotnet-test:test-analysis-extensions` skill`` |
| `- The ` `` `code-testing-generator` `` ` agent (or any test-generation workflow) calls this skill as a pre-completion self-review step on freshly generated tests, before declaring the run finished` | `- Any test-generation workflow can call this skill as a pre-completion self-review step on freshly generated tests` |
| `- User wants to write new tests from scratch (use ` `` `code-testing-agent` `` ` for any language, or ` `` `writing-mstest-tests` `` ` for MSTest specifically)` | `- User wants to write new tests from scratch (out of scope)` |
| `- User wants to detect test anti-patterns like flakiness or poor naming (use ` `` `test-anti-patterns` `` `)` | ``- User wants a pragmatic anti-pattern or qualitative assertion-depth audit (use the `dotnet-test:test-anti-patterns` skill)`` |
| `- User wants to measure assertion variety (use ` `` `assertion-quality` `` `)` | `- User wants quantitative assertion-diversity metrics (not provided by the curated set)` |
| `mention that ` `` `code-testing-agent` `` ` (any language) or ` `` `writing-mstest-tests` `` ` (MSTest-specific) can help write the missing tests, and ` `` `test-anti-patterns` `` ` can audit existing test quality` | `mention that ` `` `test-anti-patterns` `` ` can audit existing test quality` |

- [ ] **Step 3:** `npm run eject -- deniz-dotnet-general test-gap-analysis --patch`

- [ ] **Step 4: Manifest:**

```yaml
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/test-gap-analysis
    invocation: auto # pseudo-mutation strength reasoning — the "would my tests catch it" job
    body: patch # dead redirects dropped; qualitative audit handoff and extension-table edges declared
    depends_on: [test-analysis-extensions, test-anti-patterns]
```

- [ ] **Step 5:** Run `npm run build && npm run validate`; require `0 error(s)` and no new warning identity. Confirm the ledger records model edges to `test-analysis-extensions` and `test-anti-patterns`, and no excluded test-writing or assertion-metrics destination remains.

- [ ] **Step 6: Commit**

```bash
git add curation/deniz-dotnet-general.yaml overlays plugins opencode docs/ledger.json
git commit -m "feat: test-gap-analysis rewires dead redirects to kept homes"
```

---

### Task 8: coverage-analysis patch (T3)

**Files:**
- Create: `overlays/deniz-dotnet-general/coverage-analysis/overlay.patch`
- Modify: `curation/deniz-dotnet-general.yaml`

- [ ] **Step 1:** `npm run eject -- deniz-dotnet-general coverage-analysis --patch`

- [ ] **Step 2: Edit `overlays/deniz-dotnet-general/coverage-analysis/SKILL.md`.** Replace the complete folded frontmatter block beginning `description: >` and ending immediately before `license:` with:

```yaml
description: >
  Analyzes .NET Cobertura coverage with per-method CRAP scores. Use for
  project-wide coverage plateaus, risk hotspots, and where-to-test-next
  prioritization, or for a targeted CRAP calculation on an exact method,
  class, or source file. Project-wide requests rank complex, under-covered
  members; targeted requests filter existing coverage rows and report only
  the requested scope. DO NOT USE FOR: auditing test-code anti-patterns,
  writing tests, or running tests without coverage/CRAP context.
```

Body edits:

- Delete the `Targeted single-method CRAP analysis — use the crap-score skill instead` bullet from **When Not to Use**.
- Add `| Target scope | No | None | Exact method name, class name, or source-file path; selects the targeted workflow |` to the four-column Inputs table.
- Rename the existing `## Workflow` heading to `## Project-wide Workflow` and begin its mandatory block with `For a project-wide request,` so none of its full-report obligations leak into targeted requests.
- Insert the following section immediately before `## Project-wide Workflow`:

````markdown
## Targeted Method, Class, or File Workflow

Use this branch when the user names an exact method, class, or source file. Locate or collect
Cobertura XML using the setup and provider rules below, then stop before the project-wide analysis
and report phases.

Run the bundled method extractor over all methods:

```powershell
$allMethods = @(
    & "<skill-directory>/scripts/Extract-MethodCoverage.ps1" `
        -CoberturaPath @(<all COBERTURA file paths as array>) `
        -Filter all |
        ConvertFrom-Json
)

$targetKind = "<method|class|file>"
$target = "<exact user-provided target>"
$normalizedTarget = $target.Replace('\', '/')
$targetRows = switch ($targetKind) {
    "method" { @($allMethods | Where-Object Method -eq $target) }
    "class"  { @($allMethods | Where-Object Class -eq $target) }
    "file"   { @($allMethods | Where-Object { $_.File.Replace('\', '/') -eq $normalizedTarget }) }
}

$result = @($targetRows | ForEach-Object {
    $coverage = [double]$_.LineCoverage / 100.0
    $complexity = [double]$_.Complexity
    $crap = [Math]::Round(
        ($complexity * $complexity * [Math]::Pow(1.0 - $coverage, 3)) + $complexity,
        2)
    [PSCustomObject]@{
        Class = $_.Class
        Method = $_.Method
        File = $_.File
        Complexity = $_.Complexity
        LineCoverage = $_.LineCoverage
        CrapScore = $crap
    }
})
$result | ConvertTo-Json
```

If no row matches, report that the exact target is absent from the supplied Cobertura data and do
not invent a score. Otherwise report only the selected rows, the formula inputs, risk interpretation,
and one concrete coverage or complexity recommendation. Do not emit the project-wide dashboard,
all-below-threshold inventory, or optional ReportGenerator phase for a targeted request unless the
user separately asks for them.
````

- [ ] **Step 3:** `npm run eject -- deniz-dotnet-general coverage-analysis --patch`

- [ ] **Step 4: Manifest:**

```yaml
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/coverage-analysis
    invocation: auto # the module's single CRAP/coverage home (Cobertura-based) — including the
    # targeted method/class/file job it absorbed from the excluded crap-score
    body: patch # adds a bounded targeted branch using the existing all-method coverage extractor
```

- [ ] **Step 5:** Run `npm run build && npm run validate`; require `0 error(s)`, no new warning identity, and no `crap-score` match under `plugins/deniz-dotnet-general/skills/coverage-analysis/`. Inspect the emitted body to confirm method, class, and file matching all use `Extract-MethodCoverage.ps1 -Filter all`, compute the existing CRAP formula, and explicitly stop when the target has no Cobertura row.

- [ ] **Step 6: Commit**

```bash
git add curation/deniz-dotnet-general.yaml overlays plugins opencode docs/ledger.json
git commit -m "feat: coverage-analysis handles targeted CRAP scopes"
```

---

### Task 9: dotnet-webapi patch (T3)

**Files:**
- Create: `overlays/deniz-dotnet-general/dotnet-webapi/overlay.patch`
- Modify: `curation/deniz-dotnet-general.yaml`

- [ ] **Step 1:** `npm run eject -- deniz-dotnet-general dotnet-webapi --patch`

- [ ] **Step 2: Edit `overlays/deniz-dotnet-general/dotnet-webapi/SKILL.md`.** Replace the complete folded frontmatter block beginning `description: >` and ending immediately before `license:` with:

```yaml
description: >
  Guides creation and modification of ASP.NET Core Web API endpoints with
  correct HTTP semantics, OpenAPI metadata, and error handling. USE FOR:
  adding controller or minimal-API endpoints, wiring OpenAPI, creating .http
  test files, and setting up global API error handling. DO NOT USE FOR:
  general C# style, EF Core data access or query optimization (use
  efcore-patterns or database-performance), frontend/Blazor work, gRPC, or
  SignalR.
```

Body:

| Old | New |
|---|---|
| `- EF Core data modeling or query optimization work; use ` `` `optimizing-ef-core-queries` `` `;` | `- EF Core data modeling or query optimization work; use ` `` `dotnet-skills:efcore-patterns` `` ` and ` `` `dotnet-skills:database-performance` `` `;` |
| `see the ` `` `optimizing-ef-core-queries` `` ` skill.` (the `AsNoTracking`/seed-data sentence) | `see the ` `` `efcore-patterns` `` ` skill.` |

(The second body mention stays bare deliberately — one namespaced mention per target is enough, and it is the first.)

- [ ] **Step 3:** `npm run eject -- deniz-dotnet-general dotnet-webapi --patch`

- [ ] **Step 4: Manifest:**

```yaml
  - source: dotnet-agent-skills/plugins/dotnet-aspnetcore/skills/dotnet-webapi
    invocation: auto # HTTP semantics/endpoints — complementary to api-design's library/wire focus
    body: patch # EF handoff pointed at the excluded optimizing-ef-core-queries; rewired to the two
    # kept homes and declared
    depends_on: [efcore-patterns, database-performance]
```

- [ ] **Step 5:** Run `npm run build && npm run validate`; require `0 error(s)` and no new warning identity.

- [ ] **Step 6: Commit**

```bash
git add curation/deniz-dotnet-general.yaml overlays plugins opencode docs/ledger.json
git commit -m "feat: dotnet-webapi hands EF work to the kept homes"
```

---

### Task 10: test-analysis-extensions patch (T3)

**Files:**
- Create: `overlays/deniz-dotnet-general/test-analysis-extensions/overlay.patch`
- Modify: `curation/deniz-dotnet-general.yaml`

- [ ] **Step 1:** `npm run eject -- deniz-dotnet-general test-analysis-extensions --patch`

- [ ] **Step 2: Edit the working copy.** Replace the complete folded `SKILL.md` block beginning `description: >-` and ending immediately before `user-invocable:` with:

```yaml
description: >-
  Provides file paths to language-specific reference files for the curated
  pragmatic test-analysis pair: test-anti-patterns and test-gap-analysis.
  Do not use directly — those skills load the matching extension when they
  need framework-specific test markers, assertion APIs, skip annotations,
  sleep patterns, mystery-guest indicators, integration markers, and
  setup/teardown conventions.
```

In the `SKILL.md` body, replace `**Tag support** (for ` `` `test-tagging` `` ` skill)` with `**Tag and trait syntax**` while retaining the three capability values as neutral lookup data.

`extensions/dotnet.md`:

| Old | New |
|---|---|
| `Used by the polyglot test analysis skills (` `` `assertion-quality` `` `, ` `` `test-anti-patterns` `` `, ` `` `test-gap-analysis` `` `, ` `` `test-smell-detection` `` `, ` `` `test-tagging` `` `).` | `Used by the polyglot test analysis skills (` `` `test-anti-patterns` `` `, ` `` `test-gap-analysis` `` `).` |

In all eleven extension files (`cpp.md`, `dotnet.md`, `go.md`, `java.md`, `kotlin.md`, `powershell.md`, `python.md`, `ruby.md`, `rust.md`, `swift.md`, `typescript.md`), replace the heading `## Tag/Trait Attributes (for ` `` `test-tagging` `` `)` with `## Tag/Trait Attributes`. Retain the framework rows below each heading.

- [ ] **Step 3:** `npm run eject -- deniz-dotnet-general test-analysis-extensions --patch`

- [ ] **Step 4: Manifest:**

```yaml
  - source: dotnet-agent-skills/plugins/dotnet-test/skills/test-analysis-extensions
    invocation: auto # reference husk: per-language lookup tables for the audit pair (incl. TUnit at this pin)
    body: patch # its consumer lists named five skills and an agent; only two consumers survive here
```

- [ ] **Step 5:** Run `npm run build && npm run validate`; require `0 error(s)` and no new warning identity. Confirm `test-tagging`, `assertion-quality`, `test-smell-detection`, and `test-quality-auditor` no longer appear as artifact names anywhere under the emitted extension skill; framework tag APIs and MSTest rows remain.

- [ ] **Step 6: Commit**

```bash
git add curation/deniz-dotnet-general.yaml overlays plugins opencode docs/ledger.json
git commit -m "feat: extension tables name their two surviving consumers"
```

---

### Task 11: Keep snapshot testing general-purpose (T3)

**Files:**
- Create: `overlays/deniz-dotnet-general/snapshot-testing/overlay.patch`
- Modify: `curation/deniz-dotnet-general.yaml`

- [ ] **Step 1:** `npm run eject -- deniz-dotnet-general snapshot-testing --patch`

- [ ] **Step 2: Edit `overlays/deniz-dotnet-general/snapshot-testing/SKILL.md`.** Replace the single-line upstream `description:` value with:

```yaml
description: Use Verify for snapshot testing in .NET. Approve API surfaces, HTTP responses, rendered and generated outputs, and serialized outputs. Detect unintended changes through human-reviewed baseline files.
```

Apply these body edits:

| Old | New |
|---|---|
| `- Verifying rendered output (HTML emails, reports, generated code)` | `- Verifying rendered output (HTML reports, generated code, documents)` |
| `| Rendered HTML/emails | Yes | Catches visual regressions |` | `| Rendered HTML and generated documents | Yes | Catches visual regressions |` |

Replace the complete `### String/HTML Verification` section, from that heading through the sentence ending `viewable in browser.`, with:

````markdown
### String/HTML Verification

```csharp
[Fact]
public async Task VerifyRenderedReport()
{
    var html = await _reportRenderer.RenderAsync(
        "QuarterlyReport",
        new { Quarter = "Q1" });

    await Verify(html, extension: "html");
}
```

Creates `VerifyRenderedReport.verified.html` — viewable in a browser.
````

Delete the complete `## Email Template Testing` section, including its trailing horizontal rule,
from that heading through the separator immediately before `## API Surface Approval`.

Replace the complete code block under `### Recommended Structure` with:

```text
tests/
  MyApp.Tests/
    Snapshots/           # All verified files
      RenderedOutput/
        QuarterlyReport.verified.html
      ApiTests/
        GetUser.verified.txt
    RenderedReportTests.cs
    ApiTests.cs
    ModuleInitializer.cs
```

Delete the complete `## Integration with MJML Email Testing` section, including its trailing
horizontal rule, from that heading through the separator immediately before `## Resources`. This
also removes the dead `aspnetcore/transactional-emails` artifact handoff.

Leave ordinary domain data such as the `UserDto.Email` property untouched. Keep generic HTML
snapshot guidance, `.verified.html`, and `Verify(html, extension: "html")`.

- [ ] **Step 3:** `npm run eject -- deniz-dotnet-general snapshot-testing --patch`

- [ ] **Step 4: Manifest:** retain the existing invocation and add the patch mode:

```yaml
  - source: dotnet-skills/skills/snapshot-testing
    invocation: auto # general Verify knowledge: API, HTTP, serialization, objects, rendered output
    body: patch # email-authoring branches and their dead handoff are outside the curator's flow
```

- [ ] **Step 5:** Run `npm run build && npm run validate`; require `0 error(s)` and no new warning identity. Confirm the emitted skill has no `Email Template Testing`, `Integration with MJML`, `EmailTests`, `WelcomeEmail`, `PasswordReset`, or `transactional-emails` match. Confirm its API-surface, HTTP-response, serialization, scrubbing, CI, rendered-HTML, and Verify lifecycle sections remain.

- [ ] **Step 6: Commit**

```bash
git add curation/deniz-dotnet-general.yaml overlays plugins opencode docs/ledger.json
git commit -m "feat: keep snapshot testing general-purpose"
```

---

### Task 12: Complete the testcontainers patch (T2)

**Files:**
- Modify: `overlays/deniz-dotnet-general/testcontainers-integration-tests/overlay.patch` (regenerated)
- Modify: `curation/deniz-dotnet-general.yaml` (comment only)

- [ ] **Step 1: Preserve and prove the existing patch before re-laying the working copy**

```powershell
$overlay = "overlays/deniz-dotnet-general/testcontainers-integration-tests"
$savedPatch = Join-Path ([System.IO.Path]::GetTempPath()) "testcontainers-integration-tests.$([guid]::NewGuid()).overlay.patch"

if (-not (Test-Path "$overlay/overlay.patch")) { throw "existing overlay.patch is missing" }
Copy-Item "$overlay/overlay.patch" $savedPatch -ErrorAction Stop
try {
    npm run eject -- deniz-dotnet-general testcontainers-integration-tests --patch --force
    if ($LASTEXITCODE -ne 0) { throw "fresh eject failed" }

    git apply --check -p1 --directory=$overlay $savedPatch
    if ($LASTEXITCODE -ne 0) { throw "the saved baseline patch no longer applies cleanly" }

    git apply -p1 --directory=$overlay $savedPatch
    if ($LASTEXITCODE -ne 0) { throw "reapplying the saved baseline patch failed" }

    # Phase 2 diffs every working-copy file. Leaving the old artifact here would make the new patch
    # contain an addition of overlay.patch itself.
    Remove-Item "$overlay/overlay.patch"
}
finally {
    Remove-Item $savedPatch -ErrorAction SilentlyContinue
}
```

Expected: both `git apply --check` and `git apply` succeed silently, the working files carry the saved edits, and the old `overlay.patch` is absent. Do not continue to the new edits unless all three conditions hold.

- [ ] **Step 2: The three completions, in the working copy**

`infrastructure-patterns.md`:

| Old | New |
|---|---|
| `_network = new TestcontainersNetworkBuilder()` | `_network = new NetworkBuilder()` |

`database-patterns.md`:

| Old | New |
|---|---|
| `using Testcontainers;` | `using DotNet.Testcontainers.Builders;`<br>`using DotNet.Testcontainers.Containers;` |
| `private readonly IContainer _container;` (only in the `MigrationTests` class, where `InitializeAsync` assigns it) | `private IContainer _container = null!;` |

- [ ] **Step 3: Add the module-packages section to `SKILL.md`**, inserted immediately before the `## Common Issues and Solutions` heading:

````markdown
## Prefer Module Packages for Known Images

Testcontainers ships typed module packages for common infrastructure —
`Testcontainers.PostgreSql`, `Testcontainers.MsSql`, `Testcontainers.Redis`,
`Testcontainers.RabbitMq`. Prefer them over the generic `ContainerBuilder`: they pin a
sensible image, expose `GetConnectionString()`, and remove the port/wait-strategy
boilerplate shown in this skill's generic examples.

```csharp
// Testcontainers.PostgreSql
using Testcontainers.PostgreSql;

var postgres = new PostgreSqlBuilder().Build();
await postgres.StartAsync();
var connectionString = postgres.GetConnectionString();
```

The generic `ContainerBuilder` examples remain the right tool for custom or unlisted images.
````

- [ ] **Step 4: Recut and verify the emitted text**

```bash
npm run eject -- deniz-dotnet-general testcontainers-integration-tests --patch
npm run build && npm run validate
```

Expected: `0 error(s)` and no new warning identity. Then confirm no stale API name survives:
`Select-String -Path plugins/deniz-dotnet-general/skills/testcontainers-integration-tests/*.md -Pattern 'TestcontainersBuilder|TestcontainersContainer\b|TestcontainersNetworkBuilder|using Testcontainers;'` → no matches.
Inspect the regenerated patch headers and confirm none targets `overlay.patch`.

- [ ] **Step 5: Compile the three corrected API shapes.** Create a disposable `net8.0` console project under `[System.IO.Path]::GetTempPath()`, run `dotnet add package Testcontainers` and `dotnet add package Testcontainers.PostgreSql`, and replace its `Program.cs` with:

```csharp
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;

_ = new NetworkBuilder().Build();
IContainer container = null!;
_ = new PostgreSqlBuilder().Build();
```

Run `dotnet build` and require success. This is an API compile check, not a textual scan. Delete the disposable project in a `finally` block; if compilation fails, fix the overlay rather than weakening the check.

- [ ] **Step 6: Update the manifest comment** — extend the testcontainers item's comment to read: `# ... the patch rewrites every builder example (containers AND networks) to the post-3.0 API, fixes the` `# non-compiling MigrationTests/Respawn fixtures, and fronts the typed module packages`.

- [ ] **Step 7: Commit**

```bash
git add curation/deniz-dotnet-general.yaml overlays plugins opencode docs/ledger.json
git commit -m "fix: testcontainers patch completes the 3.x modernization it claimed"
```

---

### Task 13: ROADMAP additions (T6, T8)

**Files:**
- Modify: `docs/ROADMAP.md` (Deferred section)

- [ ] **Step 1:** Update the document's `Date:` to `2026-08-05`.

- [ ] **Step 2: Add two bullets to the Deferred section**

```markdown
- **Own TUnit test-writing skill.** The corpus's only test-writing skill was MSTest-bound and is
  excluded; the general module deliberately ships no test-writing knowledge, and its audit bodies
  no longer name a test-writing destination. A TUnit-first original skill under `skills/` needs a
  separate authoring session; that session decides its scope rather than this curation wave.
- **`expects` — manifest-side guard for bare-name edges in untouched bodies.** Design only if
  load-bearing bare-name edges accumulate in bodies there is no other reason to patch; today's
  single case (`grill-me` → `grilling`) stays deliberately unguarded. Decide the guard's shape only
  after that trigger is met.
```

- [ ] **Step 3: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: defer TUnit-writing skill and the expects guard with explicit triggers"
```

---

### Task 14: Wave close-out

- [ ] **Step 1: Full gates**

Run: `npm test && npm run typecheck && npm run build && npm run validate`
Expected: tests pass (no `tools/` change was made — this confirms it), validate reports `0 error(s)`, and warning identities match the pre-wave baseline.

- [ ] **Step 2: Ledger review**

`git diff <wave-start>..HEAD -- docs/ledger.json` must show only these semantic changes:

- Removed entries: `grade-tests`, `exp-mock-usage-analysis`, `exp-test-maintainability`, `find-untested-sources`, `migrate-vstest-to-mtp`, `migrate-xunit-to-xunit-v3`, `mjml-email-templates`, `migrate-dotnet8-to-dotnet9`, `migrate-dotnet9-to-dotnet10`.
- Invocation changes to `manual`: `convert-to-cpm`, `generate-testability-wrappers`, `migrate-static-to-wrapper`, `dotnet-trace-collect`, `dump-collect`, `migrate-nullable-references`, `thread-abort-migration`, `dotnet-aot-compat`.
- New `body: patch`: `run-tests`, `mtp-hot-reload`, `test-anti-patterns`, `test-gap-analysis`, `coverage-analysis`, `dotnet-webapi`, `test-analysis-extensions`, `snapshot-testing`.
- Description changes: `filter-syntax`, `platform-detection`, `run-tests`, `test-anti-patterns`, `test-gap-analysis`, `coverage-analysis`, `dotnet-webapi`, `test-analysis-extensions`, `snapshot-testing`.
- Model facts and matching `depends_on`: `run-tests` → `filter-syntax`, `mtp-hot-reload`, `platform-detection`; `mtp-hot-reload` → `filter-syntax`, `platform-detection`; `test-anti-patterns` → `run-tests`, `test-analysis-extensions`; `test-gap-analysis` → `test-analysis-extensions`, `test-anti-patterns`; `dotnet-webapi` → `database-performance`, `efcore-patterns`.

Anything else is a finding — stop and report.

- [ ] **Step 3: Derived manifest census**

Run `npm run inventory` and require `git diff --exit-code -- docs/inventory.md`; selection output must already be current from Task 1. Then run:

```powershell
@'
import { readFileSync } from "node:fs";
import { parse } from "yaml";

const manifest = parse(readFileSync("curation/deniz-dotnet-general.yaml", "utf8"));
const ledger = JSON.parse(readFileSync("docs/ledger.json", "utf8"));
const live = manifest.items.filter((item) => item.exclude !== true);
const excluded = manifest.items.filter((item) => item.exclude === true);
const entries = Object.entries(ledger).filter(([key]) => key.startsWith("deniz-dotnet-general/"));
const ledgerSources = new Set(entries.map(([, entry]) => entry.source));

const census = {
  auto: live.filter((item) => item.invocation === "auto").length,
  both: live.filter((item) => item.invocation === "both").length,
  manual: live.filter((item) => item.invocation === "manual").length,
  agents: live.filter((item) => item.source.includes("/agents/")).length,
  excluded: excluded.length,
};
console.log(JSON.stringify(census, null, 2));

if (entries.length !== live.length) throw new Error("manifest/ledger live-item count mismatch");
for (const item of live) {
  if (!ledgerSources.has(item.source)) throw new Error(`live source absent from ledger: ${item.source}`);
}
for (const item of excluded) {
  if (ledgerSources.has(item.source)) throw new Error(`excluded source still in ledger: ${item.source}`);
}
'@ | node --input-type=module -
```

Record the emitted census in the execution report, not in hand-written planning prose. The command must exit cleanly.

- [ ] **Step 4: Patch inventory**

`git diff <wave-start>..HEAD --name-status -- overlays/deniz-dotnet-general` may add patch artifacts only for `run-tests`, `mtp-hot-reload`, `test-anti-patterns`, `test-gap-analysis`, `coverage-analysis`, `dotnet-webapi`, `test-analysis-extensions`, and `snapshot-testing`; the existing `testcontainers-integration-tests` patch is the only modified overlay. No other overlay may change.

- [ ] **Step 5: Excluded-reference and edge sweep**

Derive the current taken, excluded, and never-curated sets mechanically with the playbook; do not substitute a copied alternation from this plan. Run Scans 1, 2, and 3 over the derived module paths. Expected: no retained Scan-1 artifact-reference finding and no audience mismatch introduced by the wave; every namespaced reference introduced by this wave resolves and has the declared edge recorded in the ledger. Record and drop framework APIs, ordinary product words, and other false positives using the playbook's classification rules.

- [ ] **Step 6: Worktree scope**

`git status --short` and `git diff <wave-start>..HEAD --name-only` may contain only the planned curation manifest, the overlay names listed in Step 4, generated output, `docs/inventory.md`, ledger/marketplace output, and `docs/ROADMAP.md`. Stop and report any unrelated path; do not absorb it into a wave commit.
