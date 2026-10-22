# .NET walls

Defaults; the coordinator may change them with a recorded reason.

| Wall | Command | Identity ratchet |
|---|---|---|
| Analyzers | `dotnet build -warnaserror -p:EnforceCodeStyleInBuild=true -p:AnalysisLevel=latest-recommended` (with `-warnaserror` off on the base run, to collect the base set) | compare (diagnostic id, file, message) from the binary or text log; a repository without its own analyzer set produces hundreds of findings on the base, so the ratchet is mandatory |
| Format | `dotnet format --verify-no-changes` on the touched files | new violations only |
| Clones | `any.md`, with `--format csharp` | base clone set |
| Other scanners | through .NET 10's ephemeral tool runner, `dnx <package>@<version>` (confirm the syntax on the installed SDK), pinned | per tool |

Traps:
- Verify commands set `DOTNET_CLI_USE_MSBUILD_SERVER=0`. A long-lived MSBuild server keeps the environment
  of the session that started it, so a restore can ignore the task's temp folder (seen: NuGet scratch hit a
  full per-user `/tmp` quota). A Gradle daemon is the same class of trap.
- Never point `DOTNET_CLI_HOME` at a fresh folder per run: each new CLI home can append to the user's
  registry `PATH` on Windows. Set `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` for workers, and never run
  two builds in one working tree (CS2012).
- `dotnet test --nologo` can make a test platform report zero tests with a non-zero exit; `total: 0` is
  never a pass.
- A `-p:` property on the command line is global and replaces each project's own value. `-p:NoWarn=...`
  wipes the projects' NoWarn lists, including documented experimental-API opt-ins, and breaks the build.
  Pass extra warning settings through an environment variable the projects append to.

A repository that has its own analyzer configuration, clone gate, or slop scanner names them in its
addendum; use those instead of the defaults.
