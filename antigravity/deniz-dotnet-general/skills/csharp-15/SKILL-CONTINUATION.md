## 3. Closed hierarchies: `closed`

`closed` on a class means only its **declaring assembly (and module)** may declare **direct**
subclasses. Because the set of direct descendants is fixed, a `switch` expression that handles them
all is exhaustive without a default arm, including in consuming assemblies.

```csharp
// Assembly 1
public closed record class JobStatus;
public record class Queued : JobStatus;
public record class Running(int PercentComplete) : JobStatus;
public record class Completed(TimeSpan Elapsed) : JobStatus;
public sealed record class Failed(string Error) : JobStatus;

// Any assembly
public static string Describe(JobStatus status) => status switch
{
    Queued => "waiting to start",
    Running(var percent) => $"{percent}% complete",
    Completed(var elapsed) => $"finished in {elapsed.TotalSeconds:F1}s",
    Failed(var error) => $"failed: {error}",
};  // no CS8509

// Assembly 2
public record class Paused : JobStatus;   // error: 'JobStatus' is a closed class
```

### Declaration rules

- `closed` applies **only to classes**, including record classes, not to structs or interfaces.
- A closed class is **implicitly `abstract`**, so you can't instantiate it. Combining `closed` with
  `sealed`, `static` or an explicit `abstract` is an error.
- Direct subtypes must be declared in the same assembly **and module** as the closed class.
- **It isn't transitive.** A plain subclass of a closed class can be extended from other
  assemblies. Mark the leaves `sealed`, or the intermediate levels `closed`, to lock them down.
- **Generic rule:** a generic class that directly derives from a closed class must use **every**
  one of its type parameters in the base class specification.

```csharp
public closed record class Tree<T>;
public record class Leaf<T>(T Value) : Tree<T>;                        // OK
public record class Branch<T>(Tree<T> Left, Tree<T> Right) : Tree<T>;  // OK
public record class Constant<U>(U Value) : Tree<int>;                  // error: U not used in the base
```

- `closed` is contextual. A type named `closed` now needs `@closed` (CS9380), and a field of type
  `closed` may now parse as a modifier (CS1519).

### Exhaustiveness rules

- **Only direct descendants count.** Indirect descendants are covered by their parent's arm.
  Ordinary subsumption still applies: put a more-derived arm (`Sedan`) **before** its base (`Car`).
  The reverse order gives CS8510 (unreachable).
- An arm for the closed base type after all direct descendants is an **unreachable-case error**.
- **Accessibility matters.** If a direct descendant isn't accessible at the switch site (for
  example, an `internal` subtype seen from another assembly, or a `protected` nested subtype), the
  switch isn't exhaustive (CS8509). Fix this with a `_` arm, a base-type arm, or by making every
  direct subtype at least as accessible as the base.
- **Nullable input:** a switch over `JobStatus?` also needs a `null` arm.
- **Type parameters:** a switch over `X` where `X : JobStatus` is exhaustive on the same terms as a
  switch over `JobStatus`.
- **Generic closed bases:** only subtypes that can actually produce the given construction are
  required. For `C<string>`, a `D2<V> : C<V[]>` case isn't needed. Constraints on subtypes
  **aren't** used to rule cases out, and a non-speakable generic subtype makes the switch
  non-exhaustive. Add a base-type arm in those situations.
- A closed class with **no** subtypes isn't exhausted by an empty switch.
- Exhaustiveness applies to `switch` *expressions*, which is where the missing-case diagnostic
  comes from.

### Other effects

- **Sealed hierarchy interface checks.** When every class in a closed hierarchy is `sealed` or
  `closed`, an explicit cast to an interface that no class in the hierarchy implements is a compile
  error, the same rule as for a sealed class.
- **Metadata.** The class gets `[IsClosedType]`, and every constructor gets
  `[CompilerFeatureRequired("ClosedClasses")]`. Older compilers and VB therefore can't derive from
  it.
