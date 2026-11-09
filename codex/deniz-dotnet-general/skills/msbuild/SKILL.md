---
name: msbuild
description: "Use for MSBuild failure investigation, build-performance analysis
  and build-speedup claims, .binlog capture/replay, or review of existing build
  XML, imports, and extension hooks, even without a checkout. Activate for
  failing dotnet build/restore/pack/publish; slow evaluation, compilation,
  copying, or no-change builds; and custom targets that rerun, skip, or leave
  stale outputs. Legacy/non-SDK or packages.config conversions and framework/API
  upgrades are migrations: DO NOT activate. Exclude SDK installation alone, C#
  refactoring, runtime CPU profiling, assertions after successful builds, and
  non-MSBuild builds."
license: MIT
---

# MSBuild troubleshooting

For advice or static review, answer from the supplied XML, package facts, or measurements when
they settle the question: no reference reads, checkout discovery, commands, or invented verification.
For requested work, start at the supplied project or artifact; discover only what the next decision
needs. Read a reference only for a specific unresolved question. References are not invocable skills.
Ten narrow MSBuild specialists stay separate skills; the routing rows below name each one, and
when a row matches, load that skill instead of answering from a reference.

## Start with the task and evidence

Before editing, follow repository instructions and inspect the project/imports. Preserve the original
command, working directory, SDK, configuration, target framework, runtime identifier, global properties,
and target. Ask only for consequential information that cannot be discovered.

An existing `.binlog` can be the only available artifact. Analyze it before requesting a new
build; paths inside it do not prove a checkout exists locally.

For a mixed request, fix correctness before measuring successful builds, and keep authoring review
separate from a performance experiment. A failing test assertion after a successful build is not
an MSBuild failure.

**Capture only:** choose an unused artifact path under the supplied workspace, unless the user
specified another artifact directory. Do not put the only deliverable in a shared/global
temporary directory where workspace artifact collection will miss it, or stage/commit the log.
Append the binary logger to the exact supplied command, run it once, and save its original
exit code before checking the new log is nonempty.
Report the artifact path and original exit code, then stop: no replay, diagnosis, or repair.
Read [capture details](references/binlog-generation.md) only for an unresolved wrapper,
logger-option, toolset, or privacy question.

## 1. Troubleshoot build issues

**Goal:** identify the independent root causes and make the smallest repair, not a broad cleanup.

| Situation | Reference if needed | Action |
| --- | --- | --- |
| A build failed and a binary log exists | [binlog-failure-analysis](references/binlog-failure-analysis.md) | Query the log through a connected binlog MCP server, or replay it with MSBuild; connect errors to the responsible project instance, target, and task. Separate root causes from cascading failures. |
| The failure needs evidence and no matching log exists | [binlog-generation](references/binlog-generation.md), then [failure analysis](references/binlog-failure-analysis.md) | Capture the original invocation once and verify the artifact. Analyze it only when requested. |
| A project/build file has incorrect conditions, items, properties, or output paths | [msbuild-antipatterns](references/msbuild-antipatterns.md) | Select the relevant catalog entries and check their exceptions before changing anything. |
| Imports or build hooks are missing, overwritten, or run in the wrong order | [extension-points](references/extension-points.md) | Inspect the import contract, discovery order, and packed NuGet layout rather than hiding a required failure. |
| Outputs are stale or required work is skipped | [incremental-build](references/incremental-build.md) | Track input-set membership as well as file timestamps; apply the repair contract below. |
| "Cannot create a file when that file already exists", "being used by another process", failures that pass on retry, or overwritten outputs across projects, TFMs, or RIDs | Load the `$deniz-dotnet-general:check-bin-obj-clash` skill | Find the project instances that share an `OutputPath` or `IntermediateOutputPath` before changing any path. |
| A generated file is missing from compilation or output | Load the `$deniz-dotnet-general:including-generated-files` skill | Check target timing and evaluation-time globs before adding items. |

