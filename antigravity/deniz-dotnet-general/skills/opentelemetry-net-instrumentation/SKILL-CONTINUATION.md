## Performance Requirements

Instrumentation MUST be cheap by default. Follow these rules to minimize overhead:

### Zero-Allocation Fast Path

```csharp
// ✅ CORRECT: Guard with cheap checks
if (ActivitySource.HasListeners())
{
    using var activity = ActivitySource.StartActivity("Operation");
    // ... expensive work
}

// ✅ CORRECT: Use TagList (struct) for metrics
var tags = new TagList
{
    { "myapp.order_type", orderType },
    { "outcome", "success" }
};
counter.Add(1, tags);
```

### Timing

```csharp
// ✅ Timestamp math (no allocation)
var startTime = Stopwatch.GetTimestamp();
try { await ProcessAsync(); }
finally { var duration = Stopwatch.GetElapsedTime(startTime); metrics.OrderProcessingSucceeded(orderType, duration); }

// ❌ Allocates: Stopwatch.StartNew() or IDisposable timing wrappers
```

### Avoid Hidden Allocations

```csharp
// ❌ Allocates: string interpolation without IsAllDataRequested guard
activity?.SetTag("item", $"Processing {itemId}"); // ❌

// ✅ Guard expensive work behind IsAllDataRequested
if (activity?.IsAllDataRequested == true)
    activity.SetTag("item", $"Processing {itemId}");
```

**Rules**:
- No `Stopwatch.StartNew()` (use `Stopwatch.GetTimestamp()`/`GetElapsedTime`)
- Prefer `TagList` (struct) over arrays/dictionaries
- No LINQ, string interpolation, or async state machines in hot paths without guards

## Testing Requirements

### Span Tests

```csharp
[Test]
public async Task Should_create_processing_span_with_correct_parent()
{
    // Arrange
    using var parent = new Activity("Parent").Start();

    // Act
    await handler.Handle(item);

    // Assert
    var processingSpan = recordedActivities.Single(a => a.OperationName == "ProcessItem");
    Assert.AreEqual(parent.Id, processingSpan.ParentId);
    Assert.AreEqual("myapp.item_type", processingSpan.Tags.First().Key);
}

[Test]
public void Should_not_introduce_breaking_changes_to_span_names()
{
    // Ensures string values in span names are under test
    Assert.AreEqual("ProcessItem", MyFeature.SpanName);
}
```

**Rules**:
- Test which spans activities connect to
- Test string values (span names, tag names) to prevent breaking changes
- Remember: telemetry is part of public API

## Versioning

- Telemetry versioning decoupled from package version
- Use SemVer semantics
- Traces and Metrics use separate versions (evolve independently)
- Start with pre-1.0.0 version until adoption/usefulness proven

```csharp
private static readonly ActivitySource ActivitySource = new("MyApp.MyComponent", "0.9.0");
private readonly Meter meter = new("MyApp.MyComponent", "0.8.0");
```

## Logs

.NET logs integrate with OpenTelemetry through the built-in `ILogger` API. The OTel SDK provides `AddOpenTelemetry()` on the logging builder to collect, process, and export logs. Log records are automatically correlated with traces via `TraceId`/`SpanId`.

See [sdk-resources-and-logs-reference.md](sdk-resources-and-logs-reference.md) for full logs integration patterns including correlation, redaction, structured logging, and severity filtering.

## Reference Files

- [traces-and-propagation-reference.md](traces-and-propagation-reference.md): SpanKind deep dive with examples, Span Links (batch, scatter/gather, trace boundary), Context Propagation (W3C traceparent, DistributedContextPropagator, custom propagators), Baggage, and the full modern exception recording pattern.
- [metrics-and-instruments-reference.md](metrics-and-instruments-reference.md): All 7 metric instrument types with creation/recording code and when-to-use guidance, observable instrument callback patterns, dimensions deep dive, exemplars, aggregation defaults, and units.
- [sdk-resources-and-logs-reference.md](sdk-resources-and-logs-reference.md): SDK initialization (ASP.NET Core + Console), Resource configuration (ResourceBuilder, AddService, AddDetector, custom detectors), Exporters (OTLP, Console, Jaeger, Zipkin, Prometheus), Instrumentation Libraries, Sampling (built-in, custom, env vars, head/tail-based), Logs integration (ILogger, correlation, redaction, structured logging), and environment variable configuration.

## References

- [OTel Specification Overview](https://opentelemetry.io/docs/specs/otel/overview/)
- [OTel Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/)
- [OTel Common Spec (AnyValue)](https://opentelemetry.io/docs/specs/otel/common/)
- [OTel Tracing SDK Spec](https://opentelemetry.io/docs/specs/otel/trace/sdk/)
- [OTel Metrics SDK Spec](https://opentelemetry.io/docs/specs/otel/metrics/sdk/)
- [OTel Logs Spec](https://opentelemetry.io/docs/specs/otel/logs/)
- [OTel Resource Spec](https://opentelemetry.io/docs/specs/otel/resource/)
- [OTel Context Spec](https://opentelemetry.io/docs/specs/otel/context/)
- [.NET Observability with OpenTelemetry (MS Learn)](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)
- [OTel .NET Manual Instrumentation](https://opentelemetry.io/docs/languages/dotnet/instrumentation/)
- [OTel .NET Metric Instruments](https://opentelemetry.io/docs/languages/dotnet/metrics/instruments/)
- [OTel .NET Sampling](https://opentelemetry.io/docs/languages/dotnet/sampling/)
- [OTel .NET Zero-Code Instrumentation](https://opentelemetry.io/docs/zero-code/net/)