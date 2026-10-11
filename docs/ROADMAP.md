# Roadmap

Date: 2026-10-11

Operational document: current orientation, next work, open decisions, known gaps, and deferred work.
It shrinks as work lands and is not a chronology. Current mechanics live in
[architecture](architecture/), working practice in [engineering](engineering/), rationale in
[ADRs](adr/), and dated proof in [research](research/) and
[experiment records](../experiments/harness-invocation/records/README.md).

## Current State

- The four current curation manifests pass through one common assembly and emit matching Claude
  Code Plugins, OpenCode Module Bundles, and native Codex Plugins plus both repository marketplaces.
  Checkout Module versions are Process 0.8.0, General 0.11.1, Akka 0.4.1, and Aspire 0.5.1. Their
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
- `dotnet/skills` is reviewed through `d68dd708`. General carries the current test-execution,
  coverage, test-quality, and testability bodies, takes the promoted `vectorization` specialist, and
  retains curator-owned report-only, manual-ceremony, TUnit-first, and targeted-CRAP boundaries.
- `dotnet-skills` is reviewed through v1.7.0 (`29878f2`); wave W2 is done. General takes
  `aot-trimming` as the `auto` partner of the `manual` `dotnet-aot-compat` ceremony, which now
  carries the warning-approval baseline, plus `cscheck` and `sdk-container-publishing`, whose patch
  fixes upstream defects verified against Microsoft Learn and the dotnet/sdk source.
  `testcontainers-integration-tests` flows unowned now that upstream teaches the Testcontainers 4.x
  API. Akka and Aspire moved only through upstream's `invocable:` to `user-invocable:` key rename.
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
- `mattpocock-skills` is reviewed through `49dd158` (2026-10-09), 46 commits past the v1.3.1 tag
  with no later tag; wave W5 is done. Process adopts upstream's `GLOSSARY.md` name for the domain
  glossary with no read of the old one, and this repository's own glossary moved with it.
  `resolving-merge-conflicts` left with its upstream deletion, its `ask-deniz` route included.
  Process takes `implement-spec`, `retro`, and `pr` as `manual`; `pr`'s Summary guidance comes
  from HumanLayer's `show-me`, so `humanlayer/skills` is an attribution-only submodule whose
  license ships in every Process notice pack and whose every component is excluded, `show-me` as
  `pr`'s merge source. `chief-of-staff` is excluded while it is in
  progress upstream. `setup-matt-pocock-skills` writes its block to `AGENTS.md`, which OpenCode and
  Codex read, and proposes importing it into `CLAUDE.md`. `ask-deniz` is recut by hand, and the
  model-reachable skills it names in plain text, ten of them, are checked facts.
- The repository is public with MIT licensing for original work, source-specific notices and exact
  upstream license copies, a public noreply marketplace contact, least-privilege secret and
  machine-path CI checks, private vulnerability reporting, and an explicit personal/no-SLA boundary.
- OpenCode supports two installation transports: a recursive clone using the repository installer,
  and the compiled npm-format Package attached to GitHub Release `installer-v0.4.0`. The Linux-built
  asset carries the schema-2 OpenCode 2 Bundles of Process 0.7.0, General 0.10.0, Akka 0.4.0, and
  Aspire 0.4.0, and passed the manual release workflow's source gate, tar-mode verifier, isolated
  Plan/Apply/status, publication, and remote re-download checks. Its exact identity and proof
  boundary are in the
  [release record](../experiments/harness-invocation/records/2026-10-09-opencode-installer-v0.4.0.md).
  The older Releases through `installer-v0.3.0` remain schema-1 historical snapshots with OpenCode 1
  shapes, and their assets were not replaced.
- Dependency-aware Module Selection is implemented in the checkout and ships in Package 0.4.0:
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

