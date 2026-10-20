# ADR-0009: The dispatch harness ships as a .NET file-based app in its own Module

Date: 2026-10-21
Status: Accepted

## Context

`delegating-to-worker-models` tells a coordinator how to hand a task to a worker model of another
family, and its dispatcher contract lists what any dispatcher must do: an isolated workspace from a
stated base, a ledger, escalated asks, and verification on the committed tree. The only dispatcher
that meets that contract lives in a private repository. Every project that wants it keeps its own
copy, because the copy's ledger root is fixed relative to the tool, a branch switch in the source
repository kills running work, and every SDK pin move means re-copying. A second, Codex-only
dispatcher grew separately in another workspace. Both dispatchers drift, and neither reaches users
of the published Modules.

Everything this repository ships today is skill, command, and agent text plus small helper scripts
in Bash, PowerShell, Node, and Python. A dispatcher is different in kind: an application of several
thousand lines with its own runtime, a security surface (it runs models that execute shell
commands), user state, and tests. Process is the Module most users install, and it carries no
toolchain requirement beyond its scripts' interpreters. The repository is public, its own tooling
stays plain TypeScript ([ADR-0004](0004-minimal-toolchain.md)), and its Bundles, Install state, and
installer accept only Native paths under `skills/`, `commands/`, and `agents/`
([Distribution and installation](../architecture/distribution-and-installation.md)).

## Decision

The dispatch harness ships in a new Module, `deniz-dispatch`, as the source of a .NET file-based
application bundled inside one of that Module's skills.

- **Harness.** It is the dispatcher that meets the `delegating-to-worker-models` dispatcher contract,
  with two equal backends: OpenCode through the OpenCode .NET SDK, and Codex through a thin JSON-RPC
  client over the standard streams of `codex app-server`.
- **Module.** `deniz-dispatch` holds the harness skill, `harness`, whose body is the harness's
  contract surface for every caller, and `delegating-to-worker-models`, which moves out of Process.
  Dispatch requires Process (its review path is Process's `requesting-code-review`); Process never
  requires Dispatch, and Process's remaining mentions of delegation become conditional cross-Module
  pointers. A user who never dispatches installs nothing new and needs no .NET.
- **Payload.** The application is C# source split with `#:include`, kept entirely inside the
  harness skill's folder with its own `global.json` and an entry file that starts with a shebang.
  It is run as `dotnet <entry>.cs`. No compiled binary ships. Because the files sit inside a skill
  folder, they are ordinary bundled text to the emitter, the Bundle manifest, the installer, and
  both Plugin trees; no Native path kind, manifest field, or installer rule changes.
- **User state stays outside the payload.** Route allow-lists, policies, ledgers, escalation files,
  and worktrees live under one override (`DISPATCH_HOME`) or the operating system's config and state
  folders, never in the skill folder, a Plugin cache, or an OpenCode config root. The skill ships an
  example allow-list and an example policy, not defaults.
- **Repository toolchain.** `tools/` stays as ADR-0004 decides. The harness's tests are a .NET test
  project in their own top-level folder, outside every skill folder so they never ship, and CI gains
  a .NET job on Windows, Linux, and macOS for the harness alone. Tests that need a real model login
  are opt-in and never run in CI.
- **Rejected.** Keeping the private copies, for the drift above. Putting the harness in Process,
  which would put a .NET requirement on every Process user. Shipping compiled binaries or a NuGet
  tool, which brings per-platform builds, signing, and a transport outside the Release model, and
  turns reviewable source into an opaque artifact. Rewriting it in TypeScript or Python, which
  abandons the maintained .NET OpenCode SDK its OpenCode backend is built on and rewrites a working
  application to gain nothing on the Codex side, whose protocol client is small in any language.

## Consequences

- A user of Dispatch needs a .NET 10 SDK and NuGet access for the app's package directives; the
  first run compiles, so it is slow. SDK selection follows the working directory's `global.json`,
  which is why the skill folder carries its own and the skill runs the app from there.
- This is the repository's first runnable product code. It inherits every public-safety rule:
  machine paths, hostnames, private document references, and names or dates in authored text
  become configuration or go. A port that cannot meet them does not land.
- OpenCode path respelling applies to every bundled text file, so the app's `#:include` targets stay
  inside its own folder and its source avoids sibling-skill paths.
- Moving `delegating-to-worker-models` renames its skill ID in all three harnesses. Every
  repository that loads it by the old name changes in the same release.
- The repository gains a second language and a CI toolchain. `validate` learns the `dotnet`
  invocation form for bundled scripts.
- Reversal trigger: if a harness ships a first-party dispatcher that meets the dispatcher contract,
  or the .NET requirement proves to block the Module's users, retire the bundled app and keep the
  Module as the judgment skill alone.
