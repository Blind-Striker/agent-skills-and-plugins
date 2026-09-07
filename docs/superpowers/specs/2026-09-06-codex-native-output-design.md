# Codex-native output design

Date: 2026-09-07

Status: Approved

## Purpose

Add Codex as a third native emission branch of the existing curation compiler. Codex passes through
the same resolution and body-assembly pipeline as Claude Code and OpenCode, then receives its own
target-native output shape. No finalized generated tree serves as another target's interchange
format. The first deliverable is installable Codex Plugin output for Codex CLI and Codex in the
ChatGPT desktop app. It must preserve the repository's transformation-and-control product boundary
while using Codex-native skills, metadata, invocation policy, namespacing, and marketplace
installation.

This specification is temporary execution input. Accepted rationale belongs in the existing ADRs,
current mechanics belong in architecture and the manifest schema, dated platform evidence belongs
in [Codex native plugin and skill surfaces](../../research/codex-native-plugin-and-skill-surfaces.md),
and unfinished work belongs in the roadmap.

## Goals

- Generate one Codex Plugin for each current curation manifest.
- Extend the existing compiler pipeline so all three targets share source resolution, omission,
  overlay/patch application, body assembly, dependency closure, provenance, and attribution.
- Resolve invocation, artifact shape, references, metadata, and body fit independently in the Codex
  emitter after that common assembly boundary.
- Install and update the generated plugins through Codex's native marketplace commands.
- Make every emitted item a valid, addressable Codex skill in the first distributable baseline.
- Keep unsupported or transformed source semantics reviewable in the generated ledger.
- Verify structure deterministically and runtime discovery through an isolated Codex experiment.

## Non-goals

- Codex IDE-extension plugin support; the IDE does not currently load plugins.
- Installing `.codex/agents/*.toml` into a user or project profile.
- A custom Codex installer, ownership database, or recovery protocol.
- Publishing to OpenAI's universal public Plugins Directory.
- Adding MCP servers, hooks, connectors, scheduled tasks, or UI solely because Codex plugins can
  carry them.
- Repartitioning the four existing curation manifests before a discovery experiment demonstrates a
  concrete catalog-pressure failure.
- Changing item bodies globally to remove every occurrence of a product name without item-level
  target-fit review.

IDE coverage and native custom agents are possible follow-up transports. They are not implied by
completion of this plugin milestone.

## Decision changes required before implementation

The existing ADRs remain `Accepted` living records. They are edited in place to state the current
decision, while Git retains the earlier wording. Because the revision is material, each affected ADR
receives the decision date of the accepted revision.

### Invocation contract

`invocation` describes required and forbidden initiation capabilities, not an exhaustive set of all
ways a harness may expose an artifact:

| Value    | Implicit/model initiation                   | Explicit/user initiation                    |
| -------- | ------------------------------------------- | ------------------------------------------- |
| `auto`   | required                                    | unspecified                                 |
| `manual` | forbidden                                   | required                                    |
| `both`   | required                                    | required                                    |
| absent   | preserve upstream or use the target default | preserve upstream or use the target default |

This is a clarification of the same neutral trigger decision, with one important corrected
guarantee: `auto` does not mean that explicit invocation must be impossible. It means the emitter
must not require a user ceremony before the model can use the item. `manual` retains the strict
safety boundary: the model must not select the item implicitly.

The current mappings remain valid:

| Target      | `auto`                             | `manual`                                      | `both`            |
| ----------- | ---------------------------------- | --------------------------------------------- | ----------------- |
| Claude Code | skill with `user-invocable: false` | skill with `disable-model-invocation: true`   | ordinary skill    |
| OpenCode    | skill                              | command                                       | skill and command |
| Codex       | ordinary skill                     | skill with `allow_implicit_invocation: false` | ordinary skill    |

Codex therefore emits the same physical skill posture for `auto` and `both`. That is not a silent
approximation: explicit reachability is unspecified by `auto`, and Codex's native skill surface
provides it. The ledger must show the resolved implicit and explicit capabilities so reviewers do
not infer a stronger model-only guarantee.

### ADR scope

- ADR-0002 keeps the separate native-output and reported-loss rules, adds Codex as a third target,
  and makes clear that a target may expose an unspecified capability without violating intent.
- ADR-0005 owns the capability contract above and removes every claim that `auto` universally means
  model-only.