1. **Upstream sync wave W6.** The remaining pin, `dotnet/skills`, is frozen at its 2026-09-06
   position. A cross-upstream review on 2026-10-08 read one decision packet per
   upstream; the packets recommend a treatment for each item; every take, skip, merge, or
   modification is still the curator's decision, and each wave measures its upstream again before
   it starts. A wave moves one submodule.
   - **W6 `dotnet/skills`** (339 commits ahead with no new stable release).
     `configuring-opentelemetry-dotnet`, `minimal-api-file-upload`, and `msbuild-server` were
     deleted upstream and are still taken in `curation/deniz-dotnet-general.yaml`.
     `code-testing-agent` is now a legacy alias that redirects to the new `code-testing` skill
     (packet: re-source to it, rename the output to `code-testing`, keep it `manual`), and a
     consolidated `dotnet/msbuild` skill sits beside the individual MSBuild skills, whose
     descriptions were recut. Thirty-one of 48 taken items changed and 20 stamps drift. A 3-way
     replay measured at `0608d892`: 15 of the 20 patch items fail a direct apply and 8 keep
     conflicts, one of them (`code-testing-agent`) also patching a file upstream deleted. Decide
     before the wave: the MSBuild estate shape, which decides whether four MCP patches are recut or
     retired; MCP posture (build MCP-server emission for every harness, or record a no-ship
     decision); and the `code-testing` name and scope.

   Measured on 2026-10-08, moving the W3 to W6 pins together failed the build: the four taken
   sources deleted upstream failed as unknown sources, and 47 stamped upstream files across 34 of
   the then 48 overlay-lock items no longer matched. Patches added since are not in that count.
   Whether to exclude, relocate, or replace each deleted source is a curator decision.
2. **Iteration 2: dependency automation and original-skill declarations.** After the upstream sync
   waves, address automatic dependency installation, cascade remove, version-range resolution, and
   the `original_skills` declaration. These four workstreams belong to the follow-up iteration, not
   the first feature. Selection/Ownership semantics, user approval for automatic changes, and version
   constraint/conflict policy must be designed in that iteration rather than assumed by the first.
   For original skills, the pressure points are additional original skills, manual original posture,
   and load-bearing outgoing edges that review-only protection cannot guard. Original skills can
   already be guarded targets, but their invocation and outgoing model edges have no manifest
   declaration. The planned surface can be keyed by the existing top-level skill directory:

   ```yaml
   original_skills:
     writing-tunit-tests:
       invocation: auto
       depends_on:
         - test-driven-development
         - test-gap-analysis
         - test-anti-patterns
         - run-tests
         - filter-syntax
   ```

   This is not a second body manifest: `skills/<plugin>/<name>/SKILL.md` keeps content and description
   ownership, while the declaration owns harness-neutral invocation and outgoing model edges. Replace
   load-bearing bare handoffs with namespaced facts; keep the human-started
   `generate-testability-wrappers` route as a namespaced user-pointer rather than `depends_on`.
   Compiler work must feed Claude invocation flags, OpenCode skill/command shape, original-source edge
   scanning, exact two-way dependency symmetry, audience reachability, sync candidates, and a ledger
   own-source marker. Validation must reject a directory with no declaration, a declaration with no
   directory, stale or undeclared edges, duplicate identities, and an inexpressible target posture.
   Acceptance requires auto, manual, both, dangling, stale, undeclared, cross-Module, and generated-
   ledger cases in all three harness trees.

   One consequence needs a curator decision when this lands. The example's `test-driven-development`
   edge targets a Process item, so it would add a General -> Process `requiredModules` edge; General
   requires only Aspire today.
3. **Prototype the curation sanity panel only when another curation wave needs it.** Deterministic
   validation proves identity, shape, linkage, ownership, and bytes; it cannot judge trigger
   competition, over-pruned overlays, or whether a transformed body still serves nearby manifest
   intent. Keep the panel a read-only `docs/agents/` playbook, never a gate. Each run receives one
   bounded packet: pinned upstream body and bundled dependency closure, manifest reason, overlay or
   patch plus lock evidence, and all three emitted harness forms. Review trigger/overlap, body-intent
   preservation, and harness fit separately. Require `file:line` evidence, a concrete consequence,
   confidence, and one of `retain`, `narrow`, `reconsider`, or `ambiguous`; preserve disagreements.
   Reviewers never edit, bless, bump versions, or fail CI. Success is a small curator decision packet,
   not a repeat of deterministic validator findings or a vote that turns model agreement into policy.
   Run it after a body-ownership pass and before declaring a Module closed. The sync packets name
   five trigger-competition cases for it: `subagent-driven-development` against `executing-plans`,
   `aot-trimming` against `dotnet-aot-compat`, the consolidated `msbuild` skill against the MSBuild
   specialists, `directory-build-organization` against `convert-to-cpm`, and `code-testing` against
   `systematic-debugging`; W6 is the natural first run.