- **Versioning.** Adding `closed` to a published class, or adding a new direct subtype to a closed
  class, is a **breaking change** for consumers, whose exhaustive switches stop being exhaustive.
  Treat the subtype list as public API.

### Union or closed hierarchy?

| Choose a union when | Choose a closed hierarchy when |
| --- | --- |
| The cases are existing or unrelated types (`int`, `string`, third-party types) | You own every case type and they form one family |
| You don't want inheritance or shared members | The cases share base members, state or virtual behavior |
| A lightweight struct wrapper suits you (watch boxing) | You want reference semantics and polymorphism |
| The same type might belong to several unions | A type belongs to exactly one family |

---

## 4. Extension indexers

C# 14 extension blocks can now declare **indexers**. Indexers are instance members, so the block
needs a **named receiver**.

```csharp
public static class SequenceIndexer
{
    extension(IEnumerable<int> sequence)
    {
        public int this[int index] => sequence.ElementAt(index);
    }
}

IEnumerable<int> numbers = Enumerable.Range(1, 10);
int third = numbers[2];

public static class SettingsExtensions
{
    extension(IDictionary<string, string> settings)
    {
        public string this[string section, string key]          // get + set
        {
            get => settings[$"{section}:{key}"];
            set => settings[$"{section}:{key}"] = value;
        }
    }
}
settings["Logging", "Level"] = "Debug";
```

### Rules

- Extension indexers support everything ordinary indexers do: `get` and `set`, expression bodies,
  `ref`-returning accessors, `scoped` parameters, attributes and `params`.
- **Not allowed:** `abstract`, `virtual`, `override`, `new`, `sealed`, `partial`, `protected` and
  related modifiers, and `init` accessors. These are the existing extension-member restrictions.
- Every type parameter of a generic extension block must appear in the receiver or the indexer's
  parameters.
- `[IndexerName("X")]` changes the metadata names: an `X` property and `get_X`/`set_X`
  implementation methods, `Item` by default. The implementation methods are static methods on the
  enclosing class with the receiver first, so you can disambiguate with
  `SettingsExtensions.get_Item(settings, "Logging", "Level")`.
- **Lookup order** (the first applicable tier wins):
  1. real instance indexers,
  2. implicit `Index`/`Range` support from instance members,
  3. extension indexers,
  4. implicit `Index`/`Range` support from extension members.

  An extension can never hijack an existing instance indexer.
- Within each scope, lookup tries `Length` before `Count`, starting with instance members and then
  moving outward through extension scopes.
- **Type inference** uses only the receiver and the index arguments, never the assigned value.
  `i[42, null] = new object();` fails inference where calling `E.set_Item(...)` would succeed.
- Null-conditional element access (`x?[i]`), index assignments in object initializers, list patterns
  and spread patterns all use the same resolution.

### Unlock `^`, ranges and list patterns with extensions

An extension `Count` or `Length`, an extension `this[int]` and a `Slice(int, int)` make a type
indexable from the end, sliceable and matchable with list patterns. Classic `this`-parameter
extension methods count too.

```csharp
public sealed class Readings(int[] values) { public int[] Values { get; } = values; }

public static class ReadingExtensions
{
    extension(Readings readings)
    {
        public int Count => readings.Values.Length;
        public int this[int index] => readings.Values[index];
        public Readings Slice(int start, int length) => new(readings.Values.AsSpan(start, length).ToArray());
    }
}

Readings r = new([10, 20, 30, 40]);
_ = r[^1];               // 40
_ = r[1..3][0];          // 20
_ = r is [10, .., 40];   // true
```

### Limitations

- Extension indexers are **never used for arrays or `string`**, even if you declare one.
- They can't appear in **expression trees**. This matters for LINQ providers such as EF Core:
  don't use extension indexers inside `IQueryable` lambdas.
- They aren't considered on `base[...]` access, or when the receiver is a type rather than a value.
- Extension `Length` and `Count` don't feed the collection-expression spread optimization, so
  `[.. x]` won't presize from them.

