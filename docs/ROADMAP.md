# Roadmap

Date: 2026-09-07

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
  That public Package remains a schema-1 historical source snapshot. Its exact identity and proof
  boundary are in the
  [release record](../experiments/harness-invocation/records/2026-09-06-opencode-installer-v0.3.0.md).
  The older Releases remain historical and their assets were not replaced.
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

## Next Up

1. **Close the remaining Codex surface experiments.** Deterministic Codex emission, native
   manifests and marketplace, invocation policy, `$plugin:skill` localization, ledger projection,
   validation, isolated CLI installation, public Git marketplace add/install/upgrade, and the
   bounded credentialed model panel are implemented.
   The follow-up estate review resolved actual skill pointers through the shared authored pipeline;
   a Codex-only body-patch seam is not justified. Optionally measure ChatGPT desktop discovery and
   whether its custom-marketplace installation state is shared with CLI. Do not claim IDE Plugin or
   native custom-agent distribution.
   The approved temporary design is
   [Codex-native output design](superpowers/specs/2026-09-06-codex-native-output-design.md), grounded
   in [Codex native plugin and skill surfaces](research/codex-native-plugin-and-skill-surfaces.md)
   and the [generated-estate audit](research/codex-generated-estate-audit.md).
2. **Iteration 2: dependency automation and original-skill declarations.** After the Codex milestone,
   feature, address automatic dependency installation, cascade remove, version-range resolution, and
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
   ledger cases in both harness trees.
3. **Prototype the curation sanity panel only when another curation wave needs it.** Deterministic
   validation proves identity, shape, linkage, ownership, and bytes; it cannot judge trigger
   competition, over-pruned overlays, or whether a transformed body still serves nearby manifest
   intent. Keep the panel a read-only `docs/agents/` playbook, never a gate. Each run receives one
   bounded packet: pinned upstream body and bundled dependency closure, manifest reason, overlay or
   patch plus lock evidence, and both emitted harness forms. Review trigger/overlap, body-intent
   preservation, and harness fit separately. Require `file:line` evidence, a concrete consequence,
   confidence, and one of `retain`, `narrow`, `reconsider`, or `ambiguous`; preserve disagreements.
   Reviewers never edit, bless, bump versions, or fail CI. Success is a small curator decision packet,
   not a repeat of deterministic validator findings or a vote that turns model agreement into policy.
   Run it after a body-ownership pass and before declaring a Module closed.
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
  standalone-skill transport. ChatGPT desktop state sharing and public OpenAI universal-directory
  submission remain unmeasured distribution work, not requirements for CLI marketplace installation.
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
  skill-relative path that no longer resolves there.
- **Linker cause text is skill-specific:** an unreachable command or agent target can receive the
  right verdict with the wrong `disable-model-invocation` explanation.
- **Parked-path regex interpolation is unescaped:** current kebab-case output names are safe, but
  `tools/validate.ts` should escape a name before constructing the expression.
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

Out of scope until there is a concrete need: OpenCode agent permission mapping; Cursor and Gemini
outputs; automated or scheduled upstream sync; the Blazor and MAUI skill estates; specialist
mobile diagnostics; NuGet trusted-publishing guidance; and packaging this repository's documentation
structure as an original skill.
