## Generic specialization via static abstract members

Static abstract interface members let you specialize a generic method for values supplied by struct tags, without duplicating its source code. The JIT generates separate native versions of `Big<C42>` and `Big<C100>`. In optimized code, it can inline the small `Value` getters and fold expressions and branches that use `T.Value`. This works even when `Big<T>` itself is too large to inline; The tradeoff is increased native code size. `T.Value` is not a C# compile-time constant: constant folding depends on JIT optimizations and getter inlining.

```cs
using System.Runtime.CompilerServices;

interface IConstant {
    static abstract int Value { get; }
}

readonly struct C42 : IConstant {
    public static int Value => 42;
}

readonly struct C100 : IConstant {
    public static int Value => 100;
}

static class Example
{
    static int Big<T>(int x) where T : struct, IConstant
    {
        int arg = T.Value;

        // The large body would go here.

        return arg == 42 ? x * arg : x + arg;
    }

    static int Use42(int x) => Big<C42>(x);
    static int Use100(int x) => Big<C100>(x);
}
```

It is also handy for scenarios where you want to leverage different vector sizes or constants in a generic way, allowing the JIT to generate optimized code for each specialization without duplicating the method's source code. Example: summing an array using different vector widths with static abstract members.

```cs
using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

class Program
{
    static void Main()
    {
        int[] values = new int[1003];

        if (Vector128.IsHardwareAccelerated)
            Console.WriteLine(Sum<Vector128<int>, Ops128>(values));
        if (Vector256.IsHardwareAccelerated)
            Console.WriteLine(Sum<Vector256<int>, Ops256>(values));
        if (Vector512.IsHardwareAccelerated) 
            Console.WriteLine(Sum<Vector512<int>, Ops512>(values));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Sum<V, TOps>(ReadOnlySpan<int> s)
        where V : struct
        where TOps : struct, IOps<V>
    {
        V acc = default;
        int count = TOps.Count;

        while (s.Length >= count)
        {
            acc = TOps.Add(acc, TOps.Load(s));
            s = s.Slice(count);
        }

        int sum = TOps.Sum(acc);
        foreach (int x in s) sum = unchecked(sum + x);
        return sum;
    }
}


interface IOps<V> where V : struct
{
    static abstract int Count { get; }
    static abstract V Load(ReadOnlySpan<int> s);
    static abstract V Add(V a, V b);
    static abstract int Sum(V v);
}

readonly struct Ops128 : IOps<Vector128<int>>
{
    public static int Count => Vector128<int>.Count;
    public static Vector128<int> Load(ReadOnlySpan<int> s) => Vector128.Create(s);
    public static Vector128<int> Add(Vector128<int> a, Vector128<int> b) => a + b;
    public static int Sum(Vector128<int> v) => Vector128.Sum(v);
}

readonly struct Ops256 : IOps<Vector256<int>>
{
    public static int Count => Vector256<int>.Count;
    public static Vector256<int> Load(ReadOnlySpan<int> s) => Vector256.Create(s);
    public static Vector256<int> Add(Vector256<int> a, Vector256<int> b) => a + b;
    public static int Sum(Vector256<int> v) => Vector256.Sum(v);
}

readonly struct Ops512 : IOps<Vector512<int>>
{
    public static int Count => Vector512<int>.Count;
    public static Vector512<int> Load(ReadOnlySpan<int> s) => Vector512.Create(s);
    public static Vector512<int> Add(Vector512<int> a, Vector512<int> b) => a + b;
    public static int Sum(Vector512<int> v) => Vector512.Sum(v);
}
```

# ASP.NET Core

Code-level advice for HTTP throughput benchmarks.

- Start from `WebApplication.CreateSlimBuilder(args)` (or `CreateEmptyBuilder` + `builder.WebHost.UseKestrelCore()`) and add only what's needed. Every middleware component (HTTPS redirection, auth, CORS, response compression, static files, developer exception page) costs something per request.
- Logging: without an `appsettings.json` that raises `Microsoft.AspNetCore` to `Warning`, the default level is `Information` and the host logs every request to the console - a massive bottleneck. Call `builder.Logging.ClearProviders()` or raise the level.
- JSON: register source-generated metadata with `builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));`. For hand-written responses, serialize with a reused `Utf8JsonWriter` (`writer.Reset(response.BodyWriter)`) instead of allocating one per request.

# Magic knobs to Tune .NET Performance

> [!WARNING]
> It is highly recommended to keep the default settings unless you have measured and understood the performance implications of changing them. 

Most of these knobs are relevant only for server workloads or performance-critical scenarios. They're not applicable for general-purpose libraries.

⭐ - known to have a visible impact on performance (typically, in synthetic benchmarks).

