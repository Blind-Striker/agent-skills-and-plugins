# Design: s13 plan correction

Date: 2026-08-05

Planning scratch (AGENTS.md, Documentation Hygiene): delete when the reviewed work merges.

## Purpose

Correct the s13 spec, both wave plans, and the reference-audit playbook before any wave executes.
The curator has amended the selection boundary during correction: email-specific authoring and
version-to-version .NET migration guides are out of scope, while general Verify snapshot testing
and general-purpose .NET knowledge stay. This pass changes how the documents implement and verify
the rulings; it does not reopen the remaining invocation or consolidation decisions.

## Scope Boundary

This correction pass may edit only:

- `docs/superpowers/specs/2026-08-04-t-items.md`
- `docs/superpowers/plans/2026-08-04-wave-a-general-curation.md`
- `docs/superpowers/plans/2026-08-04-wave-b-opencode-stub-commands.md`
- `docs/agents/reference-audit-playbook.md`
- the s13 handover, ROADMAP, and review reports where their operational relays become stale

It does not edit manifests, overlays, tools, generated output, or upstream submodules. It does not
run either wave and does not commit. The corrected documents return to the curator for the go and
execution-mode decision.

## Absorption Decision

Use **minimal honest absorption**. The surviving skills perform the practical parts of the jobs the
curator retained, without importing the excluded skills' full dashboards or academic apparatus.

### Assertion and smell review

`test-anti-patterns` remains `body: patch`.

- Its description may claim pragmatic detection of shallow assertions and common test smells.
- Its body gains a bounded assertion-depth check: assertion-free tests, trivial-only assertions,
  repeated verification of only one facet, and missing checks for relevant state, structure, error,
  or side-effect behavior.
- It does not claim assertion-diversity metrics, a metrics dashboard, the full testsmells.org
  catalog, or academic citations.
- The direct handoffs to excluded `exp-mock-usage-analysis` and `exp-test-maintainability` are
  removed. Their practical over-mocking and duplication checks stay in the existing audit table.
- `test-gap-analysis` may route qualitative assertion-depth review to `test-anti-patterns`; it must
  not route a request to measure assertion diversity there.

No `merged_from` source is needed because no excluded body or catalog is copied.

## Email and Snapshot Boundary

The curated set does not carry an email-authoring workflow.

- Exclude `mjml-email-templates` with the reason that email-template authoring is not in the
  curator's workflow. `verify-email-snapshots` remains excluded and `mailpit-integration` remains
  never curated.
- Keep `snapshot-testing` because Verify is a general .NET technique for API surfaces, HTTP
  responses, serialization, complex objects, generated output, and rendered HTML.
- Change `snapshot-testing` to `body: patch`. Its description and trigger text claim rendered or
  generated output rather than rendered email.
- Remove the dedicated `Email Template Testing` and `Integration with MJML Email Testing` sections.
  The latter also removes the dead `aspnetcore/transactional-emails` artifact handoff.
- Recast the string/HTML example as a generic rendered HTML or report example. General HTML
  snapshot guidance, `.verified.html`, and `Verify(..., extension: "html")` remain useful.
- Replace the email-specific sample directory and the `Rendered HTML/emails` decision-table row
  with generic rendered-output examples.
- Do not scrub ordinary domain data merely because a DTO has an `Email` property. The boundary is
  email workflow knowledge, not the word in application data.

Use a patch rather than a full overlay: the Verify lifecycle, installation, API/HTTP/serialization,
scrubbing, CI, and best-practice sections remain valid and should continue to flow from upstream.

## Version Migration Boundary

Exclude `migrate-dotnet8-to-dotnet9` and `migrate-dotnet9-to-dotnet10` rather than converting them to
manual ceremonies.

- Their reference closures are files bundled inside their own skill directories, not separately
  curated skill dependencies. Excluding each parent therefore removes its version-specific closure
  without another manifest decision.
- The two skills refer only to each other as artifact handoffs. Excluding both closes that pair.
- Keep independently curated general-purpose skills such as EF Core, serialization, ASP.NET Core,
  MSBuild, cryptography, container, and interop guidance. Topic overlap with a version-migration
  reference file is not dependency ownership.
