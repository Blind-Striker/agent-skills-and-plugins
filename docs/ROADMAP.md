# Roadmap

Date: 2026-10-09

Operational document: current orientation, next work, open decisions, known gaps, and deferred work.
It shrinks as work lands and is not a chronology. Current mechanics live in
[architecture](architecture/), working practice in [engineering](engineering/), rationale in
[ADRs](adr/), and dated proof in [research](research/) and
[experiment records](../experiments/harness-invocation/records/README.md).

## Current State

- The four current curation manifests pass through one common assembly and emit matching Claude
  Code Plugins, OpenCode Module Bundles, and native Codex Plugins plus both repository marketplaces.
  Checkout Module versions are Process 0.7.0, General 0.10.0, Akka 0.4.0, and Aspire 0.4.0. Their item
  posture, source pins, transformations, exclusions, and reasons live in
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
  files. Localization renders the skill-tool call and the subagent-dispatch words per harness, and
  `validate` runs the handoff-template, harness-vocabulary, and path-claim checks of
  [references and linking](architecture/references-and-linking.md). The OpenCode experiment
  scripts target OpenCode 2: the OpenCode 1 probes are retired, and `oc2-discovery.ps1` checks
  discovery against an isolated `opencode serve`. On v2.0.23 that check discovered the installed
  estate identically on the Windows workstation and the Linux host (115 dotted skills, the 26
  `manual` skills unadvertised, both agents as subagents), and an empty `OPENCODE_CONFIG_DIR` fell
  back like an unset one on Linux
  ([discovery record](../experiments/harness-invocation/records/2026-10-09-opencode2-discovery.md)).
- `dotnet/skills` is reviewed through `d68dd708`. General 0.10.0 carries the current test-execution,
  coverage, test-quality, and testability bodies, takes the promoted `vectorization` specialist, and
  retains curator-owned report-only, manual-ceremony, TUnit-first, and targeted-CRAP boundaries.
- Aspire 0.4.0 follows the reviewed merged `aspire-skills` commit `c9d042e`, whose source metadata is
  0.0.2 and guidance targets Aspire 13.5.3. This is a reviewed main-commit choice, not a claim that
  upstream published a 0.0.2 tag or Release. The eight-skill set and declared dependency closure are
  unchanged; the six official workflow patches continue to own only package-local routing.
- The repository is public with MIT licensing for original work, source-specific notices and exact
  upstream license copies, a public noreply marketplace contact, least-privilege secret and
  machine-path CI checks, private vulnerability reporting, and an explicit personal/no-SLA boundary.
- OpenCode supports two installation transports: a recursive clone using the repository installer,
  and the compiled npm-format Package attached to GitHub Release `installer-v0.3.0`. The Linux-built
  asset includes the General and Aspire updates and passed the manual release workflow's source
  gate, tar-mode verifier, isolated Plan/Apply/status, publication, and remote re-download checks.
  That public Package remains a schema-1 historical source snapshot with OpenCode 1 shapes. Its exact
  identity and proof boundary are in the
  [release record](../experiments/harness-invocation/records/2026-09-06-opencode-installer-v0.3.0.md).
  The older Releases remain historical and their assets were not replaced. Process 0.7.0 and the
  Codex support are on `master` but in no Release.
- Dependency-aware Module Selection is implemented in the checkout: schema-2 Bundles and Install
  state, compile-time `requiredModules` derivation, final-Selection presence checks,
  actual-versus-proposed status, and metadata-only Apply with exact Recovery. Feature source is
  four commits through `8be80489cd721b08f0ffa3bee711d8348d1e0ac1`; the independently reviewed tree
  `6e111fbcdcfae83d3401e89a9db27d898d8d001f` equals that source HEAD tree. Linux Package proof is a
  build-only workflow artifact that shares the public Package filename; it is not a new public
  Release and was not installed into a real profile. Exact artifact identity, both workflow runs,
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

