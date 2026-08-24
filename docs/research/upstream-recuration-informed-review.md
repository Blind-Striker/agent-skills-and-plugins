# Upstream Recuration Informed Review

Date: 2026-08-21

## Scope and authority

This is the durable evidence record for the read-only review of
`e2699bf550744e3b03f36d20f1ca3d1519afe334..5555fb15ecb4ab625b1f0991257fb9ec5f3fa52f`.
It records only findings that survived the review's adversarial verification stage. Live Git,
curation manifests, overlays, generated output, and current canon remain authoritative over this
snapshot. The operational disposition of these findings belongs in
[`docs/ROADMAP.md`](../ROADMAP.md); this file does not pre-decide which fixes to take.

The reviewed range contains four commits:

| Commit | Reviewed change |
|---|---|
| `98c200b` | Truthful sync reporting, then the dotnet-skills and superpowers pin moves |
| `7515bd3` | The mattpocock-skills wave and the owned `ask-deniz` router |
| `dd8f64c` | The dotnet-agent-skills wave, five recut patches, and two merges |
| `5555fb1` | The original `writing-tunit-tests` skill and the `code-testing-agent` take |

## Method and baseline

Seven defect dimensions ran in parallel: unreviewed sync logic, recut patches, merged content,
TUnit API claims, router truth, documentation truth, and authored/generated boundaries. Stage one
produced 25 unique candidate findings after duplicate reports were consolidated.

Every behavior-changing candidate then went to three fresh adversarial refuters with different
lenses: correctness, actual reproducibility, and recorded intent. A candidate survived only when at
least two refuters failed to kill it. Documentation-only candidates received one fresh refuter.
Seven candidates were rejected and 18 survived. Only OpenAI GPT-5.6 Sol and Luna subagents were
used. Each agent performed its own Memorizer retrieval and was instructed not to restore or rely on
the archived pre-move upstream-impact audits.

The live baseline before review was:

- `HEAD` at `901fd87`, with the reviewed range ending at `5555fb1`.
- All five submodule pins matched the committed state; no pin moved.
- `npm run inventory` completed.
- `npm run validate` completed with 0 errors and the two known warnings already recorded in the
  roadmap.
- No real Claude Code or OpenCode installation was read or changed.

## High severity

### H1 - C# 12 syntax in an older-target example

Location: [`overlays/deniz-dotnet-general/generate-testability-wrappers/overlay.patch`](../../overlays/deniz-dotnet-general/generate-testability-wrappers/overlay.patch), line 58.

The merged ambient-scope example uses the C# 12 primary-constructor spelling
`private sealed class Scope(Action restore)`, while the emitted skill advertises .NET 6-7 and .NET
Framework projects and routes no-DI projects to this pattern.

Concrete failure: a default `net7.0` project compiles with C# 11 and reports `CS9058`; a default
`net6.0` project reports `CS8936`; a .NET Framework project using its normal C# 7.3 default reports
`CS8370`. The nested-scope restoration itself is correct under C# 12, but the example does not meet
the destination skill's stated target range.

