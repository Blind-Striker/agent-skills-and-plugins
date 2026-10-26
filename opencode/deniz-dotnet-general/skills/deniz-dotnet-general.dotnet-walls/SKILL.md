---
name: deniz-dotnet-general.dotnet-walls
description: Walls for a .NET change in a repository without its own quality
  gates — pinned, ephemeral analyzer, format, clone and scanner commands for the
  verify step, their identity ratchet, and the build traps. Use during a walls
  preflight, or when a worker's verify step needs .NET quality gates.
---

# .NET walls

Defaults; the coordinator may change them with a recorded reason. Run every wall on the base first and
gate only findings that are new by identity, never by line number. A repository that names its own
analyzer configuration, clone gate or slop scanner in its agent instructions uses those instead.

| Wall | Command | Identity ratchet |
|---|---|---|
| Analyzers | `dotnet build -warnaserror -p:EnforceCodeStyleInBuild=true -p:AnalysisLevel=latest-recommended` (with `-warnaserror` off on the base run, to collect the base set) | compare (diagnostic id, file, message) from the binary or text log; a repository without its own analyzer set produces hundreds of findings on the base, so the ratchet is mandatory |
| Format | `dotnet format --verify-no-changes` on the touched files | new violations only |
| Clones | a clone detector with C# support, for example `npx -y jscpd@5.4.1 --format csharp` | base clone set |
| Other scanners | through .NET 10's ephemeral tool runner, `dnx <package>@<version>` (confirm the syntax on the installed SDK), pinned | per tool |

## Traps

- Run verify commands and workers with `DOTNET_CLI_USE_MSBUILD_SERVER=0`. A long-lived MSBuild server
  keeps the environment of the session that started it, so a restore can ignore the task's temp folder
  (seen: NuGet scratch hit a full per-user `/tmp` quota). Run one build per working tree at a time
  (CS2012).
- Keep `DOTNET_CLI_HOME` where it is: each new CLI home can append a tools folder to the user's registry
  `PATH` on Windows. Give workers `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false`.
- `dotnet test --nologo` can make a test platform report zero tests with a non-zero exit; `total: 0` is
  never a pass.
- A `-p:` property on the command line is global and replaces each project's own value. `-p:NoWarn=...`
  wipes the projects' NoWarn lists, including documented experimental-API opt-ins, and breaks the build.
  Pass extra warning settings through an environment variable the projects append to.
