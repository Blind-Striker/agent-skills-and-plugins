# ask-deniz: .NET, Aspire, and Akka.NET routes

Read this file only for .NET work. Each section applies only when its named Module is installed;
the availability rules in ask-deniz's "Optional Modules" section apply to every route here.

## .NET work: General

Applies only when the `deniz-dotnet-general` Module is installed.

Pick by the requested outcome, not by how many files exist. These clusters map the entire Module;
bold pointers are user-started ceremonies, while the reference skills support authorized work.

| Situation | Route and boundary |
|---|---|
| New C# code or a public API | `$deniz-dotnet-general:modern-csharp-coding-standards` for idioms; `$deniz-dotnet-general:api-design` for extend-only API and wire compatibility. For behavior-preserving changes use `$deniz-dotnet-general:csharp-refactoring`, not idioms that change equality or copying. |
| Concurrency or reactive events | `$deniz-dotnet-general:csharp-concurrency-patterns` chooses the abstraction; `$deniz-dotnet-general:r3-reactive-extensions` covers R3 event streams rather than ordinary request/response I/O. |
| Serialization | `$deniz-dotnet-general:serialization` chooses formats and wire evolution; use `$deniz-dotnet-general:snapshot-testing` for human-reviewed output baselines, not email authoring. |
| Service composition | `$deniz-dotnet-general:dependency-injection-patterns` groups registrations; `$deniz-dotnet-general:microsoft-extensions-configuration` handles Options and validation. |
| Database access | `$deniz-dotnet-general:efcore-patterns` owns EF usage; `$deniz-dotnet-general:database-performance` spans EF/Dapper data-access and SQL performance. |
| Web endpoints or telemetry | `$deniz-dotnet-general:dotnet-webapi` handles HTTP/OpenAPI endpoints, not Blazor or EF queries; `$deniz-dotnet-general:opentelemetry-net-instrumentation` instruments .NET. Keep an existing composition-root exporter, including Aspire ServiceDefaults, rather than adding a duplicate. |
| Nullable code or adoption | `$deniz-dotnet-general:csharp-nullable-reference-types` applies when NRT is already enabled. **`$deniz-dotnet-general:migrate-nullable-references`** enables it or migrates a systematic warning set. |
| Publish AOT/trimmed, or adopt library compatibility | `$deniz-dotnet-general:aot-trimming` turns on an app's PublishAot/PublishTrimmed and handles code-in-scope warnings. **`$deniz-dotnet-general:dotnet-aot-compat`** is the separate existing-library adoption/project-wide IL-warning ceremony; it does not publish binaries. |
| Repo layout, packages, or tools | `$deniz-dotnet-general:dotnet-project-structure`, `$deniz-dotnet-general:package-management`, and `$deniz-dotnet-general:dotnet-local-tools` cover daily conventions. New solutions may start with CPM; **`$deniz-dotnet-general:convert-to-cpm`** converts existing projects. A package update alone does not authorize conversion. |
| SDK container images | `$deniz-dotnet-general:sdk-container-publishing` covers PublishContainer/CI, not Dockerfiles, Aspire AppHost deploys, or containers used in tests. |
| Failed/slow build or build XML review | `$deniz-dotnet-general:msbuild` is the entry: diagnose, capture/replay a binlog, or report review findings. Working legacy-project conversion is a different job: `$deniz-dotnet-general:msbuild-modernization`. |
| MSBuild specialists | `$deniz-dotnet-general:directory-build-organization` for shared-file layering; `$deniz-dotnet-general:property-patterns` for concrete property defects; `$deniz-dotnet-general:item-management` for item semantics; `$deniz-dotnet-general:including-generated-files` for physical artifacts missing from compilation/output. |
| Targets, scheduling, or output clashes | `$deniz-dotnet-general:target-authoring` writes/fixes target chains and Inputs/Outputs; `$deniz-dotnet-general:build-parallelism` needs multiple project files; `$deniz-dotnet-general:check-bin-obj-clash` diagnoses shared paths, not locks held by a running app. |
| Write or strengthen tests | **`$deniz-dotnet-general:code-testing`** scopes the test-writing ceremony; it does not repair flaky/failing tests. `$deniz-dotnet-general:writing-tunit-tests` supplies TUnit API/syntax, and `$deniz-dotnet-general:cscheck` supplies property/model/Spec tests. Repair goes to the Process debugging route above. |
| Run tests, identify a runner, or rerun on edit | `$deniz-dotnet-general:run-tests` owns exact commands and filters; `$deniz-dotnet-general:platform-detection` identifies only; `$deniz-dotnet-general:mtp-hot-reload` handles explicitly requested MTP console hot reload, not IDE Test Explorer. `$deniz-dotnet-general:filter-syntax` is their consumer-only filter reference, not a separate ceremony. |
| Review test quality or behavioral gaps | `$deniz-dotnet-general:test-anti-patterns` reports pragmatic test problems; `$deniz-dotnet-general:test-gap-analysis` reports production changes the assertions would miss. Both are report-only and consume `$deniz-dotnet-general:test-analysis-extensions`; do not turn the helper into a standalone audit. |
| Coverage evidence or CRAP | `$deniz-dotnet-general:coverage-analysis` needs supplied coverage, an explicit collection request, or an explicit CRAP request. It is not a substitute for behavioral gap analysis. |
| Integration or UI tests | `$deniz-dotnet-general:testcontainers-integration-tests` provides real infrastructure without an Aspire AppHost; `$deniz-dotnet-general:playwright-blazor-testing` covers Blazor UI testing, not Blazor authoring. Existing TUnit projects stay on TUnit. |
| Hard-to-test static dependencies | `$deniz-dotnet-general:detect-static-dependencies` reports C#/.NET obstacles without edits. **`$deniz-dotnet-general:generate-testability-wrappers`** creates a missing seam; **`$deniz-dotnet-general:migrate-static-to-wrapper`** is a separately started bounded migration to that abstraction. Never automatically chain the two ceremonies. |
| Performance code review, measurement, or tuning | `$deniz-dotnet-general:analyzing-dotnet-performance` scans .NET code on a perf-audit request; `$deniz-dotnet-general:microbenchmarking` measures with BenchmarkDotNet; `$deniz-dotnet-general:vectorization` authors/reviews SIMD specifically; `$deniz-dotnet-general:blazing-fast-dotnet` tunes a running workload: profiling with counters, traces, and `perf`, JIT codegen, runtime/GC/ThreadPool knobs, contention, and ASP.NET Core and NativeAOT settings. |
| Production diagnostic artifacts | **`$deniz-dotnet-general:dotnet-trace-collect`** selects collection tools and captures performance evidence; **`$deniz-dotnet-general:dump-collect`** collects modern .NET crash dumps. Neither performs analysis of the collected files. |
| Focused language/runtime work | `$deniz-dotnet-general:csharp-scripts` needs an explicit request for C# without a project; `$deniz-dotnet-general:dotnet-pinvoke` handles native interop; `$deniz-dotnet-general:ilspy-decompile` inspects compiled assemblies. |
| Linux/WSL HTTPS trust or old thread aborts | `$deniz-dotnet-general:dotnet-devcert-trust` repairs the exact dev-cert trust error. **`$deniz-dotnet-general:thread-abort-migration`** replaces owned Thread.Abort patterns when moving to modern .NET, not every Thread API. |
| LLM change guardrail | `$deniz-dotnet-general:dotnet-slopwatch` runs after .NET code changes. Baseline/tool installation still needs the skill's own approval; the audit does not authorize approving its own baseline. |

