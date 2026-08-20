# Roadmap

Date: 2026-08-20

Operational document: current orientation, next work, open decisions, known gaps, and deferred work.
It shrinks as work lands and is not a chronology. Current mechanics live in
[architecture](architecture/), working practice in [engineering](engineering/), rationale in
[ADRs](adr/), and dated proof in [research](research/) and
[experiment records](../experiments/harness-invocation/records/README.md).

## Current State

- This GitHub repository (`Blind-Striker/agent-skills-and-plugins`) currently uses `master` as its
  default branch. The current curation manifests emit matching Claude Code Plugins and OpenCode
  Module Bundles. `deniz-process` is closed, `deniz-dotnet-general` has completed its corpus-first
  pass, and `deniz-dotnet-akka` has completed its user-guided pass with all five upstream Akka skills
  plus the native specialist agent. The Akka-owned Aspire cookbook has guarded links to its Akka,
  General, and Aspire targets; `deniz-dotnet-aspire` now carries the integration-testing target but
  remains an incomplete router proof awaiting the session below. Item posture and reasons live in the
  [manifests](../curation/) and generated [ledger](ledger.json). The real Claude profile has process
  and general enabled; Akka and Aspire remain uninstalled there. Official Claude utility Plugins are
  outside the [skill-source migration documented by the dated adapter and installer
  evidence](research/harness-adapters.md#opencode);
  future Akka and Aspire Claude installs require explicit approval rather than a standing migration
  task.
- The repository remains private. The emitted global-only OpenCode installer and private Package
  recipe are available in the root [README](../README.md); current mechanics are in
  [distribution and installation](architecture/distribution-and-installation.md). The measured real
  OpenCode Selection contains process, general, and the pre-curation Akka starter; updating Akka and
  selecting Aspire require explicit approval, and Aspire remains unsuitable for installation until
  its router is repaired. The migration and isolated Package evidence remain in the
  [installer record](../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md).

## Next Up

1. **`deniz-dotnet-aspire` curation and router repair, with the user.** The current router names five
   uncurated targets in model-facing frontmatter and labels upstream-URL routes as in-plugin. Taking
   the coherent six-skill closure requires harness-reachable handoffs. `aspire-integration-testing`
   is already present as the guarded cross-Module target of the Akka-owned Aspire cookbook; resolve
   the remaining router closure and the contradiction between `aspire-configuration` rejecting
   application-level service discovery and `aspire-service-defaults` installing it. Also assess
   version drift, deployment's missing `--non-interactive`, integration testing's `Task.Delay`, and
   Mailpit's non-compiling samples. The two standing `dotnet-devcert-trust` references to
   `aspire-configuration` and `aspire-service-defaults` should rewrite automatically if those sources
   are curated. Do not install this module until the router is repaired or removed.
2. **Public release decision.** The repo is private today. Going public needs a deliberate pass:
   repair or pull the Aspire router, and decide whether `marketplace.json`'s format-required owner
   name and email may go public.
3. **Curation sanity panel — advisory subagents, never a gate (requested 2026-07-31).** Alongside
   the deterministic linker and the TUI rounds: a few non-deterministic reviewer subagents that
   read a curated item's upstream original, its overlay/patch and the recorded intent (the
   manifest comment, ADR-0007) side by side and return *judgement*, not findings — do these two
   skills fight inside one module, did a softening pass over-prune the wording, would a small
   touch serve the recorded intent better. Output is opinion for the curator; nothing fails a
   build, nothing is deterministic, nothing repeats twice the same way — that is the point.
   Natural triggers: after a merge pass lands, before a module is declared closed. Deliberately
   after the reference-model wave; needs a short design pass for the panel prompt and the
   presentation shape before anything runs.

4. **An ADR candidate, deliberately unwritten.** Body-invocation and description-matching are two
   mechanisms of different reliability, and curation should treat that as a dial rather than an
   accident — an item that must fire needs something that names it, not merely a good description.
   Keep it unwritten until the curation rule is stable enough to state the actual contract rather
   than promoting the research synthesis prematurely. The evidence lives in
   [skill-invocation-across-harnesses.md](research/skill-invocation-across-harnesses.md) and
   [skill-framework-landscape.md](research/skill-framework-landscape.md).

## Known Gaps

- **Add dependency-aware Module Selection planning.** The Akka-owned
  `akka-net-aspire-configuration` has guarded references from `deniz-dotnet-akka` into
  `deniz-dotnet-general` and `deniz-dotnet-aspire`. Full-estate validation proves those links, but
  the installer does not yet close an arbitrary Selection over required Modules. Extend Package or
  Bundle metadata and Plan validation to add required Modules automatically or reject an incomplete
  Selection before treating a partial install as dependency-safe. Until then, the operator must
  select the cross-Module targets explicitly. See the
  [architecture limit](architecture/distribution-and-installation.md#full-estate-versus-installed-selection).
- **Same-name, different-kind semantic maps are name-only.** Qualify rewrite and linker maps by kind,
  or narrow the legality rule, before relying on a cross-kind duplicate. See the
  [architecture limit](architecture/references-and-linking.md#proof-boundary-and-current-limits).
- **`sync` candidate names come from the ledger kind segment.** Fix `tools/sync.ts` to read the name
  segment or parse ledger identities explicitly; the current CLI candidate universe is incomplete.
  See the [architecture limit](architecture/references-and-linking.md#proof-boundary-and-current-limits).
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
- **`sync` still mislabels a deleted or renamed source** as "auto-updated on next build", while the
  next build fails with `source not found in external/` — the same class as the patch mislabel
  already fixed.
- **Manifest override staleness guard.** Add an upstream-staleness guard for `frontmatter:` overrides.
  See the [transformation limit](architecture/transformation-and-emission.md#current-limits).
- **Inventory truncates descriptions at 140 characters** with no ellipsis marker — many rows cut
  mid-sentence, so curation sessions must open the upstream file to judge an item.
- **`dotnet-agent-skills` is pinned at a nightly-adjacent tag** (`skill-validator-nightly-*`),
  unlike the other four submodules, which sit on releases. Measured consequence during the general
  curation pass: the globally installed `ms-dotnet-test-frameworks` skill does not exist at the
  pin — its reference data lives inside `test-analysis-extensions` there. Hold or move is an open
  decision for the next `npm run sync`.
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
- **Own TUnit test-writing skill.** The corpus's only test-writing skill was MSTest-bound and is
  excluded; the general module deliberately ships no test-writing knowledge, and its audit bodies
   no longer name a test-writing destination. A TUnit-first original skill under `skills/` needs a
   separate authoring session, and that session decides its scope.
- **`expects` — optional manifest-side guard for bare-name edges.** Design only if load-bearing
  bare-name edges accumulate enough that review-only candidate status becomes unsafe; today's single
  case (`grill-me` → `grilling`) stays deliberately unguarded. Decide the guard's shape only after
  that trigger is met.
