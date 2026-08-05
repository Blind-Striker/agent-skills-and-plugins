# S13 plan review - GLM / OpenCode Go

Date: 2026-08-05
Reviewer: GLM-5.2 (max effort, `opencode-go/glm-5.2`), dispatched as an independent reviewer.
Lineage note: independent review performed before reading
`2026-08-05-s13-plan-review-openai.md`; OpenAI findings cross-verified afterward against live files.

Planning scratch (AGENTS.md, Documentation Hygiene): delete when the reviewed work merges.

## Verdict

The review does not pass as written. Wave A cannot satisfy its own zero-excluded-reference target
(F1, F4), the patch-extension ceremony ships `overlay.patch` in the output (F3), and
promotion-on-contact is incomplete for live edges (F2). The ledger-delta arithmetic is miscounted
(F5), and the rewritten manifest header overclaims "free of coercion" (F6). Findings F1, F2, F3, F4
block execution; F5–F10 should be corrected before the user gives the go. The curator retains the
go decision.

## Ranked findings

Evidence is `file:line` against the current pinned tree unless noted.

### High

**F1 — Task 6 misses two body-level "use X" redirects to newly-excluded items.**
`external/dotnet-agent-skills/plugins/dotnet-test/skills/test-anti-patterns/SKILL.md:90` ships
`For a deep mock audit in .NET, use ` `` `exp-mock-usage-analysis` `` `.` and `:100` ships
`For a detailed duplication analysis in .NET, use ` `` `exp-test-maintainability` `` `.` Both targets
are flipped to `exclude: true` by Task 1, and Task 6 ("test-anti-patterns patch") edits only the
"When Not to Use" bullet block (`:38`–`:45`) — it never touches the "Common Anti-Patterns" table rows.
Spec T3 (`docs/superpowers/specs/2026-08-04-t-items.md:21`) says *every* reference from a kept body
to an excluded item is rewired or removed; the manifest comments
(`curation/deniz-dotnet-general.yaml:144-147`) record that test-anti-patterns carries the core job
for both. The plan's own Task 10 Step 6 scan list (which includes `exp-mock-usage-analysis` and
`exp-test-maintainability`) would catch these, but the plan authorizes no fix — the executor would
hit a false alarm at the gate.

**F2 — Promotion-on-contact is incomplete for live (taken-item) references.**
The plan's Global Constraint (`docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:16`) and
spec T3 require bodies touched by Wave A patches to promote their *live* references to namespaced
spelling + `depends_on`. The plan only promotes the reference-husk edges and leaves these bare
`use X` redirects in patched bodies:

- `run-tests/SKILL.md:37` — `use ` `` `mtp-hot-reload` `` ``; Task 4 edits this body but does not
  namespaced-promote; Task 4's `depends_on: [filter-syntax, platform-detection]` omits
  `mtp-hot-reload`.
- `test-anti-patterns/SKILL.md:42` — `use ` `` `run-tests` `` ` for .NET`; Task 6 edits this body;
  Task 6's `depends_on: [test-analysis-extensions]` omits `run-tests`.
- `test-gap-analysis/SKILL.md:40` — `use ` `` `test-anti-patterns` `` ``; Task 7 edits this body;
  Task 7's `depends_on: [test-analysis-extensions]` omits `test-anti-patterns`.

All three targets are taken (`curation/deniz-dotnet-general.yaml:127,135,138`), so these are live
model-edges that ADR-0008 says must be declared in both directions. They remain invisible
candidate-tier edges contrary to T3 and the plan's own constraint.

**F3 — Task 11's patch-extension ceremony self-includes the old patch.**
`npm run eject -- ... --patch --force` runs `cpSync(src, dest, {recursive})` (`tools/eject.ts:206`)
into a directory that already contains `overlay.patch` from the previous Phase 2 cleanup
(`tools/eject.ts:263-268` leave `PATCH_FILE` in place). `cpSync` merges rather than replaces, so
`overlay.patch` survives alongside the fresh upstream files. After Step 1's `git apply`, the working
copy holds the patched files *plus* `overlay.patch`. Step 4's recut (`tools/eject.ts:215-217`)
snapshots both sides into `a/` and `b/`; `b/` carries `overlay.patch`, `a/` does not, so the
regenerated `git diff --no-index a b` emits an `+++ b/overlay.patch` "new file" hunk. `checkPatch`
(`tools/lib/overlay.ts:143-145`) only verifies the patch applies — a new-file hunk applies cleanly —
so the build later creates `plugins/deniz-dotnet-general/skills/testcontainers-integration-tests/overlay.patch`
in the shipped output. The existing test
(`tools/build.test.ts:381`) only covers the gamma fixture (first-cut clean), so `npm test` does not
catch this. Fix: `rm` (or move) `overlay.patch` from the overlay directory after Step 1's `git apply`
and before Step 4's recut.