---

## 5. Labeled `break` and `continue`

Put a label directly on a loop or `switch` statement, then name it from an inner statement.

```csharp
outer: for (int row = 0; row < grid.Height; row++)
{
    for (int column = 0; column < grid.Width; column++)
    {
        if (grid[row, column].IsBlocked) continue outer;   // next row
        if (grid[row, column].IsGoal)    break outer;      // leave both loops
    }
}

dispatch: switch (command.Kind)
{
    case CommandKind.Batch:
        foreach (var step in command.Steps)
        {
            if (step.IsAbort) break dispatch;   // exits the switch statement, not just the foreach
            Run(step);
        }
        break;
    default:
        Run(command);
        break;
}
```

### Rules

- `break label;` targets the nearest enclosing `for`, `foreach`, `while`, `do` or `switch`
  **statement** with that label. A `switch` *expression* isn't a target.
- `continue label;` targets only a **loop**. It can't target a `switch`.
- Only the statement **immediately** after the label is labeled. In `a: b: while (...)`, the loop is
  labeled `b`, so `break a;` doesn't target it. Use one label per loop.
- Unlabeled `break` and `continue` behave exactly as before.
- Neither form can leave a `finally` block. If the jump crosses `try` blocks, their `finally`
  blocks run in order, innermost first.
- As with unlabeled jumps, the target must enclose the statement **in the same function**. You
  can't jump out of a lambda or local function to an outer loop.
- Labels share `goto`'s label rules. A label's scope is its whole block, including nested blocks,
  and two labels with the same name and overlapping scopes are an error. **Give each labeled loop
  in a method a distinct name**, such as `rows:` and `cols:`, rather than reusing `outer:`.
- The same label can serve both `break` and `continue`, which removes the old pair of `goto`
  labels at the end of the body and after the loop.

### Refactoring guidance

- Replace a Boolean "found" flag that's re-checked at each level with `break label;`.
- Replace `goto END;` placed right after a loop with `break label;`.
- Replace `goto NEXT;` with `NEXT: ;` at the end of the body with `continue label;`.
- The **IDE0410** analyzer ("Use labeled jump statement") flags these patterns and offers fixes. Its
  option is `csharp_style_prefer_labeled_jump_statements = true|false` (default `true`). Disable it
  with `dotnet_diagnostic.IDE0410.severity = none`.
- Extracting a method is still often cleaner. Use labeled jumps when the loops share many locals or
  extraction would need tuples or `out` parameters.

---

## 6. Non-virtual static interface members on older runtimes

C# 15 lets you declare and call **non-virtual static** interface members even when the target
runtime doesn't support default interface implementations, as on .NET Framework or
`netstandard2.0`. This helps multi-targeted libraries.

```csharp
public interface IMeasurement
{
    public static double ToMeters(double centimeters) => centimeters / 100;
}

double m = IMeasurement.ToMeters(250);   // call it on the interface, not on an implementing type
```

These still require runtime support for default interface methods:

- instance default implementations,
- `static abstract` and `static virtual` members,
- `protected`, `protected internal` and `private protected` interface members.

For down-level targets, keep interface helpers static and non-virtual, and don't give them the
`protected` access modifiers.

---

## 7. Memory safety model (preview)

This is the first step of a multi-release redesign. `unsafe` moves from marking code that
*mentions pointers* to marking code that *accesses unmanaged memory*. An `unsafe` member becomes a
contract that **its callers** must uphold. **Only use this when the user opts in.** It requires
preview settings.

### Enabling it (two independent switches)

```xml
<PropertyGroup>
  <LangVersion>preview</LangVersion>                                <!-- syntax + pointer relaxations -->
  <Features>$(Features);updated-memory-safety-rules</Features>       <!-- the new rules (requires-unsafe) -->
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>                       <!-- needed to write unsafe/safe/unsafe(...) -->
</PropertyGroup>
```

In a file-based app:

```csharp
#:property LangVersion=preview
#:property Features=$(Features);updated-memory-safety-rules
```

