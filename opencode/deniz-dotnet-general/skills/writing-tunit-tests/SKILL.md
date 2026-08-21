---
name: writing-tunit-tests
description: Write tests in TUnit — the attributes, the async assertion model,
  data-driven cases, lifecycle hooks, and the parallelism rules. USE FOR writing
  or fixing a test in a TUnit project, translating a test idea into TUnit
  syntax, or reviewing TUnit test code for API mistakes. DO NOT USE FOR deciding
  what to test, judging whether a test is any good, or running tests.
---

# Writing TUnit Tests

TUnit is a .NET test framework built on Microsoft.Testing.Platform with compile-time test discovery.
Almost everything about it that surprises people comes from two design choices: **assertions are
async**, and **every test gets its own instance and runs in parallel with every other test**. Get
those two right and the rest is ordinary.

This skill is the syntax and the semantics. It does not cover:

- **what to test, and in what order** — that is the red-green loop in `test-driven-development`
- **whether a test is worth having** — `test-gap-analysis` asks whether a test would catch a real
  bug, `test-anti-patterns` catches the smells
- **running or filtering tests** — `run-tests` gives the exact command, `filter-syntax` the filter
- **making untestable code testable** — that is a seam problem, and the ceremony that builds the
  abstraction is one the human starts by name: `generate-testability-wrappers`

## Setting up a project

The template is the shortest path, and it needs installing first:

```bash
dotnet new install TUnit.Templates
dotnet new TUnit -n MyProject.Tests
```

By hand, the test project is a **console executable**, not a library:

```bash
dotnet new console -n MyProject.Tests
cd MyProject.Tests
dotnet add package TUnit
```

Then delete the generated `Program.cs` and make sure the project keeps
`<OutputType>Exe</OutputType>`. The `TUnit` meta-package pulls in the assertions, the engine, the
analyzers and coverage support.

Three things break discovery, and all three are things a .NET developer adds out of habit:

- **`Microsoft.NET.Test.Sdk`** — remove it. It is VSTest infrastructure and TUnit is not VSTest.
- **`coverlet.collector` / `coverlet.msbuild`** — remove them for the same reason. Coverage comes
  from `Microsoft.Testing.Extensions.CodeCoverage`, already in the meta-package, via `--coverage`.
- **A missing `<OutputType>Exe</OutputType>`** — without it nothing is discovered at all.

The sample project targets `net8.0`. TUnit also ships a `netstandard2.0` target, so .NET Framework
works, but it needs a polyfill package for types the old BCL lacks (`ModuleInitializerAttribute`
among them).

## The shape of a test

```csharp
public class CheckoutTests
{
    [Test]
    public async Task Discount_is_applied_to_a_gold_tier_subtotal()
    {
        var checkout = new CheckoutService();

        var total = await checkout.ApplyDiscountAsync("GOLD", 100.00);

        await Assert.That(total).IsEqualTo(80.00);
    }
}
```

`[Test]` on the method, and nothing on the class — there is no `[TestClass]` or `[TestFixture]`.
Test methods must be **public instance methods**; a private or static one is silently not
discovered.

A test with no assertion passes as long as it does not throw. That is occasionally what you want (a
smoke test that a pipeline runs at all), but state it deliberately rather than by accident.

## Assertions: everything is awaited

This is the rule that catches everyone:

```csharp
await Assert.That(result).IsEqualTo(3);
```

`Assert.That(result).IsEqualTo(3)` **without `await` does nothing at all**. The fluent chain only
builds a rule; `await` is what executes it. A forgotten `await` is not a compile error and not a
failure — the test passes, silently, whatever the value was.

Two things save you. The design reason: because every assertion is async, you never have to
remember which ones need awaiting — they all do. And the mechanical backstop: the bundled analyzer
raises **`TUnitAssertions0002`** ("Assert statements must be awaited") with a code fix. If you see
that ID in build output, that is what it means.

The catalogue, by what you are asserting:

| Asserting | Written as |
|---|---|
| Equality | `IsEqualTo(x)` / `IsNotEqualTo(x)` |
| Same instance | `IsSameReferenceAs(x)` / `IsNotSameReferenceAs(x)` |
| Booleans | `IsTrue()` / `IsFalse()` |
| Numbers | `IsGreaterThan(x)`, `IsLessThan(x)`, `IsEqualTo(x).Within(0.001)` |
| Collections | `Contains(item)` / `DoesNotContain(item)`, predicate overloads too |
| Strings | `Contains`, `StartsWith`, `EndsWith`, `Matches(pattern)` |
| Exceptions | `Throws<T>()`, `ThrowsExactly<T>()`, `ThrowsNothing()` |

Prefer the specific assertion over a boolean: `Assert.That(count).IsGreaterThan(0)` reports what it
actually saw, while `Assert.That(count > 0).IsTrue()` can only tell you `false`.

Numeric tolerance has to match the type — `Within(0.01f)` for a `float`, `Within(0.01m)` for a
`decimal`.