For build-wiring-only work, generator and source inputs are **read-only throughout**, not restored
after tests; edit the original only as the requested repair requires.
Allocate disposable scratch as a uniquely named directory **outside the project tree**, normally
the OS temporary directory from `mktemp -d`; a copy inside the project can be matched by SDK
default globs and break the later confirming build. Print the path once and write every copy, log,
and hash under it: no fixed leaf names, pointer files, pre-deleting, or globbing to recover it.
After the checks, remove exactly that path (never a pattern) and keep it in the report.
Copy the project/input tree and required imports; run every scenario that adds, deletes, empties,
or retimes inputs, or deletes `obj`/outputs, with **the copy as working directory**. A backup is not
a verification copy, nor is an original project already under a temporary path. Compare original
hashes before/after; equal final hashes do not excuse intermediate mutation. A confirming build of
the repaired original must not delete outputs. If the copy cannot reproduce the environment, report
blocked checks.

For input-set changes, prefer a persisted membership list written with `WriteLinesToFile` and
`WriteOnlyWhenDifferent`, plus actual files in `Inputs`. A path list is not a content hash.
Verify first/unchanged builds, deletion, older addition, empty inputs, and missing-output recovery
on the copy. End the repair report, however brief, with three labeled explanations, not only a checklist:

- **Cause:** state both blind spots: a deleted input vanishes from the timestamp comparison; an
  added input older than the output never looks newer; either skips required regeneration.
- **Mechanism:** identify the membership artifact and how its changes invalidate the output.
  Unchanged membership preserves skips; actual file timestamps still catch ordinary edits.
- **Checks:** list passed, failed, and not-run separately with the command or path used. "None
  failed" requires every command to exit 0; an errored script, even if fixed and rerun, is listed
  failed with its cause. If the main scenario passed but a later assertion failed, report both;
  never summarize as verified.

After a repair, rerun the original failing scenario and check affected dependents. If only a log
is available, provide an evidence-backed proposed fix and say it was not applied or rebuilt.
Do not turn a targeted failure investigation into solution-wide authoring cleanup.

## 2. Diagnose and improve build performance

**Goal:** measure the original workload, not an easier replacement.

For advice, give the verdict, decisive facts, and a concrete next step with equivalent successful
builds and required outputs. Do not re-prove a supplied timeline or read references to restate it.
Do not infer warm/no-op state or assign any part of a confounded timing gap to restore, cache, or
setting effects without isolating that cause.

| Supplied evidence | Decision | Smallest useful follow-up |
| --- | --- | --- |
| Parallel target totals include overlapping dependency waits | Keep required references and parallelism. Cumulative waits can exceed both wall time and the duration of one dependency task; they do not prove CPU utilization or healthy scheduling. | Follow the supplied timeline to the dependency task holding up consumers. For `Csc`, inspect compiler inputs and invocation, analyzer/source-generator timing, and whether compilation was expected for this workload. Measure CPU/node utilization or I/O contention if unknown; do not infer them from wait totals. Validate any change with equivalent successful builds and required outputs. |
| Commands, restore policy, or starting input/output states differ | Reject attribution to the setting, not the possibility of a real gain. | Give one matched old-setting/new-setting experiment below, not a menu of different workloads or an invented setting name. |
| Repeated successful runs have matched conditions and outputs | Accept the observed improvement for that workload; use the same summary statistic for both arms: `100 * (old - new) / old`. | Report sample count and observed elapsed-time ranges, not an invented percentage uncertainty interval. Then state residual limits: small sample, one workload/machine/SDK, what restored starting state does and does not control, and no independent rerun. Do not require a checkout to acknowledge the supplied result. |

When a minimal attribution experiment is needed, return **one two-arm table**, not alternatives:

1. Bind the treatment to the user's identified setting and old/new values. If unidentified, retain
   literal `<setting>`, `<old>`, `<new>` placeholders. Differing commands are confounds, not setting names.
   Never substitute restore policy, Build/Rebuild, or cache state for an unidentified setting.
2. Expand the **full original baseline command in both arms**. Only an explicitly identified
   command-line treatment may change its own argument; otherwise vary its configured value.

