# S13 - Review the wave plans independently, then execute them

Date: 2026-08-05

> Temporary pickup for unfinished cross-session work. Establish live state before trusting it,
> execute it against the current repository, and delete it when this follow-up ships. This is not a
> second roadmap or a policy home; durable rules and evidence are linked rather than repeated.
> Supersedes `s12-opencode-per-module-output-next.prompt.md` (its review closed in s13; its open
> items are carried in the Reference backlog below).

## Mission

Two hats, in order:

1. **Reviewer.** The first independent xAI and GLM/OpenCode-Go reviews found blocking defects in
   the spec, plans, and playbook. The correction design was approved and the documents were
   rewritten and then amended by the curator's email/snapshot and version-migration cuts. Re-review
   the final corrected set against the live repository with two independent Grok sessions before
   anything executes. Findings go to the user; nothing executes until the user says so.
2. **Builder.** Execute Wave A, then Wave B, exactly per the (possibly review-corrected) plans.
   Execution mode — subagent-driven vs inline — is the user's choice at pickup, not yours.

## Establish live state

```powershell
git status --short          # see the note below before judging cleanliness
git log --oneline -8
git submodule status        # if any pin moved since the corrected plans, every anchor needs re-verification
npm run build; npm run validate   # before Wave A: expect 0 errors; record warning identities
npm test; npm run typecheck
```

**Expected working-tree state:** the corrected planning documents and persisted review reports may
arrive uncommitted. The intended paths are the canonical spec, correction design, two plans,
`docs/superpowers/reviews/`, `docs/agents/reference-audit-playbook.md`, this handover, and related
operational relays only. No curation manifest, overlay, tool, generated output, or submodule change
belongs to the correction pass. Either committed or dirty documents are acceptable; if dirty,
committing them is the first act **after** the re-review passes and the user approves it.

## Onboarding reads (in order)

1. `AGENTS.md` — the contract: transformation not selection, compile-time vs runtime, hard rules.
2. `curation/SCHEMA.md` — the authoring grammar and the mechanism ladder.
3. Relevant ADRs: `0001` (overlay/patch mechanics — every Wave A patch task uses the eject/bless
   ceremony), `0005` (invocation grammar — retained mutating ceremonies become manual),
   `0006`/`0007` (the three axes; curation serves the curator, upstream is never the yardstick),
   `0008` (reference tiers, promotion-on-contact, `depends_on` both-directions — Wave A's edge
   promotions live under it).
4. `docs/ROADMAP.md` — operational state; the Deferred section gains two items in Wave A Task 13.
5. **The review targets:** `docs/superpowers/specs/2026-08-04-t-items.md` (the rulings —
   canonical record of what the curator decided; do not re-litigate the rulings, review their
   implementation), then `docs/superpowers/plans/2026-08-04-wave-a-general-curation.md`,
   `docs/superpowers/plans/2026-08-04-wave-b-opencode-stub-commands.md`,
   `docs/agents/reference-audit-playbook.md`.
6. `docs/superpowers/specs/2026-08-05-s13-plan-correction-design.md` and the reports under
   `docs/superpowers/reviews/` — the approved correction contract and the defects it must close.
7. `docs/research/skill-invocation-across-harnesses.md` — the measured behavior Wave B's stub
   design leans on (SKILL.md-less directories invisible to discovery; prose paths reachable;
   command bodies pasted as the message; `@` resolves project-root-only).
8. `curation/deniz-dotnet-general.yaml` (the work target) and `curation/deniz-process.yaml` (the
   posture contrast the header rewrite references).
9. `tools/build.ts` (`emitOpenCodeSkill`), `tools/eject.ts` (the patch two-phase ceremony),
   `tools/build.test.ts` (fixture vocabulary: `sp/skills/beta` bundles `references/notes.md`).

## What s13 decided (context the documents assume)

The curator's dispositions, now baked into the corrected spec/plans:

- **F1** `testcontainers-integration-tests` patch incomplete against its own recorded intent
  (`TestcontainersNetworkBuilder` survives at `infrastructure-patterns.md`, stale
  `using Testcontainers;`, non-compiling `MigrationTests` readonly assignment) → Wave A Task 12.
- **F2** shipped output still names excluded items; `coverage-analysis` and
  `test-anti-patterns` also route jobs away that the curator chose to absorb. Wave A repairs every
  true emitted-output hit. Email-specific MJML leaves the set; retained `snapshot-testing` is
  patched to keep general Verify/HTML guidance while removing email-only branches and a dead handoff.
- **F3** manifest header's "none carries disable-model-invocation" is false for the reference
  husks → Wave A Task 2.