- ADR-0006 keeps the three transformation axes and revises the invocation axis from an exact actor
  set to required, forbidden, and unspecified initiation capabilities.

Current architecture and `curation/SCHEMA.md` must change with the implementation that makes Codex
mechanics real. Before that implementation lands, the roadmap names Codex output as unfinished work
rather than letting current-mechanics documents claim it exists.

## Output topology

The compiler generates a third review tree:

```text
codex/
  deniz-process/
    .codex-plugin/plugin.json
    skills/<name>/SKILL.md
    ...skill-owned files
  deniz-dotnet-general/
  deniz-dotnet-akka/
  deniz-dotnet-aspire/

.agents/
  plugins/
    marketplace.json
```

`codex/`, `.agents/plugins/marketplace.json`, and any Codex projection added to the ledger become
generated, committed review surfaces and are never hand-edited. The repository bootstrap, workflow,
and generated-diff gates must list them explicitly.

Each Codex plugin:

- reuses the curation manifest's stable plugin name and curator version;
- has a required `.codex-plugin/plugin.json` with `skills: "./skills/"`;
- carries repository, license, author, description, and install-surface metadata derived from owned
  repository metadata rather than copied blindly from Claude manifests;
- contains no `commands/` or `agents/` compatibility directories; and
- contains only paths referenced by its manifest plus item-owned dependency closure.

The repository marketplace contains one entry per generated plugin. Each entry points to
`./codex/<plugin>` relative to the repository marketplace root and includes the required install and
authentication policy plus an appropriate category. Native `codex plugin marketplace` and
`codex plugin` commands are the installation mechanism for this milestone.

## Item resolution

Codex uses the same compiler front half as Claude Code and OpenCode. The compiler loads and scans
authored `curation/`, pinned upstream sources, overlays, and original skills; performs identity and
source preflight; applies omission, overlays, patches, and curator frontmatter; retains the selected
dependency closure; and records neutral provenance, attribution, and semantic reference facts. That
work produces one target-neutral assembled item consumed by all three emitters.

The target-neutral assembly boundary is pipeline state, not a fourth output format and not a
distributable tree. In the current implementation, part of that role is played by the
pre-localization Plugin staging tree that OpenCode reads. The implementation may make the boundary
explicit in memory or in an internal staging area, but it must not copy finalized Claude or OpenCode
output into Codex and must not resolve the same authored item through a second Codex-only pipeline.
Making this boundary explicit is a behavior-neutral refactor for the existing targets: regenerated
Claude and OpenCode paths, contents, and modes remain unchanged.

For the first milestone, every resolved item becomes a Codex skill:

- source or resolved `skill` remains a skill;
- resolved `command` becomes a skill containing the reusable command procedure; and
- resolved `agent` becomes a skill containing the reusable procedure and only the persona guidance
  needed to execute it correctly.

This flattening follows the documented Codex plugin migration path. It does not claim that a skill
is a native Codex custom agent. The Codex emitter must preflight the resulting single skill
namespace before deleting or rewriting any generated output.

The compiler filters frontmatter to fields Codex understands. Codex-only invocation policy lives in
`skills/<name>/agents/openai.yaml`; it is emitted only when policy or other supported metadata makes
the file necessary. Dropped keys and material transformations are projected in the ledger rather
than retained as ignored passengers.

## References and body fit

Codex receives its own reference-localization strategy. A resolvable explicit skill pointer uses
Codex's plugin namespace and `$<plugin>:<skill>` spelling. The linker resolves semantic identities
before rendering target syntax, as it does for the existing targets.

Body transformation is item-aware:

- target-specific invocations and navigation are rewritten when their semantic target is known;
- product names are retained when the instruction genuinely refers to that product;
- Claude-only tool, command, or agent assumptions that affect execution require a Codex rewrite,
  per-target body ownership, or an explicit curation decision; and
- a global textual replacement of `Claude`, slash commands, or namespace punctuation is forbidden.

If one authored body cannot serve all three targets, the implementation must introduce an authored
per-target body transformation seam rather than editing generated trees or weakening the fit
requirement. Its exact grammar belongs in the implementation plan only after an actual item requires
it.

## Ledger and validation

The generated ledger adds a Codex projection for every resolved item. At minimum it records:

- plugin and skill identity;
- source/resolved kind and emitted Codex kind;
- declared invocation;
- resolved implicit capability: enabled, disabled, or target default;
- resolved explicit capability: available or target default;
- emitted policy files;
- localized outgoing references and reachability;
- dropped source metadata; and
- material kind or body transformations.

