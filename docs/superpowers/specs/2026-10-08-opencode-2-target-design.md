# OpenCode 2 target design

Date: 2026-10-08

Status: Proposed

## Purpose

Make the checkout produce, check, and install the OpenCode 2 output that current canon already
states. This specification covers steps 2 to 5 of the curator's OpenCode 2 sequence (step 1 is
the canon baseline; [`docs/ROADMAP.md`](../../ROADMAP.md#next-up) lists steps 2 to 5 as sub-steps 1
to 4 of Next Up item 1): the compiler, validator, ledger, and installer
changes; the preparation of the curation pass; the measurement records; and the real-profile
migration and next Package Release.

This specification is temporary execution input. It does not restate rules. The decided rules and
their owners are:

| Rule | Owner |
|---|---|
| OpenCode 2 only, v2.0.4 floor, measured v2.0.23, no OpenCode 1 layer | [Distribution and installation](../../architecture/distribution-and-installation.md#target-opencode-runtime) |
| Invocation mapping, `<plugin>.<name>` paths, `name` = ID, agent keys, shape checks | [Transformation and emission](../../architecture/transformation-and-emission.md#opencode) |
| OpenCode reference spelling, sibling-climb respelling, OpenCode ID checks, ledger | [References and linking](../../architecture/references-and-linking.md) |
| Portable-name rule, repository-wide bare-name uniqueness, skill-tool handle authoring | [`curation/SCHEMA.md`](../../../curation/SCHEMA.md) |
| Destination, schema-1 one-off migration, Release pins, Windows bulk-Apply gate | [Distribution and installation](../../architecture/distribution-and-installation.md) |
| Why OpenCode output is namespaced and native | [ADR-0002](../../adr/0002-multi-harness-output.md) |
| Why `manual` means not offered to the model on Codex and OpenCode 2 | [ADR-0005](../../adr/0005-invocation-intent-in-the-manifest.md) |
| Why references are symbols checked in tiers | [ADR-0008](../../adr/0008-references-are-symbols.md) |
| Upstream evidence | [OpenCode 2 as the OpenCode target](../../research/opencode-2-target.md) |

When this document and an owner disagree, the owner wins and this document is corrected. The
implementation plan is
[`docs/superpowers/plans/2026-10-08-opencode-2-target.md`](../plans/2026-10-08-opencode-2-target.md).

## Baseline

- Planning tree: `9442efa` plus the uncommitted OpenCode 2 canon, the new research note, and these
  two documents. Re-resolve the base before execution.
- The committed `opencode/` tree is OpenCode 1 output: 38 commands (14 global-root stubs, 11 `both`
  duplicates, 13 inline `manual` commands), 14 parked `BODY.md` folders, bare skill folders, and two
  agents that receive no `name` key today.
- The estate has 116 ledger items (114 skills, 2 agents, 0 commands) plus one original skill,
  `skills/deniz-dotnet-general/writing-tunit-tests`. After the change the four Bundles hold 115
  skill folders, 0 commands, and 2 agents.
- `plugins/`, `codex/`, `.claude-plugin/marketplace.json`, and `.agents/plugins/marketplace.json`
  must not change in steps 2 and 4. The OpenCode change is OpenCode-only. Step 3 changes them only
  where a curator decision lands (a body patch, a posture, a Module version).

## Goals

- Emit every OpenCode artifact under its `<plugin>.<name>` ID with OpenCode 2 frontmatter.
- Render every own reference in OpenCode spelling, including the `@`/`/` pointer prefix and
  relative sibling climbs.
- Check the new rules deterministically: portable names, bare-name uniqueness across kinds,
  rendered-ID resolution, skill-tool handles, phantom skills, skill `name`, and agent keys.
- Record the OpenCode 2 projection in the ledger.
- Use `OPENCODE_CONFIG_DIR` as the installer Destination when it is set and non-empty.
- Retire every OpenCode 1 shape, check, test, and probe.
- Give the curator one decision packet for the curation pass.
- Produce the measurement records, the one-off profile migration record, and a new Release.

## Non-goals

- Any curation decision. Postures, `as:` changes, body patches, `depends_on` edits, comment and
  reason rewrites, and Module version bumps are the curator's. This document only lists them.
- An OpenCode 1 compatibility layer, a schema-1 Install-state reader, or a migration command.
- An OpenCode 2 plugin package, `skills` config entries, HTTP catalogs, `~/.agents/skills`, skill
  permission rules, or agent permission mapping.
- Upstream sync waves, Iteration 2, and the original-skill declaration surface.
- Any change to Claude Code or Codex output.

## Data flow

The pipeline order in [`buildAll`](../../../tools/build.ts) stays. Only the OpenCode branch and the
shared rewrite map change:

```text
loadManifest x4 -> scanSubmodule -> collectProblems (+ collectIdentityProblems: portable names,
  bare-name uniqueness across kinds and original skills)  -- fail before delete
-> ownSkillIdentities -> deriveModuleRequirements -> buildRewriteMap (one map, three renderers)
-> assembleItems (neutral, unchanged)
-> collectCodexEmissionProblems -> collectOpenCodeEmissionProblems  -- fail before delete
-> rm plugins/ opencode/ codex/ marketplace
-> emitClaudeItem (unchanged) -> emitOpenCode (new shapes) -> emitCodex (unchanged)
-> rewriteTree(plugins, claude) -> rewriteTree(opencode, opencode)
   -> rewriteOpenCodeSiblingClimbs(opencode) -> rewriteTree(codex, codex)
-> finalizeCodexSkillMetadata -> writeOpenCodeManifests (mode path translation) -> writeLedger
```

The OpenCode emitter reads the neutral assembled document, not the Claude-adapted document. Today
`emitOpenCodeSkill` calls `claudeDocument` only to keep its drop report byte-identical with an
older build; that reason ends with this change.

## Component design

### 1. Shared OpenCode target module

Create `tools/lib/opencode-target.ts` beside `tools/lib/codex-plugin.ts`. It holds pure functions
and constants, so `tools/build.ts` stays orchestration and `tools/validate.ts` and
`tools/lib/ledger.ts` reuse the same definitions:

```ts
export const OPENCODE_HIDE_KEY = "opencode/autoinvoke";
export const OPENCODE_AGENT_KEYS: ReadonlySet<string>; // description, mode, model, variant, request,
//   system, permissions, steps, hidden, color, disabled
export const OPENCODE_AGENT_COLOR: RegExp; // /^#[0-9a-fA-F]{6}$/
export function openCodeId(plugin: string, name: string): string; // `${plugin}.${name}`
export function splitOpenCodeId(id: string, plugins: Iterable<string>): { plugin: string; name: string } | undefined;
export function openCodeBundlePath(kind: ComponentType, plugin: string, name: string): string;
//   skill -> skills/<id>   command -> commands/<id>.md   agent -> agents/<id>.md
export interface OpenCodeSkillAdaptation {
  document: ParsedDoc;
  dropped: string[];         // sorted neutral keys not kept, excluding a consumed upstream DMI
  advertised: boolean;       // false when the hide key is written
  transformations: string[]; // e.g. "disable-model-invocation: true -> metadata.opencode/autoinvoke: false"
}
export function adaptOpenCodeSkillDocument(
  id: string, doc: ParsedDoc, invocation: CurationItem["invocation"],
): OpenCodeSkillAdaptation;
export function adaptOpenCodeAgentDocument(doc: ParsedDoc): { document: ParsedDoc; dropped: string[] };
export function adaptOpenCodeCommandDocument(doc: ParsedDoc): { document: ParsedDoc; dropped: string[] };
export function collectOpenCodeEmissionProblems(manifests: CurationManifest[], assembled: AssembledItem[]): string[];
export function claudeCounterpartPath(module: string, bundlePath: string): string | undefined; // section 5
```

`OPENCODE_SKILL_KEYS` moves from `tools/lib/ledger.ts` to this module unchanged in the behavior-neutral
checkpoint, then narrows to `name`, `description`, `metadata` with the document adaptation (plan
Task 5): OpenCode 2 reads only `name`, `description`, `metadata`, and `disable-model-invocation`, so
`license` and `compatibility` are dropped and reported (ADR-0002, ADR-0006; 41 current OpenCode skill
files carry one of them). The ledger and build import it from here.

`PORTABLE_NAME = /^[a-z0-9]+(-[a-z0-9]+)*$/` moves to `tools/lib/resolve.ts` and is exported.
`CODEX_NAME` in `tools/lib/codex-plugin.ts` becomes an import of it; the Codex message text stays.

### 2. Skill frontmatter adaptation

`adaptOpenCodeSkillDocument(id, doc, invocation)`:

1. Copy only `OPENCODE_SKILL_KEYS` from the neutral frontmatter, in source order.
2. Force `name: id` (always, including original skills).
3. Decide hiding:
   - `manual`: hide.
   - `auto`, `both`: do not hide, and remove any upstream `opencode/autoinvoke` key from `metadata`.
   - absent: hide only when the neutral frontmatter has `disable-model-invocation: true`; otherwise
     leave upstream `metadata` exactly as it is, including any upstream hide key.
4. To hide, merge `{ "opencode/autoinvoke": false }` into `metadata`:
   - no `metadata` key: add `metadata` as the last key;
   - `metadata` is a plain object: keep its keys and order, set the hide key last (overwrite an
     existing value in place);
   - `metadata` is present but not a plain object (string, array, null): stop the build with
     `<plugin>/<name>: metadata must be a mapping to carry opencode/autoinvoke`. The check is
     `collectOpenCodeEmissionProblems(manifests, assembled)` in `tools/lib/opencode-target.ts`; it
     runs on the neutral assembled documents right after `collectCodexEmissionProblems`, before any
     delete, and only for items that step 3 would hide.
5. If removing the hide key leaves `metadata` empty, delete `metadata`.
6. `dropped` lists every neutral key outside `OPENCODE_SKILL_KEYS`, sorted. A
   `disable-model-invocation` that step 3 consumed is not a drop; it is listed in `transformations`.
   `user-invocable` is always a drop.

The serializer is the existing `serializeDoc` (`yaml` `stringify`). The expected emitted form is:

```yaml
---
name: deniz-process.handoff
description: ...
metadata:
  opencode/autoinvoke: false
---
```

Report lines (order: emission order, then the existing report order):

- `opencode skill <id>: dropped frontmatter keys: <k1>, <k2>` (as today, keyed by ID);
- `opencode skill <id>: disable-model-invocation: true -> metadata.opencode/autoinvoke: false`.

The `body` passes through unchanged. Rendering happens in the rewrite pass.

### 3. Command and agent adaptation

- Command (`as: command` only): frontmatter is exactly `{ description }`. `dropped` is every other
  neutral key except `name`. The body is unchanged.
- Agent: frontmatter is exactly `{ description, mode: "subagent" }`. `dropped` is every other
  neutral key except `name`. `name` is never written. `color` is never written; if a future curator
  frontmatter sets one, it is dropped and reported, and validation would still catch a hand-made
  invalid value.

### 4. Emitter

Replace `emitOpenCodeSkill` and `emitOpenCode` in `tools/build.ts` with one `emitOpenCode` that,
for each Module and each assembled item sorted by `outName`:

- `outType === "skill"`: copy `item.dir` to `opencode/<m>/skills/<m>.<name>/` and write the adapted
  `SKILL.md`. No other file is added or removed. Original skills (`item.own`) take the same path,
  with absent invocation.
- `outType === "command"`: write `opencode/<m>/commands/<m>.<name>.md`.
- `outType === "agent"`: write `opencode/<m>/agents/<m>.<name>.md`.

Removed outright: the `BODY.md` park, the `SKILL.md` withholding filter, the self-link repointing to
`BODY.md`, the stub command template, the `both` duplicate command, the inline `manual` command, the
husk removal, and the `body parked at` report line.

An item resolved `as: command` with bundled files has nowhere to put them in OpenCode. No such item
exists today. The emitter reports `opencode command <id>: bundled files not emitted: <files>` so the
curation pass sees the cost of an `as: command` choice; the dependency files remain in Claude and
Codex as today.

### 5. Module manifest mode translation

`writeOpenCodeManifests` maps a Bundle path to its Claude Plugin counterpart before the index
lookup. New exported helper in `tools/lib/opencode-target.ts`:

```ts
export function claudeCounterpartPath(module: string, bundlePath: string): string | undefined;
// skills/<module>.<name>/<rest> -> plugins/<module>/skills/<name>/<rest>
// commands/..., agents/..., LICENSE, THIRD_PARTY_NOTICES.md, third_party/... -> undefined
```

Mode rule: `undefined` -> `100644`; otherwise `pluginModes.get(counterpart) ?? "100644"`. A
`skills/` path whose folder does not start with `<module>.` is an internal error (the emitter can
no longer produce one). The seven executable Process files keep `100755`:
`brainstorming/scripts/start-server.sh`, `brainstorming/scripts/stop-server.sh`,
`subagent-driven-development/scripts/review-package`, `.../sdd-workspace`, `.../task-brief`,
`systematic-debugging/find-polluter.sh`, and `writing-skills/render-graphs.js`. Section 1e of
`validateRepo` checks the same paths under the new `opencode/<m>/skills/<m>.<name>/` spelling.

### 6. Identity preflight

In `collectIdentityProblems` (`tools/lib/resolve.ts`):

- Add the portable-name rule for every `plugin.name`, every non-excluded `outName`, and every
  original-skill directory name:
  `<manifestPath>: <plugin>: output name <name> is not portable (^[a-z0-9]+(-[a-z0-9]+)*$)`.
- Replace `claimOpenCodeDestination(kind, ...)` with one repository-wide claim keyed by the bare
  name, whatever its kind, including original skills:
  `duplicate output name <name> from <module> (<source>) and <module> (<source>)`. It reports unless
  both claims are curated items of the same Module: those collisions keep their current owners and
  messages (the per-manifest `duplicate output identity` check for one kind, the Codex flattening
  preflight for two kinds). An original skill against a curated item of its own Module still
  reports here, as the current claim does.
- The OpenCode-specific claims by invocation (`command:` for `manual`, both claims for `both`) go
  away; invocation no longer changes shape.

The current estate has no violation of either rule (measured over the ledger: no duplicate name
across kinds, no non-portable name), so this checkpoint changes no generated byte. The fixture test
`same OpenCode name in different artifact kinds remains legal across Modules` inverts.

### 7. Reference rendering

`tools/lib/rewrite.ts` keeps one map for all three trees. The map value becomes a target, and each
renderer spells it:

```ts
export interface RewriteTarget { plugin: string; name: string; kind: ComponentType }
export function buildRewriteMap(
  manifests: CurationManifest[], components: ComponentInfo[], ownSkills?: OwnSkillIdentity[],
): Map<string, RewriteTarget>;
export function rewriteRefs(content: string, map: Map<string, RewriteTarget>, style: RefStyle): string;
```

`kind` is the target's resolved output kind (`item.as ?? comp.type ?? "skill"`; original skills are
`skill`). The duplicate-identity check for original skills compares `plugin` and `name`.

| Style | Model-edge `ns:name` | User-pointer `/ns:name` |
|---|---|---|
| `claude` | `<plugin>:<name>` | `/<plugin>:<name>` (slash kept) |
| `codex` | `$<plugin>:<name>` | `$<plugin>:<name>` (slash consumed) |
| `opencode` | `<plugin>.<name>` | slash consumed, then `@<plugin>.<name>` for skill and agent targets, `/<plugin>.<name>` for command targets |

The rendered dotted form contains no `:`, so `scanRefs` cannot re-detect it.

New function for the OpenCode tree only:

```ts
export function rewriteOpenCodeSiblingClimbs(
  content: string, depthBelowSkillFolder: number, skillIds: Map<string, string>,
): string;
```

- `skillIds` maps every emitted bare skill name (all Modules, original skills included) to its ID.
- A match is `((?:\.\./)+)(<name>)(?=/)`, where the character before the first `.` is not one of
  `[A-Za-z0-9._/-]`, `<name>` is a key of `skillIds`, and the number of `../` equals
  `depthBelowSkillFolder + 1`, so the climb lands exactly on the shared `skills/` directory of the
  installed layout. The segment becomes the ID; the rest of the text is unchanged.
- It applies to Markdown link targets and to the same climb in prose or code (for example the DOT
  labels in `subagent-driven-development`). A climb of the wrong depth, or to a name that is not an
  emitted skill (a command, an agent, `../src/Api`), is left unchanged; the linker reports a broken
  one.
- A climb into the item's own folder is respelled too, because the folder itself is renamed.
- `commands/` and `agents/` files are not rewritten: their climbs never land on `skills/`.

`build.ts` adds `rewriteOpenCodeTree(root, rewriteMap, skillIds)`, which walks
`opencode/<m>/skills/<id>/**/*.md`, computes the depth from the folder, and applies
`rewriteRefs(..., "opencode")` and then `rewriteOpenCodeSiblingClimbs`. Command and agent files get
only `rewriteRefs`.

Measured at `9442efa`: 34 Markdown link climbs and at least two prose climbs in OpenCode output
name emitted skills, each inside its own Module (Aspire and Process); none crosses Modules. The
plan's regeneration task counts the respelled occurrences and compares them with a fresh grep.

### 8. Ledger

`writeLedger` (`tools/lib/ledger.ts`) changes only the `opencode` projection:

```ts
opencode: {
  artifacts: ("skill" | "command" | "agent")[];   // probed at the ID paths
  identity: string;                               // "<plugin>.<name>"
  advertised?: boolean;                           // skills only; false when the hide key is present
  edges: Record<RefKind, string[]>;               // model: "<p>.<n>"; pointer: "@<p>.<n>" or "/<p>.<n>"
  dropped: string[];                              // from adaptOpenCode*Document on the neutral doc
  metadataTransformations?: string[];             // from the same adaptation
}
```

`parked` is removed. The edge respelling uses the known neutral facts plus a name-to-kind lookup
built from the resolved items and original skills; it never parses rendered text. The ledger key
stays `<plugin>/<kind>/<bare-name>`. `docs/agents/reference-audit-playbook.md` reads these paths,
so its collection script changes in the same task (ID paths, no `entry.opencode.parked`).

### 9. Validation

All changes are in `validateRepo` (`tools/validate.ts`); new checks take an `O` prefix. Helpers:

- `openCodeArtifact(root, module, kind, name)` builds the ID path through `openCodeBundlePath`.
- `openCodeIndex(root, manifests, components)` returns, for every emitted ID: `plugin`, bare
  `name`, resolved `kind`, curation `invocation` (absent for original skills), and whether the
  emitted `SKILL.md` carries the hide key.
- `scanOpenCodeIds(text, plugins)` returns rendered-ID tokens with their prefix (`""`, `"@"`, `"/"`),
  line, and column. A token is `([@/]?)(<plugin-alternation>)\.([a-z0-9]+(?:-[a-z0-9]+)*)` where:
  - it is not followed by `[A-Za-z0-9_-]` or `/` (a following `/` makes it a path segment);
  - with prefix `/`, the character before the slash is not `[A-Za-z0-9._/-]` (otherwise it is a path);
  - with no prefix, the character before is not `[A-Za-z0-9._/@-]`.
  Path segments are left to the path rules below.

Checks:

- **L4 (kept).** No own `namespace:name` survives in `opencode/`.
- **O1 rendered IDs resolve.** Every token names an emitted ID. `@` requires a skill or agent
  target; `/` requires a command target. Message:
  `<file>:<line>: rendered OpenCode ID <token> does not name an emitted <kind>`.
- **O2 skill-tool handles resolve.** In every `opencode/` Markdown line, each quoted handle captured
  by `/Skill tool(?: twice,)? (?:with|for) ("[^"\n]+"(?:,? (?:and|or) "[^"\n]+")*)/gi` must equal an
  emitted OpenCode skill ID. This pattern covers the three measured forms (`with "x"`,
  `twice, for "x" and "y"`, `for "x"`). Other phrasings are not detected; that is a stated limit,
  not a claim of completeness. Message:
  `<file>:<line>: skill-tool handle "<handle>" is not an emitted OpenCode skill ID — author it as a namespaced fact with a matching depends_on`.
- **O4 phantom skills.** Under each `opencode/<m>/skills/`: any `.md` file directly in `skills/`,
  and any `SKILL.md` below `skills/<id>/` other than `skills/<id>/SKILL.md`, is an error.
- **O5 skill identity.** Each `skills/<id>/SKILL.md` has frontmatter `name` equal to `<id>`, and
  `<id>` is `<m>.<bare>` for its own Module `m`. This guards the emitter's `name` = ID rule in
  [Transformation and emission](../../architecture/transformation-and-emission.md#opencode).
- **O6 agent frontmatter.** Each `agents/*.md` frontmatter key is in `OPENCODE_AGENT_KEYS`, and a
  present `color` matches `OPENCODE_AGENT_COLOR`.
- **Linker target state.** `TargetState` becomes `{ modelReachClaude, userReachClaude, ocModel,
  ocUser }`. `ocModel` is an emitted OpenCode skill without the hide key. `ocUser` is any emitted
  skill, agent, or command. Cause texts become `hidden from the OpenCode model (opencode/autoinvoke)`
  and `no OpenCode artifact`.
- **Path rules L8, R1, R2.** In the `opencode` tree, `owningItem` reads the folder ID and maps it to
  the bare name. `skillsOf(name)` returns `opencode/<owner>/skills/<owner>.<name>`. Before
  `existsSync`, a resolved path of the form `opencode/<m>/skills/<m2>.<x>/...` with `m2 != m` is
  re-rooted to `opencode/<m2>/skills/<m2>.<x>/...`, because the installed layout shares one
  `skills/` directory across Modules.
- **Retired.** L6 (parked files), the R1 `soundInSkillTree` converted-command warning, the R2
  `designed` manual-withholding warning, and the unescaped `skills/${name}/` regex they used.

### 10. Installer Destination

`resolveDestination(env, home)` in `tools/lib/opencode-install-state.ts`:

```ts
if (nonEmpty(env.OPENCODE_CONFIG_DIR)) {
  if (!isAbsolute(env.OPENCODE_CONFIG_DIR)) {
    throw new Error("OPENCODE_CONFIG_DIR must be an absolute path");
  }
  return env.OPENCODE_CONFIG_DIR;
}
if (nonEmpty(env.XDG_CONFIG_HOME)) return join(env.XDG_CONFIG_HOME, "opencode");
// <home>/.config/opencode, home source per decision Q6 below
```

- Set and non-empty wins, as canon states. Empty or unset falls through. The POSIX `??` divergence
  for an empty value (research section 2, inferred) is measured on the Linux host in the
  measurement step, not changed here.
- A relative value is refused because the installer cannot know the working directory of the
  OpenCode process that will read it. This is a new refusal; it is listed for curator review (Q7).
- No path normalization beyond what `join` does elsewhere; the topology checks in Apply still run.
- `status` and Plan output already print the Destination; no text change is needed beyond the
  removed refusal.

### 11. Tests that change

| File | Change |
|---|---|
| `tools/lib/opencode-target.test.ts` (new) | adaptation matrix, metadata merge cases, agent and command keys, ID and path helpers, counterpart paths |
| `tools/lib/rewrite.test.ts` | object map values (every test that reads a value, passes `style` as the third argument, or hand-builds a string map); OpenCode model, `@`, `/` rendering; sibling climbs by depth; Claude and Codex unchanged |
| `tools/lib/resolve` tests in `tools/build.test.ts` | portable names, cross-kind duplicate names, original-skill duplicates |
| `tools/build.test.ts` | replace the park/stub/duplicate tests (`invocation sets the Claude flags and picks the OpenCode artifact`, `a bundled manual command parks...`, `a bundle-less manual conversion...`, `both preserves...`, `manual bundle links repoint...`, `only a manual conversion reports parked files`, `same OpenCode name in different artifact kinds...`) with ID-path tests; mode translation; agent keys |
| `tools/validate.test.ts` | O1, O2, O4–O6, linker causes, L6 test (741) and converted-command warning test (944) removed, path rules across Modules, identity messages (170, 775) |
| `tools/lib/ledger.test.ts` | new `opencode` projection, no `parked` |
| `tools/lib/opencode-install-state.test.ts`, `tools/install-opencode.test.ts` | `OPENCODE_CONFIG_DIR` used, relative refused, empty falls through |
| `tools/repository-docs.test.ts` | retarget the `lab.ps1` assertion when the OpenCode lab is ported; Release pins at the Release step only |

## Generated-output expectation (end of step 2)

- `opencode/`: every skill folder renamed to its ID, 27 `manual` skills gain the hide key, 14
  `BODY.md` files and all 38 commands disappear, 2 agents renamed, own references respelled, all
  four `manifest.json` file maps and digests change.
- `docs/ledger.json`: only `opencode` projections change.
- `dist/`: the compiled installer changes with the Destination rule.
- `plugins/`, `codex/`, `.claude-plugin/marketplace.json`, `.agents/plugins/marketplace.json`,
  `docs/inventory.md`: no change.
- `npm run validate` reports exactly the curation-owned findings listed in the plan's decision
  packet (16 skill-tool handles on 12 lines) and nothing else new. Those findings clear only
  through the curation pass.

## Merge sequencing

O2 fails on the current curation, and only the curator can clear it. Recommendation: steps 2 and 3
share one branch; it merges to `master` only when `npm run validate` is clean, so `master` CI never
sees the known findings. The rejected alternative is landing O2 as a warning first and raising it to
an error later, which leaves a window in which a new unresolvable handle passes CI.

## Edge cases

- **Metadata merge.** Covered in section 2: absent `metadata`, mapping `metadata`, non-mapping
  `metadata` (preflight stop), upstream hide key under `auto`/`both` (removed), upstream hide key
  under absent (kept), empty mapping after removal (deleted).
- **Absent item with upstream DMI.** Hidden through the metadata key. The estate has no such item at
  `9442efa` (no absent ledger item carries a Claude invocation flag). See Q1.
- **Exec-bit translation.** Section 5. A Windows checkout reads modes from the Git index, so the
  translation must be tested with a staged `100755` counterpart.
- **Relative sibling links.** Section 7. Depth-checked, own folder included, commands and agents
  excluded, cross-Module climbs re-rooted by the linker.
- **`name` = ID.** Section 2 and O5, including original skills.
- **Agent native keys.** Section 3 and O6.
- **Phantom guard.** O4. `skills/deniz-dotnet-general/NOTICE.md` in the authored original-skill root
  is not copied into output (`ownSkillIdentities` lists directories only); O4 would catch it if that
  changed.
- **Skill-tool handles.** O2 accepts only an emitted skill ID; an agent or command ID in a handle is
  an error. Codex renders a promoted handle as `"$deniz-process:grilling"`, which Codex has no skill
  tool for; the wording is a curation question in the packet.
- **`OPENCODE_CONFIG_DIR` empty vs unset.** Section 10.
- **Fuzzy ranking and `AGENTS.md`.** Runtime limits already in canon; nothing to implement.

## Open questions for the curator

These are not settled by D1–D7. Each has a recommendation; none is decided by this document.

- **Q1. Absent items with upstream DMI.** Recommend: keep canon as is and revisit only if such an
  item appears; add a build report line so one cannot appear unnoticed.
- **Q2. Iteration 2 example.** Resolved by canon, not a curator question: text may name a `manual`
  item's ID, so the example's `generate-testability-wrappers` user-pointer needs no special OpenCode
  rendering.
- **Q3. Empty `OPENCODE_CONFIG_DIR` on POSIX.** Recommend: measure on the Linux host (plan
  measurement task); keep D5's "set and non-empty" unless the measurement shows OpenCode uses an
  empty root.
- **Q4. `license` and `compatibility` in OpenCode skills.** Resolved by canon, not a curator question:
  OpenCode 2 ignores both, and ADR-0002/ADR-0006 forbid silently emitting keys a target ignores, so
  they are dropped and reported (Task 5).
- **Q5. Path climbs into a `manual` item.** Resolved by canon, not a curator question: a `manual`
  item may be named, so the climb raises no invocation concern. The one `executing-plans` line,
  `../using-superpowers/references/`, points into a folder whose `references/**` is omitted; that
  is a path matter for the curation pass.
- **Q6. Home source for the fallback Destination.** The installer uses `HOME`, then `USERPROFILE`,
  then `os.homedir()`; OpenCode 2 uses `os.homedir()` (`OPENCODE_TEST_HOME` aside). Options:
  (A) keep the installer order; (B) use `os.homedir()` only. Recommend B, because the Destination
  must equal the root OpenCode reads; tests already set `USERPROFILE` and `HOME`, which
  `os.homedir()` honors on Windows and POSIX respectively. B changes two places:
  `tools/install-opencode.ts` (`io.home`) and `resolveDestination`, which today prefers `env.HOME`
  over its `home` argument.
- **Q7. Relative `OPENCODE_CONFIG_DIR`.** Recommend refusing it (section 10).
- **Q8. Merge sequencing.** Recommend one branch for steps 2 and 3 (section above).
- **Q9. Release version.** `package.json` is `0.3.0` and names the asset. Recommend `0.4.0` and tag
  `installer-v0.4.0`, because Bundle paths and the Destination rule change incompatibly.
- **Q10. Module versions.** Every Module's OpenCode bytes change. Bumping `plugin.version` also
  changes the Claude and Codex Plugin manifests. Curation decision; listed in the packet.

## Verification

Per [quality gates](../../engineering/quality-gates.md): the tooling row for each code task, the
generated-output row with the idempotence rerun for the regeneration task, the experiment selftest
row for experiment scripts, and the Release row for the Package. Runtime claims need the committed
records described in the plan; a green `validate` proves existence and audience reachability only.

## Closeout

When steps 2 to 5 close: remove the corresponding ROADMAP Known Gaps and Next Up items, move any
durable statement here to its owner, and delete this specification and the plan. The research note
and the experiment records stay.