For delegated Roslyn incremental-generator diagnosis/design, the Module also supplies
`roslyn-incremental-generator-specialist`: an agent in Claude Code/OpenCode, a skill in Codex.
Use the specialist surface the harness actually offers, not a fabricated slash command.

## Aspire work

Applies only when the `deniz-dotnet-aspire` Module is installed.

Start AppHost/CLI work through `$deniz-dotnet-aspire:aspire`. Its downstream workflows are `aspire-init`
(no AppHost, skeleton), `aspireify` (resource wiring and TS toolchains), `aspire-orchestration`
(lifecycle and CLI migration), `aspire-deployment`, and `aspire-monitoring`. Let that router choose
the workflow; these descriptive names are not direct-load instructions. Ordinary build/test work
is not an Aspire lifecycle request.

**`$deniz-dotnet-aspire:aspire-project-v2-migration`** is the user-started Project v2 resource rewrite, not CLI
entry-point migration. The additional specialists are `$deniz-dotnet-aspire:aspire-integration-testing`
for Aspire.Hosting.Testing and `$deniz-dotnet-aspire:aspire-service-defaults` for customizing the shared
project; a missing ServiceDefaults project comes from wiring or the installed template, not by hand.

## Akka.NET work

Applies only when the `deniz-dotnet-akka` Module is installed.

`$deniz-dotnet-akka:akka-net-best-practices` supplies actor-system design rules;
`$deniz-dotnet-akka:akka-hosting-actor-patterns` covers Hosting/entity actors and local/cluster seams;
`$deniz-dotnet-akka:akka-net-management` covers discovery/bootstrap/health;
`$deniz-dotnet-akka:akka-net-testing-patterns` covers actor tests; and
`$deniz-dotnet-akka:akka-net-aspire-configuration` owns the Akka/Aspire cookbook. For delegated diagnosis,
`akka-net-specialist` is the Module's agent in Claude Code/OpenCode and a skill in Codex: use the
actual specialist surface rather than a fake slash command.