- **F4** bare-name edges are unguarded by design; curator ruling: **no `expects` machinery now** —
  ROADMAP note with an accumulation trigger; `grill-me` → `grilling` stays deliberately unguarded.
- **F5** header rationale reworded (upstream is not the yardstick); curator ruling: retained
  mutating ceremonies flip `both` → `manual`; `slopwatch`, `analyzing-dotnet-performance`, and
  `dotnet-devcert-trust` stay `both`.
- Curator cuts: `grade-tests`, `exp-mock-usage-analysis`, `exp-test-maintainability`,
  `find-untested-sources`, `migrate-vstest-to-mtp`, `migrate-xunit-to-xunit-v3`,
  `mjml-email-templates`, `migrate-dotnet8-to-dotnet9`, and `migrate-dotnet9-to-dotnet10`.
  Version-specific bundled references leave with their parent; independent general-purpose skills
  stay. The testability trio and multi-framework MSTest lookup rows stay. TUnit test writing is a
  deferred own-skill authoring session.
- Minimal honest absorption: `test-anti-patterns` gains qualitative assertion-depth checks, not an
  assertion-metrics dashboard or academic smell catalog. `coverage-analysis` gains a bounded exact
  method/class/file CRAP branch over existing Cobertura extraction, not new scripts or a second
  project-wide workflow.
- Wave A adds patches for the item identities named in its close-out gate. Edge-bearing sources are
  `run-tests`, `mtp-hot-reload`, `test-anti-patterns`, `test-gap-analysis`, and `dotnet-webapi`;
  `snapshot-testing` adds a patch but no edge.
- Also verified sound in the review (no action): the MSBuild MCP patch set, the
  `resolve-project-references` merge, validate suppressions, husk `auto` posture, and manifest
  comment intent.

## Step 1 — corrected-plan re-review

Use two independent Grok sessions. The spec is canonical for *what* was decided; the plans are the *how*.
Require both reviews to check these corrected failure surfaces:

1. Every exact-string instruction resolves literally at the current submodule pin; folded
   descriptions are whole-block replacements where physical wrapping prevents a contiguous anchor.
2. Wave A covers all true excluded-artifact references found by the playbook's mechanically derived
   sets, not a copied grep alternation, and records false positives separately.
3. The two absorbed jobs stay minimal and operational: qualitative assertion-depth checks in
   `test-anti-patterns`; exact method/class/file CRAP over all extracted coverage rows in
   `coverage-analysis`, with an explicit no-row outcome.
4. The Testcontainers workflow saves and reapplies the old patch, removes the old `overlay.patch`
   before recut, prevents self-inclusion, imports `Testcontainers.PostgreSql`, and performs a real
   API compile smoke.
5. Wave A close-out compares the ledger by the named cut, invocation, patch, description, and edge
   item sets; it derives current totals rather than pinning them in prose and rejects unrelated changes.
6. Wave B tests bundled manual, bundle-less manual, and `both`; rewrites bare, `./`, `../`, and
   repeated-parent self-links; keeps `BODY.md` out of the bundle-list parenthetical; and updates the
   emitter JSDoc.
7. The Wave B stub names project-local and global paths without promising `OPENCODE_CONFIG_DIR`,
   and the isolated runtime smoke observes the model reading `BODY.md` under both supported mounts
   before ROADMAP/research claims change.
8. The playbook resolves output names in build order, adds own-skill names, makes sets disjoint,
   escapes every regex name, uses `candidateHits`-equivalent boundaries, and treats grep output as
   candidates requiring context classification.
9. Wave A introduces no warning identity; Wave B retires the two named dead-self-link identities.
   Recompute the live warning set rather than trusting a fixed total.

Report findings to the user, ranked; stop. Execution starts on the user's word, in the user's
chosen mode.

## Step 2 — Wave A, then Wave B

The plans carry every task, string, command, and gate. Cross-session rules that bind execution:

- A patch and its manifest `depends_on` land in the same commit (one-sided edges are validate
  errors).
- Curation layer carries no curator names, no dates (validate errors on both).
- Output trees are committed with the change that caused them; CI rejects stale output.
- Ledger diff review at Wave A close-out is a gate, not a formality: exactly the expected delta,
  anything else is a finding.

## Step 3 — close-out

1. Run the reference-audit playbook (`docs/agents/reference-audit-playbook.md`) over
   `deniz-dotnet-general` — Scan 1 must come back clean; report the Scan 3 inventory.
2. Delete the planning scratch (both specs, both plans, and the temporary review reports under
   `docs/superpowers/`) — it is transient by contract. The playbook and runtime evidence stay.
