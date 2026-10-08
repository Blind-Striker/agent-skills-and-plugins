# Roadmap

Date: 2026-10-08

Operational document: current orientation, next work, open decisions, known gaps, and deferred work.
It shrinks as work lands and is not a chronology. Current mechanics live in
[architecture](architecture/), working practice in [engineering](engineering/), rationale in
[ADRs](adr/), and dated proof in [research](research/) and
[experiment records](../experiments/harness-invocation/records/README.md).

## Current State

- The four current curation manifests pass through one common assembly and emit matching Claude
  Code Plugins, OpenCode Module Bundles, and native Codex Plugins plus both repository marketplaces.
  Checkout Module versions are Process 0.6.0, General 0.9.1, Akka 0.3.1, and Aspire 0.3.3. Their item
  posture, source pins, transformations, exclusions, and reasons live in
  [`curation/*.yaml`](../curation/) and the generated [ledger](ledger.json), not in this roadmap.
- OpenCode 2 is the only OpenCode target. Canon now states the decided OpenCode 2 rules: the runtime
  floor and dropped OpenCode 1 support in
  [distribution and installation](architecture/distribution-and-installation.md#target-opencode-runtime),
  the invocation mapping and `<plugin>.<name>` IDs in
  [transformation and emission](architecture/transformation-and-emission.md#opencode), and their rationale in
  [ADR-0002](adr/0002-multi-harness-output.md) and
  [ADR-0005](adr/0005-invocation-intent-in-the-manifest.md). The checkout does not implement them
  yet: emission, rewriting, validation, the ledger, the installer Destination, and the OpenCode
  experiment scripts still produce or check OpenCode 1 shapes, and the committed `opencode/` tree is
  OpenCode 1 output (38 commands, including global-root stubs and both-duplicates, and parked
  `BODY.md` folders under bare names). The responsible files are listed under
  [Known Gaps](#known-gaps).
- `dotnet/skills` is reviewed through `d68dd708`. General 0.9.1 carries the current test-execution,
  coverage, test-quality, and testability bodies, takes the promoted `vectorization` specialist, and
  retains curator-owned report-only, manual-ceremony, TUnit-first, and targeted-CRAP boundaries.
- Aspire 0.3.3 follows the reviewed merged `aspire-skills` commit `c9d042e`, whose source metadata is
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
  The older Releases remain historical and their assets were not replaced. Process 0.6.0 and the
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
   1. **Compiler, validator, and installer.** Close the OpenCode 2 implementation gap below:
      namespaced emission, the manual metadata key, removal of parks, stubs, and duplicate commands,
      native-only agent keys, the OpenCode reference and sibling-link rewrite, the neutral
      portable-name manifest rule, the new validation rules (manual-ID leak, skill-tool handle and
      rendered-ID resolution, phantom-skill guard, agent frontmatter), retirement of the park and
      stub checks, ledger probes, and the `OPENCODE_CONFIG_DIR` Destination.
   2. **Curation pass under OpenCode 2.** Review the 27 `manual` and 11 `both` items: decide which
      argument-shaped items, such as `handoff` and `teach`, become `as: command`; promote the 12
      load-bearing bare skill-tool handles in Process bodies (for example `Skill tool with
      "grilling"` in `grill-me`, `grill-with-docs`, `wayfinder`, and
      `improve-codebase-architecture`; targets `grilling`, `domain-modeling`, `research`,
      `codebase-design`, and `prototype`) to namespaced facts with matching `depends_on`; and refresh
      reasons written against OpenCode 1 plus comments that describe only two harnesses. Every
      curation decision is the curator's.
   3. **Measurement records.** Retire the OpenCode-1-bound probes (`stub-command-smoke.ps1` and the
      OpenCode 1 CLI matrices). Port only a discovery check against an isolated `opencode serve`
      HTTP API (`/api/skill`, `/api/command`, `/api/agent`; the skill routes are marked
      experimental) and one LLM record proving that a manual skill is unadvertised, `@`-attachable,
      and callable by the model through its namespaced ID. Lab isolation changes on OpenCode 2:
      `OPENCODE_CONFIG_DIR` replaces the global root, a managed background service needs
      `--standalone` or an isolated `serve`, `OPENCODE_DISABLE_PROJECT_CONFIG` skips the ancestor
      walk, and `~/.claude/skills` and `~/.agents/skills` are always-on compatibility roots.
   4. **Profiles and Release.** Measure upstream issue `anomalyco/opencode#47505` on an isolated
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

   Two consequences need a curator decision when this lands. The example's `test-driven-development`
   edge targets a Process item, so it would add a General -> Process `requiredModules` edge; General
   requires only Aspire today. And `generate-testability-wrappers` is `manual`, so under the OpenCode
   manual-ID leak rule an `auto` original skill cannot render that route as a namespaced OpenCode
   pointer; the OpenCode rendering of that route must be decided.
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

- **OpenCode 2 emission is not implemented:** `emitOpenCodeSkill` and `emitOpenCode` in
  `tools/build.ts` still emit bare `skills/<name>/` and `commands/<name>.md` paths, park manual
  bodies as `BODY.md` behind global-root stub commands, add duplicate commands for `both` and inline
  commands for manual items, write no namespaced `name` and no `opencode/autoinvoke` metadata, do not
  map an upstream `disable-model-invocation` to that key, and keep the `license` and `compatibility`
  skill keys that OpenCode 2 ignores.
  `writeOpenCodeManifests` looks up modes through `plugins/<plugin>/<path>`, so renamed namespaced
  paths would miss it and the seven executable Process files would fall back to 100644.
- **OpenCode 2 references are not rewritten:** the `opencode` style of `rewriteRefs` in
  `tools/lib/rewrite.ts` renders a bare `<name>`, not `<plugin>.<name>`, `@<plugin>.<name>`, or
  `/<plugin>.<name>`, and no pass rewrites relative `../<name>/` sibling links to
  `../<plugin>.<name>/`.
- **OpenCode 2 identity and validation are not implemented:** `claimOpenCodeDestination` in
  `collectIdentityProblems` (`tools/lib/resolve.ts`) claims only `command:<name>` for manual items
  and keys OpenCode collisions per kind on bare names, so the repository-wide bare-name check across
  kinds is absent: a skill and an agent with one bare name in different Modules pass preflight, and
  only a declared model-edge to that name fails, in `deriveModuleRequirements`. The portable-name
  rule is enforced only as a side effect of `CODEX_NAME` in `tools/lib/codex-plugin.ts`. In
  `tools/validate.ts`, the `openCodeArtifact`
  helper and the linker mapping resolve bare paths, the L6 parked-file check and the converted-command
  and parked-bundle warnings still run, and the manual-ID leak, skill-tool handle and rendered-ID
  resolution, phantom-skill, and native agent-key checks are absent.
- **OpenCode 2 ledger probes are not implemented:** `writeLedger` in `tools/lib/ledger.ts` probes
  bare OpenCode skill, command, and agent paths and records a `parked` file list. The path
  collection script in `docs/agents/reference-audit-playbook.md` reads the same bare paths and
  `entry.opencode.parked`, and changes with the ledger.
- **OpenCode 2 Destination is not implemented:** `resolveDestination` in
  `tools/lib/opencode-install-state.ts`, used by `tools/install-opencode.ts`, still refuses a
  non-empty `OPENCODE_CONFIG_DIR` instead of using it as the global root. Its `<home>` fallback also
  needs a decision: `tools/install-opencode.ts` derives home from `HOME`, then `USERPROFILE`, then
  `os.homedir()`, while OpenCode 2 uses `os.homedir()`, so on Windows with a `HOME` that differs
  from the profile folder and no XDG variable the two can resolve different config roots.
- **OpenCode 1 tests and probes remain:** `tools/build.test.ts`, `tools/validate.test.ts`,
  `tools/lib/rewrite.test.ts`, `tools/lib/ledger.test.ts`, and `tools/repository-docs.test.ts`
  assert bare paths or the park and stub shapes. In `experiments/harness-invocation/`,
  `stub-command-smoke.ps1` and the OpenCode legs of `common.ps1`, `lab.ps1`, the matrix and variant
  scripts, `ocprobe.ps1`, `verify.ps1`, and `selftest.ps1` rely on OpenCode 1 behavior such as
  `opencode debug skill`, an additive `OPENCODE_CONFIG_DIR`, and the installer's refusal of it. The
  harness-invocation `protocol.md` and `runbook.md` still describe that OpenCode 1 isolation and the
  `BODY.md` stub checks.
- **Public Release surface lags the decision:** the README OpenCode recipes and its capability
  summary (OpenCode "receives a skill, a command, or both", parked manual bodies, refused
  alternate config-dir mounts), the `package.json` version 0.3.0, and the
  `tools/repository-docs.test.ts` pins describe `installer-v0.3.0`; they change only at the next
  Release step.
- **Curation comments describe OpenCode 1 or two harnesses:** for example the "both harnesses"
  comments in `curation/deniz-dotnet-akka.yaml` and `curation/deniz-dotnet-aspire.yaml`, the
  `analyzing-dotnet-performance` reason in `curation/deniz-dotnet-general.yaml`, and the
  husk-removal reason in `curation/deniz-process.yaml`. They are refreshed in the curation pass
  above, on the curator's decisions.
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
- **Bare references are review-only:** ordinary names are candidates, not build state. Promote only
  load-bearing cases through an authored namespaced fact and matching dependency.
- **Optional writing-style reference remains external:** `brainstorming` names
  `elements-of-style:writing-clearly-and-concisely` when available, but that namespace is not curated
  here; validation keeps the unresolved optional route visible.
- **An `expects` guard remains deferred:** add manifest-side protection for bare-name expectations
  only if load-bearing bare references accumulate; today's isolated cases do not justify another
  grammar surface.
- **Converted command paths:** a body copied to an additional command location can retain a
  skill-relative path that no longer resolves there. The OpenCode 2 emission removes the implicit
  conversions (stubs, both-duplicates, inline manual commands) and retires this warning class; it
  can return only if the curation pass gives `as: command` to a skill with bundled files, such as
  `teach`. Remove this entry when the emission lands and that pass is decided.
- **Linker cause text is skill-specific:** an unreachable command or agent target can receive the
  right verdict with the wrong `disable-model-invocation` explanation.
- **Parked-path regex interpolation is unescaped:** current kebab-case output names are safe, but
  `tools/validate.ts` should escape a name before constructing the expression. The OpenCode 2
  emission retires the parked-path check that builds it; remove this entry when that lands.
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