| Arm | Treatment, supplied or placeholder | Controlled invocation and workload |
| --- | --- | --- |
| A | `<setting> = <old>` | Full original baseline command; input and starting outputs exactly as supplied |
| B | `<setting> = <new>` | Same full command, input, and starting outputs; only the named treatment differs |

3. Match non-treatment SDK, machine, configuration, instrumentation, target, restore policy,
   and input/output state exactly as supplied. Add no source edit the original workload lacked
   (keep one it had in both arms). **Matching both arms is not enough if both replace the original
   workload.** When they are controls, retain supplied Rebuild and timed restore; do not prime,
   warm, or move work outside the timer unless the original workload already did.
4. Alternate matched pairs; compare wall-time medians and observed ranges; check required outputs
   and diagnostics. Scope the result to that workload. The table is the only experiment; the last
   sentence states a validity check or residual limit, not an offer or variant.

For optimization work, establish the relevant successful baseline before changing one supported cause:

| Unresolved measurement | Reference if needed |
| --- | --- |
| Cold-output, changed-input, no-change, restore/cache state, or Build versus Rebuild | [build-perf-baseline](references/build-perf-baseline.md) |
| Unknown task cost, compiler/analyzer/reference/restore work, or overlapping waits | [build-perf-diagnostics](references/build-perf-diagnostics.md) |
| Pre-target globs, imports, property evaluation, or excess project instances | [eval-performance](references/eval-performance.md) |
| Copies/output I/O, unchanged copies, or a modified destination | [copy-to-output-directory](references/copy-to-output-directory.md) |
| Unexpected regeneration/skips, added/removed inputs, or missing outputs | [incremental-build](references/incremental-build.md) |
| Two or more project files build together and `-m` does not scale, nodes sit idle, or a serial ProjectReference chain sets the critical path | Load the `$deniz-dotnet-general:build-parallelism` skill |
| One invocation runs the same task, `Exec`, or project build more often than the work needs: per-project probes, a dependency built under several global-property sets, one process per item, or a run-once guard that races under `-m` | Load the `$deniz-dotnet-general:find-repeated-work-within-one-build` skill |
| Several steps of one build script or pipeline (build, test, publish, pack) each restore, or `--no-restore` fails with NETSDK1004 or NETSDK1047 | Load the `$deniz-dotnet-general:eliminate-redundant-restores` skill |

Stop when the suspected bottleneck is not supported by evidence, results fall within noise, or
correctness regresses. Report missing measurements rather than manufacturing a complete baseline.

## 3. Review build authoring

**Goal:** preserve build contracts, not implicitly migrate frameworks, packages, languages, or project systems.
A review or audit only reports its findings; edit files only when the user asks for fixes.

**Check evaluation phases, not just import order.** Outside targets, properties/imports are
evaluated before items. A framework set in the project body can be unavailable to an early
property condition in `Directory.Build.props` but available to an item condition in that same
file. Move only the premature property assignment after its dependency; do not relocate an
intentionally project-owned framework or a working item declaration. Preserve F# compile order
and inspect SDK defaults before adding switches to suppress supposedly duplicated items.

| Requested change | Reference if needed | Preserve |
| --- | --- | --- |
| Review/clean up project files, shared properties, item declarations, or custom targets | [msbuild-antipatterns](references/msbuild-antipatterns.md) | Intentional overrides, F# source order, required imports, package assets, and actual input/output contracts. |
| Review/improve hooks, imports, shared-file discovery, or NuGet build extensions | [extension-points](references/extension-points.md) | Prior hooks, evaluation order, direct/transitive consumer behavior, and the packed layout. |
| Restructure generation or copying as part of cleanup | [incremental-build](references/incremental-build.md) and [copy-to-output-directory](references/copy-to-output-directory.md), as applicable | First-build and no-change behavior, invalidation, generated-item registration, and output completeness. |
| Write or fix a custom target, its `Inputs`/`Outputs`, its `DependsOnTargets`/`BeforeTargets`/`AfterTargets` ordering, a `$(XxxDependsOn)` chain, or `Outputs` versus `Returns` | Load the `$deniz-dotnet-general:target-authoring` skill | The SDK target chain and every existing extension of it. |
| A concrete property defect: a condition, default, append versus overwrite, last-write-wins value, OS/TFM check, or path | Load the `$deniz-dotnet-general:property-patterns` skill | Intentional overrides and evaluation order. |
| A concrete item operation: `Include`, `Remove`, `Update`, metadata, transforms, batching, or duplicate items | Load the `$deniz-dotnet-general:item-management` skill | SDK default globs and item identity. |
| Organize shared settings across projects, or fix a `Directory.Build.props`/`.targets`/`.rsp` layering defect | Load the `$deniz-dotnet-general:directory-build-organization` skill | Props-versus-targets placement and per-project overrides. |