3. Update `docs/ROADMAP.md`: the wave-execution item added by s13 comes out; Known Gaps changes
   land in Wave B Task 5.
4. Delete this handover, re-handing whatever remains (see backlog below) if P1–P3 are still open.

## Reference backlog — NOT this pickup's work (carried from s12; do not start without the user)

### P1 — Split the OpenCode output per module

`opencode/` is one flat tree (`skills/`, `commands/`, `agents/` across all four modules); module
identity exists only in `plugins/` and the ledger. Target: per-module emission symmetric to the
Claude plugins, so a future installer can install per category. **The design tension to resolve
deliberately:** OpenCode discovery expects `skills/`, `commands/`, `agents/` at one config root
(measured), so a per-module layout is not itself mountable as one config dir — the split trades
the zero-install mount for installability, and consumption becomes installer-bound (ROADMAP).
Open questions: exact layout; whether a flat aggregate view is also emitted (s13's recorded
opinion, undecided: a transient on-demand aggregate for the experiment lab, never committed);
installer copy/symlink semantics (prior art: `opencode-switchboard`'s hash-compared sync). Names
stay flat and globally unique — ADR-0002 rejected prefixing and this work amends its
output-layout text in place, not the namespace decision. Touchpoints: the OpenCode emitter in
`tools/build.ts`; `validate`'s generated-tree scans (duplicate-name, symlink walk, parked-bundle,
L4 linker); `tools/lib/ledger.ts` if paths move; `experiments/harness-invocation/`
runbook/matrix/selftest mount assumptions; mount documentation in
`docs/research/harness-adapters.md` and `skill-invocation-across-harnesses.md`. Gates: `npm test`,
`npm run typecheck`, `npm run build`, `npm run validate`, ledger/tree delta review; CI freshness
enforces the new layout.

### P2 — `deniz-dotnet-akka` curation session (with the user)

Boundary evidence from the corpus pass (all `dotnet-skills`; upstream author is the Akka.NET
founder — deep but internally repetitive):

- The local-vs-cluster abstraction (`AkkaExecutionMode`, `GenericChildPerEntityParent`) repeats
  across `akka-best-practices`, `akka-hosting-actor-patterns`, `akka-testing-patterns` — pick one
  canonical home or accept the repetition knowingly.
- `akka-management` vs `akka-aspire-configuration` ship incompatible-looking options models for
  the same bootstrap/discovery settings — choose one canonical model before taking both.
- `akka-net-best-practices`' local pub/sub companion claims EventStream use; the shown
  implementation uses its own subscription dictionary.
- `akka-net-specialist` agent: persona overlapping all five skills; general-module precedent —
  orchestrator/persona agents excluded, only a genuine specialist kept.
- Stale cross-reference: `akka-hosting-actor-patterns` points at
  `microsoft-extensions/dependency-injection` (pre-flattening spelling).

### P3 — `deniz-dotnet-aspire` curation session (with the user)

- **Router repair** (ROADMAP item): aspire-skills is a coherent 6-skill system; `aspire` routes to
  the other five. Curating the closure means resolving upstream-URL handoffs into
  harness-reachable references. Curate from `skills/`; `.github/plugins/aspire-skills/` is a
  symlink mirror; patches cannot touch symlinks (Known Gap).
- **Policy contradiction only the curator can resolve:** `aspire-configuration` rejects
  application-level service discovery; `aspire-service-defaults` installs `AddServiceDiscovery`
  in app code.
- **Standing warnings:** `dotnet-devcert-trust` (general) carries unrewritten
  `dotnet-skills:aspire-configuration`/`aspire-service-defaults` references — they rewrite
  automatically once the aspire manifest curates those sources; verify with `npm run validate`.
- Version drift (Aspire 13.3/13.4, .NET 10 pins); `aspire-deployment`'s GitHub Actions sample
  deploys without `--non-interactive` against its own guidance; `aspire-integration-testing`
  warns against timing waits while using `Task.Delay`; `mailpit-integration` samples don't
  compile upstream and bridge into general's email cluster — decide the authoring/runtime split.

### Cross-module

`akka-net-aspire-configuration` (719-line Akka-on-Aspire cookbook naming five skills across both
modules) has no natural home: aspire module with a cross-plugin edge, akka module ditto, or
split. Decide with both manifests open.

### Standing ROADMAP items this pickup does not touch

Installer (parked), machine migration to single source of truth, curation sanity panel,
invocation-reliability ADR candidate, public-release decision — all in `docs/ROADMAP.md`.

## Delete this pickup when

The plan review is closed out, Wave A and Wave B have landed with their gates green and the
planning scratch deleted, and the P1–P3 backlog is either picked up or re-handed with whatever
deltas remain.
