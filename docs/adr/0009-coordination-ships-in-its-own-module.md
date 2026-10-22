# ADR-0009: Coordination ships in its own Module, with the dispatch harness as a .NET file-based app

Date: 2026-10-21
Status: Accepted

## Context

`delegating-to-worker-models` tells a coordinator how to hand a task to a worker model of another
family and check what comes back. The only dispatcher that runs such workers out of process lives in
a private repository. Every project that wants it keeps its own copy, because the copy's ledger root
is fixed relative to the tool, a branch switch in the source repository kills running work, and every
SDK pin move means re-copying. A second, Codex-only dispatcher grew separately in another workspace.
Both dispatchers drift, and neither reaches users of the published Modules.

The design rests on one observation from field runs
([worker-model delegation](../research/worker-model-delegation.md)): model families differ in what they are good
at. Some are strong implementers once a task is fixed and weak at design and decisions; others are
the stronger judges. Coordination assigns work by strength: the coordinator keeps the judgement (the
goal, scope, design choices, and acceptance) and hands implementation to workers, then reviews what
they return. That takes four pieces that change together: a mode that makes an agent the coordinator,
the craft of handing over one piece of work, a tool that starts out-of-process workers, and, on
harnesses without a native workflow tool the user opts into, a process for verified multi-agent waves
(`verified-waves`, in Process until now). Split across Modules, they point at each other across
Module boundaries and drift apart.

Everything this repository ships today is skill, command, and agent text plus small helper scripts
in Bash, PowerShell, Node, and Python. A dispatcher is different in kind: an application of several
thousand lines with its own runtime, a security surface (it runs models that execute shell
commands), user state, and tests. Process is the Module most users install, and it carries no
toolchain requirement beyond its scripts' interpreters. The repository is public, its own tooling
stays plain TypeScript ([ADR-0004](0004-minimal-toolchain.md)), and its Bundles, Install state, and
installer accept only Native paths under `skills/`, `commands/`, and `agents/`
([Distribution and installation](../architecture/distribution-and-installation.md)).

## Decision

Coordination ships in a new Module, `deniz-coordination`, and the dispatch harness ships in it as the
source of a .NET file-based application bundled inside one of that Module's skills.

- **Module.** `deniz-coordination` holds four skills. Each ships only to the harnesses it serves
  ([ADR-0010](0010-per-harness-item-emission.md)).
  - `coordinator`, started by the user, makes the agent the coordinator. Starting it is the user's
    explicit request for workers: it loads the craft and, where the harness ships, the harness.
    It ships to every harness.
  - `delegating-to-worker-models`, model-invocable, is the craft. It names no tool, so it works
    with any subagent mechanism, and it ships to every harness.
  - `harness`, model-invocable, is the dispatch tool and its contract for every caller. It depends
    on no other skill, so a caller that only needs the tool uses it alone. It ships to Claude Code
    and Codex, because OpenCode reaches several model families natively.
  - `verified-waves`, started by the user, moves out of Process. It ships to the harnesses without
    a native workflow tool the user opts into, which today means OpenCode and Codex.

  Coordination requires Process (the craft's review path is Process's `requesting-code-review`);
  Process never requires Coordination, and Process's remaining mentions of these skills become
  conditional cross-Module pointers.
- **The harness runs only on an explicit request.** A user's own request or the start of
  `coordinator` is that request; the harness being installed is not. Only `coordinator` depends on
  the harness, and only in the trees that ship it.
- **Harness.** The harness skill's body is the harness's contract. It runs workers on two equal
  backends: OpenCode through the OpenCode .NET SDK, and Codex through a thin JSON-RPC client over the
  standard streams of `codex app-server`.
- **Payload.** The application is C# source split with `#:include`, kept entirely inside the
  harness skill's folder with its own `global.json` and an entry file that starts with a shebang.
  It is run as `dotnet <entry>.cs`. No compiled binary ships. Because the files sit inside a skill
  folder, they are ordinary bundled text to the emitter and to the Claude Code and Codex Plugin trees; no
  OpenCode Bundle carries it, and the payload needs no new Native path kind, manifest field, or
  installer rule.
- **User state stays outside the payload.** Route allow-lists, policies, ledgers, escalation files,
  and worktrees live under one override (`DISPATCH_HOME`) or the operating system's config and state
  folders, never in the skill folder, a Plugin cache, or an OpenCode config root. The skill ships an
  example allow-list and an example policy, not defaults.
- **Repository toolchain.** `tools/` stays as ADR-0004 decides. The harness's tests are a .NET test
  project in their own top-level folder, outside every skill folder so they never ship, and CI gains
  a .NET job on Windows, Linux, and macOS for the harness alone. Tests that need a real model login
  are opt-in and never run in CI.
- **Rejected.** Keeping the private copies, for the drift above. Putting the harness in Process,
  which would put a .NET requirement on every Process user. Keeping `verified-waves` in Process,
  which keeps the coordination pieces in two Modules with pointers both ways and ships its ceremony to
  a harness whose native workflow tool already does that work. A separate Module for the text skills
  without the harness, which keeps the .NET payload out of their installs but adds a Module and
  separates the coordinator from the tool it most often drives; bundled source costs nothing until it
  runs. A model-invocable `coordinator`, which would turn ordinary subagent use into coordination
  ceremony. Shipping compiled binaries or a NuGet tool, which brings per-platform builds, signing,
  and a transport outside the Release model, and turns reviewable source into an opaque artifact.
  Rewriting the harness in TypeScript or Python, which abandons the maintained .NET OpenCode SDK its
  OpenCode backend is built on and rewrites a working application to gain nothing on the Codex side,
  whose protocol client is small in any language.

## Consequences

- A user who runs the harness needs a .NET 10 SDK and NuGet access for the app's package
  directives; the first run compiles, so it is slow. SDK selection follows the working directory's
  `global.json`, which is why the skill folder carries its own and the skill runs the app from there.
  A user who only uses the other three skills installs Coordination but needs no .NET.
- This is the repository's first runnable product code. It inherits every public-safety rule:
  machine paths, hostnames, private document references, and names or dates in authored text
  become configuration or go. A port that cannot meet them does not land.
- A Plugin cache copies only the skill's own folder, so the app's `#:include` targets stay inside
  that folder and its source avoids sibling-skill paths.
- Moving `delegating-to-worker-models` and `verified-waves` renames their skill IDs. Every
  repository and skill that names them by the old ID changes in the same release. The manual
  command people type changes too: `/deniz-process:verified-waves` becomes
  `/deniz-coordination:verified-waves`, with the matching OpenCode and Codex spellings, so the
  release notes name it.
- `coordinator` is started by the user, so a session that never starts it never enters coordination
  mode, even when it hands work to subagents. That is the intended cost: coordination is a choice per
  task. The craft stays model-invocable, so it still loads when an agent writes a prompt for another
  agent.
- Claude Code loses the `verified-waves` command; a multi-agent run there starts through its native
  workflow tool or through `coordinator`.
- An OpenCode or Codex user who selects only Process loses `verified-waves` until they also select
  Coordination, because the installer never adds a Module to a Selection. Rolling out the release
  includes that Selection change wherever the skill is in use.
- If OpenCode or Codex gains a native workflow tool the user opts into, the skill's harness list in
  the manifest changes; that is a manifest edit, not a new decision.
- The repository gains a second language and a CI toolchain. `validate` learns the `dotnet`
  invocation form for bundled scripts.
- Reversal trigger: if a harness ships a first-party dispatcher that does what this harness does, or
  the .NET requirement proves to block the Module's users, retire the bundled app and keep the Module
  as the text skills alone.