1. **OpenCode 2 migration.** The canon, dated research, spec, and plan come first; the steps below
   run in this order, and each starts on the curator's word.
   1. **Measurement records.** The OpenCode-1-bound probes are retired, and
      `experiments/harness-invocation/oc2-discovery.ps1` checks discovery against an isolated
      `opencode serve` HTTP API (`/api/skill`, `/api/command`, `/api/agent`; the skill routes are
      marked experimental) under the OpenCode 2 lab isolation that
      [the harness protocol](../experiments/harness-invocation/protocol.md#isolate) owns. The
      discovery record on both hosts is committed. Still to record: one LLM record proving that a
      manual skill is unadvertised, `@`-attachable, and callable by the model through its
      namespaced ID; its model route waits on the curator's credential choice.
   2. **Profiles and Release.** Measure upstream issue `anomalyco/opencode#47505` on an isolated
      Windows profile as
      [distribution and installation](architecture/distribution-and-installation.md#target-opencode-runtime)
      requires. Migrate the two real profiles (a Windows workstation and a Linux host, both on
      OpenCode 2) once by a manual procedure recorded in an experiment record: remove the Modules
      with the schema-1 installer, remove the then-empty schema-1 state, and install schema-2
      output. This is a one-off, not a supported product path. Then cut a new Package Release:
      bump the `package.json` version, update the `tools/repository-docs.test.ts` pins, and change
      the README consumption recipes, which describe the published Release, only at this step.
2. **Upstream sync waves, one submodule at a time, cheapest first.** All six pins are frozen at their
   2026-09-06 positions. Order and the upstream state measured on 2026-10-08:
   - `dotnet-skills`: 3 commits ahead (v1.6.0). No taken item changed and no overlay-lock stamp
     drifts; the only content change is the new `aot-trimming` candidate.
   - `asd-ste100`: 12 commits ahead, no tags. The single taken root item changed in every shipped
     path and adds `examples/linter-edge-cases.md` and a non-executable `scripts/ste-lint.py` whose
     interpreter and invocation fit need review. No overlay-lock stamp covers it, so these changes
     pass straight through on a pin move.
   - `superpowers`: v6.3.0 to v6.4.2. Nine of 14 taken items changed; six stamps drift.
     `writing-plans` drops `plan-document-reviewer-prompt.md`, `executing-plans` gains executable
     `scripts/task-start` and `scripts/task-done` (mode 100755, which the Codex mode check covers),
     `using-superpowers` gains references, and `diagnosing-superpowers` is a new candidate.
   - `aspire-skills`: 29 commits ahead. Upstream published v0.0.3 (2026-09-23); HEAD is three
     commits past it and targets Aspire 13.6. All six taken items changed and 12 stamps drift, so
     all six patches need recutting; `aspire-project-v2-migration` is a new candidate.
   - `mattpocock-skills`: 92 commits ahead (v1.3.0 and v1.3.1). `resolving-merge-conflicts` was
     deleted upstream and is still taken in `curation/deniz-process.yaml`. Eighteen of 25 taken or
     merge-source items changed; `domain-modeling` replaced `CONTEXT-FORMAT.md` with
     `GLOSSARY-FORMAT.md`; `implement-spec`, `pr`, `retro`, and `chief-of-staff` are new
     candidates. The wave also decides the `setup-matt-pocock-skills` question of patching it to
     prefer `AGENTS.md`, since OpenCode 2 loads `AGENTS.md` and never `CLAUDE.md`.
   - `dotnet/skills`: 339 commits ahead with no new stable release.
     `configuring-opentelemetry-dotnet`, `minimal-api-file-upload`, and `msbuild-server` were deleted
     upstream and are still taken in
     `curation/deniz-dotnet-general.yaml`. `code-testing-agent` is now a legacy alias that redirects
     to the new `code-testing` skill, and a consolidated `dotnet/msbuild` skill sits beside the
     individual MSBuild skills, whose descriptions were recut. Thirty-one of 48 taken items changed
     and 20 stamps drift.

   Moving several pins together fails the build: the four taken sources deleted upstream fail as
   unknown sources, and 47 stamped upstream files across 34 of the 48 overlay-lock items no longer
   match. Whether to exclude, relocate, or replace each deleted source is a curator decision.
3. **Iteration 2: dependency automation and original-skill declarations.** After the upstream sync
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
4. **Prototype the curation sanity panel only when another curation wave needs it.** Deterministic
   validation proves identity, shape, linkage, ownership, and bytes; it cannot judge trigger
   competition, over-pruned overlays, or whether a transformed body still serves nearby manifest
   intent. Keep the panel a read-only `docs/agents/` playbook, never a gate. Each run receives one
   bounded packet: pinned upstream body and bundled dependency closure, manifest reason, overlay or
   patch plus lock evidence, and all three emitted harness forms. Review trigger/overlap, body-intent
   preservation, and harness fit separately. Require `file:line` evidence, a concrete consequence,
   confidence, and one of `retain`, `narrow`, `reconsider`, or `ambiguous`; preserve disagreements.
   Reviewers never edit, bless, bump versions, or fail CI. Success is a small curator decision packet,
   not a repeat of deterministic validator findings or a vote that turns model agreement into policy.
   Run it after a body-ownership pass and before declaring a Module closed.
5. **Refine composition-selection guidance only after more runtime evidence.** ADR-0005 now owns
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

- **Public Release surface lags the decision:** the README OpenCode recipes and its capability
  summary (OpenCode "receives a skill, a command, or both", parked manual bodies, refused
  alternate config-dir mounts), the `package.json` version 0.3.0, and the
  `tools/repository-docs.test.ts` pins describe `installer-v0.3.0`; they change only at the next
  Release step.
- **Public schema-1 Release has no upgrade path:** `installer-v0.3.0` ships schema-1 Bundles in
  OpenCode 1 shapes, and the checkout installer refuses schema-1 Install state with no
  compatibility reader. A user of that Release has no supported route to schema-2 output; the
  real-profile migration is a recorded one-off, not a product path.
- **Real profiles hold schema-1 state:** both real profiles run OpenCode 2 but hold
  `installer-v0.3.0` schema-1 Install state and OpenCode 1 shapes, so the checkout installer cannot
  update or prune them until the recorded one-off migration runs.
- **Selection dependency automation:** schema-2 manifests and Install state record `requiredModules`.
  Plan presence-checks the final Selection and does not automatically add, cascade-remove, or
  range-resolve Modules. Cross-version item/API compatibility is not claimed. See
  [distribution and installation](architecture/distribution-and-installation.md#full-estate-versus-installed-selection).
- **Codex catalog pressure is real:** every call in the 117-skill Luna panel warned that descriptions
  were shortened to fit the skills context budget. The tested explicit, implicit, manual, handoff,
  reference, and generated-skill paths passed, but that bounded panel is not proof for every skill,
  model, or future catalog size.
- **Codex distribution coverage is intentionally split:** native Plugins cover Codex CLI and Codex
  in ChatGPT desktop, not the Codex IDE extension. IDE coverage would need a separately owned
  standalone-skill transport. Same-machine CLI/app-server custom-marketplace state sharing is
  measured; visual Desktop UI behavior, cross-device synchronization, and public OpenAI
  universal-directory submission remain separate distribution work, not requirements for repository
  marketplace installation.
- **Case-sensitive fact scan:** capitalized namespaced spellings can evade the lowercase scanner.
- **Bare references outside the handoff templates are review-only:** ordinary names, "see the `x`
  skill" references, routing hints without the word `skill`, unbackticked imperatives (for example
  `brainstorming`'s "the writing-plans skill"), and "Hand off to `x`" (`aspire-init` to
  `aspireify`) are candidates, not build state. Promote load-bearing cases through an authored
  namespaced fact and matching dependency; the sync wave that recuts the item is the natural point.
- **Deleted names leave the handoff universe:** a name deleted upstream at a pin move stays an
  estate name only while its manifest item lists it, but `validate` rejects a manifest source that
  no longer exists, even an excluded one (`tools/validate.ts`, section 1). Deciding how a deletion
  stays recorded is shared sync infrastructure that must land before the first pin move that
  deletes a taken source (`mattpocock-skills`, `dotnet/skills`); until then a new bare mention of a
  deleted name is not caught, while a promoted fact still fails as a dangling reference.
- **Harness phrasing is thinly measured:** one Codex run followed a body
  ``Invoke `$plugin:skill` `` handoff; no run covers the rendered OpenCode skill-tool sentence, the
  `Subagent (general)` dispatch label, or the tool-free Codex dispatch wording. No Codex subagent
  tool or agent type is recorded in repository research, so Codex wording names neither.
- **Optional writing-style reference remains external:** `brainstorming` names
  `elements-of-style:writing-clearly-and-concisely` when available, but that namespace is not curated
  here; validation keeps the unresolved optional route visible.
- **Linker cause text is skill-specific:** an unreachable command or agent target can receive the
  right verdict with the wrong `disable-model-invocation` explanation.
- **Original-skill declarations are absent:** `manual`/`both` posture and outgoing edge-source scans
  remain unavailable until the declaration surface above exists.
- **Clean-fixture debt is filtered by finding name:** split the fixture so tests prove those findings
  still exist instead of tolerating their disappearance.
- **Bless hints disagree:** primary drift prints a two-step review ceremony while merge drift suggests
  the one-step `--bless --yes` form.
- **Recorded-absent merge directories:** a directory at a stamped filename produces an imprecise
  report and can reach a raw blob-hash error.
- **Scanner layout blind spots:** grouped commands and agents are missed, while nested component
  directories can be double-counted.
- **Overlay mode drift:** overlay locks hash bytes but not executable mode.
- **Symlink-boundary patches:** emitted copies skip symlinks while `git apply` cannot patch through
  them.
- **Frontmatter override staleness:** sync reports body movement beneath an override, but no stamp or
  build-time guard exists.
- **Unstated Aspire invocation:** six official workflow items still pass upstream Claude posture
  through because their manifests state no invocation intent.
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
