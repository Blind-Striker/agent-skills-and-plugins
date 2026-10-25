# ask-deniz: .NET, Aspire, and Akka.NET routes

Read this file only for .NET work. Each section applies only when its named Module is installed;
the availability rules in ask-deniz's "Optional Modules" section apply to every route here.

## .NET work: General

Applies only when the `deniz-dotnet-general` Module is installed.

Pick by the requested outcome, not by how many files exist. These clusters map the entire Module;
bold pointers are user-started ceremonies, while the reference skills support authorized work.

| Situation | Route and boundary |
|---|---|
| New C# code or a public API | `dotnet-skills:csharp-coding-standards` for idioms; `dotnet-skills:csharp-api-design` for extend-only API and wire compatibility. For behavior-preserving changes use `dotnet:csharp-refactoring`, not idioms that change equality or copying. |
| Concurrency or reactive events | `dotnet-skills:csharp-concurrency-patterns` chooses the abstraction; `dotnet-skills:r3-reactive-extensions` covers R3 event streams rather than ordinary request/response I/O. |
| Serialization | `dotnet-skills:serialization` chooses formats and wire evolution; use `dotnet-skills:snapshot-testing` for human-reviewed output baselines, not email authoring. |
| Service composition | `dotnet-skills:microsoft-extensions-dependency-injection` groups registrations; `dotnet-skills:microsoft-extensions-configuration` handles Options and validation. |
| Database access | `dotnet-skills:efcore-patterns` owns EF usage; `dotnet-skills:database-performance` spans EF/Dapper data-access and SQL performance. |
| Web endpoints or telemetry | `dotnet-aspnetcore:dotnet-webapi` handles HTTP/OpenAPI endpoints, not Blazor or EF queries; `dotnet-skills:opentelementry-dotnet-instrumentation` instruments .NET. Keep an existing composition-root exporter, including Aspire ServiceDefaults, rather than adding a duplicate. |
| Nullable code or adoption | `dotnet-skills:csharp-nullable-reference-types` applies when NRT is already enabled. **`/dotnet-upgrade:migrate-nullable-references`** enables it or migrates a systematic warning set. |
| Publish AOT/trimmed, or adopt library compatibility | `dotnet-skills:aot-trimming` turns on an app's PublishAot/PublishTrimmed and handles code-in-scope warnings. **`/dotnet-upgrade:dotnet-aot-compat`** is the separate existing-library adoption/project-wide IL-warning ceremony; it does not publish binaries. |
| Repo layout, packages, or tools | `dotnet-skills:project-structure`, `dotnet-skills:package-management`, and `dotnet-skills:local-tools` cover daily conventions. New solutions may start with CPM; **`/dotnet-nuget:convert-to-cpm`** converts existing projects. A package update alone does not authorize conversion. |
| SDK container images | `dotnet-skills:sdk-container-publishing` covers PublishContainer/CI, not Dockerfiles, Aspire AppHost deploys, or containers used in tests. |
| Failed/slow build or build XML review | `dotnet:msbuild` is the entry: diagnose, capture/replay a binlog, or report review findings. Working legacy-project conversion is a different job: `dotnet-msbuild:msbuild-modernization`. |
| MSBuild specialists | `dotnet-msbuild:directory-build-organization` for shared-file layering; `dotnet-msbuild:property-patterns` for concrete property defects; `dotnet-msbuild:item-management` for item semantics; `dotnet-msbuild:including-generated-files` for physical artifacts missing from compilation/output. |
| Targets, scheduling, or output clashes | `dotnet-msbuild:target-authoring` writes/fixes target chains and Inputs/Outputs; `dotnet-msbuild:build-parallelism` needs multiple project files; `dotnet-msbuild:check-bin-obj-clash` diagnoses shared paths, not locks held by a running app. |
| Write or strengthen tests | **`/dotnet-test:code-testing`** scopes the test-writing ceremony; it does not repair flaky/failing tests. `deniz-dotnet-general:writing-tunit-tests` supplies TUnit API/syntax, and `dotnet-skills:cscheck` supplies property/model/Spec tests. Repair goes to the Process debugging route above. |
| Run tests, identify a runner, or rerun on edit | `dotnet-test:run-tests` owns exact commands and filters; `dotnet-test:platform-detection` identifies only; `dotnet-test:mtp-hot-reload` handles explicitly requested MTP console hot reload, not IDE Test Explorer. `dotnet-test:filter-syntax` is their consumer-only filter reference, not a separate ceremony. |
| Review test quality or behavioral gaps | `dotnet-test:test-anti-patterns` reports pragmatic test problems; `dotnet-test:test-gap-analysis` reports production changes the assertions would miss. Both are report-only and consume `dotnet-test:test-analysis-extensions`; do not turn the helper into a standalone audit. |
| Coverage evidence or CRAP | `dotnet-test:coverage-analysis` needs supplied coverage, an explicit collection request, or an explicit CRAP request. It is not a substitute for behavioral gap analysis. |
| Integration or UI tests | `dotnet-skills:testcontainers` provides real infrastructure without an Aspire AppHost; `dotnet-skills:playwright-blazor` covers Blazor UI testing, not Blazor authoring. Existing TUnit projects stay on TUnit. |
| Hard-to-test static dependencies | `dotnet-test:detect-static-dependencies` reports C#/.NET obstacles without edits. **`/dotnet-test:generate-testability-wrappers`** creates a missing seam; **`/dotnet-test:migrate-static-to-wrapper`** is a separately started bounded migration to that abstraction. Never automatically chain the two ceremonies. |
| Performance code review, measurement, or tuning | `dotnet-diag:analyzing-dotnet-performance` scans .NET code on a perf-audit request; `dotnet-diag:microbenchmarking` measures with BenchmarkDotNet; `dotnet-advanced:vectorization` authors/reviews SIMD specifically; `blazing-fast-dotnet:blazing-fast-dotnet` tunes a running workload: profiling with counters, traces, and `perf`, JIT codegen, runtime/GC/ThreadPool knobs, contention, and ASP.NET Core and NativeAOT settings. |
| Production diagnostic artifacts | **`/dotnet-diag:dotnet-trace-collect`** selects collection tools and captures performance evidence; **`/dotnet-diag:dump-collect`** collects modern .NET crash dumps. Neither performs analysis of the collected files. |
| Focused language/runtime work | `dotnet-advanced:csharp-scripts` needs an explicit request for C# without a project; `dotnet-advanced:dotnet-pinvoke` handles native interop; `dotnet-skills:ilspy-decompile` inspects compiled assemblies. |
| Linux/WSL HTTPS trust or old thread aborts | `dotnet-skills:dotnet-devcert-trust` repairs the exact dev-cert trust error. **`/dotnet-upgrade:thread-abort-migration`** replaces owned Thread.Abort patterns when moving to modern .NET, not every Thread API. |
| LLM change guardrail | `dotnet-skills:slopwatch` runs after .NET code changes. Baseline/tool installation still needs the skill's own approval; the audit does not authorize approving its own baseline. |

