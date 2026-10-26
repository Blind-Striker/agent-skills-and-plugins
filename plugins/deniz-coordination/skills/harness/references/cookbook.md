Read when a worker's build, test or tool fails under the fence or sandbox, or before the first task in
an ecosystem the harness has not run yet.

# Cookbook

The harness supports no language. It has generic knobs, and a tool works under it through
configuration alone. Every recipe below is that configuration, labelled with where it was tried.
A recipe is a starting point, never a guarantee: verify it once in your own environment, and record
the recipe beside the configuration that uses it.

## Method

1. Run the task once, fenced, and read its `verify.log` and `final.md`.
2. Name what failed: a download, a write outside the workspace and task temp (a user cache, a tool
   home, a global temp folder), a path or socket length, or a long-lived server process.
3. Pick the knob:

   | Failure | Knob |
   |---|---|
   | A download | `prepare` fetches outside the sandbox; the worker builds and tests offline ([task files](task-file.md#preparation)) |
   | A write to a user cache | the tool's own setting for a workspace-local cache (git-ignored), else a [writable root](config.md#writable-roots) |
   | A long temp or socket path | a short [`tempRoot`](config.md#temporary-folders), with short run and task ids |
   | A step the worker repeats on every run | a `prepare` command that warms it once |

4. Put the knob in the layer it belongs to: user config for machine paths, the repository's
   `.dispatch/config.jsonc` for the repository's own needs, the task file for one run.

A writable root is trusted: code a worker writes there runs unfenced later. Grant only the caches a
task needs.

## Recipes

### .NET

Tried: Codex elevated sandbox on Windows, and the opencode fence on Linux and macOS (0.1.5).

- `prepare`: `dotnet restore`, `dotnet tool restore`. The worker builds with `dotnet build
  --no-restore` and tests with `dotnet test --no-build`.
- Codex on Windows, elevated sandbox: a restore inside the sandbox that downloads packages fails with
  NU1301 (an SChannel error); after the restore in `prepare`, builds and tests work, project references
  and Microsoft.Testing.Platform included (measured once). The unelevated sandbox failed on the NuGet
  cache, MSBuild temp, named pipes and project references, often without diagnostics.
- Package location: the harness removes `NUGET_*` variables from `prepare` and the worker, so a
  `NUGET_PACKAGES` set in your shell has no effect. Packages restored to the default
  `~/.nuget/packages` are readable inside both sandboxes. A project-local `RestorePackagesPath` in
  `Directory.Build.props` (a git-ignored folder such as `.pkgs/`) makes restore and build agree without
  an environment variable.
- Build-time cache writes on Linux and macOS: generate writable roots from the repository root, then
  copy them into `preferences`:

  ```sh
  jq -n --arg home "$HOME" '{writableRoots: [
    ($home + "/.nuget/packages"), ($home + "/.local/share/NuGet"), ($home + "/.nuget/NuGet")
  ]}'
  ```

  Add the repository's own test-run folders when its tests write outside the workspace. When the lab
  already supplies an isolated `DOTNET_CLI_HOME` or package folder, grant those folders instead, and
  pass the package folder to the build (`-p:RestorePackagesPath='<absolute folder>'`). When opencode keeps
  its credential `XDG_DATA_HOME` outside the lab, scope `XDG_DATA_HOME="$HOME/.local/share"` to the
  .NET command alone, with `HOME` still the lab's isolated home; the backends' credential homes stay
  where they are.
- Test host socket: a test host on Microsoft.Testing.Platform opens a Unix socket about 55 bytes below
  `TMPDIR`, against a cap near 108 bytes (104 on macOS), and fails with `The named pipe path ... is too
  long`. Give such tasks a `tempRoot` such as `/var/tmp/d`; the run id, task id and root share about
  50 bytes.
- File-based apps (`dotnet run --file x.cs`), tried by a lane: warm the build in `prepare`
  with `dotnet run --file x.cs -- --help`. File-based commands take no `--no-restore`.

### Node

Tried: `npm` with a workspace-local cache, the opencode fence (0.1.5).

- `prepare`: `npm ci` (or `pnpm install --frozen-lockfile`), with `cache=.npm-cache` in the workspace
  `.npmrc` and `.npm-cache/` git-ignored. The worker tests with `npm test --offline`.
- On a base a worker wrote, prefer `npm ci --ignore-scripts`: `prepare` runs install scripts with
  your rights.
- Bun's install cache: a writable root at `~/.bun/install/cache`.
- Nx in a worktree, reported from a Codex run, untried here: Nx placed its workspace data under the
  main checkout, outside the sandbox. Grant that folder as a writable root, or set `NX_DAEMON=false`
  and `NX_WORKSPACE_DATA_DIRECTORY` to a git-ignored folder in the worktree.

### Python, Go, Rust

Untried starting points:

| Ecosystem | `prepare` | The worker |
|---|---|---|
| Python | `uv sync --frozen` | `uv run --offline pytest` |
| Go | `go mod download` | `GOCACHE=$TMPDIR/go-build go test ./...` (PowerShell: `$env:GOCACHE="$env:TEMP\go-build"; go test ./...`) |
| Rust | `cargo fetch` | `cargo test --offline` |

### Git submodules

Untried. A worktree task starts with its submodules empty. `prepare`: `git submodule update --init
--recursive`. It runs outside the sandbox, so the MSYS limits of Codex's Windows sandbox do not
apply to it.
