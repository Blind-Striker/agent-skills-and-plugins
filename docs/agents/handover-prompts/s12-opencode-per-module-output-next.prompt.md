# S12 - Review the general curation, then split the OpenCode output per module

Date: 2026-08-03

> Temporary pickup for unfinished cross-session work. Establish live state before trusting it,
> execute it against the current repository, and delete it when this follow-up ships. This is not a
> second roadmap or a policy home; durable rules and evidence are linked rather than repeated.

## Mission

Two hats, in order:

1. **Reviewer.** Audit what the general-curation session shipped (commit `5c36e57`) — the decisions
   and their recorded rationale — and challenge anything weak. Review with a non-authoring model
   lineage (the authoring session was Kimi K3).
2. **Builder.** Pick up the items below in priority order. P1 is the reason this handover exists.

## Establish live state

```powershell
git status --short
git log --oneline -8
git show --stat 5c36e57
npm run inventory
npm run build; npm run validate   # expect: 0 errors; 4 standing deniz-process warnings plus the
                                  # 2 devcert-trust aspire-namespace warnings named in P3 below
```

## Onboarding reads (in order)

1. `AGENTS.md` — the contract: transformation not selection, compile-time vs runtime, hard rules.
2. `curation/SCHEMA.md` — the authoring grammar and the mechanism ladder.
3. ADRs, all eight, but these carry the session: `0001` (overlay/lock mechanics), `0002` (two native
   trees, flat-namespace decision — P1 amends it), `0005` (invocation grammar), `0006` (three axes),
   `0007` (control beats fidelity), `0008` (references/linker/ledger).
4. `docs/ROADMAP.md` — operational state; Known Gaps matter here.
5. `docs/research/skill-invocation-across-harnesses.md` — measured harness behavior; the intent
   matrix; the "neither"-class resolution (added in the review target).
6. `docs/research/skill-framework-landscape.md` — why the curated set looks the way it does.
7. `docs/research/harness-adapters.md` and `docs/research/upstream-repo-layouts.md`.
8. `experiments/harness-invocation/protocol.md` + `runbook.md` — how real output is mounted and
   exercised; P1 changes what those mounts mean.
9. The manifests: `curation/deniz-process.yaml` (the mature example), then
   `curation/deniz-dotnet-general.yaml` (the review target).

## What shipped in `5c36e57` (the review target)

`deniz-dotnet-general` curated corpus-first: 157 product-facing components across
`dotnet-skills` (Aaronontheweb) and `dotnet-agent-skills` (dotnet/skills) were read at body level
(four parallel readers, spot-verified), and every in-scope item got an answer in the manifest —
71 taken (55 `auto` / 15 `both` / 1 agent), 80 excluded with reasons. Akka/Aspire items were
classified but not curated (their evidence is in P2/P3 below).

The rationale lives beside each item; the module-level reasoning is the manifest header. Load-bearing
decisions to review:

- **Invocation rule:** honor upstream fire-design — knowledge and report-only audits are `auto`;
  repo/system-mutating ceremonies are `both`; `manual` went unused. Cross-check against
  `deniz-process.yaml`'s header rule (`manual` is unreachable from other bodies, so entry points
  differ) — the two modules disagree in posture because the upstreams disagree in design
  (mattpocock flags most skills user-only; MS/Aaron flag nothing). Is that reconciliation sound?
- **Three taste calls the curator made:** the full MSBuild estate (17 skills, 3 router agents
  excluded); `testcontainers-integration-tests` patched to the post-3.0 `ContainerBuilder` API
  (plus an upstream-missing Respawn fixture constructor completed); `analyzing-dotnet-performance`
  as `both` rather than `manual`/`as: agent`.