- `AllowUnsafeBlocks` is independent of the other two. A project with the updated rules but without
  `AllowUnsafeBlocks` simply gets errors when it calls requires-unsafe APIs.
- A future stable `<MemorySafetyRules>` property is planned but **not implemented**. Don't use it.
- Some docs and diagnostics call this language version "C# 16" (`langversion:16`), meaning the
  preview version that follows C# 15.

### What needs an `unsafe` context

| Operation | Original model | Preview model |
| --- | --- | --- |
| Declare a pointer type, take `&x` | unsafe | **safe** |
| `fixed` statement | unsafe | **safe** |
| `stackalloc` converted to a pointer | unsafe | **safe** |
| `sizeof(T)` for any unmanaged `T` | unsafe | **safe** |
| `*p`, `p->m`, `p[i]` | unsafe | unsafe |
| Function pointer invocation | unsafe | unsafe |
| Fixed-size buffer element access | unsafe | unsafe |
| Calling a member marked `unsafe` | no caller requirement | **caller needs unsafe** (with the updated rules) |
| `stackalloc` to `Span<T>` without an initializer in a `[SkipLocalsInit]` member | safe | **unsafe** (with the updated rules) |

The relaxations (the first four rows) apply with `LangVersion=preview` even when the assembly
hasn't opted in to the updated rules.

### Requires-unsafe members (with the updated rules)

- `unsafe` on a member's signature makes it **requires-unsafe**: every caller must be in an
  `unsafe` context. It no longer creates an unsafe context for the body, so wrap the dereference
  in an inner `unsafe { }` block.

```csharp
public static unsafe int ReadInt32(byte* source)   // obligation flows to callers
{
    unsafe { return *(int*)source; }               // unsafe operation scoped inside
}

unsafe { int v = ReadInt32(buffer); }              // the caller takes on the audit
```

- **Propagate or discharge.** A member either marks itself `unsafe` (propagate), or validates its
  inputs, uses an inner `unsafe` block and keeps a safe signature (discharge). A discharging member
  is an *unsafe boundary*.

```csharp
public static int SumBytes(byte[] source)          // safe signature
{
    ArgumentNullException.ThrowIfNull(source);
    fixed (byte* first = source)                   // no unsafe needed to pin
    {
        unsafe
        {
            // SAFETY: the null check and source.Length bound every read to the pinned array.
            int total = 0;
            for (int i = 0; i < source.Length; i++) total += first[i];
            return total;
        }
    }
}
```

- **Document contracts.** Put a `/// <safety>...</safety>` block on each requires-unsafe member
  describing what callers must guarantee, which an analyzer can require. Put a `// SAFETY:` comment
  inside each `unsafe` block explaining why that spot is sound.
- **Errors under the updated rules:**
  - `unsafe` on a type declaration, a static constructor, a finalizer, a `delegate` or a lambda.
  - Adding `unsafe` to an override or implementation of a member that isn't requires-unsafe.
  - Converting a requires-unsafe method to a delegate outside an unsafe context.
- An `unsafe` constructor gets an unsafe context in its `: base(...)`/`: this(...)` initializer.
- A type whose parameterless constructor is `unsafe` doesn't satisfy a `new()` or `struct`
  constraint in declarations such as base types, field types and aliases (CS9376). In expressions,
  it satisfies them only inside an `unsafe` context.
- Local functions and lambdas don't inherit `unsafe` or `safe` from the containing member. Mark a
  local function `unsafe` explicitly. Lambdas can't be `unsafe`.
- Both parts of a `partial` member must agree on `unsafe`/`safe`.
- **Properties:** put `unsafe`/`safe` on the property *or* on individual accessors, not both, and
  not the same modifier on every accessor. Event accessors can't be marked individually.
- **Fields:** an `unsafe` field forces every write into an `unsafe` context. Use it when the field's
  type can't express an invariant, such as a `byte*` plus a length. `readonly unsafe` adds a
  write-once guarantee. Marking a property or event `unsafe` doesn't affect its backing field.

