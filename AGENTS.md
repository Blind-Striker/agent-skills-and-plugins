# Agent Instructions

Harness-neutral bootstrap for LLM/code agents working in this repository. Keep this file focused on
rules that apply before task-specific context is known; load detailed canon through the routing table
below. Current state and next steps live in `docs/ROADMAP.md`.

## Purpose and phase boundary

Personal multi-harness skill/plugin marketplace. Upstream repos are vendored as git submodules in
`external/`; `curation/*.yaml` decides what is packaged into the `deniz-*` Claude Code plugins
(`plugins/`) and same-named OpenCode Module Bundles (`opencode/`).

Selection is the cheapest thing here; the product is **transformation and control**, not fidelity to
upstream. Each item is resolved per harness for trigger, artifact shape, and target fit. Source kind
is the resolver default when `as:` is absent, never a binding constraint. The result serves the
curator's working style, and heavy modification is a normal outcome
([ADR-0006](docs/adr/0006-output-is-a-transformation.md),
[ADR-0007](docs/adr/0007-control-beats-fidelity.md)).

The product crosses three phases: **compile-time transformation** into separate harness-native Plugin
and Bundle output, **install-time byte-preserving composition** of selected OpenCode Bundles, and
**skill runtime** after a harness discovers or invokes an artifact. Compilation must make runtime
needs resolve where the artifact lands, but curation changes upstream runtime behavior only when it
contradicts recorded item intent; it never re-solves a runtime problem upstream already solved.

## Always-on rules

- Never hand-edit `external/`, `plugins/`, `opencode/`, `dist/`,
  `.claude-plugin/marketplace.json`, `docs/inventory.md`, or `docs/ledger.json`. `external/` is
  read-only upstream evidence; the others are generated, committed review surfaces.
- Authored work belongs in `curation/`, `overlays/` and `overlays/overlays.lock.json`, original
  `skills/`, `tools/`, `docs/`, or `experiments/`, according to the task owner below.
- Before any item-level take, skip, merge, or modification decision, run `npm run inventory`, use the
  generated scanner-visible catalog to find candidates, and read the actual upstream source and
  dependency closure. Record the why beside the item in `curation/*.yaml`.
- Never commit secrets, tokens, or machine-specific paths. The sole machine-path exception is the
  synthetic detector control
  `experiments/harness-invocation/tests/fixtures/has-machine-path.txt`; extending its allowlist
  requires a scanner change in the same commit. Manifest comments, `overlays/`, and original
  `skills/` stamp no curator names or dates; Git carries provenance and `validate` enforces it.
- Prefer the smallest correct change. When intent is ambiguous, present the options, recommend one,
  and ask rather than deciding silently.
- Never resolve a conflict among implementation, current canon, and accepted ADR intent silently.
  Follow the ADR guide: correct stale canon or record the implementation gap with its responsible
  file or symbol.
- A change whose purpose is not to change behavior must preserve runtime, build, curation, and
  installer behavior.
- Documentation changes are lossless relocations, not shortening exercises. Keep affected docs
  current in the same change, give each current claim one owner, and leave brief relays elsewhere.
- Verify before claiming. Use the applicable gate in `docs/engineering/quality-gates.md`; "builds"
  and "works" are different claims.
- `AGENTS.md` is the harness-neutral bootstrap. `CLAUDE.md` only imports it into Claude Code, while
  OpenCode reads it natively. Claude import parsing also applies inside this file, so keep every
  literal `@` token in code formatting.

## Task routing

Read the relevant owner before proposing or changing the work. ADRs explain why a decision exists;
dated research supplies evidence and may intentionally preserve superseded positions.

| Task | Read first |
|---|---|
| Item curation, invocation, shape, body ownership, overlays, or harness emission | `docs/architecture/transformation-and-emission.md`, `curation/SCHEMA.md`, and `docs/engineering/workflow.md` |
| References, localization, `depends_on`, reachability, candidates, or ledger semantics | `docs/architecture/references-and-linking.md` and `curation/SCHEMA.md` |
| Module Bundles, Package transport, Selection, Ownership, installer Plan/Apply/Recovery, or Destination | `docs/architecture/distribution-and-installation.md` and `CONTEXT.md` |
| Repository workflow, upstream sync, generated-output discipline, or task closeout | `docs/engineering/workflow.md` |
| Tests, CI, formatting, generation review, or completion claims | `docs/engineering/quality-gates.md` and the scripts in `package.json` |
| Documentation placement/lifecycle or ADR creation/revision | `docs/engineering/documentation.md` and `docs/adr/README.md` |
| Domain terminology | `CONTEXT.md` |
| Dated research or evidence | `docs/research/README.md`; for repeatable harness measurements, `experiments/harness-invocation/protocol.md` and `experiments/harness-invocation/records/README.md` |
| Agent-only playbooks or handovers | `docs/agents/README.md` and `docs/engineering/documentation.md` |
| Current status, queue, open questions, known gaps, or deferred work | `docs/ROADMAP.md` and any active handover under `docs/agents/handover-prompts/` |

## Authority

- Live code, configuration, curation data, and generators are the mechanical authority for current
  behavior. `curation/*.yaml` owns item intent and nearby reasons; `curation/SCHEMA.md` owns manifest
  grammar; `package.json` owns commands.
- `docs/architecture/` and `docs/engineering/` own current technical and working canon.
- `docs/adr/` owns accepted decisions, rationale, trade-offs, and consequences; it is not a second
  mechanics manual.
- `docs/research/` is dated evidence and decision history, not current policy. `experiments/` owns
  repeatable methods and committed measurement records.
- `CONTEXT.md` owns vocabulary; `docs/ROADMAP.md` owns operational state; `docs/agents/` owns
  agent-only operation and temporary handovers.
- Generated inventory and ledger files are projections to regenerate and review, not authored truth
  to repair by hand. Memory, transient plans, handovers, and dated evidence never outrank live
  repository sources and current canon.