String assertions take modifiers: `.IgnoringCase()`, `.IgnoringWhitespace()`, `.WithTrimming()`,
`.WithComparison(StringComparison.Ordinal)`.

### Exceptions

The fluent form wraps a delegate, and it is the same `Throws<T>()` whether the delegate is sync or
async:

```csharp
await Assert.That(() => service.Parse("nonsense")).Throws<FormatException>();
await Assert.That(async () => await service.LoadAsync(id)).Throws<HttpRequestException>();
```

There is **no fluent `.ThrowsAsync<T>()`**. There is a static helper of that name which returns the
exception when you want to assert on its contents:

```csharp
var ex = await Assert.ThrowsAsync<ValidationException>(async () => await service.SaveAsync(bad));
await Assert.That(ex.Message).Contains("email");
```

### Combining

`.And` and `.Or` both chain — but **never in the same chain**. Mixing them throws
`MixedAndOrAssertionsException` at runtime, and the analyzer `TUnitAssertions0001` flags it first.
Split into two awaited assertions instead.

## Data-driven tests

Inline rows:

```csharp
[Test]
[Arguments("GOLD", 100.00, 80.00)]
[Arguments("SILVER", 100.00, 90.00)]
[Arguments("NONE", 100.00, 100.00)]
public async Task Discount_matches_the_tier(string tier, double subtotal, double expected)
{
    var total = await new CheckoutService().ApplyDiscountAsync(tier, subtotal);
    await Assert.That(total).IsEqualTo(expected);
}
```

Rows from a method — the attribute is **`[MethodDataSource]`**, and the source method must be
**static**:

```csharp
[Test]
[MethodDataSource(nameof(DiscountCases))]
public async Task Discount_matches_the_tier(string tier, double subtotal, double expected) { }

public static IEnumerable<(string tier, double subtotal, double expected)> DiscountCases() =>
[
    ("GOLD", 100.00, 80.00),
    ("SILVER", 100.00, 90.00),
];
```

Note the tuple: rows are strongly typed, not `object[]`. `[MethodDataSource(typeof(Other),
nameof(Other.Cases))]` reaches another class, and `[MethodDataSource<T>(nameof(T.Cases))]` is the
AOT-safe generic form. `[InstanceMethodDataSource]` exists when the source has to be an instance
member.

**`[ClassDataSource<T>]` is not a row provider.** Despite the name it is the shared-fixture
mechanism — the equivalent of xUnit's `IClassFixture<T>`. It injects one managed instance whose
lifetime you choose:

```csharp
[ClassDataSource<DatabaseFixture>(Shared = SharedType.PerClass)]
public class OrderRepositoryTests(DatabaseFixture db) { }
```

`SharedType` is `None`, `PerClass`, `PerAssembly`, `PerTestSession`, or `Keyed` (with `Key`). If you
actually want rows from a class, use `[MethodDataSource(nameof(MyClass.Rows))]`.

For a generated source, subclass `DataSourceGeneratorAttribute<T…>` (or the async / untyped
variants) and override `GenerateDataSources(DataGeneratorMetadata)`. It returns `Func<…>` delegates,
not values. There is no `ITestDataSource` — that is an MSTest interface and it does not exist here.

Also available: `[MatrixDataSource]` with `[Matrix(...)]` per parameter for a cross product,
`[TestDataRow<T>]`, and `[GenerateGenericTest(typeof(...))]` for generic test methods.

## Parallelism, and the instance rule that goes with it

**Every test is eligible to run concurrently with every other test — including the ones in the same
class.** There is no implicit per-class serialisation. If you are coming from xUnit, this is the
assumption to drop: xUnit collections serialise a class, TUnit does not.

Paired with that: **a fresh instance of the test class is created for every test method.**

Together those two make instance fields useless as a channel between tests, and the failure is
quiet:

```csharp
public class Wrong
{
    private int _value;                          // reset for every test

    [Test]
    public void Sets_it() => _value = 99;

    [Test]
    public async Task Reads_it()
        => await Assert.That(_value).IsEqualTo(99);   // fails: different instance, _value is 0
}
```

Share deliberately instead — `static` for genuinely global state, `[ClassDataSource<T>]` for a
fixture with a managed lifetime.

When a test genuinely cannot run beside others, say so:

- `[NotInParallel]` — with optional constraint keys and an `Order`
- `[ParallelGroup("name")]` — everything in the group runs together, apart from other groups
- `[ParallelLimiter<T>]` — cap concurrency, where `T : IParallelLimit` has a parameterless
  constructor and an `int Limit`. Mind the spelling: the attribute ends in **-er**, the interface
  does not. Assembly-wide: `[assembly: ParallelLimiter<MyLimit>]`

Ordering is a dependency, not a number: `[DependsOn(nameof(OtherTest))]` makes this test wait for
that one while everything else keeps running. `ProceedOnFailure = true` runs it even if the
dependency failed.

## Lifecycle hooks

`[Before(X)]` and `[After(X)]` where `X` is `Test`, `Class`, `Assembly`, `TestSession`, or
`TestDiscovery`. They nest outermost to innermost — TestSession, Assembly, Class, Test — and the
`After` side unwinds in reverse.