### The `safe` keyword

`safe` attests that a declaration is sound. Ordinary declarations are safe by default, so use it
only where the compiler requires a choice:

- every **`extern`** method, including `LibraryImport` partials,
- every instance field of a type with `LayoutKind.Explicit`, `[ExtendedLayout]`, or a nonzero
  `Pack`/`Size`.

For a backing field, annotate the auto-property or field-like event instead. Static and const
fields are exempt.

```csharp
[LibraryImport("libc")]
internal static safe partial int getpid();                 // no pointers: attest safe

[LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
internal static unsafe partial nint strlen(byte* str);      // dereferences a pointer: propagate
```

`safe` and `unsafe` can't both appear on one declaration. There's no `safe { }` block. Newer
compilers (after .NET 11.0.100) require `AllowUnsafeBlocks` for both declaration modifiers and
report CS9401 without it.

### `unsafe(expression)`

`unsafe(expression)` creates an unsafe context for exactly one expression, ending at the closing
parenthesis. Use it where a block can't go, or to keep the unsafe scope minimal:

```csharp
static readonly int Signature = unsafe(ReadSignature());       // field initializer
return await unsafe(ReadSignatureCoreAsync());                 // only the call is unsafe, not the await
int b = unsafe(c[null]);                                       // indexer with a pointer parameter (CS9363 otherwise)
```

It also works in constructor initializers and `catch (...) when (...)` filters. It requires
`AllowUnsafeBlocks` and the preview language version.

### Async and other details

- `await` is now allowed inside `unsafe` blocks, but **not inside a `fixed` statement**. Pointers
  kept alive across an `await` are your responsibility.
- **Cross-assembly behavior.** An opted-in assembly gets `[module: MemorySafetyRules(...)]`, and its
  requires-unsafe members get `[RequiresUnsafe]`. You can't apply either attribute yourself.

| Caller | Callee | Result |
| --- | --- | --- |
| Updated rules | Updated rules | `unsafe` markers flow through metadata. Calls need an unsafe context. |
| Updated rules | Original rules | Compat mode: any callee member with a pointer or function pointer in its signature is treated as requires-unsafe. |
| Original rules | Updated rules | The original pointer rules apply. A requires-unsafe member with no pointer in its signature is callable from safe code. |

### Breaking changes when moving to the preview version

- Lambdas can now bind to delegates with pointer parameters, so overloads can become ambiguous
  (CS0121). Fix this with explicit lambda parameter types: `M((int x) => { })`.
- `safe` becomes a modifier, so a type named `safe` needs `@safe`.
- Indexers and members with pointer parameters now need `unsafe` at the call site (CS9363).

---

## 8. Other compiler breaking changes in the .NET 11 SDK

Watch for these when you upgrade:

- `[with(x, y), z]` is now collection arguments, not a call to a method named `with`, at
  LangVersion 15 or later. Use `@with(x, y)` to call the method.
- `union Foo;` inside a type can now parse as a union declaration (CS9370). Use `@union`.
- A type named `closed` needs `@closed` (CS9380), and fields of that type may produce CS1519.
- A `Span<T>`/`ReadOnlySpan<T>` collection expression now has *declaration-block* safe-context.
  Assigning a loop-local `Span<int> items = [x];` to an outer `scoped` span is now an error. Use an
  array, or hoist the span out of the loop.
- `nameof(this.P)` inside an attribute is now an error. Write `nameof(P)`.
- `(X.Y) when` in a switch-expression arm now parses as the constant pattern `(X.Y)` followed by a
  `when` clause, not as a cast.
- `x && y`, where `x` is statically an interface type and `y` is `dynamic`, is now error CS7083.
  Cast `x` to a concrete type or to `dynamic`.
