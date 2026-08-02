# Roadmap

Date: 2026-08-02

Operational document: what is done, what is next. It shrinks as work lands. How things work and why
lives in [AGENTS.md](../AGENTS.md) and [docs/adr/](adr/).

## Current State

- `tools/` provides `build`, `inventory`, `eject`, `sync`, and `validate`; CI runs the checks,
  regenerates committed output, and rejects stale output.
- This private GitHub repository (`Blind-Striker/agent-skills-and-plugins`, default branch
  `master`) vendors upstreams in `external/`; `docs/inventory.md` catalogs them.
- Four `deniz-*` plugins, matching `opencode/` output, and `marketplace.json` are generated and
  committed. `deniz-process` is closed; the three dotnet modules each retain one pipeline-proof
  starter for future curation.
- `docs/ledger.json` records resolved output state. `npm run validate` has no errors on the current
  build; converted-command-shape warnings remain under Known Gaps.
- Harness-invocation experiments, their protocol, and committed evidence live in
  `experiments/harness-invocation/`; the [adapter guide](research/harness-adapters.md) describes
  harness-native behavior.

## Next Up

1. **Per-module curation sessions.** `deniz-process` is closed. The three dotnet modules hold one
   pipeline-proof starter each; fill them against `docs/inventory.md`, with the user, one plugin
   per session. `docs/research/skill-framework-landscape.md` is the standing input; the why of
   each decision goes beside the item. These modules differ from `deniz-process` in a way worth
   planning for: their upstreams are single-vendor and overlapping by *subject* rather than by
   job (both `aspire-skills` and `dotnet-skills` cover Aspire), so the merge question that shaped
   `deniz-process` is likely replaced by a naming and scope question.
2. **Aspire router repair.** `deniz-dotnet-aspire` ships the upstream `aspire` skill, a router whose
   five targets are not curated. The misdirection sits in two different places, and they are worth
   separating: the frontmatter `description` ends `INVOKES: aspire-init, aspireify,
   aspire-orchestration, aspire-deployment, aspire-monitoring`, which is the part injected into the
   system prompt and therefore the part that actually misleads a model; the body's routing table
   points at upstream GitHub URLs, which resolve for a human reader, but labels three rows
   "(in-plugin)" when they are not. So the options are wider than eject-and-rewrite: curate the
   targets, or override `frontmatter.description` alone, or own the body. Bare-name references are
   invisible to `validate`.
3. **Public release decision.** The repo is private today. Going public needs a deliberate pass:
   fix or pull the aspire router, and decide whether `marketplace.json`'s embedded owner name and
   email (format-required) may go public. Until then, do not install `deniz-dotnet-aspire`.
4. **Machine migration to single source of truth.** The end state (user, 2026-07-31): every
   globally installed skill set on this machine — Claude-side plugins, OpenCode's superpowers
   package, `~/.config/opencode/skills/`, `~/.agents/skills/` — is uninstalled as its `deniz-*`
   replacement lands, leaving this repo as both harnesses' only skill source. Staged, not
   big-bang. First in line: the OpenCode superpowers package (`plugin:` entry in
   `~/.config/opencode/opencode.json`), because measured precedence (package cache >
   `OPENCODE_CONFIG_DIR` mount > global `.config` skills) means it shadows curated output for
   every colliding name — the one global our tree cannot shadow away.
5. **OpenCode installer — parked (2026-07-31), deliberately not a side quest yet.** The user's
   model is an installer, symmetric to the Claude marketplace; a permanent `OPENCODE_CONFIG_DIR`
   is rejected (env vars are for throwaway tests, consumption goes through a native mechanism).
   Candidates, in current order: (a) OpenCode's own package mechanism (`opencode.json` `plugin:`
   list — measured today: package cache tops discovery precedence; feasibility of a git-subdir
   ref for `opencode/` unmeasured), (b) a switchboard-style installer — prior art in the user's
   `opencode-switchboard` repo (`tools/install.py`): hash-compared sync, manifest-owned prune,
   and the known hard part, surgical JSONC merge into the user's existing config behind
   validation gates with backup restore ("config concat" concern), (c) a sync command in
   `tools/` (a deliberate ADR-0004 growth decision). wshobson/agents is the third reference:
   generate harness-native trees, commit only registries. The behavioural half is settled: the
   real tree was mounted three ways and exercised in a TUI on 2026-07-31 — discovery, commands,
   model-mediated composition, parked-bundle reachability and all three invocation surfaces
   exercised on real output (`docs/research/skill-invocation-across-harnesses.md`). Only the
   install mechanism remains open.