A requested conversion of a legacy project to SDK style, or of `packages.config` to
`PackageReference`, is a project-system migration, not this route: load the
`$deniz-dotnet-general:msbuild-modernization` skill.

Record the existing contract; a matching catalog pattern alone is not a defect. Leave correct code
alone. Validate authorized changes against original configurations, consumers, and affected build behavior.

Keep optional and required imports distinct, including inside aggregation files:

```xml
<Import Project="optional.targets" Condition="Exists('optional.targets')" />
<Import Project="required.targets" />
```

The required import must load or fail, not silently skip. Explain why a condition on an entire
mixed path list can suppress a valid required import; semicolon-separated imports themselves
are supported. Check optional present/absent crossed with required present/absent, preserving
order when both exist. When checks run, report the observed order and failure cases, not only
"verification passed." Do not replace configurable paths with hard-coded values unnecessarily.

For a packaged forwarder, keep these bindings separate:

| Binding | Meaning |
| --- | --- |
| `NuspecFile` | Selects the XML manifest, possibly outside the project; not a payload mapping. |
| `NuspecBasePath` | Base directory for the manifest's `file src` lookups on disk; it neither locates nor selects the manifest. |
| Mapping `src` / `target` | Reads the source file and emits its mapped archive path; does not implicitly ship the source path too. |
| Consumer import | Resolves from the importing file inside the **package**, not source, feed, or the consumer's framework. |

Do not substitute the manifest path for a mapping's `src`. For a no-change verdict, explain how
`NuspecFile` selection, `NuspecBasePath` src lookup, mapping, packed target, and relative import connect, and why guarding
a required import would hide a broken package. If supplied package facts settle this, stop:
no `Exists` guard, reorganization, or confirmatory rebuild/repack. Inspect artifacts only when
verification is requested or contents remain unknown.

## Shared evidence and safety

- For analysis, read the log through a connected binlog MCP server or
  [MSBuild replay](references/binlog-failure-analysis.md#replay-a-binary-log),
  never binary-file text parsing. Replay success and replay duration are not the recorded
  build's result or duration.
- Resolve these files relative to this skill. Do not search other installations or checkouts for
  MSBuild guidance; the only other skills this one hands work to are the specialists its routing
  rows name. After a failed reference read, allow one listing of `references`
  to distinguish a missing bundled file from a present-but-unreadable tool integration failure.
  Report the error and reduced coverage; do not retry through shell commands or other readers,
  and do not pretend unread guidance was followed. Never work around a policy denial.
- Do not clean outputs/caches, stop shared build servers, disable diagnostics, change SDKs, or
  serialize builds as generic fixes. Scope any necessary destructive experiment to approved,
  owned outputs. Keep binary and replayed logs local unless sharing is authorized.
- Distinguish recorded facts from hypotheses. Missing source, log detail, tools, or platform
  support must be reported explicitly, not replaced with assumed values or invented verification.

## Completion

Advice: verdict, decisive evidence, necessary next measurement; no manufactured verification.
Repository work: cause/decision, minimal change (or no change), and exact successful, failed,
and not-run checks. Performance claims also need comparable measurements and uncertainty.
Scale length to the task, but never drop a required report field.
