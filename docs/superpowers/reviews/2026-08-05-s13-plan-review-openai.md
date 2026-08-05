# S13 plan review - OpenAI

Date: 2026-08-05
Reviewer: GPT-5.6 Sol (OpenAI)

Planning scratch (AGENTS.md, Documentation Hygiene): delete when the reviewed work merges.

## Verdict

The review does not pass as written. Findings 1-5 and the Task 11 ceremony need correction
before the plans are execution-ready. The curator retains the go decision.

## Findings

1. **High: Wave A cannot satisfy its zero-reference target.** The plan leaves direct references
   to excluded artifacts: `exp-mock-usage-analysis` and `exp-test-maintainability` remain in
   `external/dotnet-agent-skills/plugins/dotnet-test/skills/test-anti-patterns/SKILL.md:90,100`;
   `test-tagging` remains once in the extension skill body and in all eleven extension tables;
   `mjml-email-templates` still redirects to excluded `verify-email-snapshots` at
   `plugins/deniz-dotnet-general/skills/mjml-email-templates/SKILL.md:21`. That is at least 15
   surviving artifact references. The hand-maintained pattern at
   `docs/superpowers/plans/2026-08-04-wave-a-general-curation.md:468-469` also omits
   `verify-email-snapshots`, so it can report clean while T3 and the spec's end state remain false.

2. **High: Task 11's patch-extension ceremony self-includes the old patch.** `--patch --force`
   copies upstream into the existing overlay directory but does not delete `overlay.patch`
   (`tools/eject.ts:196-207`). After the old patch is manually applied, the recut snapshots the
   whole directory (`tools/eject.ts:215-217`), including that retained `overlay.patch`, so the newly
   generated patch contains an added `overlay.patch` file. Delete or move the old patch after
   applying it and before the second eject.

3. **High: The two claimed capability absorptions are description-only.** The proposed
   `test-anti-patterns` wording claims assertion-diversity and named testsmells.org work, but its
   body gains neither the metrics workflow from `assertion-quality` nor the formal catalog from
   `test-smell-detection`. `test-gap-analysis` is then actively redirected to this nonexistent
   capability. Similarly, `coverage-analysis` is told to absorb targeted CRAP analysis, but
   `Compute-CrapScores.ps1` only emits top-N hotspots and has no target filter
   (`scripts/Compute-CrapScores.ps1:17-20,129`), while the workflow still mandates a project-wide
   report. It also drops the excluded skill's single-file scope. The curator approved
   consolidation, not these unsupported capability claims.

4. **High: Promotion-on-contact is incomplete.** The plan itself requires the first live reference
   to each target to become namespaced and declared, but patched bodies retain direct bare
   redirects: `run-tests` -> `mtp-hot-reload` at `run-tests/SKILL.md:37`,
   `test-anti-patterns` -> `run-tests` at `test-anti-patterns/SKILL.md:42`, and
   `test-gap-analysis` -> `test-anti-patterns` at `test-gap-analysis/SKILL.md:40`. Their proposed
   `depends_on` lists omit those targets. They remain invisible candidate edges contrary to T3 and
   ADR-0008.

5. **High: The replacement manifest header is still factually false.** It claims auto
   descriptions are "free of coercion" at `wave-a-general-curation.md:92-97`. Kept `run-tests`
   says "ALWAYS use" in its description (`run-tests/SKILL.md:4`), and the patched
   `test-anti-patterns` description retains "INVOKE whenever" (`test-anti-patterns/SKILL.md:6`).
   That stronger wording was not part of T5. Remove the claim or obtain a curator ruling to rewrite
   those descriptions.

6. **Medium: The playbook's core scan is not operationally reliable.** A literal end-to-end Scan 1
   on the live module derived 80 excluded names and produced 131 hit lines, dominated by false
   positives such as excluded agent name `msbuild` matching the product and `build-perf` matching
   taken `build-perf-*` skills. The unbounded alternation at
   `docs/agents/reference-audit-playbook.md:36` needs escaped token boundaries and a more mechanical
   artifact-context filter. Its name derivation also omits item-level `name:` precedence, treats
   composite ledger keys as names, and misses own skills because `writeLedger` only iterates
   manifest items (`tools/lib/ledger.ts:69-74`). Wave A then contradicts the playbook's "never
   hand-maintain" rule by supplying a partial list.

