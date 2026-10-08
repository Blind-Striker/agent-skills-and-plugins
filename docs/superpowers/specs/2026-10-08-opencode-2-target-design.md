# OpenCode 2 target design

Date: 2026-10-09

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
- **O2 skill-tool handles resolve** (retired in W0 by the skill-tool call template on the canonical
  tree, section 13). In every `opencode/` Markdown line, each quoted handle captured
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

## W0 correctness

A green `validate` after step 2 did not mean OpenCode 2 worked: six runtime breaks remained at the
current pins. The curator decided on 2026-10-08 to close them on this branch before the merge,
with the smallest mechanism that keeps each one from returning silently. Canon now states the
rules: [References and linking](../../architecture/references-and-linking.md) (harness phrasing,
handoff templates, harness vocabulary check, path claims), [ADR-0008](../../adr/0008-references-are-symbols.md)
(why), and [`curation/SCHEMA.md`](../../../curation/SCHEMA.md#dependencies) (authoring). This
section is the execution design; the plan's Phase B2 is its task list.

### Curator decisions (settled 2026-10-08)

1. **X1, per-harness load rendering.** A load-bearing "load this skill" sentence is written
   natively per harness: Claude `Skill tool` plus `deniz-process:x`, OpenCode 2 `skill` tool plus
   `deniz-process.x`, Codex `$deniz-process:x` with Codex wording. Its second consumer is the
   Claude-only subagent-dispatch vocabulary.
2. **Close the six breaks before the merge**, all at the current pins, no submodule moves:
   (a) `brainstorming` names `skills/brainstorming/visual-companion.md`, whose OpenCode folder is
   `deniz-process.brainstorming`; (b) `executing-plans` names the omitted
   `../using-superpowers/references/`; (c) load-bearing bare handoffs in the dotnet-test cluster and
   `check-bin-obj-clash`; (d) `ask-deniz`'s bare route to `resolving-merge-conflicts`, made a
   checked fact so its later upstream deletion is caught; (e) generic Skill-tool prose in `handoff`
   and `wayfinder`; (f) Claude-only dispatch words in OpenCode. Plus the 16 bare skill-tool handles.
3. **X2 and X3**, only as much as makes (a)–(f) impossible to reintroduce silently.
4. **`writing-for-agents`**: narrower description, `invocation: both`, reason beside the item.
5. **`teach` and `handoff` stay skills**; every `manual` and `both` item stays a skill (no
   `as: command`).
6. **Module versions**, minor everywhere: Process 0.6.0 → 0.7.0, General 0.9.1 → 0.10.0,
   Akka 0.3.1 → 0.4.0, Aspire 0.3.3 → 0.4.0. Canon check
   ([workflow](../../engineering/workflow.md#bump-the-module-version-with-the-bytes)): Process changes
   `invocation` and `depends_on`, General changes `depends_on`, and every Module's OpenCode artifact
   names moved in step 2, which is a rename of the surface. Nothing in W0 lowers or raises a level.
7. **Stale comments** (plan Appendix A6) are refreshed, and the ROADMAP's "12 handles" becomes "12
   lines, 16 handles".

### Design judgement

Three designs were scored on correctness in all three harnesses, checkability, minimality, author
ergonomics, and risk (1–5 each):

| Design | Correct | Checkable | Minimal | Ergonomic | Risk | Total |
|---|---|---|---|---|---|---|
| Phrase templates over upstream wording, no new syntax | 4 | 4 | 4 | 5 | 4 | **21** |
| Explicit `{{@load ns:x}}` constructs, `retired:` manifest key, ledger `bareHandoffs` | 4 | 5 | 2 | 2 | 3 | 16 |
| Frames over upstream wording, wider clause-start detection, ledger `bodyTransformations` | 4 | 4 | 3 | 5 | 3 | 19 |

The phrase-template design wins. Grafted from the others:

- the OpenCode sentence names the backticked `` `skill` `` tool, and O2 retires: the payload rule on
  the canonical tree, the linker, and O1 already prove every handle, so one rule owns it;
- a harness vocabulary check on `opencode/` and `codex/`, whose patterns live beside the
  rendering tables, with a test that every non-Claude rendering passes them;
- every skill-tool call span renders whatever its payload, so a bare handle is reported once, on the
  canonical tree, and not again as leaked vocabulary;
- Codex dispatch wording names no tool and no agent type, because neither is recorded in this
  repository's research;
- one path scanner in `refs.ts`, moved behavior-neutrally first; a text-file test that also requires
  a lossless UTF-8 round trip; the manual-folder rule judged from the Claude tree's
  `disable-model-invocation`, the state the linker already reads, and applied to Markdown links too.

Rejected: a body marker and reserved sigil; a `retired:` manifest key (sync infrastructure owns
deletions); ledger projections of heuristic hits; detecting ``Use `x` `` without the word `skill`
(13 routing hints in when-not-to-use lists that canon keeps as candidates); a single-handle
`for "x"` form (absent from the estate, so it fails closed); a separate "bare name in OpenCode
path" rule (the fail-closed resolution already reports it); `Subagent (default)` for Codex (agent
type not in repository research).

### 12. Harness phrasing (X1)

**Grammar** (`tools/lib/refs.ts`, the one owner for rewriting and validation):

```ts
export interface SkillToolCall {
  index: number;                 // start of the verb
  end: number;                   // one past the span
  verb: "Call" | "call" | "calls" | "calling";
  form: "with" | "twice" | "generic";
  payloads: { index: number; text: string }[]; // quoted handles, quotes excluded
}
export function scanSkillToolCalls(content: string): SkillToolCall[];
/** Indexes of every case-insensitive `skill tool` that no call span covers. */
export function straySkillToolMentions(content: string): number[];

const SKILL_TOOL_CALL =
  /\b(Call|call|calls|calling) the Skill tool(?: with "([^"\n]*)"| twice, for "([^"\n]*)" and "([^"\n]*)"| for(?![A-Za-z]| "))/g;
const SKILL_TOOL_MENTION = /\bskill tool/gi;
```

**Rendering** (`tools/lib/rewrite.ts`):

```ts
export function renderHarnessPhrasing(content: string, style: RefStyle): string; // claude: identity
export function localize(content: string, map: Map<string, RewriteTarget>, style: RefStyle): string;
// = rewriteRefs(renderHarnessPhrasing(content, style), map, style)
export const CLAUDE_ONLY_VOCABULARY: readonly RegExp[];
export function claudeOnlyVocabulary(content: string): { index: number; match: string }[];
```

- Skill-tool call spans render first, then the dispatch table, then `rewriteRefs` renders the facts,
  which the phrasing step leaves neutral. A span renders whatever its payload is; the payload rule
  belongs to validation (section 13).
- OpenCode: inside each span, `the Skill tool` becomes ``the `skill` tool``; verbs and quoted
  payloads stay.
- Codex: the verb maps `Call`→`Invoke`, `call`→`invoke`, `calls`→`invokes`,
  `calling`→`invoking`. `with "p"` becomes `` <Verb> `p` ``; `twice, for "p" and "q"` becomes
  `` <Verb> `p` and `q` ``; the generic form becomes `<Verb>` alone. `rewriteRefs` then renders a
  fact inside the backticks as `$plugin:name`.
- Dispatch table, matched exactly, for OpenCode and Codex:

| Pattern | OpenCode 2 | Codex |
|---|---|---|
| `/Subagent \(general-purpose\)/g` | `Subagent (general)` | `Subagent` |
| ``/`general-purpose`(\s+)subagent/g`` | `` `general`$1subagent `` | `subagent` |
| ``/`Agent`( calls?)\b/g`` | `` `subagent`$1 `` | `subagent$1` |

- `CLAUDE_ONLY_VOCABULARY`: `/\bskill tool/gi`, `/Subagent \(general-purpose\)/g`,
  ``/`general-purpose`/g``, `/\bgeneral-purpose\s+(?:sub)?agent\b/g`, ``/`Agent`/g``,
  `/\bAgent tool\b/g`, `/\bTask tool\b/g`, `/\bsubagent_type\b/g`. Measured on `opencode/` and
  `codex/` at `7c1f642`: the only hits are the 13 Skill-tool lines and the 11 dispatch lines per tree;
  the two prose uses of "general-purpose" do not match.
- **Build.** `rewriteTree` (Claude and Codex) and `rewriteOpenCodeTree` call `localize` where they
  call `rewriteRefs` today, on Markdown only. The ledger does not change: edges still come from the
  neutral facts.

Rendered at the current pins (after curation promotes the handles):

| Site | OpenCode 2 | Codex |
|---|---|---|
| `grill-me:7` | ``Call the `skill` tool with "deniz-process.grilling".`` | ``Invoke `$deniz-process:grilling`.`` |
| `wayfinder:79` | ``…a subagent that calls the `skill` tool with "deniz-process.research".`` | ``…a subagent that invokes `$deniz-process:research`.`` |
| `wayfinder:126` | ``…call the `skill` tool for whichever skills the `## Notes` block names. If in doubt, call the `skill` tool twice, for "deniz-process.grilling" and "deniz-process.domain-modeling".`` | ``…invoke whichever skills the `## Notes` block names. If in doubt, invoke `$deniz-process:grilling` and `$deniz-process:domain-modeling`.`` |
| `handoff:11` | ``…the next agent should call the `skill` tool for.`` | `…the next agent should invoke.` |
| `requesting-code-review/SKILL.md:84-85` | ``dispatch a `general`↵subagent`` | `dispatch a subagent` |
| `requesting-code-review/SKILL.md:91` | ``two `subagent` calls`` | `two subagent calls` |
| 9 prompt-template labels | `Subagent (general):` | `Subagent:` |

### 13. Handoff templates and the vocabulary check (X2)

**Grammar** (`tools/lib/refs.ts`):

```ts
export interface Handoff { template: "imperative" | "load" | "route"; name: string; index: number }
export function scanHandoffs(content: string): Handoff[]; // index = the name's opening backtick

const NAME = String.raw`\x60([a-z][a-z0-9]*(?:-[a-z0-9]+)*)\x60`;
const SENTENCE = String.raw`(?:[^.;:!?|\n]|\n(?![ \t]*(?:\n|[-*+>#|]|\d+\.)))*?`;
const IMPERATIVE = new RegExp(String.raw`\b(?:Load|load|Use|use|Follow|follow|Invoke|invoke|Call|call)\b${SENTENCE}${NAME}\s+skill\b`, "g");
const LOAD = new RegExp(String.raw`\b(?:Load|load|Invoke|invoke)\s+${NAME}`, "g");
const LIST_TAIL = new RegExp(String.raw`^(?:,\s*|\s+and\s+|\s+or\s+)${NAME}`); // repeated after a LOAD hit
const ROUTE = new RegExp(String.raw`→\s*\*{0,2}${NAME}`, "g");
```

**Check** (`tools/validate.ts`, new section after the linker, over every Markdown file in
`plugins/`, where facts carry their own-namespace spelling):

- H1 skill-tool call. For each span, each payload must be exactly one `scanRefs` model fact whose
  address is the whole payload and whose namespace is an own Module; otherwise
  `<file>:<line>: skill-tool handle "<p>" is not a namespaced fact — author it as "ns:<p>" with a matching depends_on`.
  For each `straySkillToolMentions` index:
  `<file>:<line>: "skill tool" outside a recognized skill-tool call — reword it into a form in curation/SCHEMA.md Dependencies`.
- H2–H4 for each `scanHandoffs` hit whose name is an estate name:
  `<file>:<line>: load-bearing handoff names \`<name>\` bare — author it as ns:<name> with depends_on, or /ns:<name> if the human is the audience`;
  when the name is not emitted (not a linker target):
  `<file>:<line>: load-bearing handoff names \`<name>\`, which this estate does not emit — reroute or remove it`.
- Estate names: `addressOf(c)` and `c.name` for every scanned component (export `addressOf` from
  `rewrite.ts`), `resolveItem(...).outName` for every manifest item including excluded ones, and
  every `ownSkillIdentities` name. Emitted names are the linker's `targetState` keys.
- O2 retires: `SKILL_TOOL_HANDLE`, `skillToolHandles`, and its loop leave `tools/validate.ts`; the
  phrasing tests in `tools/lib/refs.test.ts` absorb its unit test.
- V harness vocabulary: every Markdown file under `opencode/` and `codex/`, each
  `claudeOnlyVocabulary` hit:
  `<file>:<line>: Claude-only vocabulary "<match>" in <tree>/ — reword it into a form the harness phrasing renders (curation/SCHEMA.md Dependencies)`.

Measured at `7c1f642` over `plugins/` (probe of exactly this grammar; a line is the line of the name):

| Template | Hits |
|---|---|
| H1 payload | 16 handles on 12 lines: `grill-me:7`; `grill-with-docs:8` (2); `improve-codebase-architecture:14, 65, 67, 72`; `wayfinder:79, 80, 81` (2), `113` (2), `117, 126` (2) |
| H1 stray | none: `handoff:11` and `wayfinder:126` match the generic form |
| H2 imperative | `build-perf-baseline:398` (build-perf-diagnostics); `check-bin-obj-clash:47` (binlog-generation); `code-testing-agent:60` (run-tests); `msbuild-antipatterns/references/additional-antipatterns.md:187, 245` (check-bin-obj-clash); `mtp-hot-reload:60` (platform-detection, split by a newline); `test-anti-patterns:20, 53` (test-analysis-extensions). Silent: `aspire-orchestration:133` and `references/agent-workflows.md:96` (`dotnet-inspect`, outside the estate) |
| H3 load | `aspire-init/references/init-workflow.md:126` (aspireify, "Re-invoke"); `code-testing-agent:187` (test-gap-analysis, test-anti-patterns); `run-tests:36, 173` (filter-syntax), `:64` (platform-detection); `test-gap-analysis:43` and `references/mutation-catalog.md:31` (test-analysis-extensions; the verb ends the line before) |
| H4 route | `ask-deniz:86` (systematic-debugging), `:97` (resolving-merge-conflicts). Silent: Aspire arrows naming `azure-diagnostics`, `docker`, `kubectl` |

### 14. Path integrity (X3)

**Grammar** (`tools/lib/refs.ts`; the `CLIMB` regex moves here from `rewrite.ts`):

```ts
export interface PathClaim {
  kind: "climb" | "item-root";
  index: number;        // first character of the claim
  segmentIndex: number; // first character of the segment
  segment: string;
  path: string;         // read to whitespace, quote, backtick, ")" or "]"; one trailing "." dropped
}
/** Landing climbs (needs depth) and item-root paths. */
export function scanPathClaims(content: string, depthBelowSkillFolder: number): PathClaim[];
```

- Landing climb: `((?:\.\./)+)([a-z0-9]+(?:-[a-z0-9]+)*)(?=/)`, no `[A-Za-z0-9._/-]` before it, and
  `../` count = depth + 1.
- Item-root: `skills/([a-z0-9]+(?:-[a-z0-9]+)*)/`, no `[A-Za-z0-9._/~$-]` before it.

**Text files** (`tools/lib/rewrite.ts`): `isBundledText(bytes: Buffer): boolean` is true when the
bytes hold no NUL and `Buffer.from(bytes.toString("utf8"), "utf8").equals(bytes)`.

**Build.** `rewriteOpenCodePaths(content, depthBelowSkillFolder, skillIds)` replaces
`rewriteOpenCodeSiblingClimbs`: both claim kinds whose segment is a key of `skillIds` get the ID
segment. `rewriteOpenCodeTree` walks every file of every `skills/<id>/`: a `.md` file gets
`localize` then `rewriteOpenCodePaths`; any other file that `isBundledText` accepts gets only
`rewriteOpenCodePaths`; a file is written only when its text changed. The Module manifest keeps
taking modes from the Claude counterpart, so a respelled `100755` script keeps its mode.

**Check** (`tools/validate.ts`, rule P in the L8 section, every bundled text file of every skill
folder in `plugins/`, `opencode/`, and `codex/`; the segment is read back with `bareName`):

- P1 fail closed: a landing climb's full path must exist (OpenCode through `reRootOpenCode`; a
  trailing `/` needs a directory):
  `<file>:<line>: sibling path <path> does not resolve in <tree>/ — the target item was renamed, excluded, omitted, or never existed`.
- P2 item-root: when the bare segment is an estate name, the path must exist under the tree's skills
  root (`plugins/<p>/`, `codex/<p>/`, or `opencode/<m>/` then `reRootOpenCode`):
  `<file>:<line>: item path <path> does not resolve in <tree>/`.
- P3 manual folder: a claim whose bare segment names another item with
  `modelReachClaude === false` in the linker's `targetState`, and a Markdown link that resolves into
  such an item's folder:
  `<file>:<line>: path <path> lands in manual item <name>'s folder — a path is a read that bypasses Claude's model-invocation block; point the human with /ns:<name> instead`.
- R1 skips a Markdown link that is a landing climb, because P judged it; R1's other branches and R2
  stay.

Measured at `7c1f642`: every landing climb resolves except `executing-plans:15`
`../using-superpowers/references/` (P1 and P3, in all three trees); `brainstorming:252`
`skills/brainstorming/visual-companion.md` resolves in Claude and Codex and fails P2 in OpenCode
until the respelling lands; `writing-skills` `skills/path/` and `skills/testing/` name no estate
item; no Markdown link lands in a manual item's folder; no non-Markdown bundled file holds a claim.

### 15. W0 curation application

All edits at the current pins, through the existing mechanisms. A new patch uses the two-pass
`eject --patch`. Extending a patch re-cuts it: save the old patch, run
`eject --patch --force` (which deletes the old patch and lays a pristine working copy), reapply the
old patch with `git apply`, add the new edit, run `eject --patch` again. A full overlay
(`ask-deniz`) is edited in place; its lock stamps upstream bytes, not overlay text, so no re-bless.
Namespaces are the scanned plugin names: `mattpocock-skills`, `superpowers`, `dotnet-test`,
`dotnet-msbuild`, `aspire`.

| Item (Module) | Mechanism | Edit | `depends_on` after |
|---|---|---|---|
| grill-me (Process) | new patch | `:7` `"grilling"` → `"mattpocock-skills:grilling"` | [grilling] |
| grill-with-docs (Process) | new patch | `:8` both handles namespaced | [domain-modeling, grilling] |
| improve-codebase-architecture (Process) | extend patch | `:14`, `:72` codebase-design; `:65` grilling; `:67` domain-modeling | [codebase-design, domain-modeling, grilling] |
| wayfinder (Process) | extend patch | `:79`, `:117` research; `:80` prototype; `:81`, `:113`, `:126` grilling + domain-modeling | [domain-modeling, grilling, prototype, research] |
| executing-plans (Process) | new patch | `:15` drop "; see the per-platform tool refs in `../using-superpowers/references/`" | unchanged |
| ask-deniz (Process) | overlay edit | overlay `:84` → `` `superpowers:systematic-debugging` ``; `:95` → `` `mattpocock-skills:resolving-merge-conflicts` `` | [resolving-merge-conflicts, systematic-debugging] |
| writing-for-agents (Process) | new patch + `invocation: both` | SKILL.md frontmatter `description` narrowed | none |
| run-tests (General) | new patch | `:36`, `:173` `` `dotnet-test:filter-syntax` ``; `:64` `` `dotnet-test:platform-detection` `` | [filter-syntax, platform-detection] |
| mtp-hot-reload (General) | extend patch | `:60` `` `dotnet-test:platform-detection` `` | [platform-detection] |
| check-bin-obj-clash (General) | extend patch | `:47` `` `dotnet-msbuild:binlog-generation` `` | [binlog-generation] |
| test-anti-patterns (General) | extend patch | `:20`, `:53` `` `dotnet-test:test-analysis-extensions` `` | [test-analysis-extensions] |
| test-gap-analysis (General) | extend patch, new target `references/mutation-catalog.md` | `:42-43` and catalog `:30-31` `` `dotnet-test:test-analysis-extensions` `` | [test-analysis-extensions] |
| code-testing-agent (General) | extend patch | `:60` `` `dotnet-test:run-tests` ``; `:187` `` `dotnet-test:test-gap-analysis` `` and `` `dotnet-test:test-anti-patterns` `` | [run-tests, test-anti-patterns, test-gap-analysis, writing-tunit-tests] |
| build-perf-baseline (General) | new patch | `:398` `` `dotnet-msbuild:build-perf-diagnostics` `` | [build-perf-diagnostics] |
| msbuild-antipatterns (General) | new patch on `references/additional-antipatterns.md` | `:187`, `:245` `` `dotnet-msbuild:check-bin-obj-clash` `` | [check-bin-obj-clash] |
| aspire-init (Aspire) | extend patch (`references/init-workflow.md` already a target) | `:126` `` Re-invoke `aspire:aspireify` `` | unchanged |

Line numbers are `plugins/` lines at `7c1f642`; an overlay or upstream line can differ by the
frontmatter. Every target is `auto` or `both`, so each new fact is model-reachable; a `manual`
source may hold model edges. No edit crosses a Module, so `requiredModules` do not change.
Breaks (a), (e), and (f) need no curation: X3 respells (a), X1 renders (e) and (f).

Comments and reasons (no names or dates in the curation layer): the why beside every changed item;
the General header's reference-posture paragraph (load forms are facts, "see" mentions stay
candidates); `test-anti-patterns`' "sibling references preserve the upstream bare style";
`test-gap-analysis`' "the bundled mutation catalog flows by copy"; `writing-for-agents` (both, so
retro can load it later; the narrowed trigger keeps routine canon edits from firing it; it no
longer shares `writing-skills`' manual posture); `teach` and `handoff` (stay skills); Appendix A6:
`curation/deniz-dotnet-akka.yaml` agent comment, `curation/deniz-dotnet-aspire.yaml` "both
harnesses", `analyzing-dotnet-performance` reason, `writing-skills` husk sentence.

### 16. W0 generated-output expectation

- `plugins/`: the promoted facts in Claude spelling, the `executing-plans` clause gone,
  `writing-for-agents` without `disable-model-invocation` and with its new description, and four
  `plugin.json` versions. No phrasing change: Claude rendering is identity.
- `codex/`: the same facts, the rendered skill-tool sentences and dispatch words, `writing-for-agents`
  without its `allow_implicit_invocation: false` policy, versions.
- `opencode/`: the same, plus `skills/deniz-process.brainstorming/visual-companion.md`, 26 hidden
  skills (27 minus `writing-for-agents`), and the four `manifest.json` versions and digests.
- `docs/ledger.json`: the new `depends_on` and model edges in all three spellings, `body: patch` on
  the newly patched items, and `writing-for-agents`' invocation, flags, advertisement, Codex policy,
  and description. Any other ledger change is a defect.
- `.claude-plugin/marketplace.json`, `.agents/plugins/marketplace.json`, and `docs/inventory.md`:
  no change. Neither marketplace carries a version, and the inventory reads upstream sources.
- `npm run validate` exits 0, and a second build plus inventory produces a byte-identical diff.

## Merge sequencing

O2 fails on the current curation, and only the curator can clear it. Recommendation: steps 2 and 3
share one branch; it merges to `master` only when `npm run validate` is clean, so `master` CI never
sees the known findings. The rejected alternative is landing O2 as a warning first and raising it to
an error later, which leaves a window in which a new unresolvable handle passes CI.

The curator extended this rule on 2026-10-08: the branch also closes the six OpenCode 2 runtime
breaks of section W0 before it merges, so the merge waits for the W0 regeneration task and a clean
`npm run validate`.

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
- **Skill-tool handles.** O2 accepted only an emitted skill ID; an agent or command ID in a handle
  was an error. Codex rendered a promoted handle as `"$deniz-process:grilling"`, which Codex has no
  skill tool for. W0 settles both: the handle is a fact checked on the canonical tree, the linker
  and O1 check its target, and the whole sentence renders per harness (sections 12 and 13).
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
- **Q5. Path climbs into a `manual` item.** Resolved by canon, not a curator question. A `manual`
  item may be named, but a path into its folder is a read that bypasses Claude's model-invocation
  block, so W0 makes it an error (section 14). The one `executing-plans` line,
  `../using-superpowers/references/`, points into a folder whose `references/**` is omitted and
  whose item is `manual`; W0 drops the clause with a body patch (section 15).
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