Recorded intent: the item is a manual migration ceremony whose merged ambient-seam correction must
work where the destination skill routes the user
([manifest](../../curation/deniz-dotnet-general.yaml#L175-L179)).

### H2 - .NET 8 TimeProvider overloads presented to .NET 6-7 users

Location: [`overlays/deniz-dotnet-general/generate-testability-wrappers/overlay.patch`](../../overlays/deniz-dotnet-general/generate-testability-wrappers/overlay.patch), lines 79-89.

The replacement table gives `Task.Delay(delay, timeProvider, token)` and
`new CancellationTokenSource(delay, timeProvider)` without a target-framework qualification. Those
static BCL overloads first exist in .NET 8. The `Microsoft.Bcl.TimeProvider` path advertised for
.NET 6-7 instead exposes `timeProvider.Delay(delay, token)` and
`timeProvider.CreateCancellationTokenSource(delay)` extension methods.

Concrete failure: an in-memory compilation against the .NET 6 or .NET 7 reference assemblies plus
`Microsoft.Bcl.TimeProvider` reports `CS1501` for `Task.Delay` and `CS1729` for
`CancellationTokenSource`. The extension forms compile against the same references.

Primary evidence: the package's
[`TimeProviderTaskExtensions`](https://github.com/dotnet/runtime/blob/5535e31a712343a63f5d7d796cd874e563e5ac14/src/libraries/Microsoft.Bcl.TimeProvider/src/System/Threading/Tasks/TimeProviderTaskExtensions.cs)
uses those extension forms before .NET 8.

### H3 - Expression indexes contradicted by a provider-wide absolute

Location: [`overlays/deniz-dotnet-general/database-performance/overlay.patch`](../../overlays/deniz-dotnet-general/database-performance/overlay.patch), lines 33-37.

The merged text says an index requires a bare column, a function predicate forces a whole-table
scan, and adding another index changes nothing. The destination is deliberately SQL-level guidance
covering EF Core and Dapper, so no provider restriction narrows that claim.

Concrete failure: PostgreSQL can use `CREATE INDEX users_lower_name_idx ON users (lower(name))` for
`WHERE lower(name) = 'alice'`. SQL Server can similarly use qualifying indexed computed columns.
The current wording can make a reader reject a working expression-index design and perform an
unnecessary schema or query rewrite.

Primary evidence: PostgreSQL's
[`Indexes on Expressions`](https://www.postgresql.org/docs/current/indexes-expressional.html) uses
`lower(column)` as its canonical example; EF Core's
[`efficient querying`](https://learn.microsoft.com/ef/core/performance/efficient-querying#use-indexes-properly)
guidance distinguishes a simple index from a persisted computed column or expression index.

Recorded intent: the manifest calls this the SQL-level half of the merge and says it spans both EF
Core and Dapper ([manifest](../../curation/deniz-dotnet-general.yaml#L58-L64)).

### H4 - TUnit0058 and TUnit0059 map to the wrong problems

Location: [`skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md`](../../skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md), lines 319-324.

The skill maps `TUnit0059` to runtime-resolved data sources and `TUnit0058` to missing generic-test
generation. Current TUnit analyzer code maps `TUnit0058` to `HookUnknownParameters` and `TUnit0059`
to `AbstractTestClassWithDataSources`.

Concrete failure: a real `TUnit0058` caused by an unsupported hook parameter is diagnosed as a
generic-test problem and the reader is sent toward `[GenerateGenericTest]`; a real `TUnit0059` is
sent toward data-source/AOT remediation instead of the abstract-class inheritance rule.

Primary evidence: TUnit v1.65.38
[`DiagnosticIds.cs`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Analyzers/DiagnosticIds.cs)
and
[`AnalyzerReleases.Shipped.md`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Analyzers/AnalyzerReleases.Shipped.md).
The same release's AOT documentation repeats the wrong mappings, so this finding is an upstream
documentation/source conflict rather than an unsupported version guess.

### H5 - TestContext is shown as an injected test-method parameter

Location: [`skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md`](../../skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md), lines 275-281.

The example takes `TestContext context` on an ordinary `[Test]` method. Current TUnit implicitly
supplies only a trailing `CancellationToken`; ordinary parameters require test data. Test code reads
the current context through `TestContext.Current`.

Concrete failure: the example receives error `TUnit0038` (`No data source provided`) and does not
run. If the analyzer is suppressed, the generated invoker still has no argument to supply.

Primary evidence: TUnit v1.65.38
[`TestMethodParametersAnalyzer.cs`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Analyzers/TestMethodParametersAnalyzer.cs)
and the current
[`TestContext` guide](https://github.com/thomhurst/TUnit/blob/v1.65.38/docs/docs/writing-tests/test-context.md).
The migration pages that show method injection conflict with the executable analyzer and generator.

### H6 - TUnit.Mocks prerequisites are absent from the stated setup

Location: [`skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md`](../../skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md), setup at lines 35-42 and 51, example at lines 287-304.

The skill establishes a `net8.0` project with only the `TUnit` meta-package, then presents
`IGreeter.Mock()` as available. `TUnit.Mocks` is a separate package, and its generated static
extension API needs a C# 14-capable compiler.

Concrete failure: the stated project cannot resolve `IGreeter.Mock()`. Adding `TUnit.Mocks` while
remaining on a default C# 12 compiler produces `TM004` until the compiler/language requirement is
met.

Primary evidence: the TUnit v1.65.38
[`mocking guide`](https://github.com/thomhurst/TUnit/blob/v1.65.38/docs/docs/writing-tests/mocking/index.md)
requires separate package installation and states the C# 14 requirement.

## Medium severity

### M1 - Sync posture text ignores overlay body ownership

Location: [`tools/sync.ts`](../../tools/sync.ts), lines 181-186.

The consequence branch accounts for manifest `invocation:` and `frontmatter:` overrides but not
`body: overlay`. It can therefore say an upstream metadata change "flows straight into output" even
when the overlay owns `SKILL.md` and overlay drift stops the build before emission.

Concrete failure: a description change under the owned `requesting-code-review` body produces both
an `OVERLAY - review, then re-bless` status and a contradictory `this flows straight into output`
POSTURE line. An in-memory `syncReport` call reproduced the pair.

Recorded intent: overlays replace upstream-backed files and later upstream improvements do not flow
into those files ([schema](../../curation/SCHEMA.md#L34-L35)).

### M2 - Sync misses malformed frontmatter that the build still parses

Location: [`tools/sync.ts`](../../tools/sync.ts), lines 145-159.

The parse check always constructs `<source>/SKILL.md` and occurs after the excluded-item early
return. It therefore misses file-shaped commands and agents, and it skips excluded skills. The
global scanner still parses all three shapes before item exclusion is applied
([scanner](../../tools/lib/scan.ts#L69-L101)).

Concrete failure: malformed YAML in a changed file-shaped agent is reported as "auto-updated on
next build"; malformed YAML in an excluded skill is reported as "excluded - no action". The next
build aborts with `YAMLParseError` during the global scan. Both report branches were reproduced with
an in-memory `SyncIO`.

### M3 - TestDataRow is presented as an attribute

Location: [`skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md`](../../skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md), lines 193-194.

`[TestDataRow<T>]` appears in the same bracketed list as real attributes, but `TestDataRow<T>` is a
wrapper record returned by a data source.

Concrete failure: applying `[TestDataRow<int>]` produces `CS0616` because the type does not derive
from `Attribute`.

Primary evidence: TUnit v1.65.38
[`TestDataRow.cs`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Core/TestDataRow.cs)
and the
[`Test Data Row` guide](https://github.com/thomhurst/TUnit/blob/v1.65.38/docs/docs/writing-tests/test-data-row.md).

### M4 - Unawaited assertions are not silent under the stated default setup

Location: [`skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md`](../../skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md), lines 87-94.

The text says a forgotten `await` is not a compile error and the test silently passes, then mentions
the bundled analyzer only as a backstop. In the setup established by the same skill, that analyzer is
included and its rule is an enabled error.

Concrete failure: `Assert.That(result).IsEqualTo(3);` produces error `TUnitAssertions0002`; the build
stops before the test can pass. The silent runtime behavior exists only when the analyzer is removed,
suppressed, or demoted, and needs that qualification.

Primary evidence: TUnit v1.65.38
[`AwaitAssertionAnalyzer`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Assertions.Analyzers/AwaitAssertionAnalyzer.cs)
and the
[`Awaiting Assertions` guide](https://github.com/thomhurst/TUnit/blob/v1.65.38/docs/docs/assertions/awaiting.md).

### M5 - Attribute scope and precedence are overgeneralized

Location: [`skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md`](../../skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md), lines 260-271.

The sentence after the table says all listed attributes apply at method, class, or assembly level and
the most specific wins. `DisplayNameAttribute` does not support assembly scope, while `Category` and
`Property` are additive metadata rather than single values resolved by specificity.

Concrete failure: `[assembly: DisplayName("Suite")]` produces `CS0592`. Categories from assembly,
class, and method are combined, and `Property` can retain multiple values for the same key rather
than dropping the less-specific values.

Primary evidence: TUnit v1.65.38
[`DisplayNameAttribute`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Core/Attributes/TestMetadata/DisplayNameAttribute.cs),
[`CategoryAttribute`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Core/Attributes/TestMetadata/CategoryAttribute.cs),
and
[`PropertyAttribute`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Core/Attributes/TestMetadata/PropertyAttribute.cs).

### M6 - MethodDataSource is narrower than the current API

Location: [`skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md`](../../skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md), lines 157-175 and 319-321.

The skill states that `[MethodDataSource]` members must be static and repeats the rule as an AOT
requirement. Current TUnit supports public instance sources when it can construct the test/provider
instance, and its generator emits a reflection-free factory for those cases.

Concrete failure: a public instance source on a constructible test class is valid and covered by
TUnit's AOT execution tests, but the skill rejects or rewrites it as invalid.

Primary evidence: TUnit v1.65.38
[`MethodDataSourceAttribute.cs`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Core/Attributes/TestData/MethodDataSourceAttribute.cs)
and the executable regression fixture for
[`issue 6361`](https://github.com/thomhurst/TUnit/blob/v1.65.38/tests/TUnit.TestProject/Bugs/6361/Tests.cs).

### M7 - object-array rows are supported, not prohibited

Location: [`skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md`](../../skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md), line 172.

The sentence "rows are strongly typed, not `object[]`" is categorical. TUnit supports
`IEnumerable<object[]>` for matching test parameters; tuples provide stronger compile-time checking
but are a recommendation, not an API requirement.

Concrete failure: a valid `IEnumerable<object[]>` source is reported or rewritten as invalid even
though TUnit's engine tests and AOT documentation cover that shape.

Primary evidence: TUnit v1.65.38
[`MethodDataSourceWithParametersTest`](https://github.com/thomhurst/TUnit/blob/v1.65.38/tests/TUnit.TestProject/MethodDataSourceWithParametersTest.cs)
and its
[`AOT guide`](https://github.com/thomhurst/TUnit/blob/v1.65.38/docs/docs/writing-tests/aot.md).

### M8 - Staticness mistakes are build failures, not silent discovery failures

Location: [`skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md`](../../skills/deniz-dotnet-general/writing-tunit-tests/SKILL.md), lines 73-74 and 242-249.

The skill says private/static test methods and incorrectly static hooks fail silently at discovery.
The bundled analyzers report enabled errors, and generated invocations can also produce ordinary C#
compiler errors.

Concrete failure: a static test produces `TUnit0048`, an instance class-level hook produces
`TUnit0007`, and a static test-level hook produces `TUnit0016`. The build fails before normal test
discovery, so the current troubleshooting description points at the wrong failure boundary.

Primary evidence: TUnit v1.65.38
[`AnalyzerReleases.Shipped.md`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Analyzers/AnalyzerReleases.Shipped.md)
and [`Rules.cs`](https://github.com/thomhurst/TUnit/blob/v1.65.38/src/TUnit.Analyzers/Rules.cs).

### M9 - Sync candidates omit original skill names

Location: [`tools/sync.ts`](../../tools/sync.ts), lines 291-296.

The sync candidate universe comes only from ledger keys. Ledger generation loops over manifest
items, while original skills are emitted separately, so names such as `writing-tunit-tests` never
enter the automatic candidate scan.

Concrete failure: when a changed upstream `SKILL.md` gains or loses the bare name
`writing-tunit-tests`, sync emits no `CANDIDATE EDGES` line. An in-memory reproduction produced the
line immediately when that name was manually added to the candidate set.

Evidence boundary: current architecture accurately describes the ledger-derived implementation
([references and linking](../architecture/references-and-linking.md#proof-boundary-and-current-limits)),
and one intent refuter treated this as a deliberate proof boundary. Two independent technical
refuters nevertheless reproduced a missing candidate for a known emitted output name, so the claim
survived the review's two-of-three threshold. The disposition still requires a curator decision.

## Low severity

### L1 - ask-deniz overpromises a documentation artifact

Location: [`overlays/deniz-process/ask-deniz/SKILL.md`](../../overlays/deniz-process/ask-deniz/SKILL.md), lines 21-23.

The router says `/grill-with-docs` leaves a paper trail in `CONTEXT.md` and ADRs. The delegated
domain-modeling behavior creates those files lazily: `CONTEXT.md` changes only when a term is
resolved, and an ADR is skipped unless all admission conditions hold.

Concrete failure: a valid session that settles a reversible implementation choice using existing
vocabulary can complete with no new term and no ADR-worthy decision, leaving neither file behind.
The stateful capability exists, but the unconditional router summary is not guaranteed.

### L2 - The guarded-reference reproduction omits a second validation error

Location: [`docs/ROADMAP.md`](../ROADMAP.md), lines 78-97 at the reviewed head.

The reproduction tells the reader to add the namespaced fact to `code-testing-agent` and then shows
only the OpenCode namespace-leak error, saying the Claude tree passes. The item has no corresponding
`depends_on` declaration.

Concrete failure: validation also derives the fact from the Plugin body and reports
`undeclared dependency: writing-tunit-tests`. The reproduction is not solely a one-harness failure
unless `depends_on: [writing-tunit-tests]` is added before the run, or both errors are recorded.

Recorded intent: namespaced model facts and manifest declarations must agree in both directions
([references and linking](../architecture/references-and-linking.md#depends_on-and-audience-reachability)).

### L3 - The roadmap reports the previous General version

Location: [`docs/ROADMAP.md`](../ROADMAP.md), line 49 at the reviewed head.

The current-state section says `deniz-dotnet-general` is 0.4.0. The authoritative curation manifest
and both emitted harness outputs are 0.6.0.

Concrete failure: a reader using the operational document as intended sees a stale Module version
even though the same review range introduced the 0.6.0 surface. The roadmap owns current operational
state rather than chronology.

## Verification boundary and disposition

The review did not move pins, change curation, regenerate output, or touch real installations. The
repository baseline's green validation proves only the deterministic checks already implemented; it
does not invalidate these source/API and report-truthfulness findings.

No finding in this record is yet accepted as a fix plan. The next step is to disposition the 18
survivors against live Git: take, narrow, defer with a reason, or reject with contrary evidence. Once
a disposition lands, update the roadmap and the relevant current owner rather than rewriting this
dated evidence snapshot.