- Synthesized `ref readonly`-returning delegates and `ref readonly` local functions now need
  `System.Runtime.InteropServices.InAttribute` (CS0518 if it's missing).

---

## 9. Agent checklist

- [ ] Confirm the project is on C# 15 (`net11.0`, or an explicit `LangVersion` of 15). Don't add
      `latest`.
- [ ] Use `[with(...), ...]` instead of construct-then-add boilerplate when a capacity, comparer or
      constructor argument matters. Never use it with arrays or spans.
- [ ] Model "one of these types" with `union`. Model "one of my subclasses" with `closed`. Switch
      on the union or the closed base, not on `.Value` or `object`.
- [ ] In exhaustive switches, prefer explicit `null` arms over `_`, and always handle `null` when
      default union values or nullable inputs can arrive.
- [ ] Seal the leaves of closed hierarchies, and treat their subtype lists as public API.
- [ ] Hand-write a union with `TryGetValue`/`HasValue` for value-type cases on hot paths.
- [ ] Give each labeled loop a unique label in the method. `continue` never targets a `switch`.
- [ ] Don't use extension indexers on arrays or strings, or inside expression trees and
      `IQueryable` queries.
- [ ] Touch the memory-safety preview only when the user has opted in. Keep `unsafe` blocks
      minimal, and document every requires-unsafe member with `/// <safety>`.

## Diagnostic quick reference

| Diagnostic | Meaning in C# 15 context |
| --- | --- |
| CS8121 | A type pattern isn't compatible with any union case type (for example `pet is Pet`). |
| CS8509 | Switch not exhaustive: a missing union case, or an inaccessible closed-hierarchy subtype. |
| CS8655 | Union contents are maybe-null and there's no `null` arm. |
| CS8510 | Unreachable arm (a base arm after its subtypes, or `Car` before `Sedan`). |
| CS0121 / CS0411 | `with(...)` arguments don't pick overloads or infer type arguments. |
| CS9370 / CS9380 / CS1519 | `union`/`closed` used as an identifier. Escape it with `@`. |
| CS9363 | A member with pointer parameters (for example an indexer) called outside `unsafe` (preview). |
| CS9376 | A requires-unsafe parameterless constructor used where `new()`/`struct` is required (preview). |
| CS9392 | An explicit-layout field needs `safe`/`unsafe` (preview). |
| CS9401 / CS0227 | `safe`/`unsafe` modifiers or blocks used without `AllowUnsafeBlocks`. |
| `SwitchExpressionException` | Run time: a `default` union (null contents) reached a switch with no `null` arm. |

## Sources

- What's new in C# 15: https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-15
- Collection expressions (arguments): https://learn.microsoft.com/dotnet/csharp/language-reference/operators/collection-expressions#collection-expression-arguments
- Spec, collection expression arguments: https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-15.0/collection-expression-arguments
- Union types: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union
- Spec, unions: https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-15.0/unions
- `closed` modifier: https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/closed
- Patterns (closed hierarchy and union patterns): https://learn.microsoft.com/dotnet/csharp/language-reference/operators/patterns
- Spec, closed hierarchies: https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-15.0/closed-hierarchies
- Extension members (indexers): https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/extension#extension-indexers
- Spec, extension indexers: https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-15.0/extension-indexers
- Jump statements: https://learn.microsoft.com/dotnet/csharp/language-reference/statements/jump-statements
- Spec, labeled break/continue: https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-15.0/labeled-break-continue
- IDE0410: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0410
- Interface static members and runtime support: https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/interface#static-interface-members-and-runtime-support
- Unsafe code (updated memory safety model): https://learn.microsoft.com/dotnet/csharp/language-reference/unsafe-code
- Spec, memory safety: https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/unsafe-evolution
- Compiler options (enabling the memory safety rules): https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-options/language#enable-the-updated-memory-safety-rules
- Compiler breaking changes in .NET 11: https://learn.microsoft.com/dotnet/csharp/whats-new/breaking-changes/compiler%20breaking%20changes%20-%20dotnet%2011
- Roslyn language feature status: https://github.com/dotnet/roslyn/blob/main/docs/Language%20Feature%20Status.md
