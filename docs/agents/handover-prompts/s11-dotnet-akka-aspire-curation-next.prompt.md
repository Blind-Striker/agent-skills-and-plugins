# S11 - Akka and Aspire module curation, armed with the general-pass boundary evidence

Date: 2026-08-03

> Temporary pickup for unfinished cross-session work. Establish live state before trusting it,
> execute it against the current repository, and delete it when this follow-up ships. This is not a
> second roadmap or a policy home; durable rules and evidence are linked rather than repeated.

## Where this comes from

The s10 corpus-first pass read every product-facing body under `external/dotnet-skills/`,
`external/dotnet-agent-skills/`, and `external/aspire-skills/` and curated `deniz-dotnet-general`.
Akka and Aspire items were classified but deliberately not curated. The findings below are the
boundary evidence that pass produced; each names its destination decision.

## Commands to run

```powershell
git status --short
npm run inventory
```

Then read `AGENTS.md`, `curation/SCHEMA.md`, `docs/ROADMAP.md#next-up`, ADR-0005/0006/0007/0008,
`docs/research/skill-invocation-across-harnesses.md`, and the general module's manifest header
(the invocation rule recorded there applies to these sessions too).

## Akka findings (destination: `curation/deniz-dotnet-akka.yaml`)

All from `dotnet-skills`; the upstream author is the Akka.NET founder, so the content is deep but
internally repetitive.

- **Canonical ownership must be decided once.** The local-vs-cluster abstraction
  (`AkkaExecutionMode`, `GenericChildPerEntityParent`) is repeated across `akka-best-practices`
  (taken as the starter), `akka-hosting-actor-patterns`, and `akka-testing-patterns`. Pick one
  canonical home and de-duplicate the others, or accept the repetition knowingly.
- **Settings-model divergence.** `akka-management` and `akka-aspire-configuration` ship
  incompatible-looking options models for the same cluster bootstrap/discovery settings. Choose
  one canonical settings model before taking both.
- **`akka-net-best-practices` inconsistency:** its local pub/sub companion claims EventStream use
  while the shown implementation uses its own subscription dictionary.
- **`akka-net-specialist` agent:** persona overlapping all five skills; if taken, its handoff to
  the curated skills should be made explicit. The general module's precedent: orchestrator/persona
  agents were excluded; only a genuinely deep specialist (roslyn) was kept.
- **Stale cross-references:** `akka-hosting-actor-patterns` points at
  `microsoft-extensions/dependency-injection` (pre-flattening spelling).

## Aspire findings (destination: `curation/deniz-dotnet-aspire.yaml`)

- **The router needs its closure or a rewrite** (ROADMAP Next Up #2 — this session did not change
  it). aspire-skills is a coherent 6-skill system (~1,257 body lines + ~5,071 reference lines over
  30 files): `aspire` routes to `aspire-init`, `aspireify`, `aspire-orchestration`,
  `aspire-deployment`, `aspire-monitoring`. Curating the closure means resolving all upstream-URL
  handoffs into harness-reachable references. `.github/plugins/aspire-skills/` is a symlink
  mirror — curate from `skills/`. Known gap: patches cannot touch symlinks; some curated skills
  carry them.
- **Policy contradiction only the curator can resolve:** dotnet-skills' `aspire-configuration`
  rejects application-level service discovery (AppHost emits explicit env config), while
  `aspire-service-defaults` installs `AddServiceDiscovery` in app code. Taking both as-is ships a
  contradiction; decide the policy first.
- **Standing warnings this session left behind:** `dotnet-devcert-trust` (general module) carries
  unrewritten `dotnet-skills:aspire-configuration` and `dotnet-skills:aspire-service-defaults`
  references. They rewrite automatically once the aspire manifest curates those sources — closing
  the aspire module should clear both warnings; verify with `npm run validate`.
- **Version drift:** aspire-skills mixes Aspire 13.3/13.4 and .NET 10 pins;
  `aspire-deployment`'s GitHub Actions sample invokes deployment without `--non-interactive`
  despite its own safety guidance requiring it.
- **`aspire-integration-testing` internal contradiction:** warns against timing waits while its
  own reference examples use `Task.Delay`; CLI/MCP snippets looked stale against aspire-skills.
- **`mailpit-integration` samples don't compile** upstream (`with` on a non-record settings class,
  unimplemented `WaitForMessagesAsync`); it also bridges into the general module's email cluster
  (`mjml-email-templates`, taken). Decide whether email authoring stays general while Mailpit
  stays aspire.

## Cross-module

- **`akka-net-aspire-configuration` has no natural home** (719-line Akka-on-Aspire cookbook naming
  five skills across both modules). Options: aspire module with a curated cross-plugin edge to the
  akka items, akka module ditto, or split. Decide with both manifests open.
- **`dotnet-skills` is pinned where `docs/inventory.md` says; the corpus read is one sync old at
  most.** Re-run `npm run sync` judgement applies per the nightly-pin Known Gap.

## Delete this pickup when

Both modules are curated and validated, or when a further successor narrows what remains. Do not
leave the findings above living only here — as each lands, its home is the manifest comment beside
the item.
