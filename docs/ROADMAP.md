# Roadmap

Date: 2026-10-13

Operational document: current orientation, next work, open decisions, known gaps, and deferred work.
It shrinks as work lands and is not a chronology. Current mechanics live in
[architecture](architecture/), working practice in [engineering](engineering/), rationale in
[ADRs](adr/), and dated proof in [research](research/) and
[experiment records](../experiments/harness-invocation/records/README.md).

## Current State

- The four current curation manifests pass through one common assembly and emit matching Claude
  Code Plugins, OpenCode Module Bundles, and native Codex Plugins plus both repository marketplaces.
  Checkout Module versions are Process 0.12.1, General 0.14.1, Akka 0.4.1, and Aspire 0.6.0. Their
  item posture, source pins, transformations, exclusions, and reasons live in
  [`curation/*.yaml`](../curation/) and the generated [ledger](ledger.json), not in this roadmap.
- OpenCode 2 is the only OpenCode target. Canon now states the decided OpenCode 2 rules: the runtime
  floor and dropped OpenCode 1 support in
  [distribution and installation](architecture/distribution-and-installation.md#target-opencode-runtime),
  the invocation mapping and `<plugin>.<name>` IDs in
  [transformation and emission](architecture/transformation-and-emission.md#opencode), and their rationale in
  [ADR-0002](adr/0002-multi-harness-output.md) and
  [ADR-0005](adr/0005-invocation-intent-in-the-manifest.md). The checkout implements them in
  emission, reference rewriting, validation, the ledger, and the installer Destination, and the
  committed `opencode/` tree is OpenCode 2 output: one skill folder per skill item under its
  `<plugin>.<name>` ID, `manual` skills hidden with `opencode/autoinvoke: false`, namespaced
  agents, and no stubs, `both` duplicate commands, inline manual commands, or parked `BODY.md`
  files. Localization renders the skill-tool call and the subagent-dispatch words per harness and
  OpenCode's todo instructions as Markdown checklists, and `validate` runs the handoff-template,
  harness-vocabulary, OpenCode todo, and path-claim checks of
  [references and linking](architecture/references-and-linking.md). The OpenCode experiment
  scripts target OpenCode 2: the OpenCode 1 probes are retired, and `oc2-discovery.ps1` checks
  discovery against an isolated `opencode serve`. On v2.0.23 that check discovered the installed
  estate identically on the Windows workstation and the Linux host (115 dotted skills, the 26
  `manual` skills unadvertised, both agents as subagents), and an empty `OPENCODE_CONFIG_DIR` fell
  back like an unset one on Linux
  ([discovery record](../experiments/harness-invocation/records/2026-10-09-opencode2-discovery.md)).
  A tier-2 model panel on the same version measured the decided `manual` meaning: the hidden skill
  is absent from the model's offered skill guidance, `@`-attachable, and loadable by an explicit
  instruction naming its exact ID
  ([manual-skill record](../experiments/harness-invocation/records/2026-10-09-opencode2-manual-skill.md)).
  The curator's real OpenCode 2 profiles on the Windows workstation and the Linux host were moved
  once from `installer-v0.3.0` schema-1 state to the checkout's schema-2 output, with every unowned
  file byte-identical, by a recorded manual procedure that is not a product path
  ([profile migration record](../experiments/harness-invocation/records/2026-10-09-opencode2-profile-migration.md)).
  The rollout is done: the public `installer-v0.4.0` Package installed the four Modules fresh into
  the real OpenCode 2.0.26 profiles of the macOS workstation (the first macOS measurement) and the
  WSL distro on the Windows workstation, with every foreign file byte-identical; every existing
  Claude Code and Codex installation of the marketplace moved to the Module versions of that rollout;
  real-profile smoke tests on the Windows workstation and the Linux host, both now on 2.0.26, passed
  self-diagnostics and 10 of 10 skill tries with `openai/gpt-5.6-luna` at variant `low`; and the
  migration backups were deleted
  ([rollout record](../experiments/harness-invocation/records/2026-10-09-opencode2-rollout.md)).
  The `installer-v0.5.0` rollout then updated all four real OpenCode profiles to that Package's
  Module versions with one identical Plan and moved every Claude Code and Codex installation of the
  marketplace to them; Codex in the WSL distro and on the Linux host gained the marketplace and all
  four Plugins, enabled, and approved rollout leftovers, including an unused OpenCode 1 plugin SDK
  install in two config roots, were deleted
  ([v0.5.0 rollout record](../experiments/harness-invocation/records/2026-10-11-v0.5.0-rollout.md)).
  The `installer-v0.6.0` rollout updated all four real OpenCode profiles to Process 0.8.1, General
  0.13.0, Akka 0.4.1, and Aspire 0.6.0 with one identical finding-free Plan and moved every Claude
  Code and Codex installation of the marketplace to them, changing only `deniz-skills` configuration
  entries; its discovery checks used a throwaway database
  ([v0.6.0 rollout record](../experiments/harness-invocation/records/2026-10-12-v0.6.0-rollout.md)).
  The `installer-v0.7.0` rollout updated all four real OpenCode profiles to the checkout Module
  versions with one identical finding-free Plan, which added `verified-waves`, hidden, and moved
  every Claude Code and Codex installation of the marketplace to them, changing only `deniz-skills`
  configuration entries; its discovery checks used a throwaway database
  ([v0.7.0 rollout record](../experiments/harness-invocation/records/2026-10-12-v0.7.0-rollout.md)).
  The `installer-v0.7.1` rollout updated all four real OpenCode profiles to Process 0.10.1 with one
  identical finding-free Plan of 2 replaces and moved every Claude Code and Codex installation of
  the marketplace to it, changing only `deniz-skills` configuration entries; its discovery checks
  used the [runbook](../experiments/harness-invocation/runbook.md)'s throwaway data, state, cache,
  and database roots and left the real ones unchanged
  ([v0.7.1 rollout record](../experiments/harness-invocation/records/2026-10-12-v0.7.1-rollout.md)).
  From the next release on, `harness-and-os-ai-config-centralize` owns installation on each machine
  and the per-machine rollout records. This repository keeps Package release records; the existing
  rollout records remain historical evidence. The location of the runbook's real-profile discovery
  method remains an open curator decision: move it there, or keep it here as the method it follows.
- `dotnet/skills` is reviewed through `1d40f95` (main, 2026-10-09; it has no release-shaped
  tag); wave W6 is done, and with it every upstream sync wave of the 2026-10-08 review. General
  takes upstream's consolidated `msbuild` skill as the auto entry for build failures, build
  performance, and build-file review; it routes to eight kept MSBuild specialists through checked
  facts, and nine former specialists are excluded as merged into it. `code-testing` succeeds the
  `code-testing-agent` alias as a `manual` ceremony that writes and strengthens tests only: it runs
  its pipeline inline, writes TUnit-first, and hands a repair or flaky-test request to Process's
  `systematic-debugging`, so General now requires the Process Module as well as Aspire.
  `csharp-refactoring` is taken as `both` and `mtp-hot-reload` moved to `both`.
  `configuring-opentelemetry-dotnet`, `minimal-api-file-upload`, and `msbuild-server` left with
  their upstream deletion. `run-tests` and `platform-detection` state that SDK 10 with
  Microsoft.Testing.Platform 2.x has no VSTest bridge, `run-tests` collects coverage as Cobertura,
  and the reference husks stay loadable through facts against upstream's new read-only stance.
  General keeps its report-only, manual-ceremony, TUnit-first, and targeted-CRAP boundaries.
- `dotnet-skills` is reviewed through v1.7.1 (`84b5bee`); waves W2 and W2b are done. General
  takes `aot-trimming` as the `auto` partner of the `manual` `dotnet-aot-compat` ceremony, which
  now carries the warning-approval baseline; `aot-trimming` also owns turning on `PublishAot` and
  `PublishTrimmed` in an app, new or existing. General also takes `cscheck` and
  `sdk-container-publishing`.
  Upstream v1.7.1 fixed the `sdk-container-publishing` defects this repository reported, so its
  patch keeps only this curation's own guard, its `PublishAot` route to `aot-trimming`, and its
  trigger description; its real-world examples file now ships.
  Upstream now teaches the Testcontainers 4.x API, so `testcontainers-integration-tests` owns only
  a TUnit guard, as `aspire-integration-testing` and `playwright-blazor-testing` do;
  `snapshot-testing`'s patch carries the same guard beside its removal of the email-authoring
  branches. Akka and Aspire moved only through upstream's `invocable:` to `user-invocable:` key
  rename.
  `opentelemetry-net-instrumentation`'s exporter guard was reported upstream as
  [Aaronontheweb/dotnet-skills#98](https://github.com/Aaronontheweb/dotnet-skills/issues/98), currently
  open with no comments. The next sync checks whether the patch can shrink; retiring it remains the
  curator's decision.
- `aspire-skills` is reviewed through `da1230c`, three commits past the v0.0.3 tag (2026-09-23);
  wave W3 is done. Its source metadata is 0.0.3 and its guidance covers the Aspire 13.5 and 13.6
  release family; the post-tag commit carries the 13.6 agent-setup and MCP opt-in guidance that
  `aspire-init` and the router need. Following merged main ahead of release tags is the reviewed
  choice, not a claim about upstream Releases. The six official workflow patches still own only
  package-local routing, and those items now state `both`, which keeps their passthrough posture.
  Aspire takes `aspire-project-v2-migration` as `manual`, reached by a user-pointer in the router,
  and `aspire-init` declares its edge to the router. Description length is now checked in every
  harness tree, so `aspireify` and `aspire-orchestration` shorten their rendered descriptions
  instead of Codex truncating them.
- `superpowers` is reviewed through v7.0.0 (`bb92a77`, released 2026-10-09) and `asd-ste100`
  through `32511c6`; wave W4 is done. v7 rebuilt `brainstorming`, which now flows pristine under a
  description override stamped against its upstream body; its namespacing patch retired. The four
  items that share one review contract landed together: `executing-plans` is the autonomous inline
  executor, `subagent-driven-development` and `writing-plans` carry small patches, and the
  `requesting-code-review` overlay holds v7's two reviewer sections in its two-axis design. Each
  harness gets only its own tool notes from `using-superpowers`, OpenCode's authored here. Process
  takes `diagnosing-superpowers` as `both`. `asd-ste100` names its linter's interpreter, bounds when
  it runs, and states its limits; its invalid test fixture is omitted.
  The checkout now puts size/authority gates in `writing-plans`, both plan executors, and
  `using-git-worktrees`: ordinary work does not manufacture another ceremony, and already approved
  execution/workspace choices are preserved. Process weight is the agent's judgment, with one
  question only when it is genuinely torn; work that arrives already sized (an explicit plan request,
  an approved design) is not sized again; a resumed plan keeps the executor its ledger records; a
  delegation request without a plan is plain delegation; and the default branch needs an earlier
  answer or a standing instruction before its first commit. The selected heavy SDD workflow retains
  per-task review and whole-suite checks. `ask-deniz` keeps its .NET, Aspire, and Akka maps in a
  reference file read only for .NET work, and `verified-waves` treats a sequential chain as
  not-small rather than solo by default. These authored contracts have structural regression tests, not new
  model-compliance measurements; intent remains beside the items in curation.
- `mattpocock-skills` is reviewed through `49dd158` (2026-10-09), 46 commits past the v1.3.1 tag
  with no later tag; wave W5 is done. Process adopts upstream's `GLOSSARY.md` name for the domain
  glossary with no read of the old one, and this repository's own glossary moved with it.
  `resolving-merge-conflicts` left with its upstream deletion, its `ask-deniz` route included.
  Process takes `implement-spec`, `retro`, and `pr` as `manual`; `pr`'s Summary guidance comes
  from HumanLayer's `show-me`, so `humanlayer/skills` is an attribution-only submodule whose
  license ships in every Process notice pack and whose every component is excluded, `show-me` as
  `pr`'s merge source. `chief-of-staff` is excluded while it is in
  progress upstream. `setup-matt-pocock-skills` writes its block to `AGENTS.md`, which OpenCode and
  Codex read, and proposes importing it into `CLAUDE.md`. `ask-deniz` is recut by hand as the one
  `both` router for the whole estate: all General items, its nine manual ceremonies explicitly,
  Aspire's router/migration and two extra specialists, and the Akka set. Its 67 cross-Module
  `optional_refs` are checked facts under installed-Module guards and stay out of `requiredModules`,
  so Process remains independently installable. Manual ceremonies remain conditional human pointers;
  the two agents are descriptive specialist options, not fake skill-load or slash commands. The
  [cheatsheet](cheatsheet.md) and routing tests keep the human map aligned; runtime compliance of the
  new routing and guards is unmeasured.
- `verified-waves`, design A of the
  [ultracode and orchestration research](research/claude-ultracode-and-orchestration-ports.md#port-designs),
  ships in Process 0.10.0 as an original `manual` skill, declared under `original_skills`
  ([`curation/SCHEMA.md`](../curation/SCHEMA.md#original_skills)), that runs one task as verified
  multi-agent waves. One shared body in this repository's own words, not named "ultracode", holds
  the method, and per-harness tool notes through `harness_files` hold the tools: Claude Code's notes
  route into the native Workflow tool, which accepts a skill whose instructions say to call it as
  the user's opt-in; OpenCode's run background `subagent` waves; Codex's run `spawn_agent` waves
  that branch on the multi-agent version the model's tool list shows. `ask-deniz` and the
  [cheatsheet](cheatsheet.md) route people to it. The curator-approved prior-art approaches, the
  rejected ones, the skill outline, and the rulings on its authoring points are in the research
  note's
  [adopted approaches](research/claude-ultracode-and-orchestration-ports.md#adopted-approaches-curator-approved).
  The 2026-10-12 tier-2
  [forward-test panel](../experiments/harness-invocation/verified-waves-panel.md) supports two claims
  that the skill works, both for P2, each on the model named below: in OpenCode at `76bc5c8` and in
  Codex at `ace815d`, P2 ran twice and passed both times. No other prompt ran at least twice in a
  harness and passed every time there
  ([completion-claim vocabulary](engineering/quality-gates.md#completion-claim-vocabulary)).
  - OpenCode 2.0.26, on the free `opencode/muse-spark-1.3-contributor-free` model
    ([record](../experiments/harness-invocation/records/2026-10-12-verified-waves-opencode.md)): P2
    passed in 1 of 3 attempts on the text at `8bc8da3`, and in 2 of 2 attempts of a re-run at
    `76bc5c8`, each in a fresh lab. P0, P1, P3, P4, P5a, P6, P7, and P8 each passed on the text at
    `8bc8da3`, observed once (sample).
  - Claude Code 2.1.296
    ([record](../experiments/harness-invocation/records/2026-10-12-verified-waves-claude.md)): only
    P0's structural half ran, observed once (sample): the `init` event lists the skill. P0's
    model-surface half was skipped for want of a credential. P1 to P8 are not run, by curator
    decision: Claude Code uses its native Workflow tool.
  - Codex CLI 0.162.1, on `gpt-6-luna` at reasoning effort `max`
    ([record](../experiments/harness-invocation/records/2026-10-12-verified-waves-codex-gpt-6-luna.md),
    which supersedes the P0-only
    [first record](../experiments/harness-invocation/records/2026-10-12-verified-waves-codex.md)):
    on the text at `ace815d`, P2 passed in 2 of 2 attempts. P0, P1, P4, P5a, P6, P7, P8, and the
    multi-agent V2 run of P2 each passed, observed once (sample). P3 failed: its gate child's
    `npm test` hit `spawn EPERM` in the Windows sandbox and never passed.
- The repository is public with MIT licensing for original work, source-specific notices and exact
  upstream license copies, a public noreply marketplace contact, least-privilege secret and
  machine-path CI checks, private vulnerability reporting, and an explicit personal/no-SLA boundary.
- OpenCode supports two installation transports: a recursive clone using the repository installer,
  and the compiled npm-format Package attached to GitHub Release `installer-v0.9.0`. The Linux-built
  asset carries the schema-2 OpenCode 2 Bundles of Process 0.12.1, General 0.14.1, Akka 0.4.1, and
  Aspire 0.6.0 with the installer of Package 0.5.0, unchanged, and passed the manual release
  workflow's source gate, tar-mode verifier, isolated Plan/Apply/status, publication, and remote
  re-download checks. Its exact identity and proof boundary are in the
  [release record](../experiments/harness-invocation/records/2026-10-17-opencode-installer-v0.9.0.md).
  The previous schema-2 Releases `installer-v0.8.0`
  ([record](../experiments/harness-invocation/records/2026-10-13-opencode-installer-v0.8.0.md)),
  `installer-v0.7.1`
  ([record](../experiments/harness-invocation/records/2026-10-12-opencode-installer-v0.7.1.md)),
  `installer-v0.7.0`
  ([record](../experiments/harness-invocation/records/2026-10-12-opencode-installer-v0.7.0.md)),
  `installer-v0.6.0`
  ([record](../experiments/harness-invocation/records/2026-10-12-opencode-installer-v0.6.0.md)),
  `installer-v0.5.0`
  ([record](../experiments/harness-invocation/records/2026-10-11-opencode-installer-v0.5.0.md)), and
  `installer-v0.4.0`
  ([record](../experiments/harness-invocation/records/2026-10-09-opencode-installer-v0.4.0.md)) remain
  published. The older Releases through `installer-v0.3.0` remain schema-1 historical snapshots with
  OpenCode 1 shapes, and their assets were not replaced.
- Dependency-aware Module Selection is implemented in the checkout and ships since Package 0.4.0:
  schema-2 Bundles and Install state, compile-time `requiredModules` derivation, final-Selection
  presence checks, actual-versus-proposed status, and metadata-only Apply with exact Recovery. Feature source is
  four commits through `8be80489cd721b08f0ffa3bee711d8348d1e0ac1`; the independently reviewed tree
  `6e111fbcdcfae83d3401e89a9db27d898d8d001f` equals that source HEAD tree. Linux Package proof is a
  build-only workflow artifact that shares the 0.3.0 Package filename; it was not a public Release
  and was not installed into a real profile. Exact artifact identity, both workflow runs,
  and the presence-only proof boundary are in the
  [schema-2 record](../experiments/harness-invocation/records/2026-09-06-module-selection-schema2.md).
- Aspire CLI, TypeScript, testing, and package examples remain intentionally upstream-owned. Build,
  generation, and linking do not prove every example in a consumer environment; this is an accepted
  public limitation, not a claim to repair by silently forking the bodies.
- Codex CLI and a fresh Codex app-server share the default same-machine Codex plugin profile in both
  directions. The public Git marketplace and Process 0.6.0 were installed from each surface,
  discovered and executed from the other, then removed with byte-identical configuration recovery.
  This does not claim visual Desktop UI behavior, hot reload in an existing chat, or account-level
  cross-device synchronization; exact evidence lives in the
  [state-sharing record](../experiments/harness-invocation/records/2026-09-07-codex-desktop-cli-plugin-sharing.md).

## Next Up

1. **Claude plugin-shipped workflows.** Claude Code runs a saved Workflow script that a plugin ships
   in its `workflows/` directory as `/<plugin>:<meta.name>`
   ([Claude docs](https://code.claude.com/docs/en/workflows#distribute-a-workflow-in-a-plugin)), so a
   repeatable playbook such as the [curation sanity panel](agents/curation-sanity-panel.md) or an
   independent review could ship as `/deniz-process:<name>`. The build has no authored source or
   Claude emitter path for workflow scripts yet. Open question for the curator, not decided: the
   surface is Claude-only, so what answers the "same standard in every harness" rule
   ([ADR-0007](adr/0007-control-beats-fidelity.md), "All harnesses, every time")? One candidate:
   Claude Code gets the scripted form, and OpenCode and Codex get `verified-waves`' model-driven
   form of the same playbook.
2. **Refine composition-selection guidance only after more runtime evidence.** ADR-0005 now owns
   required and forbidden initiation capabilities, but it does not claim that descriptions reliably
   cause model selection. Namespaced body facts prove deterministic existence and audience
   reachability once a source runs, but do not make it run. Descriptions provide probabilistic
   selection pressure and can reach work with no explicit caller. The current candidate rule is:
   load-bearing composition uses guarded body facts; opportunistic passive knowledge can use honest
   descriptions; ceremonies need a human surface. Promote it to a durable decision only when another
   bounded runtime sample confirms the trade-off. Evidence remains in
   [skill-invocation-across-harnesses.md](research/skill-invocation-across-harnesses.md) and
   [skill-framework-landscape.md](research/skill-framework-landscape.md).

## Known Gaps

- **Windows bulk Apply can crash a running OpenCode 2 server:** on v2.0.23 an isolated server died
  with a Bun segmentation fault in its file watcher during a full-estate install or remove in 4 of
  33 sessions, all with the lab on the system volume
  ([bulk-Apply record](../experiments/harness-invocation/records/2026-10-09-opencode2-bulk-apply-windows.md)).
  The fault is upstream (`anomalyco/opencode#47505`) and the Apply itself always completed. The
  installer's mitigation is a warning after a Windows Apply that changed files, owned by
  [distribution canon](architecture/distribution-and-installation.md#target-opencode-runtime); it
  never stops or blocks on OpenCode, and reopening `opencode` restarts a stopped service. Four
  real-profile update Applies on each of the Windows workstation and the Linux host left the running
  service up ([v0.5.0](../experiments/harness-invocation/records/2026-10-11-v0.5.0-rollout.md),
  [v0.6.0](../experiments/harness-invocation/records/2026-10-12-v0.6.0-rollout.md),
  [v0.7.0](../experiments/harness-invocation/records/2026-10-12-v0.7.0-rollout.md), and
  [v0.7.1](../experiments/harness-invocation/records/2026-10-12-v0.7.1-rollout.md) rollout records);
  four samples each are not a rate, and a Linux failure rate is not measured.
- **Earlier schema-1 Releases have no upgrade path:** the Packages of Releases through
  `installer-v0.3.0` ship schema-1 Bundles in OpenCode 1 shapes, and the installer, from the
  checkout or a Release from `installer-v0.4.0` on, refuses schema-1 Install state with no
  compatibility reader. A
  user of such a Release has no supported route to schema-2 output; the real-profile migration is a
  recorded one-off, not a product path.
- **Selection dependency automation:** schema-2 manifests and Install state record `requiredModules`.
  Plan presence-checks the final Selection and does not automatically add, cascade-remove, or
  range-resolve Modules. Cross-version item/API compatibility is not claimed. See
  [distribution and installation](architecture/distribution-and-installation.md#full-estate-versus-installed-selection);
  the automation is [deferred](#deferred).
- **Codex catalog pressure is real:** every call in the 117-skill Luna panel warned that descriptions
  were shortened to fit the skills context budget. The tested explicit, implicit, manual, handoff,
  reference, and generated-skill paths passed, but that bounded panel is not proof for every skill,
  model, or future catalog size. The listing budgets of both Claude Code and Codex are measured in
  the [catalog-budget record](../experiments/harness-invocation/records/2026-10-11-skill-catalog-budgets.md).
  The curator cancelled estate-wide description shortening and its proposed 190-character validation
  gate because of their maintenance cost. The per-machine Claude Code and Codex budget raises stay;
  their configuration belongs to `harness-and-os-ai-config-centralize`. OpenCode has no listing budget.
  The existing 1,024-character skill-description limit is unchanged; residual truncation remains a
  known limitation, not an active shortening task.
- **Codex update re-enables a disabled plugin:** on Codex CLI 0.153.4, upgrading an installed
  plugin with `codex plugin add <plugin>@deniz-skills` set a plugin the user had disabled back to
  `enabled = true` in the Codex configuration; the rollout restored the line by hand
  ([rollout record](../experiments/harness-invocation/records/2026-10-09-opencode2-rollout.md)). The
  behavior is upstream; the README's Codex section documents installation only and names no update
  step or re-disable check.
- **Codex distribution coverage is intentionally split:** native Plugins cover Codex CLI and Codex
  in ChatGPT desktop, not the Codex IDE extension. IDE coverage would need a separately owned
  standalone-skill transport. Same-machine CLI/app-server custom-marketplace state sharing is
  measured; visual Desktop UI behavior, cross-device synchronization, and public OpenAI
  universal-directory submission remain separate distribution work, not requirements for repository
  marketplace installation.
- **Case-sensitive fact scan:** capitalized namespaced spellings can evade the lowercase scanner.
- **Bare references outside the handoff templates are review-only:** ordinary names, "see the `x`
  skill" references, routing hints without the word `skill`, unbackticked imperatives such as
  "invoke the writing-plans skill", and "Hand off to `x`" (`aspire-init` to
  `aspireify`) are candidates, not build state. Promote load-bearing cases through an authored
  namespaced fact and matching dependency; the next recut of that item is the natural point.
- **Ledger-history names need Git history:** a shallow or archived checkout knows only the names
  its history holds; a name deleted before any ledger listed it is unknown; a renamed taken item
  keeps only its output name
  ([references and linking](architecture/references-and-linking.md#proof-boundary-and-current-limits)).
- **`path:line` claims:** `../x/SKILL.md:42` is read as one path and does not resolve
  ([references and linking](architecture/references-and-linking.md#proof-boundary-and-current-limits)).
- **Harness phrasing is partly measured:** on one model, the rendered OpenCode skill-tool sentence
  in `deniz-process.grill-me` was followed
  ([record](../experiments/harness-invocation/records/2026-10-09-opencode2-manual-skill.md)), and
  Codex followed a fixture ``Invoke `$plugin:skill` `` handoff and the rendered
  `$deniz-process:grill-me` handoff
  ([record](../experiments/harness-invocation/records/2026-10-09-codex-rendered-handoff.md)).
  Neither rendered handoff is isolated as the cause of the load: its target `grilling` is an auto
  skill that triggers on "grill", a no-token "Grill me" prompt loaded it on Codex, and the OpenCode
  control dropped that word together with the attachment. Closing this needs, on each harness, a
  rendered handoff fixture whose source and target share no trigger words. No run covers the
  `Subagent (general)` dispatch label, the rendered unbackticked `general-purpose subagent`, or
  the tool-free Codex dispatch wording, which names no tool although Codex CLI 0.153.4 ships
  `spawn_agent` on by default
  ([references and linking](architecture/references-and-linking.md#harness-phrasing)).
- **Linker cause text is skill-specific:** an unreachable command or agent target can receive the
  right verdict with the wrong `disable-model-invocation` explanation.
- **Manifest keys outside the grammar are ignored:** `loadManifest` in
  [`tools/lib/manifest.ts`](../tools/lib/manifest.ts) rejects an unknown key only inside an
  `original_skills` declaration, and `checkHarnessFiles` rejects an unknown harness key inside any
  `harness_files` mapping, on a curated item and an original skill alike. An unknown top-level or
  item key, such as a misspelled `invocation`, is silently ignored.
- **Clean-fixture debt is filtered by finding name:** split the fixture so tests prove those findings
  still exist instead of tolerating their disappearance.
- **Full-file overlays have no replay:** an upstream move under a full-file overlay is reviewed
  through the `--bless` diff and ported by hand, as W5 ported `ask-deniz`, `requesting-code-review`,
  `systematic-debugging`, and `test-driven-development`.
- **W5's runtime paths are unmeasured:** `implement-spec` creates each implementer's worktree with
  git where the harness gives a subagent none, which OpenCode 2 does not, and no run has exercised
  that path. Codex runs its implementers through `spawn_agent`, on by default in Codex CLI
  0.153.4, but no run has exercised `implement-spec` or the two-reviewer dispatch of
  `requesting-code-review` there either.
- **`verified-waves` panel prompts that failed or were skipped:** the 2026-10-12 panel's
  [Codex](../experiments/harness-invocation/records/2026-10-12-verified-waves-codex-gpt-6-luna.md)
  leg ran at `ace815d` on `gpt-6-luna` (`max`) and failed P3. The
  [Claude Code](../experiments/harness-invocation/records/2026-10-12-verified-waves-claude.md) leg
  skipped P0's model-surface half for want of a credential; its record says how to complete P0.
  P1 to P8 are not run in Claude Code, by curator decision, because Claude Code uses its native
  Workflow tool, so they are not gaps. The
  [OpenCode](../experiments/harness-invocation/records/2026-10-12-verified-waves-opencode.md) leg
  failed P2 on the text at `8bc8da3`, and a re-run at `76bc5c8` passed it. By prompt:
  - P0's model-surface half: skipped in Claude Code.
  - P2: in Codex it passed in 2 of 2 attempts at `ace815d` on `gpt-6-luna`, so P2 meets the panel's
    bar there too (sample, not a rate). In OpenCode it failed in 2 of 3 attempts on the text at
    `8bc8da3`, because children wrote results files without their `REPORT` block (12 and 7 of 13),
    and one of the two reused an earlier attempt's run-id and wrote over its ledger. The skill text
    now answers both causes: a brief's last step puts the returned `REPORT`
    block first in the results file, the parent completes any results file that lacks it, and a new
    run checks that its ledger directory does not exist yet. A re-run at `76bc5c8` passed in 2 of 2
    attempts: in each, all 13 children's results files started with their `REPORT` block, and the
    session checked that its ledger directory was free before it created it. Each attempt ran in a
    fresh lab, so no run has met an existing ledger directory or an incomplete results file: the
    step that picks another run-id and the parent's completion of a results file are unmeasured.
  - P3: failed in Codex: the gate child's `npm test` hit `spawn EPERM` in the unelevated Windows
    sandbox twice and never passed. The writers' coordination line could not be checked, because
    Codex stores briefs encrypted.
- **The Codex multi-agent V1 path is unmeasured:** Codex catalog metadata can select multi-agent V2
  even with its feature flag off
  ([research](research/claude-ultracode-and-orchestration-ports.md#capability-matrix)), so the
  `verified-waves` Codex notes branch on the model's actual tool list. The panel measured this for
  one model: the bundled catalog sets `multi_agent_version: "v2"` for `gpt-6-luna`, and every
  session got the V2 tools with `features.multi_agent_v2` unset, `true`, or `false`. Under V1 the
  multi-agent tools are deferred behind `tool_search` for every model in the bundled catalog, so a
  V1 session starts without `spawn_agent` in view, and the notes load the tools through
  `tool_search` before the check (same research section). No session was served V1, so no run has
  exercised that deferral step: there were 0 `tool_search` calls in the panel's 82 rollouts.
- **Two Codex model rules for spawned children:** the `using-superpowers` Codex notes tell every
  `spawn_agent` call to set `model` and `reasoning_effort` per the Model Selection rules of the
  skill being executed, while the `verified-waves` Codex notes leave both unset for the children
  that skill spawns unless the user asks. The curator kept the skill's own rule and left
  `using-superpowers` unpatched, so a session that loads both reads both rules.
- **`verified-waves` notes that a sample contradicts or leaves open:** the OpenCode notes say that
  a ledger outside the project asks for `external_directory` permission, but in the OpenCode panel
  no run raised a permission prompt for a ledger below the server's own temp root, in the first run
  or in the P2 re-run. The model there also ended its turn after the plan in 3 of the 5 sessions
  that got the P2 prompt. `SKILL.md` now says to start the first wave in the same turn as the plan,
  and in the P2 re-run at `76bc5c8` the model still ended its turn after the plan in 1 of 2
  attempts. Each is a sample, not a rate
  ([OpenCode record](../experiments/harness-invocation/records/2026-10-12-verified-waves-opencode.md)).
  In the Codex panel, three more, each seen once
  ([Codex record](../experiments/harness-invocation/records/2026-10-12-verified-waves-codex-gpt-6-luna.md)):
  - Both gate children reported FAIL on `spawn EPERM` in the sandbox without asking for
    escalation, while the children that asked got PASS. `SKILL.md` now says that a check the
    sandbox refuses is never FAIL: the child requests the harness's escalation once, if it has one,
    and is otherwise BLOCKED with the command and the denial, and each harness's notes say how it
    escalates. That rule is unmeasured.
  - The model asked its yes/no questions through `request_user_input_async`, which lets the turn go
    on. In P3 the parent went on past its question and closed the run without the answer; the
    panel's driver did not surface the question while that turn ran, so this is partly a driver gap.
    `SKILL.md` now says to end the turn after the question and never go on past it in the same
    turn, and the Codex notes say the same for that tool. That rule is unmeasured.
  - In P3, once its announced agent total was spent, the parent wrote a fix itself instead of
    dispatching a writer. `SKILL.md` now says that the parent never does an item's work: when the
    total is spent, it asks the yes/no question about going over it, or stops and reports what is
    left. That rule is unmeasured.
- **Consumer repositories set up before W5 keep the old layout:** with no read of the old names,
  a repository that has `CONTEXT.md`, or an `## Agent skills` block in `CLAUDE.md`, needs its
  glossary renamed and `setup-matt-pocock-skills` run again. The new run writes its block to
  `AGENTS.md` and leaves the old one in `CLAUDE.md`, so remove that block from `CLAUDE.md` by hand;
  otherwise Claude Code sees two `## Agent skills` blocks once `CLAUDE.md` imports `AGENTS.md`.
- **`to-tickets` sub-issues are GitHub-only upstream:** the GitLab and local tracker templates name
  no sub-issue operation, and the rule against modifying a parent issue sits beside the operation
  that attaches its sub-issues. Both are upstream's; neither is patched.
- **Scanner layout blind spots:** grouped commands and agents are missed, while nested component
  directories can be double-counted.
- **Overlay mode drift:** overlay locks hash bytes but not executable mode.
- **Symlink-boundary patches:** emitted copies skip symlinks while `git apply` cannot patch through
  them.
- **Frontmatter override on an owned body:** an override on an item with no `body:` is stamped and
  stops the build when upstream moves, but one on an item with `body:` is guarded only by that
  overlay's or patch's stamps, so an overlay or patch that never touches `SKILL.md` leaves it
  unguarded. Every current override on such an item sits on a body that stamps `SKILL.md`.
- **Description content beyond length:** the build and `validate` check the rendered
  1,024-character description limit in every tree; whether a tag or angle-bracket rule for
  descriptions should be checked too, and where its boundary lies, is still open. At the current
  pins a tag-only rule would flag two descriptions, both from dotnet-skills:
  `modern-csharp-coding-standards` and `r3-reactive-extensions`.
- **Aspireify activation tension remains upstream-owned:**
  `external/aspire-skills/skills/aspireify/SKILL.md` advertises integration/authoring and toolchain
  work while its Detection section still requires an unwired AppHost and its header says one-time.
  The reviewed update preserves this inherited runtime scope rather than widening it through a
  routing-only curation patch.
- **Module version bumps are policy-only:** emitted bytes can move under an unchanged curator version;
  the Bundle digest detects it, but validation does not enforce the human-facing version rule.
- **Inventory descriptions truncate silently:** long descriptions stop at 140 characters without an
  ellipsis marker.
- **Test-project scaffolding repair is uncovered:** `scaffold-dotnet-test-project` stays excluded
  because it has no TUnit path, and no taken item adds a missing project reference, registers a test
  project in its solution, or diagnoses a CI run that discovers zero tests.
- **`dotnet-agent-skills` has no release-shaped current pin:** its rolling tool tag and stale v1.0.0
  are not suitable boundaries, so each sync is a reviewed hold-or-main-commit decision.
- **Non-empty `hooks.include` is unsupported:** implement it only for a concrete wanted hook.
- **Frontmatter reserialization adds drift:** passthrough skills are parsed and emitted, not promised
  byte-identical to upstream.
- **Runtime proof remains bounded:** links prove existence and audience reachability, not model
  selection, instruction following, or permission behavior.

## Deferred

Out of scope until there is a concrete need: OpenCode agent permission mapping; emitting OpenCode
skill permission rules, which needs configuration mutation; Cursor and Gemini outputs; automated or
scheduled upstream sync; the Blazor and MAUI skill estates; specialist mobile diagnostics; NuGet
trusted-publishing guidance; and packaging this repository's documentation structure as an original
skill.

- **Iteration 2: dependency automation:** automatic dependency installation, cascade remove, and
  version-range resolution. These three workstreams belong to the follow-up iteration, not the
  first feature. Selection/Ownership semantics, user approval for automatic changes, and version
  constraint/conflict policy must be designed in that iteration rather than assumed by the first.
  Deferred because the estate is four Modules with one publisher and one installer: Plan already
  refuses an incomplete Selection and names each missing Module, and the automation would serve
  only the OpenCode installer. Reopen when someone other than the curator installs the estate, or
  when the Module count makes hand Selection error-prone.
- **MCP servers:** this repository ships none, in any harness. dotnet/skills registers its BinlogMcp
  server at its own plugin level, as an auto-downloaded prerelease, and that registration does not
  travel with curated items; OpenCode would also need an installer change to own an `opencode.json`
  fragment. The `msbuild` skill reads binlogs through text-log replay, and a kept specialist that
  offers the MCP server as its first workflow falls back to replay where no server is configured.
  A user who installs the server separately gets it wherever a body offers it.
- **.NET 11 skills:** `system-text-json-net11` and `migrate-dotnet10-to-dotnet11` stay excluded
  as preview-era content until .NET 11 reaches general availability; revisit both then. A taken
  migration would be `manual`, and `system-text-json-net11` would need its dotnet-install block
  and its unconditional "not done until the program runs" mandate removed.
- **OpenSpec:** parked after the [OpenSpec evaluation](research/openspec-evaluation.md): nothing
  recorded calls for it, and the one gap it fills, a behaviour spec that outlives the merge, is
  absent by this repository's own [plan lifecycle](engineering/documentation.md#handovers-and-plans).
  Reopen when:
  - the curator decides some behaviour spec should outlive the merge; then revisit that lifecycle
    rule, decide where living specs sit beside `GLOSSARY.md` and the ADRs, and choose between
    borrowing WHEN/THEN scenarios through an overlay and curating OpenSpec's skills as `manual`
    items;
  - a real repository needs it, with several contributors or agents changing the same capabilities
    over months; evaluate it in a throwaway sandbox first, then curate its skills;
  - Iteration 2 (Deferred) lands, which lowers the cost of curating its
    skills;
  - OpenSpec adds an invocation control to its skill generator and moves to skills-only delivery
    ([#2022](https://github.com/Fission-AI/OpenSpec/issues/2022)); skills-only alone is not enough,
    because on OpenCode it removes the only way to start a workflow on purpose;
  - upstream slows down, for example with a stable 2.x skill contract, so sync cost drops; or
  - superpowers or `mattpocock-skills` adopt living or delta specs, which makes it an ordinary
    sync-review decision.
- **Keyword-injected orchestration (design B):** deferred behind the orchestration skill,
  `verified-waves` (Current State). A `manual` skill is already a one-token trigger in every
  harness, so the only gain is a standing session mode outside Claude Code. OpenCode would need a
  plugin file, which the installer may not own without an ADR that widens
  [installer ownership](architecture/distribution-and-installation.md#destination-selection-and-ownership);
  Codex would need emitted plugin hooks, a hook script, each user's trust in that hook, and the
  Legacy plugin manifest format, since the Agent Plugins format skips plugin hooks. Revisit only if
  the curator wants a standing session mode in OpenCode or Codex
  ([evidence](research/claude-ultracode-and-orchestration-ports.md#port-designs)).