| Environment variable (CoreCLR Release, .NET 10.0+) | Default | Values to try | Purpose and trade-offs |
| --- | --- | --- | --- |
| ⭐`DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS` | `0` | `1` | Run I/O continuations directly on poller threads instead of dispatching them to workers - beneficial for workloads with many small requests. ⚠️ Blocking or lengthy continuations can stall I/O ⚠️ |
| ⭐`DOTNET_ThreadPool_UnfairSemaphoreSpinLimit` | `70` in .NET 10.0; `1024` in current .NET 11.0 | `0`, `0x400`, `0xC00` | Trade worker wake latency against CPU spent spinning. `0` might be useful as a mitigation for high CPU usage (especially on arm64) at the cost of throughput. Large values might improve synthetic benchmark performance. |
| ⭐`DOTNET_GCDynamicAdaptationMode` | `1` | `0` | Disable DATAS for server GC. Can improve throughput at the expense of memory; compare against tuning DATAS (e.g. `DOTNET_GCDGen0GrowthPercent`) rather than disabling it. |
| ⭐`DOTNET_HillClimbing_Disable` | `0` | `1` | Disable ThreadPool hill climbing (improves time to steady state). |
| `DOTNET_SYSTEM_NET_SOCKETS_THREAD_COUNT` | Automatic; processor count when inline completions are enabled | `1`, `2`, `4`, `8`, up to processor count | Tune I/O poller count. Fewer pollers can reduce scheduling and cache overhead; too few can serialize processing. Values are decimal. |
| `DOTNET_gcServer` | varies by environment | `1` | Typically enabled by default for server workloads. Might improve the performance of desktop/console tools. |
| `DOTNET_GCDTargetTCP` | Effective target `2%` (configuration `0`) | `1` | Lower DATAS's target throughput-cost percentage. Encourages more heaps when GC cost exceeds the target. Only relevant with DATAS enabled. |
| `DOTNET_GCDGen0GrowthPercent` | Effective multiplier `100%` (configuration `0`) | `0xC8` | Scale DATAS's gen0 budget multiplier to 200% of its default. Trades memory for potentially fewer collections. Only relevant with DATAS enabled. |
| `DOTNET_TC_CallCountingDelayMs` | `100` ms; longer on a single processor | `0` | Only for short-lived console tools where JIT warmup takes too long: remove the tiering delay while retaining the normal call-count threshold. Can increase compilation work early in execution. |
| `DOTNET_TieredCompilation` | `1` | `0` | Alternative for short-lived console tools where JIT warmup takes too long: disable tiering and JIT optimized code on first use. Removes tiered warmup but disables dynamic PGO and can increase initial JIT cost. |

Using NativeAOT or disabling tiered compilation via `DOTNET_TC_CallCountingDelayMs=0`/`DOTNET_TieredPGO=0`/`DOTNET_TieredCompilation=0` may significantly improve performance for short-lived console tools or benchmarks that measure the entire process rather than just steady-state execution.

# NativeAOT-specific tuning

Recommended reading: Microsoft's [Optimizing AOT deployments](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/optimizing) article, plus the runtime's [CPU instruction-set tuning guide](https://github.com/dotnet/runtime/blob/9da219572c1/src/coreclr/nativeaot/docs/optimizing.md). The latter describes implementation-specific options that can change between SDK versions.

> [!NOTE]
> These are publish-time MSBuild properties: republish for each experiment. NativeAOT has no JIT or tiered compilation, so the CoreCLR JIT/tiering environment variables above do not apply.

| MSBuild setting | Purpose and trade-offs |
| --- | --- |
| `-p:OptimizationPreference=Speed` | Optimize for execution time (`--Ot`) instead of the default size/speed balance. A good throughput experiment, but can increase executable size and instruction-cache pressure. |
| `-p:OptimizationPreference=Size` | Optimize for executable size (`--Os`). Useful when distribution size or memory footprint matters more than peak throughput; measure the speed trade-off. |
| `-p:IlcInstructionSet=native` | Target instruction sets supported by the build machine's CPU. Useful for local benchmarks or homogeneous deployments; no cross-architecture compilation support, and the executable may not run on older CPUs. Use an explicit instruction-set baseline for heterogeneous deployments. |
| `-p:IlcInstructionSet=armv8.1-a` | If `IlcInstructionSet=native` is not acceptable for you, consider explicitly raising the baseline from ARMv8.0-A to ARMv8.1-A to include LSE (crucial for atomics-heavy workloads). |

```sh
dotnet publish -c Release -r linux-arm64 -p:PublishAot=true -p:OptimizationPreference=Speed -p:IlcInstructionSet=native
```

# General notes

- Consider filing a [dotnet/runtime](https://github.com/dotnet/runtime/issues) issue only for a clear, reproducible missed optimization in the JIT or libraries with meaningful measured impact. Check for duplicates, include a minimal repro and measurements, and obtain the user's approval before filing.
- Compare equivalent execution plans before blaming the language. Consider pairwise comparisons, stronger filtering, and verification requirements before tuning code generation.
- Don't draw conclusions about workloads from tiny synthetic benchmark results.