- Remove both items from the ceremony-flip task and replace any Wave B real-output example that
  names one of them with a retained bundled manual item.

## Source Pools and Categories

A submodule is a source pool, not a package boundary. Different manifests may curate different
items from the same upstream submodule. This is already the model for `dotnet-skills`: general .NET
items feed `deniz-dotnet-general`, while Akka.NET items feed `deniz-dotnet-akka`.

The later Akka curation session may therefore take the remaining `akka-*` skills and specialist
agent from `dotnet-skills`. Cross-cutting items such as Akka-on-Aspire need one deliberate home and
cross-plugin references where necessary; do not duplicate the same output identity across plugins,
because OpenCode emits one flat namespace.

Akka and Aspire candidates that are absent from the general manifest are not general-module
exclusions. Explicit `exclude: true` entries remain considered manifest decisions, including
deferred estates recorded for a future package.

Do not publish fixed taken/excluded totals in hand-written documentation. Derive current counts from
the manifests, inventory, or ledger at review and close-out time so later curation cannot make prose
stale.

### Targeted CRAP analysis

`coverage-analysis` remains `body: patch`, touching `SKILL.md` only.

- Its trigger may claim targeted method, class, or source-file CRAP analysis.
- A targeted branch reuses the existing coverage collection path.
- It invokes `Extract-MethodCoverage.ps1` with `-Filter all`, filters rows by the requested exact
  method, class, or file, and calculates CRAP for those rows from `Complexity` and `LineCoverage`
  with the formula already stated by the skill.
- The targeted response reports only the selected scope and says when the target is absent from the
  Cobertura data. It does not force the project-wide hotspot report for a targeted request.
- The project-wide workflow and scripts remain unchanged. No PowerShell overlay or `merged_from`
  declaration is needed.

## Wave A Corrections

### Reference closure

The corrected plan resolves every excluded-artifact reference in kept output, including the misses
found by the independent reviews:

- Remove the two experimental handoffs from `test-anti-patterns`.
- Remove `test-tagging` as an artifact name from the extension skill body and all eleven extension
  table headings; retain the framework tag lookup data under neutral headings.
- Add a `snapshot-testing` patch that removes its email-only branches and dead
  `aspnetcore/transactional-emails` handoff while retaining general rendered-HTML guidance.
- Keep the already planned removals and rewrites in `run-tests`, `mtp-hot-reload`,
  `test-gap-analysis`, `coverage-analysis`, `dotnet-webapi`, and the extension skill.

Every touched body promotes the first live reference to each taken target. The corrected manifest
shapes are therefore at least:

| Source item | `depends_on` after correction |
|---|---|
| `run-tests` | `filter-syntax`, `mtp-hot-reload`, `platform-detection` |
| `mtp-hot-reload` | `filter-syntax`, `platform-detection` |
| `test-anti-patterns` | `run-tests`, `test-analysis-extensions` |
| `test-gap-analysis` | `test-analysis-extensions`, `test-anti-patterns` |
| `dotnet-webapi` | `database-performance`, `efcore-patterns` |

`snapshot-testing` gains no `depends_on`: its patch removes a dead handoff and adds no live artifact
reference. The plan must re-derive this table from the final planned text before execution; it is
not authority for edges introduced by later wording changes.

### Header and author wording

- The header states only the approved rule: honest trigger descriptions permit automatic
  selection; mutating ceremonies are manual; the three approved `both` exceptions remain.
- It does not claim every automatic description is coercion-free.
- ROADMAP deferral of the own TUnit skill records the need and separate authoring session only. It
  does not pre-decide Verify, Testcontainers, xUnit, or other future scope.
- The deferred `expects` entry records the accumulation trigger and current unguarded composition;
  it does not pre-commit a field schema or validator behavior.

### Anchors and patch ceremony

- Exact-string instructions quote physical upstream wrapping. A folded YAML description is edited
  as a complete folded block or with explicit newline-aware anchors, never a normalized sentence
  described as an exact string.
- Task 12 re-lays the working copy, applies the old patch, and then removes the old
  `overlay.patch` before edits and recut. The plan verifies that no `overlay.patch` addition appears
  in the regenerated patch.
