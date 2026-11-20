# ADR-0009: Coordination ships in its own Module, with the dispatch harness as a .NET file-based app

Date: 2026-11-20
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

The first record had the coordinator's Claude Code and Codex notes send implementation to the
dispatch harness. In T3 Code, which reaches every provider it lists on several machines through its
own delegation tools, that pin pushed the coordinator to the harness every time; the curator pasted a
"T3 only, ignore the dispatch harness" paragraph into each lane, and the lead rebuilt by hand what the
harness gives (the pasted worker contract, a base check, settling from git, the record of the model
that ran). What matters is choosing the right model for each piece, so the mechanism follows the
model.

The first record also made `coordinator` a skill only the user starts and kept the curator's model
profiles inside the craft. Claude Code renders such a skill as one the model cannot load, so a lane
that a lead opened, or that resumed after a restart, could not load the coordinator from its brief;
in a six-lane run, one lane a lead opened coordinated from its handoff without it, and whether the
others had loaded it could not be verified. And a craft that names
models ships one curator's model choices, which go stale within weeks, to every user of an open
source Module that other teams also use.

## Decision

Coordination ships in a new Module, `deniz-coordination`, and the dispatch harness ships in it as the
source of a .NET file-based application bundled inside one of that Module's skills.

- **Module.** `deniz-coordination` holds six skills. Each ships only to the harnesses it serves
  ([ADR-0010](0010-per-harness-item-emission.md)).
  - `coordinator`, model-invocable with a description that fires only when the user or a lead's
    brief asks for a coordinator or a lane lead, makes the agent the coordinator. That request, not
    the loading of the skill, is the request for workers through any delegation mechanism the agent
    has; it loads the craft and names no tool. It ships to every harness.
  - `delegating-to-worker-models`, model-invocable, is the craft. It picks each piece's model first
    and then a delegation mechanism that reaches it, with no fixed order among mechanisms: the host's
    own subagents, whose notes it carries per harness, the dispatch harness, or T3 Code. It names no
    model: the model comes from the user, a routing they set up, or model profiles they name. It ships
    to every harness.
  - `model-profiles`, model-invocable, holds one curator's profiles of the models they delegate to:
    roles, cost, how to brief each model, its worker block, and where to look in review. It takes
    effect only when the user, a brief, or the repository addendum names it. It ships to every
    harness.
  - `harness`, model-invocable, is the dispatch tool and its contract for every caller. It depends
    on no other skill, so a caller that only needs the tool uses it alone; its section for the craft
    maps the craft's steps onto task files. It ships to Claude Code, Codex, and Antigravity, because OpenCode
    reaches several model families natively.
  - `t3-delegation`, model-invocable, maps the craft onto T3 Code's delegation tools, which reach
    every provider T3 lists on several machines. It repeats none of T3's own orchestration
    instructions, which win where they differ, and it ships to every harness, because a T3 session
    can run on any of them.
  - `verified-waves`, started by the user, moves out of Process. It ships to the harnesses without
    a native workflow tool the user opts into, which today means OpenCode, Codex, and Antigravity.

  Coordination requires Process (the craft's review path is Process's `requesting-code-review`);
  Process never requires Coordination, and Process's remaining mentions of these skills become
  conditional cross-Module pointers.
- **Workers on other providers run only on an explicit request.** A user's own request for
  workers, a model, or the mechanism, or a coordinated or delegated run that the user or a
  coordinator the user started asked for, is that request; loading a skill, or the harness or T3
  being available, is not. The craft names the harness only from the host notes that the Claude Code,
  Codex, and Antigravity trees ship.
- **Harness.** The harness skill's body is the harness's contract. It runs workers on two equal
  backends: OpenCode through the OpenCode .NET SDK, and Codex through a thin JSON-RPC client over the
  standard streams of `codex app-server`.
- **Payload.** The application is C# source split with `#:include`, kept entirely inside the
  harness skill's folder with its own `global.json` and an entry file that starts with a shebang.
  It is run as `dotnet <entry>.cs`. No compiled binary ships. Because the files sit inside a skill
  folder, they are ordinary bundled text to the emitter and to the Claude Code, Codex, and Antigravity Plugin trees; no
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
  runs. One Module per mechanism (the dispatch harness, T3), which multiplies Modules for skills that
  change together. A fixed order among mechanisms, which picks the wrong one outside the setting it
  was written for; the model a piece needs decides instead. A `coordinator` only the user starts, which a lane opened by a lead cannot load on Claude Code;
  its description now keeps ordinary subagent use out of coordination. Model profiles inside the
  craft, which ship one curator's model choices as the method's defaults. Shipping compiled binaries or a NuGet tool, which brings per-platform builds, signing,
  and a transport outside the Release model, and turns reviewable source into an opaque artifact.
  Rewriting the harness in TypeScript or Python, which abandons the maintained .NET OpenCode SDK its
  OpenCode backend is built on and rewrites a working application to gain nothing on the Codex side,
  whose protocol client is small in any language.

## Consequences

- A user who runs the harness needs a .NET 10 SDK and NuGet access for the app's package
  directives; the first run compiles, so it is slow. SDK selection follows the working directory's
  `global.json`, which is why the skill folder carries its own and the skill runs the app from there.
  A user who only uses the other skills installs Coordination but needs no .NET.
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
- `coordinator` loads only when the user or a lead's brief asks for a coordinator, so a session
  that hands work to subagents without that request never enters coordination mode. Its description
  is the guard, so a stray load stays possible; it authorizes no workers, because the request does.
  The craft stays model-invocable, so it still loads when an agent writes a prompt for another agent.
- Without named model profiles the craft chooses among the models a mechanism reaches and reviews
  the first runs of each more strictly; a user who wants the curator's choices names
  `model-profiles`, and a repository names it in its addendum.
- Claude Code loses the `verified-waves` command; a multi-agent run there starts through its native
  workflow tool or through `coordinator`.
- An OpenCode or Codex user who selects only Process loses `verified-waves` until they also select
  Coordination, because the installer never adds a Module to a Selection. Rolling out the release
  includes that Selection change wherever the skill is in use.
- If OpenCode, Codex, or Antigravity gains a native workflow tool the user opts into, the skill's harness list in
  the manifest changes; that is a manifest edit, not a new decision.
- The repository gains a second language and a CI toolchain. `validate` learns the `dotnet`
  invocation form for bundled scripts.
- Reversal trigger: if a harness ships a first-party dispatcher that does what this harness does, or
  the .NET requirement proves to block the Module's users, retire the bundled app and keep the Module
  as the text skills alone.
