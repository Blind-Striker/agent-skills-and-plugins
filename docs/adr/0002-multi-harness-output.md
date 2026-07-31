# ADR-0002: Harness-native output instead of a common format

Date: 2026-07-31
Status: Accepted

## Context

Two harnesses matter: Claude Code (primary) and OpenCode (secondary). They agree on the important
part — `SKILL.md` is an open standard (agentskills.io) and OpenCode reads skills natively — but they
diverge everywhere else: plugin packaging and `marketplace.json` are Claude Code concepts, command
and agent markdown differ in shape, and features like hooks and `allowed-tools` have no OpenCode
equivalent.

The wrong move is to invent an intermediate format rich enough for both and generate from it. That
lands on the lowest common denominator, and every harness gets a slightly worse artifact than it
could have had.

## Decision

One source (`curation/` + `overlays/` + `skills/`), one build, and a separate native artifact tree per
harness: `plugins/` for Claude Code, `opencode/` for OpenCode (`opencode/skills/`,
`opencode/commands/`, `opencode/agents/` — the directory names OpenCode documents). Per harness:

- **Skills pass through** — the shared `SKILL.md` format, no translation.
- **Commands and agents are transformed** into each harness's markdown shape.
- **Unmappable features are dropped, not approximated**, and every drop is listed in the build report.
  Silent loss is the failure mode being designed against.

Model borrowed from wshobson/agents: harness-native artifacts, not lowest-common-denominator
translations.

### Alternatives considered

- **Carry the plugin name into the artifact name for OpenCode**, as wshobson does
  (`<plugin>-<skill>`), so a flat namespace cannot collide. Rejected. wshobson needs it because its
  source types are fixed and two plugins can genuinely ship the same name; here the uniqueness rule
  below already makes a collision impossible, and where one ever threatens, the item-level `name:`
  charges the rename to that item instead of lengthening every name. Measured across the five
  vendored repos: no two skills share a name, and no two come close — what overlaps is *subject*
  (both `aspire-skills` and `dotnet-skills` cover Aspire, under different names), which is a
  curation choice rather than a naming problem. The cost lands where it hurts most: under ADR-0005
  a `manual` item becomes an OpenCode command, and a command is a name the user types.
  Reconsider only if the uniqueness rule itself becomes the constraint — a prefix is what would buy
  back the ability to curate one upstream skill into two modules.

## Consequences

- Claude Code output is not constrained by what OpenCode can express, and vice versa.
- Per-harness adapters stay small because skills — the bulk of the content — need no adapter at all.
- A third harness (Codex, Cursor, Gemini) means a new emitter, not a redesign. None is planned.
- Anything OpenCode cannot represent is visible in the build report, so a curation decision can be
  made instead of discovering the gap in use. OpenCode agent permission mapping is deliberately absent
  in the first version.
- The OpenCode tree is flat (`opencode/skills/`, `opencode/commands/`, `opencode/agents/`), not
  namespaced by plugin, so every output name must be unique across all `deniz-*` plugins —
  `validate` treats a cross-plugin duplicate as an error, and the same name cannot be curated into
  two modules. Claude Code is not the reason: its `/` menu namespaces every plugin skill and refuses
  a bare name, so a duplicate is unambiguous *there* for a user. It is not unambiguous for the
  model, which resolves the bare name and silently takes the first in listing order — so the rule
  earns its keep on both sides, but only for items the model can reach. `invocation` (ADR-0005)
  would allow narrowing the constraint to `auto` and `both` items, leaving `manual` ones free to
  share a name; it stays global because a name is also an identity everywhere else — the ledger's
  keys, the rewrite map's values and `depends_on` targets (ADR-0008) all resolve by output name,
  whatever the trigger.
- The OpenCode tree is emitted from `plugins/` *before* cross-reference rewriting, and each tree is
  then rewritten with its own map — `<plugin>:<name>` for Claude Code, bare names for OpenCode,
  which addresses a skill by its `name` field. The order is load-bearing: emitting after the Claude
  rewrite is how OpenCode once ended up carrying `deniz-*:name` references it cannot resolve, since
  OpenCode has no plugin concept.
