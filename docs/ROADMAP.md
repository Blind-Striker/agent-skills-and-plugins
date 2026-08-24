# Roadmap

Date: 2026-08-24

Operational document: current orientation, next work, open decisions, known gaps, and deferred work.
It shrinks as work lands and is not a chronology. Current mechanics live in
[architecture](architecture/), working practice in [engineering](engineering/), rationale in
[ADRs](adr/), and dated proof in [research](research/) and
[experiment records](../experiments/harness-invocation/records/README.md).

## Current State

- This GitHub repository (`Blind-Striker/agent-skills-and-plugins`) currently uses `master` as its
  default branch. The current curation manifests emit matching Claude Code Plugins and OpenCode
  Module Bundles. `deniz-process`, `deniz-dotnet-general`, `deniz-dotnet-akka`, and
  `deniz-dotnet-aspire` have completed their current curation passes. Aspire 0.3.0 follows the current
  `microsoft/aspire-skills` pin and carries the complete six-skill official workflow package,
  `aspire-integration-testing`, and the selected `aspire-service-defaults` specialist. Upstream eval
  assets are omitted; package-local model handoffs are localized and linker-checked;
  `aspire-configuration` and the broken Mailpit cookbook are explicitly excluded. General's devcert
  skill now retains one guarded cross-Module edge to ServiceDefaults, while the closed Akka cookbook
  keeps its guarded General and Aspire edges. Item posture and reasons live in the
  [manifests](../curation/) and generated [ledger](ledger.json). The real Claude profile still has
  process and General enabled; Akka and Aspire remain uninstalled there. Official Claude utility
  Plugins are outside the [skill-source migration documented by the dated adapter and installer
  evidence](research/harness-adapters.md#opencode); future Akka and Aspire Claude installs require
  explicit approval rather than a standing migration task.
- Four upstream pins have moved in the current recuration initiative. `dotnet-skills` is at
  `13e26d3` (v1.5.0): the OpenTelemetry body remains upstream-owned with its known dependency-free
  callback defect recorded beside the item, while `csharp-nullable-reference-types` is now passive
  `auto` knowledge for nullable-enabled code. Its migration playbook is omitted and project-wide
  adoption still routes to the existing manual `migrate-nullable-references` ceremony.
  `superpowers` is at `b36e082` (v6.3.0): the `using-superpowers` patch was recut over upstream's
  new platform line, the `requesting-code-review` overlay absorbed upstream's no-subagent contract
  before re-blessing, and the `brainstorming` description override was rewritten to cover all three
  of upstream's paths instead of only the architectural one. `mattpocock-skills` is at `0ab1b63` (v1.2.3-33):
  the deleted `writing-great-skills` source was replaced by `writing-for-agents` rather than
  patched around, the `research` patch was recut so upstream's own wording survives as its context,
  three merge sources were re-blessed and `diagnosing-bugs`' secret-redaction rules were absorbed
  into the owned `systematic-debugging` body, `wait-what`, `to-questionnaire` and `wizard` were
  taken as `manual`, ten read-and-rejected components were recorded as exclusions, and `ask-matt`
  became the owned router `ask-deniz`. The single-purpose `asd-ste100` source was added at reviewed
  master commit `d5ce157`; Process takes its root skill as `auto`, and `wait-what` now has a guarded
  handoff to it in both harnesses. `deniz-process` is 0.4.0. `dotnet-agent-skills` is at `516db1e`: five drifted patches were
  recut after reading their new source intent, `test-gap-analysis` lost the upstream step that made
  a report-only audit write test files, `platform-detection`'s husk posture became this repo's own
  choice once upstream dropped its posture keys, and two new components were answered. Two exclusions
  became merges rather than rejections — `testability-obstacle`'s ambient-seam corrections went into
  `generate-testability-wrappers` (they caught a real defect there: the shipped scope example cleared
  the slot on dispose while the rule beneath it asked for the previous value restored), and the
  rewritten `optimizing-ef-core-queries` was split by level, its SQL-level sargability and keyset
  paging into `database-performance` and its `EF.CompileQuery` guidance into `efcore-patterns`.
  `deniz-dotnet-general` is 0.8.0. Every module's scanner-visible corpus now has an explicit answer.
- The repository remains private. The emitted global-only OpenCode installer and private Package
  recipe are available in the root [README](../README.md); current mechanics are in
  [distribution and installation](architecture/distribution-and-installation.md). The measured real
  OpenCode Selection contains process, General 0.2.0, and the pre-curation Akka starter. General and
  Akka now differ from generated output, while Aspire remains unselected; any update must explicitly
  keep General, Akka, and Aspire together until dependency-aware Selection planning exists. No Apply
  was performed in this curation pass. The migration and isolated Package evidence remain in the
  [installer record](../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md).
- Live installation status on 2026-08-24 is behind generated output. Claude Code has Process 0.2.0
  and General 0.2.0 enabled; Akka, Aspire, and the new ASD-STE100 surface are not installed there.
  OpenCode Selection has Process 0.2.0, General 0.2.0, and Akka 0.1.0, all differing from current
  Bundles; Aspire is unselected, with no lock or Recovery. Installing every Module is a supported
  complete Selection and avoids the unresolved arbitrary-subset dependency gap. It still requires a
  read-only Plan first and explicit approval before Apply; no real installation changed in this wave.

## Next Up

1. **Add an original-skill declaration surface.** Original skills are now guarded targets and
   participate in `sync` candidates, but their bodies are not guarded sources and their invocation
   cannot be expressed neutrally for both harnesses. Add a manifest-owned declaration keyed by the
   existing top-level skill directory, for example:

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

   The declaration is not a second body manifest: `skills/<plugin>/<name>/SKILL.md` continues to own
   content and description, while this surface owns harness-neutral invocation and outgoing model
   edges. Replace the current bare handoffs with namespaced facts; keep the human-started
   `generate-testability-wrappers` route as a namespaced user-pointer rather than `depends_on`.
   Compiler work must feed the existing Claude invocation flags and OpenCode skill/command shape,
   scan original-skill Markdown as an edge source, enforce exact two-way dependency symmetry and
   audience reachability, and add original entries to the ledger with an explicit own-source marker.
   Validation must reject a directory with no declaration, a declaration with no directory, stale or
   undeclared edges, duplicate identities, and a posture the target harness cannot express. This one
   design closes both current gaps: original outgoing source guards and original `manual`/`both`
   posture. Acceptance requires auto, manual, both, dangling, stale, undeclared, cross-Module, and
   generated-ledger tests in both harness trees.
2. **Prepare a deliberate public release.** The repo is private today, and green generation is not a
   public-release gate by itself. Before changing visibility:

   - decide whether intentionally upstream-owned Aspire examples and their stated proof limits are
     acceptable in a public package;
   - decide whether the format-required marketplace owner name and email may be public, or replace
     them with approved public contact metadata;
   - choose and add a root license for this repository's original code, declare package license
     metadata, and audit that every redistributed upstream MIT copyright/permission notice travels
     with Plugin, Bundle, and Package output where required;
   - add repository-wide secret/token and machine-path CI scans, with only the named synthetic
     machine-path fixture allowlisted, then scan current files and Git history before visibility
     changes;
   - review public README wording, private authenticated Release recipes, Release tags/assets and
     hashes, security/contact expectations, and generated marketplace metadata for a public audience;
   - run the full gate from a clean recursive-submodule checkout and inspect the public GitHub view
     before announcing availability.

   Until those checks pass, the repository is functionally consumable by its owner but not approved
   for public release.
3. **Design the curation sanity panel as an agent playbook, never a gate.** Deterministic validation
   answers identity, shape, linkage, ownership and byte questions; it cannot judge whether two skills
   trigger-compete, an overlay over-pruned upstream, or a transformed body still serves its nearby
   manifest intent. Start with a read-only `docs/agents/` playbook rather than product code. Each run
   receives one bounded item packet: pinned upstream original and bundled dependency closure,
   manifest reason, overlay/patch plus lock evidence, and both emitted harness forms. Use isolated
   reviewers for trigger/overlap, body-intent preservation, and harness fit; current curator routing
   limits agents to OpenAI Luna/Sol. Require evidence as `file:line`, a concrete consequence, a
   confidence level, and one of `retain`, `narrow`, `reconsider`, or `ambiguous`. Present a merged
   advisory table and preserve disagreements; reviewers never edit, bless, bump versions, or fail CI.
   Natural triggers are after a merge/overlay pass and before declaring a Module closed. The panel is
   successful when it gives the curator a small decision packet without re-reporting deterministic
   linker/validator findings or turning model agreement into policy.
4. **An invocation ADR candidate, deliberately unwritten.** Body handoffs and description matching
   are mechanisms of different reliability. A namespaced body fact gives deterministic existence and
   audience proof once its source runs, but does not make the source run. A trigger description is
   probabilistic selection pressure, but can reach work with no explicit caller. Stabilize a rule for
   when a capability needs one, the other, or both: load-bearing composition should use guarded body
   facts; opportunistic passive knowledge can rely on honest descriptions; ceremonies need a human
   user surface. Keep the ADR unwritten until more runtime evidence confirms that this is the actual
   durable dial rather than today's synthesis. Evidence remains in
   [skill-invocation-across-harnesses.md](research/skill-invocation-across-harnesses.md) and
   [skill-framework-landscape.md](research/skill-framework-landscape.md).

## Recommended Sequence

1. Install all four current Modules and all four Claude Plugins only after reviewing the read-only
   Plans; an all-Module Selection is complete even before dependency-aware subset planning exists.
2. Design and implement `original_skills` when another original skill, a manual original posture, or
   more load-bearing outgoing edges make the current review-only protection insufficient.
3. If public release is the next goal, perform licensing/notice, privacy metadata, secret-history,
   machine-path and public transport checks before changing repository visibility.
4. Prototype the sanity panel as documentation and prompts; promote no part of it into CI.
5. Write the invocation ADR only after the declaration surface and another bounded runtime sample
   make the trigger-versus-handoff rule stable.

## Known Gaps

- **Add dependency-aware Module Selection planning.** The Akka-owned
  `akka-net-aspire-configuration` has guarded references from `deniz-dotnet-akka` into General and
  Aspire, while General's `dotnet-devcert-trust` now has a guarded reference into Aspire's
  `aspire-service-defaults`. Full-estate validation proves those links, but the installer does not
  yet close an arbitrary Selection over required Modules. Extend Package or Bundle metadata and Plan
  validation to add required Modules automatically or reject an incomplete Selection before
  treating a partial install as dependency-safe. Until then, the operator must select General,
  Akka, and Aspire together. See the
  [architecture limit](architecture/distribution-and-installation.md#full-estate-versus-installed-selection).
- **Aspire runtime examples remain upstream-owned.** The official Microsoft package is pinned to
  current upstream and transformed only for package-local references; the selected dotnet-skills
  integration-testing body is also unowned. Review identified version-sensitive CLI/TypeScript
  examples and testing API samples that deterministic generation and linkage do not prove. They were
  deliberately preserved rather than forked, so a green build must not be described as proof that
  every upstream sample compiles or matches the installed Aspire CLI version.
- **Same-name, different-kind semantic maps are name-only.** Qualify rewrite and linker maps by kind,
  or narrow the legality rule, before relying on a cross-kind duplicate. See the
  [architecture limit](architecture/references-and-linking.md#proof-boundary-and-current-limits).
- **No repository-wide secret/token scanner.** The prohibition is always-on but currently depends on
  review. Add a repo-wide CI guard without introducing real secret fixtures.
- **Repo-wide machine-path scan in CI.** `experiments/harness-invocation/selftest.ps1` scans only
  the experiment tree; automate the always-on prohibition across the repository with an explicit
  fixture allowlist.
- **Optional writing-style reference remains external.** The curated `brainstorming` body says to
  use `elements-of-style:writing-clearly-and-concisely` when available, but that namespace is not
  curated here. `validate` keeps the reference visible until the style skill is either taken or the
  optional handoff is removed.
- **Some command copies still paste their whole body into the OpenCode chat.** A command is a
  template, and the TUI renders its body as the user's message. Bundled `manual` conversions with
  files use the current global-only parked-body stub; project-local mounts are historical experiment
  evidence, not product support or a current product gap. The remaining full-paste cases are `both`
  items and bundle-less `manual` items. Revisit only if those command bodies become noisy in
  practice. See [transformation and emission](architecture/transformation-and-emission.md#opencode).
- **Native-tree parked-body permission observation is unmeasured.** The prior config-dir mount asked
  for folder access because parked files were outside the project. Installer composition puts them
  under the normal global config root; only an isolated human TUI observation can establish whether
  that shape prompts, so no permission configuration is inferred yet.
- **Case-sensitive reference scan.** Decide how to catch capitalized spellings without losing the
  lowercase scanner's false-positive boundary; `Superpowers:Foo` currently slips through. See the
  [architecture limit](architecture/references-and-linking.md#proof-boundary-and-current-limits).
- **Bare names stay invisible, by decision rather than by omission.** Upstream names are ordinary
  words, so candidates never become build state automatically. `grill-me` → `grilling` remains
  deliberately undeclared and would be silent if its target changed; promote a load-bearing case
  only through an item-level spelling and dependency decision. See
  [references and linking](architecture/references-and-linking.md#one-grammar-three-evidence-tiers)
  and the measured mismatch in the
  [invocation research](research/skill-invocation-across-harnesses.md#the-composition-pattern).
- **Converted commands can break skill-relative paths.** Resolve each reported case through a body,
  shape, or emitter decision; no one relative spelling serves both copies of every `both` item. See
  the [architecture limit](architecture/references-and-linking.md#proof-boundary-and-current-limits).
- **Linker's unreachable-cause string assumes a skill target.** A model-edge pointing at a
  command or agent reports "(disable-model-invocation in the Claude tree)" — right verdict,
  wrong cause text. A `kind` on the target state fixes the parenthetical.
- **The parked-path check interpolates output names into a RegExp unescaped.** Current kebab-case
  names are safe; escape the name in `tools/validate.ts` before constructing the expression.
- **`manual` cannot be expressed for an original skill.** `emitOpenCode` reads its invocation from
  the map `buildAll` fills from manifest items only, so an own skill always resolves to `undefined`
  and OpenCode always emits it as a skill. Writing `disable-model-invocation: true` into an own
  body therefore makes it user-only in Claude while it stays model-discoverable in OpenCode — the
  two harnesses disagree. Absent frontmatter and `user-invocable: false` are the two postures the
  path expresses coherently. Feed own skills into the invocation map, or say in
  [documentation](engineering/workflow.md) that own skills are model-reachable by construction.
- **Own-skill edge source scan.** Extend derived-edge analysis to scan original skills under
  `skills/` as sources. See the [architecture limit](architecture/references-and-linking.md#proof-boundary-and-current-limits).
- **The clean-fixture test filters two named findings (`FIXTURE_DEBT`)** instead of asserting
  their presence, so it also passes if the linker stops reporting them. A fixture split retires
  the tolerance.
- **`--bless` hints disagree on ceremony.** The primary-drift message prints the two-step form
  (see the diff, re-run with `--yes`); the merge-drift messages hand over `--bless --yes`
  one-step. The drift is printed either way, so nothing is hidden — but one ceremony should win.
- **A directory appearing at a recorded-absent merge filename** reports "(appeared upstream)"
  though it could never be stamped, and the non-null sibling branch would `blobSha` a directory
  into a raw git error instead of a finding.
- **Scanner blind spots.** Broaden `tools/lib/scan.ts` discovery to handle grouped commands and
  agents without double-counting nested skill directories. See the
  [transformation limit](architecture/transformation-and-emission.md#current-limits).
- **Overlay mode drift.** Include executable mode in overlay lock stamps so a mode-only upstream
  change forces review and re-blessing. See the
  [transformation limit](architecture/transformation-and-emission.md#current-limits).
- **Symlink-boundary patches.** Decide how to support or report patches that touch symlink paths;
  `aspire-skills` carries symlinks inside curated skills. See the
  [transformation limit](architecture/transformation-and-emission.md#current-limits).
- **Manifest override staleness guard.** `npm run sync` now reports an override standing in front
  of a body that moved, so a pin move surfaces it for rereading. There is still no stamp and no
  build-time guard, so an override can go stale between pin moves without anything saying so.
  See the [transformation limit](architecture/transformation-and-emission.md#current-limits).
- **Six Aspire items never got an invocation decision.** `aspire`, `aspire-init`, `aspireify`,
  `aspire-orchestration`, `aspire-deployment` and `aspire-monitoring` state no `invocation:`, so
  Microsoft's own frontmatter posture passes straight through and a future flip would land in output
  unreviewed — the class of surprise `platform-detection` turned out to be. `npm run sync` now
  reports such a flip, but the dial itself is still unset. Decide each of the six, or record that
  passthrough is the intent.
- **The Module version bump is policy, not a gate.** [`workflow.md`](engineering/workflow.md)
  now states when `plugin.version` moves, but nothing enforces it: a wave can change a Module's
  emitted bytes and leave its version alone, and only the Bundle digest would notice. Decide whether
  `validate` should compare a Module's committed digest against the version that last shipped it.
- **Inventory truncates descriptions at 140 characters** with no ellipsis marker — many rows cut
  mid-sentence, so curation sessions must open the upstream file to judge an item.
- **`dotnet-agent-skills` has no release-shaped pin to move to, and that is settled.** Its whole
  repository carries two tags: a rolling `skill-validator-nightly` that marks a tool rather than a
  release, and a `v1.0.0` hundreds of commits behind. `git describe` reports
  `skill-validator-nightly-N-g<sha>` only because that tag is the nearest reachable one, which is
  what made the pin look nightly. A reviewed `main` commit is the only available boundary; the
  choice each sync is hold-or-move, not release-or-main. The `ms-dotnet-test-frameworks` skill the
  earlier note expected to reappear has never existed on `main` at any point, so nothing is waiting
  on it — its reference data lives inside `test-analysis-extensions`.
- **Dead `invocable:` metadata in the Claude tree.** Upstream `dotnet-skills` sets
  `invocable: true|false`, which is a field in neither target harness. The OpenCode adapter now
  drops and reports it; the Claude tree still carries it, because that tree passes upstream
  frontmatter through and filtering it would need a recognised-key list for Claude Code too. Strip
  it per item during curation, or leave it as harmless noise — a decision, not a bug.
- **`hooks.include` unimplemented.** The build throws on a non-empty list. Deliberate: no upstream
  hook is wanted yet (this is how the superpowers session-start behaviour stays off). Implement only
  when a specific hook is.
- **Re-serialization drift.** Frontmatter is parsed and re-emitted, so a passthrough skill is never
  byte-identical to upstream. Harmless, but it adds noise to `npm run sync` diffs.

## Deferred

Out of scope until there is a concrete need: OpenCode agent permission mapping; Codex, Cursor and
Gemini outputs; automated or scheduled upstream sync (`npm run sync` stays manual).

- **Deferred .NET estates, recorded during the general curation pass** (excluded with reasons in
  `curation/deniz-dotnet-general.yaml`, not rejected forever): the Blazor cluster (9 skills, plus
  `convert-blazor-server-to-webapp`), the MAUI cluster (8 + `dotnet-maui-doctor`), the specialist
  diagnostics bundle (`android-tombstone-symbolication`, `apple-crash-symbolication`,
  `clr-activation-debugging` — pairs naturally with a MAUI module), and
  `nuget-trusted-publishing` (release/security workflow). Revisit if a UI, mobile, or
  release-engineering module becomes real.
- **Docs-structure template skill.** Package this repo's documentation structure (single canonical
  home + relay principle, evergreen/operational split, audience-based placement, ADR and roadmap
  skeletons) as an original skill under `skills/` so any repo can adopt it. The skill form looks
  right, but the idea needs its own brainstorming session before any work starts.
- **`expects` — optional manifest-side guard for bare-name edges.** Design only if load-bearing
  bare-name edges accumulate enough that review-only candidate status becomes unsafe; today's single
  case (`grill-me` → `grilling`) stays deliberately unguarded. Decide the guard's shape only after
  that trigger is met.