- **Seven hash-stamped patches:** six replay-first MSBuild binlog bodies (the "plugin bundles
  BinlogMcp" claim is false where they land — the MCP registration is plugin-level upstream) and
  the testcontainers API rewrite. One merge: `resolve-project-references` → `build-perf-diagnostics`
  (`merged_from`, blessed).
- **Reference posture:** no `depends_on` anywhere — MS bodies point at sibling skills by bare name
  (candidate-tier, ADR-0008), and the reference husks (`filter-syntax`, `platform-detection`,
  `test-analysis-extensions`) are taken `auto` as the grammar's answer to upstream's
  double-flagged "neither" class. Is leaving those edges unguarded acceptable, or should the
  critical ones (`run-tests` → `filter-syntax`/`platform-detection`) be promoted with patches?
- **Framework fit:** curator uses TUnit (primary), xUnit (occasional), never NUnit/MSTest — the
  MSTest line was excluded on that basis. Noted gap: no corpus skill writes TUnit tests; an
  own-skill candidate.
- **Tolerated upstream content bugs** (deliberately flowing, noted in manifest comments): the
  `Result<T>` self-contradiction in `modern-csharp-coding-standards`, `msbuild-modernization`'s
  nonexistent `dotnet migrate-packages-config`, the `msbuild-server` vs `build-perf-baseline`
  activation conflict.
- **`tools/validate.ts` grew 23 exact prose-address suppressions** (`/bl:`, docker images, CSS
  selectors) — confirm none of them suppress a real reference.

## P1 — Split the OpenCode output per module (the main pickup)

**Current state:** `opencode/` is one flat tree — `skills/`, `commands/`, `agents/` mixing all four
modules (96 + 37 + 1 artifacts). Module identity exists only in `plugins/` and the ledger.
**Target state:** the OpenCode tree is emitted per module, symmetric to the Claude plugins, so a
future installer can install per category (the user's explicit requirement; ROADMAP Next Up on the
installer).

**The design tension that must be resolved deliberately, not silently:** OpenCode discovery expects
`skills/`, `commands/`, `agents/` at a config root (measured in the invocation research). A
per-module layout (`opencode/<plugin>/{skills,commands,agents}/`) is **not itself mountable** as
one config dir — the three-mount consumption the experiments validated (`OPENCODE_CONFIG_DIR`,
project-local, global) works precisely because the tree is flat today. So the split trades the
zero-install mount for per-category installability, and its consumption story is bound to the
installer decision (ROADMAP). Design questions to answer in the work:

- Exact layout, and whether a flat aggregate view is also emitted (and if so, committed or not).
- Does the installer copy/symlink module subtrees into the config dir's `skills/`/`commands/`?
  (Prior art the ROADMAP already names: `opencode-switchboard`'s hash-compared sync.)
- Names stay flat and globally unique — the split is install-time granularity, **not** runtime
  namespacing. ADR-0002 rejected prefixing artifact names and this pickup does not reopen that; it
  amends ADR-0002's output-layout text (rewrite the ADR in place per Working Style).
- Touchpoints to update in the same change: the OpenCode emitter in `tools/build.ts`; `validate`'s
  generated-tree scans (duplicate-name check, symlink walk, parked-bundle checks, the L4 linker);
  `tools/lib/ledger.ts` if paths move; `experiments/harness-invocation/` runbook/matrix/selftest
  mount assumptions; the mount documentation in `docs/research/harness-adapters.md` and
  `skill-invocation-across-harnesses.md`.
- Gates: `npm test`, `npm run typecheck`, `npm run build`, `npm run validate`, and review the
  ledger/tree delta. CI's freshness check will enforce the new layout.

## P2 — `deniz-dotnet-akka` curation session (with the user)

Boundary evidence from the corpus pass (all `dotnet-skills`; upstream author is the Akka.NET
founder — deep but internally repetitive):

- The local-vs-cluster abstraction (`AkkaExecutionMode`, `GenericChildPerEntityParent`) repeats
  across `akka-best-practices` (the starter), `akka-hosting-actor-patterns`, and
  `akka-testing-patterns`. Pick one canonical home or accept the repetition knowingly.
- `akka-management` vs `akka-aspire-configuration` ship incompatible-looking options models for the
  same bootstrap/discovery settings — choose one canonical model before taking both.
- `akka-net-best-practices`' local pub/sub companion claims EventStream use; the shown
  implementation uses its own subscription dictionary.
- `akka-net-specialist` agent: persona overlapping all five skills; if taken, wire its handoff to
  the curated skills explicitly. General-module precedent: orchestrator/persona agents excluded;
  only a genuinely deep specialist kept.
- Stale cross-reference: `akka-hosting-actor-patterns` points at
  `microsoft-extensions/dependency-injection` (pre-flattening spelling).

## P3 — `deniz-dotnet-aspire` curation session (with the user)

Boundary evidence from the corpus pass:

- **Router repair (the ROADMAP's aspire-router item, untouched):** aspire-skills is a coherent 6-skill system
  (~1,257 body lines + ~5,071 reference lines, 30 files): `aspire` routes to `aspire-init`,
  `aspireify`, `aspire-orchestration`, `aspire-deployment`, `aspire-monitoring`. Curating the
  closure means resolving upstream-URL handoffs into harness-reachable references. Curate from
  `skills/`; `.github/plugins/aspire-skills/` is a symlink mirror. Known Gap: patches cannot touch
  symlinks; curated skills carry them.
- **Policy contradiction only the curator can resolve:** `aspire-configuration` rejects
  application-level service discovery; `aspire-service-defaults` installs `AddServiceDiscovery` in
  app code. Decide the policy before taking either.
- **Standing warnings this pickup inherits:** `dotnet-devcert-trust` (general) carries unrewritten
  `dotnet-skills:aspire-configuration` and `dotnet-skills:aspire-service-defaults` references.
  They rewrite automatically once the aspire manifest curates those sources — closing aspire
  should clear both; verify with `npm run validate`.
- Version drift: aspire-skills mixes Aspire 13.3/13.4 and .NET 10 pins; `aspire-deployment`'s
  GitHub Actions sample deploys without `--non-interactive` against its own safety guidance.
- `aspire-integration-testing` warns against timing waits while its own examples use `Task.Delay`.
- `mailpit-integration` samples don't compile upstream (`with` on a non-record, unimplemented
  `WaitForMessagesAsync`); it bridges into general's email cluster (`mjml-email-templates`,
  taken) — decide the email authoring/runtime split.

## Cross-module

- `akka-net-aspire-configuration` (719-line Akka-on-Aspire cookbook naming five skills across both
  modules) has no natural home: aspire module with a curated cross-plugin edge, akka module ditto,
  or split. Decide with both manifests open.

## Delete this pickup when

The review is closed out, P1 has landed with its ADR-0002 amendment, and P2/P3 are either done or
re-handed with whatever deltas remain.
