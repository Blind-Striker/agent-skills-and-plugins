# Roadmap

Date: 2026-08-18

Operational document: what is done, what is next. It shrinks as work lands. How things work and why
lives in [AGENTS.md](../AGENTS.md) and [docs/adr/](adr/).

## Current State

- `tools/` provides `build`, `inventory`, `eject`, `sync`, `validate`, and `install:opencode`; CI runs
  the checks, regenerates committed harness output and installer JavaScript, and rejects stale
  `plugins/`, `opencode/`, `dist/`, marketplace, inventory, or ledger output.
- This private GitHub repository (`Blind-Striker/agent-skills-and-plugins`, default branch
  `master`) vendors upstreams in `external/`; `docs/inventory.md` catalogs them.
- Four `deniz-*` Claude Code Plugins, matching per-Module OpenCode Bundles with deterministic
  manifests, the emitted `deniz-skills` installer under `dist/`, and `marketplace.json` are generated
  and committed. `deniz-process` is closed; `deniz-dotnet-general` is curated (corpus-first pass over
  the two general-scope upstreams); akka and aspire each retain one pipeline-proof starter. The real
  Claude profile already has the desired `deniz-process` and `deniz-dotnet-general` Plugins enabled;
  Akka and Aspire stay uninstalled there until their curation sessions close.
- The OpenCode installer composes an explicit Selection into the normal global Native tree with
  exact Ownership, zero-write Plan, under-lock recomputation and transactional Apply/Recovery. It
  supports Install, status, Update, and Remove; it has no force/reset/legacy migration,
  project-local target, runtime package adapter, or JSON-config mutation. The local npm-format
  tarball and the downloaded private GitHub Release asset are both verified: the versioned
  `installer-v0.1.0` Release exists (tag pinned to commit `5ab4117`; GitHub reports Releases as
  non-immutable, so the recorded asset SHA-256 is the replacement detection, not a prevention) and
  its `deniz-agent-skills-0.1.0.tgz` asset measured identical to the local pack in an isolated
  profile. The real OpenCode profile was migrated through the same Plan/Apply path (2026-08-18);
  the 25 model-routing control-plane roots (skill `subagent-model-routing`, three router commands,
  21 routing agent files) were preserved byte-for-byte and remain outside Module Ownership. Its
  current Selection is `deniz-process`, `deniz-dotnet-general`, and the Akka starter; the Aspire
  starter was removed after restart verification and remains unselected until its router is repaired.
- Machine migration is complete for sources that currently have curated replacements. The OpenCode
  Superpowers package entry and `~/.agents/skills/` were both measured absent; no cleanup was needed.
  Official Claude utility Plugins are outside this skill-source migration, and future Akka/Aspire
  Claude installs belong to their curation tasks rather than a standing machine-migration task.
- `docs/ledger.json` records resolved output state. `npm run validate` has no errors on the current
  build; expected warning identities remain under Known Gaps or the future module work below.
- Harness-invocation experiments, their protocol, and committed evidence live in
  `experiments/harness-invocation/`; the [adapter guide](research/harness-adapters.md) describes
  harness-native behavior. The
  [packed-installer record](../experiments/harness-invocation/records/2026-08-18-opencode-module-installer.md)
  proves isolated Plan/Apply, private Release download equivalence, and OpenCode discovery while
  leaving human permission and model-driven reads explicitly unmeasured.

## Next Up

1. **`deniz-dotnet-akka` curation session, with the user.** The five upstream skills are deep but
   repetitive. The local-vs-cluster abstraction repeats across best-practices, hosting, and testing;
   management and Aspire configuration present competing option models; the best-practices
   EventStream companion actually uses its own subscription dictionary; the specialist agent
   overlaps the skill set; and hosting still names the pre-flattening
   `microsoft-extensions/dependency-injection` address. Choose canonical homes deliberately and
   record each take, merge, or exclusion beside the item.
2. **`deniz-dotnet-aspire` curation and router repair, with the user.** The current router names five
   uncurated targets in model-facing frontmatter and labels upstream-URL routes as in-plugin. Taking
   the coherent six-skill closure requires harness-reachable handoffs. Resolve the contradiction
   between `aspire-configuration` rejecting application-level service discovery and
   `aspire-service-defaults` installing it. Also assess version drift, deployment's missing
   `--non-interactive`, integration testing's `Task.Delay`, and Mailpit's non-compiling samples. The
   two standing `dotnet-devcert-trust` references to `aspire-configuration` and
   `aspire-service-defaults` should rewrite automatically if those sources are curated. Do not
   install this module until the router is repaired or removed.
3. **Cross-module Akka/Aspire placement.** `akka-net-aspire-configuration` is a long cookbook naming
   skills from both modules and has no natural home. Decide its owning module and edge direction with
   both manifests open rather than allowing either session to claim it implicitly.
4. **Public release decision.** The repo is private today. Going public needs a deliberate pass:
   repair or pull the Aspire router, and decide whether `marketplace.json`'s format-required owner
   name and email may go public.
5. **Curation sanity panel — advisory subagents, never a gate (requested 2026-07-31).** Alongside
   the deterministic linker and the TUI rounds: a few non-deterministic reviewer subagents that
   read a curated item's upstream original, its overlay/patch and the recorded intent (the
   manifest comment, ADR-0007) side by side and return *judgement*, not findings — do these two
   skills fight inside one module, did a softening pass over-prune the wording, would a small
   touch serve the recorded intent better. Output is opinion for the curator; nothing fails a
   build, nothing is deterministic, nothing repeats twice the same way — that is the point.
   Natural triggers: after a merge pass lands, before a module is declared closed. Deliberately
   after the reference-model wave; needs a short design pass for the panel prompt and the
   presentation shape before anything runs.