For delegated Roslyn incremental-generator diagnosis/design, the Module also supplies
`roslyn-incremental-generator-specialist`: an agent in Claude Code/OpenCode, a skill in Codex.
Use the specialist surface the harness actually offers, not a fabricated slash command.

## Aspire work

Applies only when the `deniz-dotnet-aspire` Module is installed.

Start AppHost/CLI work through `aspire:aspire`. Its downstream workflows are `aspire-init`
(no AppHost, skeleton), `aspireify` (resource wiring and TS toolchains), `aspire-orchestration`
(lifecycle and CLI migration), `aspire-deployment`, and `aspire-monitoring`. Let that router choose
the workflow; these descriptive names are not direct-load instructions. Ordinary build/test work
is not an Aspire lifecycle request.

**`/aspire:aspire-project-v2-migration`** is the user-started Project v2 resource rewrite, not CLI
entry-point migration. The additional specialists are `dotnet-skills:aspire-integration-testing`
for Aspire.Hosting.Testing and `dotnet-skills:aspire-service-defaults` for customizing the shared
project; a missing ServiceDefaults project comes from wiring or the installed template, not by hand.

## Akka.NET work

Applies only when the `deniz-dotnet-akka` Module is installed.

`dotnet-skills:akka-best-practices` supplies actor-system design rules;
`dotnet-skills:akka-hosting-actor-patterns` covers Hosting/entity actors and local/cluster seams;
`dotnet-skills:akka-management` covers discovery/bootstrap/health;
`dotnet-skills:akka-testing-patterns` covers actor tests; and
`dotnet-skills:akka-aspire-configuration` owns the Akka/Aspire cookbook. For delegated diagnosis,
`akka-net-specialist` is the Module's agent in Claude Code/OpenCode and a skill in Codex: use the
actual specialist surface rather than a fake slash command.