- The Testcontainers requirement is narrowed to the named legacy API and readonly-assignment
  defects unless the plan adds a real compiling-snippet gate. The module example includes the
  required `using Testcontainers.PostgreSql;` or a fully qualified builder name.

### Derived-state and ledger gate

Hand-written documents carry no fixed inventory totals. The close-out gate derives taken/excluded
and invocation counts from the final manifest and compares the ledger structurally against the wave
start.

The expected semantic delta is expressed as item identities rather than totals: the final cut list,
the retained ceremony-flip list, the new patch-mode item names, the exact fact/dependency edges, and
the description-bearing item names. The gate rejects every unrelated ledger entry change.

## Wave B Corrections

### Test contract

Tests precede emitter changes and cover all stated branches:

- Bundled `manual`: emits a short command, parks full body as `BODY.md`, keeps no `SKILL.md`, names
  project and global paths in prose, contains `$ARGUMENTS`, and uses no `@` path.
- Bundle-less `manual`: command output remains byte-identical to current inline behavior and leaves
  no parked directory.
- `both`: skill and command remain unchanged and no `BODY.md` is introduced.
- Link rewriting covers bare `SKILL.md`, `./SKILL.md`, `../SKILL.md`, and repeated parent climbs.
- The RED expectation names all failing tests, including the existing invocation test whose new
  `BODY.md` assertion fails before implementation.

The emitter regex accepts zero or more `../` segments or one `./` segment before `SKILL.md`. The
implementation writes `BODY.md` before collecting the parked-file report. The report names
`BODY.md` as the parked body but excludes it from the parenthetical bundle-file list.

### Verification and documentation

- Build and validate still target zero errors after the emitter change. The two dead-self-link
  warning identities retire; the unrelated standing identities remain and no new one appears.
- The real-tree ledger diff is a gate: every bundled manual item gains `BODY.md` in
  `opencode.parked`, with no unrelated semantic delta.
- The research behavior bullet is rewritten as a complete paragraph, narrowing the remaining wall
  paste to `both` and bundle-less manual items.
- `emitOpenCodeSkill` comments describe the new parked-body and rewritten-link behavior.
- A runtime smoke of the exact stub is recorded for the supported project-local and global mount
  forms before the Known Gap is declared retired. `OPENCODE_CONFIG_DIR` remains an experiment mount,
  not a distribution promise.

## Playbook Corrections

Name derivation is mechanical and preserves output identity:

- Taken names are the output-name segment of ledger keys, union emitted own-skill frontmatter names.
- Excluded names resolve in build order: item-level `name:`, scanner/frontmatter name, then source
  basename.
- Never-curated names come from inventory rows whose curated column is empty.
- Sets are made disjoint for the scan: a name that resolves to live taken output is not treated as
  excluded or never-curated merely because another source with that name was rejected.

The grep alternation escapes every name and uses token boundaries equivalent to
`candidateHits` in `tools/lib/refs.ts`; `rg -w` is insufficient for hyphenated names. The first pass
produces candidates only. A hit counts as an artifact reference when the sentence treats the name as
one: backticked or slash/path-spelled, adjacent to skill/command/agent language, or governed by a
use/load/invoke/route handoff. Product words, CLI commands, longer taken names, and framework API
rows are dropped with the reason recorded.

Wave A consumes the playbook's derived set. It does not carry a hand-maintained alternation.

## Verification Before Execution

The corrected documents are ready for a new reviewer pass when all of these hold:

1. Every plan old-string is found literally at the pinned source or explicitly marked as a folded
   block edit.
2. A current-tree reference sweep demonstrates that the corrected Wave A task list covers every
   true excluded-artifact hit and records false positives separately.
3. Invocation, exclusion, bundle, warning, patch-mode, edge, and ledger expectations reconcile.
4. Wave B tests cover every behavior branch and link spelling claimed by the implementation sketch.
5. No document claims full assertion metrics, the full academic smell catalog, coercion-free auto
   descriptions, or a compiling-snippet guarantee that the plans do not implement.
6. Historical OpenAI, xAI, and GLM/OpenCode-Go findings are dispositioned in the corrected
   documents, and the final amended set receives two independent Grok reviews.

After that review, the curator chooses go/no-go and subagent-driven versus inline execution.
