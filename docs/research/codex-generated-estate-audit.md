# Codex generated-estate target-fit audit

Date: 2026-09-07

This dated audit records the first target-fit pass over the generated Codex estate. It is evidence
for the next design decision, not permission to mutate curation. Current transformation rules live
in [transformation and emission](../architecture/transformation-and-emission.md).

## Scope and method

The audited build contains four plugins and 117 native `SKILL.md` files. The pass began after
`npm run inventory`, then combined the deterministic validator with corpus scans over every
generated Markdown file for:

- unsupported or leaked frontmatter, command arguments, model/permission fields, and Claude tool
  call syntax;
- semantic `$plugin:skill` reachability and relative dependency-file links;
- names of all 117 emitted skills used with slash-style invocation syntax;
- Claude Code and OpenCode product names, config paths, and host-specific instructions;
- the two source agents adapted to skills; and
- per-plugin name-and-description budget pressure.

The scan classifies text in product comparison, upstream reference material, code examples, URLs,
and explicitly labeled per-platform sections as genuine references rather than target leakage.

## Clean structural findings

- All 117 skills have Codex-valid names, required descriptions, allowed frontmatter, link-free
  output trees, reachable namespaced references, and intact relative dependency closure.
- Exactly 27 `manual` items have `agents/openai.yaml`; 70 `auto`, 11 `both`, eight undeclared
  curated items, and the one original skill have no implicit-disabling policy.
- The Akka.NET and Roslyn source agents retain useful procedural persona text as skills without
  leaked model, permission, or custom-agent metadata.
- No command argument placeholder survived as runtime instruction. The two dollar-number scan hits
  are a monetary pressure-test example and a regular-expression replacement example.
- The one `allowed-tools` key is native Codex skill metadata. The only Claude-specific Bash-tool
  parameter is inside an explicitly labeled Claude Code subsection paired with a separate Codex
  launch subsection.
- Two Aspire descriptions exceeded the native limit only after namespaced Codex reference
  localization. The emitter now truncates them deterministically to 1,024 characters after
  localization, reports the change, validates the bound, and records `metadataTransformations` in
  the ledger.

## Follow-up classification and resolution

The first corpus scan returned 50 slash-shaped candidates across 13 generated files in seven
`deniz-process` items:

- `ask-deniz`;
- `improve-codebase-architecture`;
- `setup-matt-pocock-skills`;
- `to-spec`;
- `to-tickets`;
- `triage`; and
- `wayfinder`.

The 50 candidates were not 50 equivalent skill invocations. A source-level review separated
user-started skill pointers from semantic skill handoffs, host commands such as `/clear`, filesystem
paths such as `/tmp`, URLs, closing HTML tags, and the GitLab `/blocked_by` quick action. The latter
groups remain literal because they are not repository skill symbols.

The actual skill pointers are now authored as namespaced facts in the existing common curation and
overlay layer. Model-started handoffs also declare matching `depends_on` edges; user-started routes
remain pointers rather than false model dependencies. Each emitter localizes those shared facts to
its native spelling, including `$deniz-process:<skill>` for Codex. This deliberately corrects the
status quo across targets where the previous text was ambiguous or non-native; it does not promise
byte identity for Claude or OpenCode as a design constraint.

No Codex-only body-patch seam was needed. A heuristic that rewrites every `/known-name` remains
unsafe because the corpus contains multiple slash-shaped non-skill forms. Authored namespaced facts
preserve ADR-0008's deterministic symbol boundary and keep body ownership in the common pipeline.

## Runtime boundary

The [structural CLI record](../../experiments/harness-invocation/records/2026-09-07-codex-plugin-structural.md)
proves isolated marketplace listing, installation, cache materialization, and removal. The later
[behavioural record](../../experiments/harness-invocation/records/2026-09-07-codex-plugin-behaviour.md)
adds a credentialed Luna panel over the installed 117-skill estate: explicit invocation, manual
implicit suppression, repeated auto/both selection, cross-skill handoff, bundled-reference loading,
an uninstalled negative control, and two generated-skill probes all passed. Every call warned that
descriptions were shortened for the skills context budget, so catalog pressure remains visible even
though the measured generated implicit probe succeeded.