4. **Refine composition-selection guidance only after more runtime evidence.** ADR-0005 now owns
   required and forbidden initiation capabilities, but it does not claim that descriptions reliably
   cause model selection. Namespaced body facts prove deterministic existence and audience
   reachability once a source runs, but do not make it run. Descriptions provide probabilistic
   selection pressure and can reach work with no explicit caller. The current candidate rule is:
   load-bearing composition uses guarded body facts; opportunistic passive knowledge can use honest
   descriptions; ceremonies need a human surface. Promote it to a durable decision only when the
   original-skill declaration and another bounded runtime sample confirm the trade-off. Evidence
   remains in
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
  never stops or blocks on OpenCode, and reopening `opencode` restarts a stopped service. Linux bulk
  Apply against a running server is not measured.
- **Earlier schema-1 Releases have no upgrade path:** the Packages of Releases through
  `installer-v0.3.0` ship schema-1 Bundles in OpenCode 1 shapes, and the installer, from the
  checkout or `installer-v0.4.0`, refuses schema-1 Install state with no compatibility reader. A
  user of such a Release has no supported route to schema-2 output; the real-profile migration is a
  recorded one-off, not a product path.
- **Selection dependency automation:** schema-2 manifests and Install state record `requiredModules`.
  Plan presence-checks the final Selection and does not automatically add, cascade-remove, or
  range-resolve Modules. Cross-version item/API compatibility is not claimed. See
  [distribution and installation](architecture/distribution-and-installation.md#full-estate-versus-installed-selection).
- **Codex catalog pressure is real:** every call in the 117-skill Luna panel warned that descriptions
  were shortened to fit the skills context budget. The tested explicit, implicit, manual, handoff,
  reference, and generated-skill paths passed, but that bounded panel is not proof for every skill,
  model, or future catalog size.
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
  namespaced fact and matching dependency; the sync wave that recuts the item is the natural point.
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
  the tool-free Codex dispatch wording. No Codex subagent
  tool or agent type is recorded in repository research, so Codex wording names neither.
- **Linker cause text is skill-specific:** an unreachable command or agent target can receive the
  right verdict with the wrong `disable-model-invocation` explanation.
- **Original-skill declarations are absent:** `manual`/`both` posture and outgoing edge-source scans
  remain unavailable until the declaration surface above exists.
- **Clean-fixture debt is filtered by finding name:** split the fixture so tests prove those findings
  still exist instead of tolerating their disappearance.
- **Full-file overlays have no replay:** an upstream move under a full-file overlay is reviewed
  through the `--bless` diff and ported by hand, as W5 ported `ask-deniz`, `requesting-code-review`,
  `systematic-debugging`, and `test-driven-development`.
- **W5's runtime paths are unmeasured:** `implement-spec` creates each implementer's worktree with
  git where the harness gives a subagent none, which OpenCode 2 does not, and no run has exercised
  that path. Codex has no subagent tool in this repository's research, so there it is upstream's
  "only a slower implement", and the two-reviewer dispatch of `requesting-code-review` is likewise
  unmeasured there.
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
  descriptions should be checked too, and where its boundary lies, is still open.
- **Aspireify activation tension remains upstream-owned:**
  `external/aspire-skills/skills/aspireify/SKILL.md` advertises integration/authoring and toolchain
  work while its Detection section still requires an unwired AppHost and its header says one-time.
  The reviewed update preserves this inherited runtime scope rather than widening it through a
  routing-only curation patch.
- **Module version bumps are policy-only:** emitted bytes can move under an unchanged curator version;
  the Bundle digest detects it, but validation does not enforce the human-facing version rule.
- **Inventory descriptions truncate silently:** long descriptions stop at 140 characters without an
  ellipsis marker.
- **`dotnet-agent-skills` has no release-shaped current pin:** its rolling tool tag and stale v1.0.0
  are not suitable boundaries, so each sync is a reviewed hold-or-main-commit decision.
- **Dead Claude `invocable:` metadata:** it is not a target-harness field; OpenCode drops and reports
  it while Claude passthrough still carries it as harmless noise.
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