Validation must reject:

- an invalid or missing Codex plugin manifest;
- marketplace entries that escape the repository root or point at a missing plugin;
- invalid, duplicate, or colliding plugin/skill identities after kind flattening;
- a `manual` skill without `allow_implicit_invocation: false`;
- an `auto` or `both` skill whose emitted policy disables implicit invocation;
- unresolved or wrongly spelled Codex namespaced references;
- emitted Claude/OpenCode-only frontmatter not explicitly allowed by Codex;
- dependency files outside the emitted skill closure;
- machine-specific paths, secrets, curator stamps, or attribution/license regressions; and
- generated drift or a non-idempotent second generation.

Validation proves deterministic structure, policy, references, and bytes. It does not prove that a
model will select a skill or follow its instructions.

## Runtime verification

Extend the harness-invocation experiment with a Codex leg using an empty external lab and isolated
Codex configuration/cache locations. Do not install into or mutate the real user profile.

The recorded matrix must include:

1. negative and positive isolation controls;
2. marketplace discovery and one-plugin installation;
3. discovery of every skill in a small fixture plugin;
4. explicit `$<plugin>:<skill>` invocation;
5. `manual` refusal under an implicit trigger plus successful explicit invocation;
6. `auto` and `both` implicit eligibility without a claim that either fires deterministically;
7. cross-plugin reference resolution;
8. plugin update and cache-path observations;
9. large-catalog listing, truncation warnings, and explicit addressability for all four plugins; and
10. repeated bounded behavioural samples for description-driven selection.

Record the Codex version, host surface, model when one is used, plugin and marketplace identities,
sanitized paths, fixture commit or digest, exact assertions, and whether each result is structural,
observed once, or repeated. Structural inspection may gate the build; probabilistic model behaviour
remains evidence rather than a deterministic CI assertion.

## Delivery sequence

The implementation plan should use these review boundaries:

1. accept the design and revise ADR-0002, ADR-0005, and ADR-0006 in place;
2. add an isolated Codex structural/runtime probe and record the baseline;
3. make the existing common assembly boundary explicit without changing Claude or OpenCode output,
   then add Codex plugin emission and marketplace generation;
4. implement invocation metadata, reference localization, and command/agent-to-skill adaptation;
5. extend ledger, validation, tests, documentation, and generated-output discipline; and
6. audit generated target fit, run the full gates twice for idempotence, and perform an isolated
   native marketplace install/update verification.

Do not overlap compiler or ledger edits with an unmerged Selection change. Rebase the execution plan
on the accepted Selection tree and re-identify symbols and test counts before implementation.

## Acceptance criteria

The Codex Plugin milestone is complete only when:

- all four curation manifests generate valid, separately reviewable Codex Plugins;
- the generated marketplace exposes exactly those four plugins through repository-relative paths;
- every curated item has one resolved Codex artifact or a deliberate manifest-level exclusion with
  a nearby reason;
- `auto`, `manual`, `both`, and absent invocation cases satisfy the capability table and appear
  truthfully in the ledger;
- command and agent transformations retain their dependency closure and runnable procedure;
- the common-pipeline refactor leaves generated Claude and OpenCode paths, contents, and modes
  unchanged relative to the implementation baseline;
- all deterministic Codex manifest, identity, reference, reachability, attribution, and safety
  checks pass;
- full repository tests, typecheck, lint, formatting, build, inventory, validation, public-safety,
  generated-diff review, and second-run idempotence gates pass;
- an isolated supported Codex surface discovers, installs, invokes, updates, and removes a fixture or
  generated plugin without modifying the real profile; and
- documentation claims plugin support only for the surfaces actually measured.

## Follow-up milestone triggers

Open a separate design only when one of these needs becomes concrete:

- IDE users need packaged access to the curated catalog: design a standalone `.agents/skills`
  transport with ownership and update semantics.
- A curated agent loses material value as a skill: design optional `.codex/agents/*.toml`
  installation and treat the evolving configuration format as an explicit compatibility risk.
- The General plugin exceeds practical skill-discovery limits: measure the failure, then consider
  plugin repartitioning, description reduction, or a router skill.
- Public-directory submission becomes a goal: add publication metadata, policy review, visual
  assets, and external-review acceptance as a separate release initiative.