**Staticness is not optional and getting it wrong fails silently:**

| Hook level | Must be |
|---|---|
| `Test` | instance |
| `Class`, `Assembly`, `TestSession`, `TestDiscovery` | **static** |
| every `[BeforeEvery(...)]` / `[AfterEvery(...)]` | **static** |

`[Before(Class)]` runs once for *this* class. `[BeforeEvery(Class)]` runs before *every* class in
the session — a different job, and the `Every` family is easy to miss.

Never write `async void` on a hook; it is a compile error here.

Multiple `[After(Test)]` methods all run even when one throws, and the exceptions are aggregated —
so cleanup does not silently stop halfway.

## Control attributes

| Attribute | Effect |
|---|---|
| `[Repeat(n)]` | **`n` more runs, so `n + 1` total.** `[Repeat(3)]` executes four times |
| `[Retry(n)]` | Re-run on exception. Subclass `RetryAttribute` and override `ShouldRetry` to be selective |
| `[Skip("reason")]` | Reason is required. Subclass for conditional skipping; `Skip.Test(reason)` skips at runtime, including from a `[Before(Test)]` hook |
| `[Timeout(30_000)]` | **Milliseconds.** Take a `CancellationToken` parameter and pass it down, or the timeout cannot actually stop the work. Each retry gets a fresh one |
| `[Category("Smoke")]` | Groups tests for filtering |
| `[Property("key", "value")]` | Arbitrary metadata — the `[Trait]` equivalent |
| `[DisplayName("Adds $a and $b")]` | `$parameterName` interpolates the argument |

These apply at method, class or assembly level, and the most specific wins: method beats class beats
assembly.

## Test output

There is no `ITestOutputHelper`. Take a `TestContext` parameter, or reach for `TestContext.Current!`:

```csharp
[Test]
public async Task Writes_a_note(TestContext context)
{
    context.Output.WriteLine("about to call the API");
}
```

`Output.WriteError(...)` and `Output.AttachArtifact(...)` are there too.

## Mocking

TUnit ships its own mocking library, `TUnit.Mocks` — source-generated, so it works under Native AOT
where reflection-based mockers do not:

```csharp
var mock = IGreeter.Mock();
mock.Greet(Any()).Returns("Hello!");

IGreeter greeter = mock;
var result = greeter.Greet("Alice");

mock.Greet("Alice").WasCalled(Times.Once);
```

`Mock.Of<IGreeter>()` is the alternative entry point. `TUnit.Mocks.Http` and `TUnit.Mocks.Logging`
cover those two cases specifically. Ordinary mocking libraries still work — they are just libraries
— but they give up AOT compatibility.

## Compile-time discovery, and what it forbids

Tests are found by a Roslyn source generator, not by reflection. That is what makes Native AOT and
single-file publish work — and it is the reason a few things that compile in other frameworks do not
compile here.

**Under Native AOT these are hard rules.** A reflection mode exists (`--reflection`,
`[assembly: ReflectionMode]`, or `TUNIT_EXECUTION_MODE=reflection`) for cases the generator cannot
see, such as bUnit Razor components — but it is exactly the thing AOT removes, so an AOT-published
suite has no escape hatch. Write for the generator from the start.

What the generator has to be able to see at compile time:

- **Data-source methods must be static and named literally.** Building a source reflectively, or
  resolving the method name at runtime, fails with **`TUnit0059`**. The AOT-safe spelling is
  `[MethodDataSource<T>(nameof(T.Cases))]`.
- **Generic test methods and classes need `[GenerateGenericTest(typeof(...))]`**, one per closed
  type you want run. Without it the build fails with **`TUnit0058`** — a compile error, not a
  silently skipped test.
- **Mocking has to be generated too.** Reflection-based mockers cannot run under AOT, which is why
  `TUnit.Mocks` is source-generated.

## Coming from another framework

| xUnit | TUnit |
|---|---|
| `[Fact]`, `[Theory]` | `[Test]` |
| `[InlineData]` | `[Arguments]` |
| `[MemberData(nameof(X))]` | `[MethodDataSource(nameof(X))]` |
| `[ClassData(typeof(X))]` | `[MethodDataSource(nameof(X.Rows))]` |
| `[Trait("k","v")]` | `[Property("k","v")]` |
| `IClassFixture<T>` | `[ClassDataSource<T>(Shared = SharedType.PerClass)]` |
| `ICollectionFixture<T>` | `[ClassDataSource<T>(Shared = SharedType.Keyed, Key = "name")]` |
| `IAsyncLifetime` | `[Before(Test)]` / `[After(Test)]` |
| `ITestOutputHelper` | `TestContext.Output` |
| constructor + `IDisposable` | `[Before(Test)]` / `[After(Test)]` |

From **NUnit**, the trap is the opposite of xUnit's: NUnit reuses one fixture instance across a
class, TUnit creates a fresh one per test.

From **MSTest**, the trap is `ITestDataSource`, which does not exist here.