6. **Curation sanity panel — advisory subagents, never a gate (requested 2026-07-31).** Alongside
   the deterministic linker and the TUI rounds: a few non-deterministic reviewer subagents that
   read a curated item's upstream original, its overlay/patch and the recorded intent (the
   manifest comment, ADR-0007) side by side and return *judgement*, not findings — do these two
   skills fight inside one module, did a softening pass over-prune the wording, would a small
   touch serve the recorded intent better. Output is opinion for the curator; nothing fails a
   build, nothing is deterministic, nothing repeats twice the same way — that is the point.
   Natural triggers: after a merge pass lands, before a module is declared closed. Deliberately
   after the reference-model wave; needs a short design pass for the panel prompt and the
   presentation shape before anything runs.

8. **An ADR candidate, deliberately unwritten.** Body-invocation and description-matching are two
   mechanisms of different reliability, and curation should treat that as a dial rather than an
   accident — an item that must fire needs something that names it, not merely a good description.
   That sentence survives any re-curation and therefore qualifies, but minting an ADR immediately
   before the rules are rewritten would produce a document written to the wrong contract. The
   evidence lives in [skill-invocation-across-harnesses.md](research/skill-invocation-across-harnesses.md)
   and [skill-framework-landscape.md](research/skill-framework-landscape.md).

9. **Queued probe: does stating the intent fire the discipline?** The propensity number was
   measured against requests that named no intent at all ("implement X"). The curator's own usage
   states intent without naming skills ("let's go test-driven"), which is a fairer test of what an
   honest description buys, and cheap to run on the existing lab.

## Known Gaps

- **Ledger omits full Claude invocation flags.** `LedgerEntry` in `tools/lib/ledger.ts` records the
  declared `invocation` and artifact kinds but not resolved Claude frontmatter such as
  `user-invocable` and `disable-model-invocation`. A ledger review cannot inspect the complete
  Claude posture without opening generated output.
- **Repo-wide machine-path scan in CI.** `experiments/harness-invocation/selftest.ps1` scans only
  the experiment tree; automate the Hard Rule across the repository with an explicit fixture
  allowlist.
- **Long-body `manual` conversions paste their whole body into the OpenCode chat.** A command is
  a template, and the TUI renders the entire body as the user's message: seven-line grill-me is
  clean, 150-line brainstorming is a wall on every invocation. Cosmetic, not correctness — the
  model follows the pasted body either way. Candidate emitter change: a stub command body that
  points at the body parked beside the bundle, which needs the parked directory to carry the body
  under a non-discoverable name. A decision, with the installer conversation the natural place.
- **Out-of-project bundle reads prompt for folder access** under a config-dir mount — parked
  files live outside the project tree, so the first read asks permission. Whether to
  pre-authorize is an installer-decision detail (config `permission` block).
- **Case-sensitive reference scan.** The unrewritten-reference pattern is lowercase-only by design
  (avoids prose false positives), so a `Superpowers:Foo` spelling slips through.
- **Bare names stay invisible, by decision rather than by omission.** Upstream names are ordinary
  words, so a bare-name scan measurably over-reports; ADR-0008 keeps that tier as candidates,
  surfaced by `sync` for human reading and never build state. The three spellings and what each
  earns are tabulated in
  [upstream-repo-layouts.md](research/upstream-repo-layouts.md#superpowers). Now measured rather
  than assumed: the tier is load-bearing. `grill-me` → `grilling` is the one composition watched
  firing at runtime and it lives entirely there — no declaration, no guard, silent if the target is
  ever renamed or excluded. Across the module, twenty-seven bodies name a curated item as a bare
  `/name` and eleven of those targets are `auto`, so the slash claims a user surface they do not
  have. Whether to promote any of them into the convention is an open curation decision; promoting
  the eleven as spelled would be linker errors, which is the point.
- **A converted command cannot resolve a sibling-item path — on the filesystem, and only there.**
  A command is one file in `commands/`, so a `../<item>/` path written for a skill directory does
  not land, and no single spelling serves both copies of a `both` item. `validate` names each case.
  The implementation narrows the warning when the same link resolves from the skill copy. The
  correctness case it still guards is a target with no `skills/<name>/` directory at all (excluded,
  or a `manual` item whose empty bundle made the emitter drop the husk), where the same climb lands
  nowhere. The mount-point decision below is therefore less urgent than it looked.
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
  unlike the other four submodules, which sit on releases. Hold or move is an open decision for the
  next `npm run sync`.
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

- **Docs-structure template skill.** Package this repo's documentation structure (single canonical
  home + relay principle, evergreen/operational split, audience-based placement, ADR and roadmap
  skeletons) as an original skill under `skills/` so any repo can adopt it. The skill form looks
  right, but the idea needs its own brainstorming session before any work starts.
