---
name: migrate-nullable-references
description: >
  Enable nullable reference types in a C# project and systematically resolve all
  warnings. USE FOR: adopting NRTs in existing codebases, file-by-file or
  project-wide migration, fixing CS8602/CS8618/CS86xx warnings, annotating APIs
  for nullability, cleaning up null-forgiving operators, upgrading dependencies
  with new nullable annotations. DO NOT USE FOR: projects already fully migrated
  with zero warnings (unless auditing suppressions), fixing a handful of
  nullable warnings in code that already has NRTs enabled, suppressing warnings
  without fixing them, C# 7.3 or earlier projects. INVOKES:
  Get-NullableReadiness.ps1 scanner script.
disable-model-invocation: true
license: MIT
---

# Nullable Reference Migration

Enable C# nullable reference types (NRTs) in an existing codebase and systematically resolve all warnings. The outcome is a project (or solution) with `<Nullable>enable</Nullable>`, zero nullable warnings, and accurately annotated public API surfaces — giving both the compiler and consumers reliable nullability information.

## When to Use

- Enabling nullable reference types in an existing C# project or solution
- Systematically resolving CS86xx nullable warnings after enabling the feature
- Annotating a library's public API surface so consumers get accurate nullability information
- Upgrading a dependency that has added nullable annotations and new warnings appear
- Analyzing suppressions in a code base that has already enabled NRTs to determine whether they can be removed

## When Not to Use

- The project already has `<Nullable>enable</Nullable>` and zero warnings — the migration is done unless the user wants to re-examine suppressions with a view to removing unnecessary ones (see Step 6)
- The user only wants to suppress warnings without fixing them (recommend against this)
- The code targets C# 7.3 or earlier, which does not support nullable reference types

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project or solution path | Yes | The `.csproj`, `.sln`, or build entry point to migrate |
| Migration scope | No | `project-wide` (default) or `file-by-file` — controls the rollout strategy |
| Build command | No | How to build the project (e.g., `dotnet build`, `msbuild`, or a repo-specific build script). Detect from the repo if not provided |
| Test command | No | How to run tests (e.g., `dotnet test`, or a repo-specific test script). Detect from the repo if not provided |

Read [the remaining instructions](SKILL-CONTINUATION.md) to continue.