6. **An ADR candidate, deliberately unwritten.** Body-invocation and description-matching are two
   mechanisms of different reliability, and curation should treat that as a dial rather than an
   accident — an item that must fire needs something that names it, not merely a good description.
   That sentence survives any re-curation and therefore qualifies, but minting an ADR immediately
   before the rules are rewritten would produce a document written to the wrong contract. The
   evidence lives in [skill-invocation-across-harnesses.md](research/skill-invocation-across-harnesses.md)
   and [skill-framework-landscape.md](research/skill-framework-landscape.md).

## Known Gaps

- **Repo-wide machine-path scan in CI.** `experiments/harness-invocation/selftest.ps1` scans only
  the experiment tree; automate the Hard Rule across the repository with an explicit fixture
  allowlist.
- **Optional writing-style reference remains external.** The curated `brainstorming` body says to
  use `elements-of-style:writing-clearly-and-concisely` when available, but that namespace is not
  curated here. `validate` keeps the reference visible until the style skill is either taken or the
  optional handoff is removed.
- **Some command copies still paste their whole body into the OpenCode chat.** A command is a
  template, and the TUI renders its body as the user's message. Bundled `manual` conversions emit a
  short stub whose project-local and global paths are runtime-probed; the remaining full-paste cases
  are `both` items and bundle-less `manual` items. Correctness is unaffected. Revisit only if those
  remaining command bodies become noisy in practice.
- **Native-tree parked-body permission observation is unmeasured.** The prior config-dir mount asked
  for folder access because parked files were outside the project. Installer composition puts them
  under the normal global config root; only an isolated human TUI observation can establish whether
  that shape prompts, so no permission configuration is inferred yet.
- **Case-sensitive reference scan.** The unrewritten-reference pattern is lowercase-only by design
  (avoids prose false positives), so a `Superpowers:Foo` spelling slips through.
- **Bare names stay invisible, by decision rather than by omission.** Upstream names are ordinary
  words, so a bare-name scan measurably over-reports; ADR-0008 keeps that tier as candidates,
  surfaced by `sync` for human reading and never build state. The three spellings and what each
  earns are tabulated in
   [upstream-repo-layouts.md](research/upstream-repo-layouts.md#superpowers). Now measured rather
   than assumed: the tier is load-bearing. `grill-me` → `grilling` is the one composition watched
   firing at runtime and it lives entirely there — no declaration, no guard, silent if the target is
   ever renamed or excluded. The [invocation research](research/skill-invocation-across-harnesses.md#the-composition-pattern)
   inventories the broader spelling mismatch and its re-derivation command. Whether to promote any
   candidate into the convention remains an item-level curation decision.
- **A converted command cannot resolve a sibling-item path — on the filesystem, and only there.**
  A command is one file in `commands/`, so a `../<item>/` path written for a skill directory does
  not land, and no single spelling serves both copies of a `both` item. `validate` names each case.
  The implementation narrows the warning when the same link resolves from the skill copy. The
  correctness case it still guards is a target with no `skills/<name>/` directory at all (excluded,
   or a `manual` item whose empty bundle made the emitter drop the husk), where the same climb lands
   nowhere. This filesystem gap remains distinct from the installer and mount decisions above.
- **Linker's unreachable-cause string assumes a skill target.** A model-edge pointing at a
  command or agent reports "(disable-model-invocation in the Claude tree)" — right verdict,
  wrong cause text. A `kind` on the target state fixes the parenthetical.
- **L6 interpolates the output name into a RegExp unescaped.** Safe for kebab-case names; one
  `escapeRegExp` call removes the latent throw.
- **Own skills are edge targets but never edge sources.** The derived-edge scan walks manifest
  items only, so the day an own skill under `skills/` carries a namespaced reference, nothing
  scans or declares it. No own skill exists today.
- **The clean-fixture test filters two named findings (`FIXTURE_DEBT`)** instead of asserting
  their presence, so it also passes if the linker stops reporting them. A fixture split retires
  the tolerance.
- **`--bless` hints disagree on ceremony.** The primary-drift message prints the two-step form
  (see the diff, re-run with `--yes`); the merge-drift messages hand over `--bless --yes`
  one-step. The drift is printed either way, so nothing is hidden — but one ceremony should win.
- **A directory appearing at a recorded-absent merge filename** reports "(appeared upstream)"
  though it could never be stamped, and the non-null sibling branch would `blobSha` a directory
  into a raw git error instead of a finding.
- **Scanner blind spots.** Commands and agents are discovered only directly under a
  `commands/`/`agents/` directory, so upstream subdirectory grouping is silently missing from the
  inventory; conversely such a directory nested inside a skill is double-counted as a standalone
  component.
- **File modes are invisible to both overlay guards.** `git hash-object` hashes content, and
  patches are cut with `core.fileMode=false`, so upstream flipping a bundled script's executable
  bit produces no signal. `validate` now reads `git ls-files` to compare built output against
  upstream, but the overlay *lock* still records content only, so a mode-only upstream change does
  not force a re-bless.
- **A patch cannot touch anything at or beyond a symlink.** `git apply` refuses such paths outright;
  `aspire-skills` carries symlinks inside curated skills. Undocumented in ADR-0001.
- **`sync` still mislabels a deleted or renamed source** as "auto-updated on next build", while the
  next build fails with `source not found in external/` — the same class as the patch mislabel
  already fixed.
- **Manifest overrides have no staleness guard.** Overlays are guarded — a patch stops applying, a
  full-file overlay's recorded hash stops matching — but a `frontmatter.description` override
  written for one upstream body keeps being applied after upstream rewrites that body, with nothing
  to notice. Same class as overlay drift, rarer, and unsolved.
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