**F4 — Task 10 Step 6 scan list is incomplete; the zero-matches expectation is false.**
The hand-maintained 14-name alternation at
`docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:469` omits at least
`verify-email-snapshots`, which survives as a body redirect at
`plugins/deniz-dotnet-general/skills/mjml-email-templates/SKILL.md:21`
(`` `testing/verify-email-snapshots` `` "); no Wave A task edits `mjml-email-templates`.
`test-tagging` (also excluded, also omitted from any patch) survives three ways: in the
test-analysis-extensions body at `external/.../test-analysis-extensions/SKILL.md:57`
(`**Tag support** (for ` `` `test-tagging` `` ` skill)`), and in `## Tag/Trait Attributes (for
` `` `test-tagging` `` `)` headings across all eleven per-language extension tables
(`external/.../extensions/{dotnet,python,typescript,java,go,ruby,rust,swift,kotlin,powershell,cpp}.md`,
lines ~103–130, verified by `rg`). The playbook's false-positive discipline
(`docs/agents/reference-audit-playbook.md:74-83`) only authorizes dropping "foreign-framework API"
hits — it does not cover backticked artifact names in section headings, so the executor has no
authority to drop these 11 heading hits. Step 6's "zero matches" claim is contradicted by ≥12
surviving hits across the plugin. Compounding F1, the scan would also flag the two
test-anti-patterns hits it would otherwise be expected to find.

### Medium

**F5 — Spec and plan both miscount the patch/edge ledger delta.**
`docs/superpowers/specs/2026-08-04-t-items.md:48` says "new fact edges + `depends_on` for the six
patched items"; `docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:591` says "six items
gained `body: patch`". Both are wrong. Tasks 4–10 create **seven** new `body: patch` items
(run-tests, mtp-hot-reload, test-anti-patterns, test-gap-analysis, coverage-analysis, dotnet-webapi,
test-analysis-extensions). Only **five** gain `depends_on` + fact edges (the two that don't —
coverage-analysis and test-analysis-extensions — only *remove* references to excluded items; they
add no namespaced model-edge). Adding F2's three missing live-edge declarations changes the five
further. An executor following the gate literally would stop on a false alarm.

**F6 — The replacement manifest header's "free of coercion" claim is false.**
Wave A Task 2 Step 2
(`docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:92-97`) writes that auto
descriptions are "honest, trigger-shaped, and free of coercion, so model selection is safe." But
`run-tests/SKILL.md:4` carries `ALWAYS use when the user` and `test-anti-patterns/SKILL.md:6`
carries `INVOKE whenever asked to audit or review tests`. Task 4's description edit removes only
the `code-testing-agent` redirect; Task 6's description edit never touches the `INVOKE whenever`
clause. T5 (`docs/superpowers/specs/2026-08-04-t-items.md:23`) authorized dropping the *false*
"none carries disable-model-invocation" claim and the *upstream-fidelity* rationale — it did not
authorize removing coercive trigger wording from kept descriptions, and the curator approved policy,
not each sentence (handover `s13-plan-review-and-wave-execution.prompt.md:98-101`). Either soften
the header ("trigger-shaped", drop "free of coercion") or add `frontmatter.description` overrides
for run-tests and test-anti-patterns.

**F7 — Two claimed capability absorptions are description-only (body does not deliver).**
Task 6's description (`docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:262`) adds
`shallow or low-variety assertions, or named test smells from the testsmells.org catalog` to
test-anti-patterns's USE-FOR line. The body's "Common Anti-Patterns" table
(`external/.../test-anti-patterns/SKILL.md:85-109`) has no assertion-variety *metrics* workflow
(counting/grading assertion diversity — `assertion-quality`'s actual job) and no formal 19-smell
*catalog* (`test-smell-detection`'s actual job); it stays a pragmatic, named-by-name anti-pattern
table. Task 7 then routes test-gap-analysis's `User wants to measure assertion variety (use
` `test-anti-patterns` `)` at a body that does not deliver a metrics workflow. Separately, Task 8's
coverage-analysis body edit claims "in scope: run the pipeline below and filter the report to the
named method or class," but `Compute-CrapScores.ps1` has `-TopN` only and no target filter
(`-[Target]Method|Class`) — the absorbed "targeted single-method CRAP" job is delivered via
project-wide-run-then-manually-filter, a heavier workflow than `crap-score`'s single-file scope. The
curator approved consolidation (manifest comments at `curation/deniz-dotnet-general.yaml:217-219,
214`); the trigger wording is non-coercive; but ADR-0007's honest-trigger rule is about selection
wording matching body capability, and these descriptions overclaim what the bodies deliver.

**F8 — The playbook's Scan 1 is not operationally reliable as written.**
My own end-to-end walk (alternation of the 80 currently-excluded names) produced ≥131 hit lines,
dominated by false positives: the excluded agent name `msbuild` matched `dotnet msbuild` CLI
invocations and `msbuild-antipatterns`/`msbuild-modernization` taken-skill prefixes, and `build-perf`
(excluded agent) matched `build-perf-diagnostics`/`build-perf-baseline` (taken skills), because
`rg -w` treats `-` as a word boundary for hyphenated names. The playbook's
`docs/agents/reference-audit-playbook.md:36` alternation needs escaped token boundaries (e.g.
`(?<![A-Za-z0-9-])name(?![A-Za-z0-9-])`) and a more mechanical artifact-context filter than "looks
namespaced." Its name derivation also has gaps: "Taken: keys of `docs/ledger.json`"
(`docs/agents/reference-audit-playbook.md:24`) yields composite keys like
`deniz-process/skill/teach`, not bare output names; and `writeLedger` iterates manifest items only
(`tools/lib/ledger.ts:69-74`), so own skills under `skills/` would be missing the day one exists
(none today). Wave A's hand-maintained 14-name list at `wave-a:469` contradicts the playbook's
"Never hand-maintain these lists; derive them fresh each run" (`reference-audit-playbook.md:22-23`).

**F9 — Wave B's tests do not enforce its stated contract.**
Task 1's new tests (`docs/superpowers/plans/2026-08-04-wave-b-opencode-stub-commands.md:28-98`)
cover `../SKILL.md` and bare `SKILL.md` repoints but not the actual `./SKILL.md` spelling that
`teach/RESOURCES-FORMAT.md` uses (the real dead-link case). The implementation regex at
`wave-b:129` — `(\]\((?:\.\.\/|\.\/)?)SKILL\.md` — matches at most one `../` or `./` prefix, so
`../../SKILL.md` climbs would silently slip through (none exist today, but the contract claims
generality). The tests do not assert: both explicit mount paths in the stub, absence of `@`-file
syntax, byte-identical bundle-less `manual` output, or unchanged `both` output (the plan's
Global Constraint `wave-b:13` promises byte-identical). The RED expectation at `wave-b:108-109`
also undercounts: Task 1 Step 3 (`wave-b:104`) adds a `BODY.md` existence assertion to the
`invocation sets the Claude flags` test at `tools/build.test.ts:115`, which becomes a fourth
failing test before implementation, beyond the "two new + one updated" the plan describes. A
runtime smoke of the stub under the supported mounts is also absent.

**F10 — T2 "no non-compiling example survives" has no compiling gate.**
Task 11 Step 3 (`docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:521-526`) adds a
snippet using `PostgreSqlBuilder().Build()` with only a `// Testcontainers.PostgreSql` comment —
no `using Testcontainers.PostgreSql;`. Step 4's verification
(`wave-a:539`) greps only for removed legacy API names (`TestcontainersBuilder|...|using Testcontainers;`),
so the new snippet's missing import is not checked. Either compile representative snippets or
narrow T2 from "no non-compiling example survives" (`wave-a:538`) to the specific
readonly-assignment and pre-3.0-API defects actually checked.

### Low

**F11 — Several Wave A old-string anchors span YAML-folded description newlines.**
The `run-tests` description wraps `writing test code (use` + `code-testing-agent),` across two
lines and Task 4 (`wave-a:175`) correctly denotes this with a `+`. Tasks 6, 8, 9, 10 do not:
`test-anti-patterns/SKILL.md:11-12` has `duplicated tests, or magic` / `values — in .NET` (Task 6
old-string `duplicated tests, or magic values` with a literal space), `coverage-analysis/SKILL.md:9-13`
wraps `where to add` / `tests, coverage analysis...` (Task 8 old-string with spaces),
`dotnet-webapi/SKILL.md:9-10` wraps `EF Core data access or query` / `optimization (use` (Task 9),
`test-analysis-extensions/SKILL.md:4-6` and `:9-13` wrap (Task 10). None of these old-strings match
the upstream files as literal substrings. Now-blocking, but the executor must adapt each by
searching for the wrapping fragments rather than the literal string.

**F12 — Wave B has no explicit ledger-diff review gate.**
The cross-session rule (`s13 handover:137-139`) says "Ledger diff review at Wave A close-out is a
gate, not a formality." Wave A Task 13 Step 2 (`wave-a:589-591`) calls for it; Wave B has no
equivalent close-out task. The ledger will change: `opencode.parked` gains `BODY.md` for every
bundled `manual` item across both plugins (deniz-dotnet-general's 7 flipped items + deniz-process's
teach/writing-great-skills/writing-skills/triage/etc., verified via
`tools/lib/ledger.ts:97-98,130`). The plan's Task 2 Step 2 (`wave-b:158`) acknowledges a
`ledger.test.ts` adjustment but does not treat the real-tree ledger diff as a gate.

**F13 — Wave B documentation follow-through leaves contradictions.**
(a) Wave B Task 4 Step 3 (`wave-b:214`) replaces only the final pointer clause of the measured
behavior bullet at `docs/research/skill-invocation-across-harnesses.md:239-243`, leaving
`long-body manual conversions are noisy` immediately before `bundled manual conversions now emit
a stub that points at the parked body instead.` — the stale "noisy" clause should narrow to
`both` items + bundle-less manual. (b) The `emitOpenCodeSkill` JSDoc at `tools/build.ts:462-466`
says "Their references are not rewritten yet: the spelling depends on which mount point is
supported, which is an open curation decision, so the drop is reported." Wave B makes the
curation decision and rewrites them; the JSDoc is not in any task's edit list. (c) Wave A Task 12's
TUnit ROADMAP bullet (`wave-a:560-565`) pre-decides the deferred skill's scope
(`…Verify and Testcontainers wiring, with occasional-xUnit notes`) beyond the curator's T6 ruling
(`wave-a spec:24`), which deferred the skill *and* its authoring session.

### Observations (not blocking, not findings)

- The patch-extension ceremony (Task 11 Step 1) is sound in principle: submodule pins are dated
  2026-07-29 (`git log -1 external/dotnet-skills` → `2026-07-29 22:14:24 +0300`), predating the
  2026-08-04 spec, so patch context still matches; `cpSync` merges (preserves `overlay.patch`);
  `git apply -p1 --directory=…` from the repo root is exactly the form `tools/lib/overlay.ts:100-109`
  documents as the in-tree-correct invocation. **But F3 above must be fixed first.**
- The Wave B `bundledManual` implementation (traced through `tools/build.ts:476-518` and the plan's
  Task 2 Step 1) does correctly preserve byte-identical behavior for bundle-less `manual` (husk
  removal → `!existsSync(destSkill)` → `bundledManual` false → inline body) and for `both`
  (`wantsSkill` true → `bundledManual` false → inline body, no BODY.md). The stub spells both mount
  paths in prose, consistent with the research finding that `@` resolves project-root-only
  (`skill-invocation-across-harnesses.md:144-154`).
- The link-repoint regex covers exactly the two parked-link cases that trigger validate dead-link
  warnings today: `teach/RESOURCES-FORMAT.md:29` (`[SKILL.md](./SKILL.md)`) and
  `writing-great-skills/GLOSSARY.md:3` (`[…](SKILL.md)`). The other three parked files mentioning
  `SKILL.md` (`triage/AGENT-BRIEF.md:93`, `writing-skills/anthropic-best-practices.md`,
  `writing-skills/examples/CLAUDE_MD_TESTING.md:95`) carry only prose/code mentions, not `](…SKILL.md`
  link syntax, so the regex correctly leaves them alone. Post-Wave-B `0/4` validate arithmetic holds
  — assuming F3 and F4-class issues do not add warnings.

## OpenAI Claim Verification

| # | OpenAI claim (summary) | Verdict | Independent evidence |
|---|---|---|---|
| 1 | Wave A can't satisfy zero-ref target: exp-mock-usage-analysis + exp-test-maintainability at test-anti-patterns:90,100; test-tagging in ext body + 11 ext tables; mjml-email-templates:21 → verify-email-snapshots; Step 6 list omits verify-email-snapshots | **Confirmed** | I independently found the two test-anti-patterns misses (F1) and the 11 extension-table `## Tag/Trait Attributes (for `test-tagging`)` hits. Verified `plugins/.../test-analysis-extensions/SKILL.md:57` body hit verbatim and `plugins/.../mjml-email-templates/SKILL.md:21` (` `testing/verify-email-snapshots` `) verbatim. Confirmed the Step 6 alternation at `wave-a:469` omits `verify-email-snapshots`. I had missed the body:57 hit and the mjml-email-templates redirect independently. |
| 2 | Task 11 recut self-includes old overlay.patch; eject.ts:196-207 preserves it, :215-217 snapshots it | **Confirmed** | Traced eject.ts Phase 1 (`:196-210`) cpSync-into-existing-dir (preserves `overlay.patch` because `cpSync recursive` merges and `listFiles` excludes `PATCH_FILE` so Phase 1 fires), and Phase 2 (`:215-217`) `cpSync(dest → b/)` carrying `overlay.patch` while `a/` (fresh upstream) lacks it → diff emits an added-file hunk. `checkPatch` would not catch a new-file hunk. The build would ship `overlay.patch` in the output. I missed this independently. |
| 3 | Two claimed absorptions description-only; bodies gain neither metrics workflow nor testsmells.org catalog; coverage-analysis script has no target filter | **Partly confirmed** | Verified test-anti-patterns body (`:85-109`) has no metrics/counting workflow and no 19-smell catalog. Verified `Compute-CrapScores.ps1:17-20` has `-TopN` only, no `-TargetMethod`/`-TargetClass`. But the curator approved consolidation (`curation:217-219,214`), the trigger wording is non-coercive, and ADR-0007's honest-trigger rule is about selection wording — so the gap is real (F7) but I rate it Medium, not High; the deeper body-delivery concern is runtime, not compile-time. |
| 4 | Promotion-on-contact incomplete: run-tests:37 → mtp-hot-reload, test-anti-patterns:42 → run-tests, test-gap-analysis:40 → test-anti-patterns; depends_on omits them | **Confirmed** | Verified all three lines verbatim (`run-tests/SKILL.md:37`, `test-anti-patterns/SKILL.md:42`, `test-gap-analysis/SKILL.md:40`) and the proposed `depends_on` lists at `wave-a:194,236,286,333,421` none include those targets. I missed this independently because I only checked excluded-item redirects, not live-edge promotion. |
| 5 | New header's "free of coercion" false: run-tests:4 "ALWAYS use", test-anti-patterns:6 "INVOKE whenever" | **Confirmed (run-tests) / Partly (test-anti-patterns)** | `run-tests/SKILL.md:4` has `ALWAYS use when the user` — imperative coercion, definitely contradicts "free of coercion". `test-anti-patterns/SKILL.md:6` has `INVOKE whenever asked to audit or review tests` — arguably honest trigger phrasing (names the condition), not coercion, but a header that says "free of coercion" should not rely on that distinction. Either way the header overclaims (F6); T5 did not authorize removing either. |
| 6 | Playbook Scan 1 not reliable: 131 hits dominated by false positives; needs token boundaries; name derivation ignores item-level `name:` and own skills; Wave A hand-maintains a partial list | **Confirmed** | My own Scan 1 walk produced the same false-positive flood (`msbuild` matching `dotnet msbuild` commands and `msbuild-antipatterns` taken-skill prefixes; `build-perf` matching `build-perf-diagnostics`/`build-perf-baseline` taken skills, because `rg -w` treats `-` as a word boundary). The playbook's "Taken: keys of docs/ledger.json" yields composite keys (`deniz-process/skill/teach`), not bare names. `writeLedger` at `tools/lib/ledger.ts:69-74` iterates manifest items only. F8 stands. |
| 7 | Wave B tests don't enforce contract: no ./SKILL.md, no ../../, no both mount paths, no absence of @, no byte-identical bundle-less/both; regex supports ≤1 ../; RED undercounts (4 failing, not 3) | **Confirmed** | Task 1 Step 2 test uses `../SKILL.md` and bare `SKILL.md` — not `./SKILL.md` (the actual teach case). Regex at `wave-b:129` `(?:\.\.\/|\.\/)?` matches ≤1 prefix. Task 1 Step 3 (`wave-b:104`) adds a `BODY.md` assertion to the `:115` test → fourth failing test, but Step 4 (`wave-b:108-109`) describes only two new + one updated. F9 stands. |
| 8 | Ledger arithmetic wrong: 7 new body:patch (not 6); only 5 have fact edges + depends_on | **Confirmed (matches my independent finding F5)** | Counted Tasks 4–10 = 7 new `body: patch` items. `depends_on` appears in only run-tests, mtp-hot-reload, test-anti-patterns, test-gap-analysis, dotnet-webapi (5). `spec:48` "six patched items" and `wave-a:591` "six items gained `body: patch`" both wrong. F5 matches OpenAI 8 exactly. |
| 9 | T2 "no non-compiling example survives" has no compile gate; PostgreSqlBuilder snippet lacks the using | **Partly confirmed** | Task 11 Step 3 snippet (`wave-a:521-526`) uses `PostgreSqlBuilder` with only a `// Testcontainers.PostgreSql` comment, no `using Testcontainers.PostgreSql;`. Step 4 verification (`wave-a:539`) greps only for removed legacy names. But the snippet is an illustrative fragment (common docs convention), not a full example — so the literal T2 claim overstates, but the missing-import concern is real. F10 stands at Low-Medium. |
| 10 | Doc follow-through contradictions: research bullet leaves stale "noisy" clause; emitOpenCodeSkill JSDoc not updated; TUnit ROADMAP bullet pre-decides scope | **Confirmed** | (a) `skill-invocation-across-harnesses.md:239-243` edit leaves "long-body manual conversions are noisy" before the fix clause. (b) `tools/build.ts:462-466` JSDoc says "references are not rewritten yet" — stale after Wave B; not in any task. (c) Wave A Task 12 TUnit bullet (`wave-a:560-565`) specifies "Verify and Testcontainers wiring, with occasional-xUnit notes" beyond the curator's "defer to authoring session" ruling. F13 stands. |

## Additional findings not in the OpenAI report

- **F11 (Low)** — Tasks 6/8/9/10 old-string anchors span YAML-folded description newlines that the
  plan renders as literal-space strings; Task 4 uses `+` to denote wrapping, the others don't, so
  those anchors won't match as literal substrings. OpenAI's "Verified Claims" asserts all anchors
  exist — true semantically, not literally.
- **F12 (Low)** — Wave B has no explicit ledger-diff review gate even though the ledger changes
  (BODY.md added to `opencode.parked` for every bundled `manual` item across both plugins).
- **Observation** — The patch-extension ceremony is sound in principle IF F3 is fixed; my read of
  `tools/lib/overlay.ts:100-109` confirms `git apply -p1 --directory=<rel>` from the repo root is the
  in-tree-correct form (the plan's command is exactly that).
- **Observation** — Wave B's `bundledManual` branch correctly preserves byte-identical behavior for
  bundle-less `manual` and `both` items (traced; not executed).

## Verified-sound claims and residual uncertainty

Confirmed against live files:

- Final count arithmetic: 65 taken = 51 `auto` / 3 `both` / 10 `manual` / 1 agent; 86 excluded.
  Independently re-derived from the manifest (current 55/15/0 invocation + 1 agent; after Task 1:
  −4 auto −2 both; after Task 2: −10 both +10 manual → 51/3/10/1+1=65). 80 → 86 excluded.
- Exactly seven of the ten flipped general items carry bundled files: convert-to-cpm (5),
  dotnet-trace-collect (5), dump-collect (3), migrate-dotnet8-to-dotnet9 (10), migrate-dotnet9-to-dotnet10
  (10), migrate-nullable-references (5), dotnet-aot-compat (1); the three bundle-less flips are
  generate-testability-wrappers, migrate-static-to-wrapper, thread-abort-migration. Verified by
  `Get-ChildItem -Recurse` on each upstream directory.
- The two parked-link cases are exactly `teach/RESOURCES-FORMAT.md` (`./SKILL.md`) and
  `writing-great-skills/GLOSSARY.md` (bare `SKILL.md`). Verified by `rg 'SKILL\.md'` over every
  parked bundle (the other three parked files mention `SKILL.md` only in prose/code, not link
  syntax).
- Baseline at review time: `npm test` → 134 pass / 0 fail; `npm run typecheck` → clean;
  `npm run validate` → 0 errors, 6 warnings (dotnet-devcert-trust ×2 aspire-namespace,
  elements-of-style, subagent-driven-development relative ref, teach dead-link,
  writing-great-skills dead-link).
- The `0/6` Wave A and `0/4` Wave B warning arithmetic is mechanically consistent — modulo F3/F4
  not introducing new warnings.

Residual uncertainty:

- I did not run `npm run build` (another reviewer is active and generated output is shared, per the
  task). All build-behavior claims (F3 recut shipping `overlay.patch`; Wave B byte-identical
  preservation) are reasoned from `tools/` reading, not executed. The handover explicitly flags the
  patch ceremony as "never executed"; F3 confirms the risk is real and the executor should dry-run
  the recut in isolation.
- A third review file `docs/superpowers/reviews/2026-08-05-s13-plan-review-xai.md` exists. I did not
  read it (the task only directed cross-verification against the OpenAI report, and independence was
  to be preserved). Cross-checking against xai is out of scope for this dispatch.

## Commands and checks actually run

- `git status --short`; `git log --oneline -8`; `git submodule status`; `git log -1 --format="%H %ci"
  external/dotnet-skills external/dotnet-agent-skills` (pins dated 2026-07-29, predating the spec).
- `rg`/`Select-String` against `external/dotnet-agent-skills/.../run-tests|/mtp-hot-reload|/test-anti-patterns|/test-gap-analysis|/coverage-analysis|/dotnet-webapi|/test-analysis-extensions/...` to
  verify each Wave A patch-task old-string anchor.
- `rg 'SKILL\.md'` over every parked OpenCode bundle to verify the link-repoint regex's coverage
  vs. prose-mention false positives.
- `rg` over `external/.../test-analysis-extensions/extensions/*.md` for `test-tagging` hits (11
  heading hits found across the 11 per-language files).
- `rg` over `plugins/deniz-dotnet-general/` for the 6 newly-excluded item names to find kept-body
  references — found the two test-anti-patterns misses (F1) independently.
- Full 80-name Scan 1 walk over `plugins/deniz-dotnet-general/` to verify the playbook procedure's
  false-positive profile (F8).
- `npm run validate` → 0 errors, 6 warnings (warning identities recorded above).
- `npm test` → 134 pass / 0 fail.
- `npm run typecheck` → clean.
- `node -e` against `docs/ledger.json` to confirm the `opencode.parked` field structure for a
  bundled `manual` item (`teach`).
- Counted `invocation: auto|both|manual` and `exclude: true` occurrences in
  `curation/deniz-dotnet-general.yaml`; counted bundled files in each of the 10 flipped items'
  upstream directories.
- Read `tools/eject.ts`, `tools/lib/overlay.ts`, `tools/build.ts:440-567`, `tools/build.test.ts`,
  `tools/testutil.ts`, all 8 ADRs, `docs/ROADMAP.md`, the spec, both plans, the playbook, and the
  invocation research.