7. **Medium: Wave B's tests do not enforce its stated contract.** They do not cover the real
   `./SKILL.md` spelling, repeated parent climbs such as `../../SKILL.md`, both explicit mount paths,
   absence of `@`, byte-identical bundle-less manual output, or unchanged `both` output. The
   implementation regex at `wave-b-opencode-stub-commands.md:129` supports at most one `../`. The
   RED expectation is also wrong: adding the BODY assertion to the existing invocation test creates
   a fourth failing test, beyond the two new tests and changed report assertion described at lines
   106-109. A runtime smoke of the exact stub under the supported mounts is also absent.

8. **Medium: The ledger close-out arithmetic contradicts the plan.** Tasks 4 through 10 create
   seven new `body: patch` items, not six. Only five currently receive declared fact edges, while
   correcting promotion-on-contact adds more. Both
   `docs/superpowers/specs/2026-08-04-t-items.md:48` and Wave A Task 13 at line 591 describe a delta
   that cannot be the actual intended delta.

9. **Medium: T2's "compiling examples" claim has no compiling gate.** The new PostgreSQL module
   snippet uses `PostgreSqlBuilder` without `using Testcontainers.PostgreSql;` at
   `wave-a-general-curation.md:521-526`. The verification only greps for removed legacy names.
   Either compile representative snippets or narrow T2 from "no non-compiling example survives" to
   the specific readonly-assignment/API defects actually checked.

10. **Low: Documentation follow-through would leave contradictions.** The research edit at
    `wave-b-opencode-stub-commands.md:214` replaces only the final pointer clause, leaving
    "long-body `manual` conversions are noisy" immediately before saying bundled manual conversions
    now use stubs (`skill-invocation-across-harnesses.md:239-243`). The stale `emitOpenCodeSkill`
    JSDoc at `tools/build.ts:462-466` is not included in the plan. The proposed TUnit ROADMAP bullet
    also pre-decides Verify, Testcontainers, and xUnit scope beyond the curator-approved ruling to
    defer an own TUnit skill to a separate authoring session.

## Cross-review Addendum

The xAI and GLM reviews independently confirmed the numbered findings above and added two useful
corrections:

11. **Medium: Several description anchors are semantic matches, not literal exact strings.** Tasks
    6, 8, 9, and 10 present old strings with spaces where the pinned YAML folded descriptions carry
    physical newlines. That contradicts the plan's exact-string rule and can make a literal
    replacement silently no-op. The plan should either quote the real wrapped text or instruct the
    executor to edit the complete folded description block.

12. **Low: Wave B lacks a real-tree ledger-diff gate.** Adding `BODY.md` changes each bundled
    manual item's `opencode.parked` list through `tools/lib/ledger.ts:97-98,130`. Wave B mentions a
    possible ledger-test adjustment but never requires review of the generated `docs/ledger.json`
    delta before commit.

## Verified Claims

- All body-level anchor targets and the intended description text exist at the current submodule
  pins. Several description strings are not literal contiguous matches because YAML folding wraps
  them across lines; see the cross-review addendum.
- The final count arithmetic is correct: 65 taken = 51 auto / 3 both / 10 manual / 1 agent; 86
  excluded.
- Exactly seven flipped general items have bundles.
- None of those seven bundles links to its own `SKILL.md`.
- The two current parked-link cases are exactly `teach` using `./SKILL.md` and
  `writing-great-skills` using bare `SKILL.md`.
- The `0/6` Wave A and `0/4` Wave B validate-warning arithmetic is mechanically consistent, but
  validate cannot detect the surviving bare-reference defects above.
- Fresh baseline at review time: 134/134 tests passed, typecheck and build succeeded, and validate
  reported 0 errors and 6 warnings.
- No source, manifest, overlay, plan, or generated-output edit was made during the review.
