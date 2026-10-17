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
- `dotnet test --nologo` can make a test platform report zero tests with a non-zero exit; `total: 0` is
  never a pass.

A repository that has its own analyzer configuration, clone gate, or slop scanner names them in its
addendum; use those instead of the defaults.
