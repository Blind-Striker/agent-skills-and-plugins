# Roadmap

Date: 2026-08-25

Operational document: current orientation, next work, open decisions, known gaps, and deferred work.
It shrinks as work lands and is not a chronology. Current mechanics live in
[architecture](architecture/), working practice in [engineering](engineering/), rationale in
[ADRs](adr/), and dated proof in [research](research/) and
[experiment records](../experiments/harness-invocation/records/README.md).

## Current State

- The four current curation manifests emit matching Claude Code Plugins and OpenCode Module Bundles.
  Their item posture, source pins, transformations, exclusions, and reasons live in
  [`curation/*.yaml`](../curation/) and the generated [ledger](ledger.json), not in this roadmap.
- The repository is public with MIT licensing for original work, source-specific notices and exact
  upstream license copies, a public noreply marketplace contact, least-privilege secret and
  machine-path CI checks, private vulnerability reporting, and an explicit personal/no-SLA boundary.
- OpenCode supports two installation transports: a recursive clone using the repository installer,
  and the compiled npm-format Package attached to GitHub Release `installer-v0.2.0`. The corrected
  Linux-built asset passed the manual release workflow's full source gate, tar-mode verifier, isolated
  Plan/Apply/status, publication, and remote re-download checks. Its exact identity and proof boundary
  are in the [correction record](../experiments/harness-invocation/records/2026-08-25-opencode-installer-v0.2.0-posix-correction.md).
  The older 0.1.0 Release remains historical rather than current.
- Aspire CLI, TypeScript, testing, and package examples remain intentionally upstream-owned. Build,
  generation, and linking do not prove every example in a consumer environment; this is an accepted
  public limitation, not a claim to repair by silently forking the bodies.

## Next Up

1. **Add dependency-aware Module Selection planning before advertising arbitrary subsets as
   dependency-safe.** The complete estate links, but the installer does not close a selected subset
   over cross-Module `depends_on`. Until then, selecting all Modules is complete; partial Selections
   must include General, Akka, and Aspire together when those guarded edges are needed.
2. **Add an original-skill declaration only when the current limit becomes material.** The trigger is
   another original skill, a manual original posture, or enough load-bearing outgoing edges that
   review-only protection is no longer sufficient. Original skills can be guarded targets, but their
   invocation and outgoing model edges have no manifest declaration. A future surface can be keyed by
   the existing top-level skill directory:

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
4. **Write the invocation ADR only after more runtime evidence.** Namespaced body facts prove
   deterministic existence and audience reachability once a source runs, but do not make it run.
   Descriptions provide probabilistic selection pressure and can reach work with no explicit caller.
   The current candidate rule is: load-bearing composition uses guarded body facts; opportunistic
   passive knowledge can use honest descriptions; ceremonies need a human surface. Record it only
   when the original-skill declaration and another bounded runtime sample confirm that this is the
   durable dial. Evidence remains in
   [skill-invocation-across-harnesses.md](research/skill-invocation-across-harnesses.md) and
   [skill-framework-landscape.md](research/skill-framework-landscape.md).

## Known Gaps

- **Selection dependency closure:** Module manifests and Install state carry file identity, not a
  dependency graph. See
  [distribution and installation](architecture/distribution-and-installation.md#full-estate-versus-installed-selection).
- **Same-name, different-kind reference maps are name-only:** a cross-kind duplicate can overwrite
  target state or lose kind semantics. See
  [references and linking](architecture/references-and-linking.md#proof-boundary-and-current-limits).
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

Out of scope until there is a concrete need: OpenCode agent permission mapping; Codex, Cursor and
Gemini outputs; automated or scheduled upstream sync; the Blazor and MAUI skill estates; specialist
mobile diagnostics; NuGet trusted-publishing guidance; and packaging this repository's documentation
structure as an original skill.
