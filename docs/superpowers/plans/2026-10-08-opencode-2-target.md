# OpenCode 2 Target Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development (recommended) or deniz-process:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Date: 2026-10-09

**Goal:** Make the checkout emit, check, ledger, and install OpenCode 2 output as current canon
states, then prepare the curation pass, produce the measurement records, migrate the two real
profiles once, and cut the next Package Release (steps 2 to 5 of the curator's OpenCode 2
sequence, in which step 1 is the canon baseline; [`docs/ROADMAP.md`](../../ROADMAP.md#next-up)
lists steps 2 to 5 as sub-steps 1 to 4 of Next Up item 1). Step numbers in this plan and the spec
follow the curator's sequence.

**Architecture:** The common assembly stays unchanged. A new pure module,
`tools/lib/opencode-target.ts`, owns OpenCode IDs, paths, and frontmatter adaptation; `tools/build.ts`
wires it into the OpenCode emitter; `tools/lib/rewrite.ts` gets one target-valued map with three
renderers plus a depth-checked sibling-climb rewrite; `tools/lib/ledger.ts` and `tools/validate.ts`
read the same definitions; `resolveDestination` adopts `OPENCODE_CONFIG_DIR`. Claude Code and Codex
output must not change.

**Tech Stack:** Node 24 (`node --test`, native TypeScript stripping), TypeScript 7 (`tsc --noEmit`),
Biome, `yaml`, Git index modes, PowerShell 7 for experiment scripts, OpenCode 2 (`@opencode/cli`).

**Spec:** [`docs/superpowers/specs/2026-10-08-opencode-2-target-design.md`](../specs/2026-10-08-opencode-2-target-design.md).
Executors read the spec, the canon it relays to, and this plan. The spec's section numbers are cited
as "spec §N".

## Global Constraints

- OpenCode target: OpenCode 2 only; floor v2.0.4; measured v2.0.23
  (`anomalyco/opencode@0fd7e2829449b052abf0078666669302923d77af`); no OpenCode 1 layer.
- Every OpenCode ID is `<plugin>.<name>`; skill frontmatter `name` equals the ID; agents never get
  `name`; agent `color`, if present, matches `^#[0-9a-fA-F]{6}$`.
- Manual hiding key: `metadata: {"opencode/autoinvoke": false}`, merged into existing metadata.
- Portable-name rule: `^[a-z0-9]+(-[a-z0-9]+)*$` for every plugin name and output name.
- Bare output names are unique across the whole repository, whatever their kind.
- Destination: `OPENCODE_CONFIG_DIR` when set and non-empty, else `$XDG_CONFIG_HOME/opencode`, else
  `<home>/.config/opencode`. Install state stays schema 2; no schema-1 reader.
- `plugins/`, `codex/`, `.claude-plugin/marketplace.json`, `.agents/plugins/marketplace.json`, and
  `docs/inventory.md` must be byte-identical before and after step 2 (Tasks 2 to 12) and step 4
  (Tasks 14 to 17). Step 3 (Phase B2) changes `plugins/` and `codex/` only where W0 rendering or a
  curator decision lands (harness phrasing, a promoted fact, a body patch, a posture, a Module
  version); neither marketplace nor `docs/inventory.md` changes.
- Never hand-edit `external/`, `plugins/`, `opencode/`, `codex/`, `dist/`, either marketplace,
  `docs/inventory.md`, or `docs/ledger.json`.
- Every curation decision (posture, `as:`, body patch, `depends_on`, reasons, comments, Module
  versions) is the curator's. This plan prepares decisions; it never makes them.
- Never commit machine-specific paths, hostnames, usernames, or secrets; `npm run check:public-safety`
  enforces part of this.
- Commit only on the curator's word; end commit messages with the attribution line the session
  supplies.

---

## Decision gates before execution

Answer these before the task that needs them. Recommendations are in spec "Open questions".

| Gate | Question | Needed by |
|---|---|---|
| G1 | Commit the canon baseline as reviewed? | Task 1 |
| G2 (Q8) | Steps 2 and 3 on one branch, merged only when `validate` is clean? | Task 2 |
| G3 (Q6) | Fallback home: keep `HOME` -> `USERPROFILE` -> `os.homedir()`, or `os.homedir()` only? | Task 11 |
| G4 (Q7) | Refuse a relative `OPENCODE_CONFIG_DIR`? | Task 11 |
| G5 | Every item in the curation decision packet (settled 2026-10-08; spec "W0 correctness") | W0.10–W0.13 |
| G6 | Go for each real-profile migration, per machine | Task 18 |
| G7 (Q9, Q10) | Release version and tag; Module version bumps (bumps settled 2026-10-08) | W0.13, Task 19 |

## File structure

| File | Responsibility | Tasks |
|---|---|---|
| `tools/lib/opencode-target.ts` (new) | OpenCode IDs, Bundle paths, key sets, skill/command/agent adaptation, emission preflight, Claude counterpart path | 2, 5 |
| `tools/lib/opencode-target.test.ts` (new) | Pure tests for the above | 2, 5 |
| `tools/lib/resolve.ts` | `PORTABLE_NAME`, identity preflight | 2, 4 |
| `tools/lib/codex-plugin.ts` | imports `PORTABLE_NAME` | 2 |
| `tools/lib/refs.ts`, `tools/lib/refs.test.ts` | path claims, skill-tool call spans, handoff templates | W0.1, W0.2, W0.6, W0.7 |
| `tools/lib/rewrite.ts`, `tools/lib/rewrite.test.ts` | target-valued map, three renderers, sibling climbs; harness phrasing, `localize`, Claude-only vocabulary, path respelling, `isBundledText` | 3, 7, W0.1, W0.3, W0.6, W0.7 |
| `tools/build.ts`, `tools/build.test.ts` | OpenCode emitter, OpenCode rewrite pass, manifest modes; `localize` wiring, text-file path respelling | 3, 6, 7, W0.4, W0.7 |
| `tools/lib/ledger.ts`, `tools/lib/ledger.test.ts` | OpenCode projection | 2, 8 |
| `docs/agents/reference-audit-playbook.md` | path collection script | 8 |
| `tools/validate.ts`, `tools/validate.test.ts` | retirements, linker state, path rules, O1, O2, O4–O6; H1–H4, V, P (O2 retired) | 9, 10, W0.5, W0.6, W0.8 |
| `tools/lib/opencode-install-state.ts` (+ test), `tools/install-opencode.ts` (+ test) | Destination | 11 |
| `tools/testutil.ts` | `opencodeId` helper for fixtures | 6 |
| `curation/*.yaml`, `overlays/**`, `overlays/overlays.lock.json` | W0 curation application | W0.10–W0.13 |
| generated trees, `dist/`, `docs/ledger.json` | regenerated only | 12, W0.14 |
| `docs/ROADMAP.md`, architecture docs | gap removal and canon touch-ups at closeouts | 12, W0.14, 20 |
| `experiments/harness-invocation/**` | retire OpenCode 1 probes, port discovery check, records | 14–18 |
| `tools/repository-docs.test.ts` | lab assertion retarget; Release pins | 14, 19 |
| `README.md`, `package.json`, `package-lock.json` | Release step only | 19 |

## Checkpoints

Behavior-neutral checkpoints (Tasks 2 and 3) must leave every generated tree byte-identical; prove
it with `npm run build` and the scoped `git diff --exit-code` before committing. Behavior tasks
(4 to 11) commit tooling and tests only: on the feature branch the committed generated trees are
intentionally stale until Task 12 regenerates them. Do not push a branch that triggers CI before
Task 12 unless CI is expected to fail on freshness.

**Known red window (Tasks 6 to 9).** Task 6 moves every emitted OpenCode path, but the ledger probes
(`writeLedger`) move only in Task 8 and the validator's path, linker, and park checks
(`openCodeArtifact`, section 1e, `targetState`, L6, L8/R1/R2) move only in Task 9. Fixture tests
that build and then validate or read the ledger (`tools/validate.test.ts`, `tools/lib/ledger.test.ts`)
therefore fail from Task 6 until Task 9 closes. Inside this window, each task runs its own named
test files plus `npm run typecheck`, `npm run lint`, `npm run format:check`, and
`npm run check:public-safety`, and lists the still-failing test names in its commit body; `npm test`
must be back to `# fail 0` at the end of Task 9. Do not push inside the window. If the curator wants
every commit green instead, fold Task 8 and the path/linker/L6 part of Task 9 into Task 6.

Run the five tooling commands at the end of every tooling task outside that window:

```text
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
```

Expected: every command exits 0; `npm test` reports `# fail 0`.

---

## Phase A: Canon baseline

### Task 1: Land the reviewed canon baseline

**Files:**
- Commit (already edited, do not rewrite): `CONTEXT.md`, `curation/SCHEMA.md`, `docs/ROADMAP.md`,
  `docs/adr/0002-multi-harness-output.md`, `docs/adr/0005-invocation-intent-in-the-manifest.md`,
  `docs/adr/0006-output-is-a-transformation.md`, `docs/adr/0008-references-are-symbols.md`,
  `docs/agents/README.md`, `docs/agents/reference-audit-playbook.md`,
  `docs/architecture/distribution-and-installation.md`,
  `docs/architecture/references-and-linking.md`,
  `docs/architecture/transformation-and-emission.md`, `docs/cheatsheet.md`, the four modified
  research notes, `docs/research/opencode-2-target.md`, this plan, and the spec.

- [ ] **Step 1: Wait for gate G1.** The curator reviews the diff. The research note already follows the topic-name rule
  (`docs/research/opencode-2-target.md`).
- [ ] **Step 2: Check the documentation gate.**

```text
npm test
npm run check:public-safety
git diff --check
```

Expected: `# fail 0`, public-safety passes, no whitespace errors.

- [ ] **Step 3: Commit.**

```bash
git add CONTEXT.md curation/SCHEMA.md docs
git commit -m "docs: decide OpenCode 2 as the OpenCode target"
```

---

## Phase B: Compiler, validator, ledger, installer (step 2)

### Task 2: Shared names and OpenCode helpers (behavior-neutral)

**Files:**
- Create: `tools/lib/opencode-target.ts`, `tools/lib/opencode-target.test.ts`
- Modify: `tools/lib/resolve.ts` (export `PORTABLE_NAME`), `tools/lib/codex-plugin.ts:254`
  (`CODEX_NAME` -> import), `tools/lib/ledger.ts:13` (move `OPENCODE_SKILL_KEYS`),
  `tools/build.ts:34` (import path)

**Interfaces:**
- Produces: `PORTABLE_NAME: RegExp` (resolve.ts); `OPENCODE_SKILL_KEYS`, `OPENCODE_HIDE_KEY`,
  `OPENCODE_AGENT_KEYS`, `OPENCODE_AGENT_COLOR`, `openCodeId(plugin, name): string`,
  `splitOpenCodeId(id, plugins): { plugin: string; name: string } | undefined`,
  `openCodeBundlePath(kind: ComponentType, plugin, name): string`,
  `claudeCounterpartPath(module, bundlePath): string | undefined` (opencode-target.ts).

- [ ] **Step 1: Branch per gate G2.**

```bash
git switch -c opencode-2-target
```

- [ ] **Step 2: Write the failing tests** in `tools/lib/opencode-target.test.ts`:

```ts
import assert from "node:assert/strict";
import { test } from "node:test";
import { PORTABLE_NAME } from "./resolve.ts";
import {
  claudeCounterpartPath,
  OPENCODE_AGENT_COLOR,
  OPENCODE_AGENT_KEYS,
  OPENCODE_HIDE_KEY,
  OPENCODE_SKILL_KEYS,
  openCodeBundlePath,
  openCodeId,
  splitOpenCodeId,
} from "./opencode-target.ts";

test("OpenCode IDs join plugin and name with a dot", () => {
  assert.equal(openCodeId("deniz-process", "brainstorming"), "deniz-process.brainstorming");
  assert.deepEqual(splitOpenCodeId("deniz-process.handoff", ["deniz-process", "deniz-dotnet-general"]), {
    plugin: "deniz-process",
    name: "handoff",
  });
  assert.equal(splitOpenCodeId("other.handoff", ["deniz-process"]), undefined);
  assert.equal(splitOpenCodeId("deniz-process.", ["deniz-process"]), undefined);
});

test("Bundle paths place every kind under its ID", () => {
  assert.equal(openCodeBundlePath("skill", "deniz-process", "x"), "skills/deniz-process.x");
  assert.equal(openCodeBundlePath("command", "deniz-process", "x"), "commands/deniz-process.x.md");
  assert.equal(openCodeBundlePath("agent", "deniz-dotnet-akka", "y"), "agents/deniz-dotnet-akka.y.md");
});

test("a skill file maps to its Claude counterpart; generated documents have none", () => {
  assert.equal(
    claudeCounterpartPath("deniz-process", "skills/deniz-process.brainstorming/scripts/start-server.sh"),
    "plugins/deniz-process/skills/brainstorming/scripts/start-server.sh",
  );
  assert.equal(claudeCounterpartPath("deniz-process", "commands/deniz-process.x.md"), undefined);
  assert.equal(claudeCounterpartPath("deniz-process", "agents/deniz-process.x.md"), undefined);
  assert.equal(claudeCounterpartPath("deniz-process", "LICENSE"), undefined);
  assert.throws(() => claudeCounterpartPath("deniz-process", "skills/brainstorming/SKILL.md"), /not an OpenCode ID/);
});

test("key sets and patterns match canon", () => {
  assert.deepEqual([...OPENCODE_SKILL_KEYS].sort(), ["compatibility", "description", "license", "metadata", "name"]);
  assert.equal(OPENCODE_HIDE_KEY, "opencode/autoinvoke");
  assert.deepEqual([...OPENCODE_AGENT_KEYS].sort(), [
    "color", "description", "disabled", "hidden", "mode", "model", "permissions", "request", "steps", "system", "variant",
  ]);
  assert.ok(OPENCODE_AGENT_COLOR.test("#a1B2c3"));
  assert.ok(!OPENCODE_AGENT_COLOR.test("info"));
  assert.ok(PORTABLE_NAME.test("deniz-process"));
  for (const bad of ["a.b", "a_b", "a:b", "a--b", "-a", "A"]) assert.ok(!PORTABLE_NAME.test(bad), bad);
});
```

- [ ] **Step 3: Run it to verify it fails.**

Run: `node --test tools/lib/opencode-target.test.ts`
Expected: FAIL with `ERR_MODULE_NOT_FOUND` for `opencode-target.ts` (or a missing `PORTABLE_NAME`
export).

- [ ] **Step 4: Implement.** In `tools/lib/resolve.ts` add
  `export const PORTABLE_NAME = /^[a-z0-9]+(-[a-z0-9]+)*$/;`. In `tools/lib/codex-plugin.ts` replace
  the local `CODEX_NAME` regex with `import { PORTABLE_NAME } from "./resolve.ts"` and use it where
  `CODEX_NAME` was tested; keep every message string. Create `tools/lib/opencode-target.ts`:

```ts
import type { ComponentType } from "./manifest.ts";

/** Skill frontmatter OpenCode 2 recognises; every other key is reported as dropped. */
export const OPENCODE_SKILL_KEYS: ReadonlySet<string> = new Set([
  "name", "description", "license", "compatibility", "metadata",
]);
/** OpenCode 2's native "do not advertise to the model" key, nested under `metadata`. */
export const OPENCODE_HIDE_KEY = "opencode/autoinvoke";
/** Native OpenCode 2 agent keys; any other key sends the file through the OpenCode 1 migrator. */
export const OPENCODE_AGENT_KEYS: ReadonlySet<string> = new Set([
  "description", "mode", "model", "variant", "request", "system", "permissions", "steps", "hidden", "color", "disabled",
]);
export const OPENCODE_AGENT_COLOR = /^#[0-9a-fA-F]{6}$/;

export function openCodeId(plugin: string, name: string): string {
  return `${plugin}.${name}`;
}

export function splitOpenCodeId(id: string, plugins: Iterable<string>): { plugin: string; name: string } | undefined {
  for (const plugin of plugins) {
    const prefix = `${plugin}.`;
    if (id.startsWith(prefix) && id.length > prefix.length) {
      return { plugin, name: id.slice(prefix.length) };
    }
  }
  return undefined;
}

export function openCodeBundlePath(kind: ComponentType, plugin: string, name: string): string {
  const id = openCodeId(plugin, name);
  return kind === "skill" ? `skills/${id}` : `${kind}s/${id}.md`;
}

/** Bundle path -> committed Claude Plugin path whose Git index mode the Bundle file inherits. */
export function claudeCounterpartPath(module: string, bundlePath: string): string | undefined {
  if (!bundlePath.startsWith("skills/")) {
    return undefined;
  }
  const [, folder, ...rest] = bundlePath.split("/");
  const prefix = `${module}.`;
  if (!folder?.startsWith(prefix) || folder.length === prefix.length) {
    throw new Error(`internal error: ${module}/${bundlePath} is not an OpenCode ID path`);
  }
  return ["plugins", module, "skills", folder.slice(prefix.length), ...rest].join("/");
}
```

  Delete `OPENCODE_SKILL_KEYS` from `tools/lib/ledger.ts` and import it from
  `./opencode-target.ts` there and in `tools/build.ts`.

- [ ] **Step 5: Run the tests and the tooling commands.**

Run: `node --test tools/lib/opencode-target.test.ts tools/lib/codex-plugin.test.ts`
Expected: PASS. Then the five tooling commands: all exit 0.

- [ ] **Step 6: Prove behavior neutrality.**

```text
npm run build
git diff --exit-code -- plugins opencode codex dist .claude-plugin .agents/plugins/marketplace.json docs/ledger.json
git status --short -- plugins opencode codex dist .claude-plugin .agents docs/ledger.json
```

Expected: no diff, no untracked generated path.

- [ ] **Step 7: Commit.**

```bash
git add tools/lib/opencode-target.ts tools/lib/opencode-target.test.ts tools/lib/resolve.ts tools/lib/codex-plugin.ts tools/lib/ledger.ts tools/build.ts
git commit -m "refactor: share portable names and OpenCode target helpers"
```

### Task 3: Target-valued rewrite map (behavior-neutral)

**Files:**
- Modify: `tools/lib/rewrite.ts`, `tools/lib/rewrite.test.ts`, `tools/build.ts:88-92,476-490`

**Interfaces:**
- Produces: `interface RewriteTarget { plugin: string; name: string; kind: ComponentType }`;
  `buildRewriteMap(manifests, components, ownSkills?): Map<string, RewriteTarget>` (no `style`
  argument); `rewriteRefs(content, map, style: RefStyle): string`. In this task the `opencode` style
  still renders the bare `name` with the slash kept, exactly as today.

- [ ] **Step 1: Rewrite the map tests** in `tools/lib/rewrite.test.ts` to the object form, keeping
  every existing case. Replace the first test body and add a kind test:

```ts
test("map covers included items with renames, skips excluded", () => {
  const map = buildRewriteMap([manifest], components);
  assert.deepEqual(map.get("superpowers:brainstorming"), { plugin: "deniz-process", name: "brainstorming", kind: "skill" });
  assert.deepEqual(map.get("superpowers:tdd"), { plugin: "deniz-process", name: "deniz-tdd", kind: "skill" });
  assert.equal(map.has("superpowers:dropped"), false);
});

test("map records the resolved target kind", () => {
  const m: CurationManifest = {
    plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
    items: [{ source: "sp/skills/brainstorming", as: "command" }],
  };
  assert.equal(buildRewriteMap([m], components).get("superpowers:brainstorming")?.kind, "command");
});

test("one map renders all three styles; OpenCode stays bare in this checkpoint", () => {
  const map = buildRewriteMap([manifest], components, own);
  const text = "Use superpowers:brainstorming, then /deniz-process:my-own.";
  assert.equal(rewriteRefs(text, map, "claude"), "Use deniz-process:brainstorming, then /deniz-process:my-own.");
  assert.equal(rewriteRefs(text, map, "codex"), "Use $deniz-process:brainstorming, then $deniz-process:my-own.");
  assert.equal(rewriteRefs(text, map, "opencode"), "Use brainstorming, then /my-own.");
});
```

  Update every other test in the file that reads a map value or passes `style` as the third
  argument, keeping its intent:
  - `map keys on the upstream address, not the frontmatter name` and
    `commands and agents are addressed by file name without the extension` compare
    `{ plugin, name, kind }` objects (`kind: "command"` in the second);
  - `original skill targets use Plugin-qualified Claude and bare OpenCode spellings` becomes one
    `buildRewriteMap([manifest], components, own)` call whose `deniz-process:my-own` value is
    `{ plugin: "deniz-process", name: "my-own", kind: "skill" }`;
  - `an original skill rewrite key cannot overwrite another target` calls
    `buildRewriteMap([collisionManifest], colliding, own)` (no `"claude"` argument; the old
    four-argument call would pass a string as `ownSkills`) and keeps its regex;
  - `Codex map preserves the owning plugin ...` calls `buildRewriteMap([other], components)`;
  - the hand-built string maps in `rewriteRefs replaces longest keys first`,
    `rewriteRefs stops at ref-token boundaries`,
    `Codex renders model edges and user pointers as valid namespaced dollar references`, and
    `Codex rewriting keeps token boundaries and literal dollar amounts` become target objects, for
    example `new Map([["sp:foo", { plugin: "p", name: "foo", kind: "skill" }]])` and
    `["superpowers:tdd", { plugin: "deniz-process", name: "test-driven-development", kind: "skill" }]`,
    with unchanged expected strings.

- [ ] **Step 2: Run to verify failure.**

Run: `node --test tools/lib/rewrite.test.ts`
Expected: FAIL (`map.get(...)` returns a string, and `buildRewriteMap` still takes `style`).

- [ ] **Step 3: Implement** in `tools/lib/rewrite.ts`:

```ts
export interface RewriteTarget { plugin: string; name: string; kind: ComponentType }

export function buildRewriteMap(
  manifests: CurationManifest[],
  components: ComponentInfo[],
  ownSkills: OwnSkillIdentity[] = [],
): Map<string, RewriteTarget> {
  const bySource = new Map(components.map((c) => [c.sourcePath, c]));
  const map = new Map<string, RewriteTarget>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) continue;
      const c = bySource.get(item.source);
      if (!c) continue;
      map.set(`${c.namespace}:${addressOf(c)}`, {
        plugin: m.plugin.name,
        name: item.name ?? c.name,
        kind: item.as ?? c.type,
      });
    }
  }
  for (const own of ownSkills) {
    const value: RewriteTarget = { plugin: own.plugin, name: own.name, kind: "skill" };
    const existing = map.get(own.address);
    if (existing && (existing.plugin !== value.plugin || existing.name !== value.name)) {
      throw new Error(`reference identity ${own.address} resolves to both ${existing.plugin}:${existing.name} and ${own.address}`);
    }
    map.set(own.address, value);
  }
  return map;
}

function render(target: RewriteTarget, style: RefStyle): string {
  switch (style) {
    case "claude": return `${target.plugin}:${target.name}`;
    case "codex": return `$${target.plugin}:${target.name}`;
    case "opencode": return target.name; // replaced in Task 7
  }
}

export function rewriteRefs(content: string, map: Map<string, RewriteTarget>, style: RefStyle = "claude"): string {
  let out = "";
  let cut = 0;
  for (const ref of scanRefs(content)) {
    const target = map.get(ref.address);
    if (target === undefined) continue;
    const start = style === "codex" && ref.kind === "pointer" ? ref.index - 1 : ref.index;
    out += content.slice(cut, start) + render(target, style);
    cut = ref.index + ref.address.length;
  }
  return out + content.slice(cut);
}
```

  Import `type ComponentType` from `./manifest.ts`. Keep the existing error text shape of the
  duplicate-identity throw (the collision test pins `reference identity <address> ... <existing> ... <address>`). In
  `tools/build.ts` build one map (`const rewriteMap = buildRewriteMap(manifests, components, ownSkills)`)
  and pass it to the three `rewriteTree` calls with their styles. Update the module doc comment
  that says OpenCode "addresses a skill by its `name` alone".

- [ ] **Step 4: Run tests.**

Run: `node --test tools/lib/rewrite.test.ts tools/build.test.ts`
Expected: PASS. Then the five tooling commands.

- [ ] **Step 5: Prove behavior neutrality** with the Task 2 Step 6 commands. Expected: no diff.

- [ ] **Step 6: Commit.**

```bash
git add tools/lib/rewrite.ts tools/lib/rewrite.test.ts tools/build.ts
git commit -m "refactor: one target-valued reference map for all harnesses"
```

### Task 4: Identity preflight — portable names and repository-wide bare names

**Files:**
- Modify: `tools/lib/resolve.ts` (`collectIdentityProblems`), `tools/build.test.ts:844-930`,
  `tools/validate.test.ts:170,775`

**Interfaces:**
- Consumes: `PORTABLE_NAME` (Task 2).
- Produces: problem strings
  `<manifestPath>: <plugin>: output name <name> is not portable (^[a-z0-9]+(-[a-z0-9]+)*$)`,
  `<manifestPath>: plugin name <name> is not portable (^[a-z0-9]+(-[a-z0-9]+)*$)`, and
  `duplicate output name <name> from <module> (<source>) and <module> (<source>)`.

- [ ] **Step 1: Write the failing tests** in `tools/build.test.ts`. Replace
  `same OpenCode name in different artifact kinds remains legal across Modules` with:

```ts
test("a bare output name claimed by two kinds across Modules aborts before deleting output", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "curation", "deniz-other.yaml"),
    "plugin:\n  name: deniz-other\n  description: Other\n  version: 0.1.0\nitems:\n  - source: sp/skills/delta\n    as: agent\n    name: alpha\n",
  );
  // manifests load in file-name order, so deniz-other claims first
  assert.throws(() => buildAll(root), /duplicate output name alpha from deniz-other \(sp\/skills\/delta\) and deniz-process \(sp\/skills\/alpha\)/);
  assert.ok(existsSync(opencodeModulePath(root, "deniz-process", "manifest.json")), "nothing was deleted");
});

test("a non-portable output name aborts in preflight for every harness", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n    name: alpha.beta\n",
  );
  assert.throws(() => buildAll(root), /output name alpha\.beta is not portable/);
});

test("an original skill shares the repository-wide bare-name claim", () => {
  const root = makeRepo();
  mkdirSync(join(root, "skills", "deniz-other", "delta"), { recursive: true });
  writeFileSync(join(root, "skills", "deniz-other", "delta", "SKILL.md"), "---\nname: delta\ndescription: D\n---\n");
  writeFileSync(
    join(root, "curation", "deniz-other.yaml"),
    "plugin:\n  name: deniz-other\n  description: Other\n  version: 0.1.0\nitems: []\n",
  );
  assert.throws(() => buildAll(root), /duplicate output name delta/);
});
```

  Update the two existing cross-Module tests at `tools/build.test.ts` lines 844 and 861, and
  `duplicate output name across plugins is rejected by build preflight` at
  `tools/validate.test.ts:170` and `:775`, to expect the new message. The same-Module curated tests keep their current
  messages and must stay green unchanged: `a duplicate output identity within one plugin aborts the
  build` (764), `a flattened Codex namespace collision aborts ...` (784), `the same output name in
  different artifact kinds is rejected by the flattened Codex namespace` (931), and in
  `tools/validate.test.ts` lines 179, 229, and 373.

- [ ] **Step 2: Run to verify failure.**

Run: `node --test tools/build.test.ts`
Expected: FAIL on the three new tests (the cross-kind case builds; `alpha.beta` passes preflight
until Codex rejects it with a different message).

- [ ] **Step 3: Implement** in `collectIdentityProblems`: replace `openCodeDestinations` and
  `claimOpenCodeDestination` with one `claimName(name, module, source)` keyed by bare name; call it
  once per non-excluded item and once per original-skill directory. A claim reports a duplicate
  unless both claims are curated items of the **same** Module: the schema rule is "across
  manifests" (`curation/SCHEMA.md`), and same-Module curated collisions already have owners (the
  per-manifest `duplicate output identity` check for one kind, Codex flattening for two kinds).
  Reporting them again here would double-report and would pre-empt the Codex preflight message that
  tests 784 and 931 pin. An original skill against a curated item of its own Module has no other
  preflight owner, so it still reports (`an own skill colliding with a curated item is rejected by
  build preflight`, `tools/validate.test.ts:775`, updates its expected message). Add the portable
  checks for `manifest.plugin.name`,
  each `outName`, and each original-skill directory name. Delete the invocation-dependent claim
  branches.

- [ ] **Step 4: Run tests.** `node --test tools/build.test.ts` -> PASS; five tooling commands.

- [ ] **Step 5: Confirm the estate is unaffected.** `npm run build` then the Task 2 Step 6 diff:
  expected no generated change (the estate has no cross-kind duplicate or non-portable name).

- [ ] **Step 6: Commit.**

```bash
git add tools/lib/resolve.ts tools/build.test.ts tools/validate.test.ts
git commit -m "feat: enforce portable and repository-unique output names"
```

### Task 5: OpenCode document adaptation (pure)

**Files:**
- Modify: `tools/lib/opencode-target.ts`, `tools/lib/opencode-target.test.ts`

**Interfaces:**
- Consumes: `ParsedDoc` (`tools/lib/frontmatter.ts`), `CurationItem["invocation"]`,
  `AssembledItem` (`tools/lib/assemble.ts`).
- Produces:

```ts
export interface OpenCodeSkillAdaptation { document: ParsedDoc; dropped: string[]; advertised: boolean; transformations: string[] }
export function adaptOpenCodeSkillDocument(id: string, doc: ParsedDoc, invocation: CurationItem["invocation"]): OpenCodeSkillAdaptation;
export function adaptOpenCodeCommandDocument(doc: ParsedDoc): { document: ParsedDoc; dropped: string[] };
export function adaptOpenCodeAgentDocument(doc: ParsedDoc): { document: ParsedDoc; dropped: string[] };
export function openCodeWouldHide(doc: ParsedDoc, invocation: CurationItem["invocation"]): boolean;
export function collectOpenCodeEmissionProblems(manifests: CurationManifest[], assembled: AssembledItem[]): string[];
```

- [ ] **Step 1: Write the failing tests** (append to `tools/lib/opencode-target.test.ts`):

```ts
const doc = (frontmatter: Record<string, unknown>) => ({ frontmatter, body: "Body.\n" });
const ID = "deniz-process.x";

test("skill adaptation: name is the ID and unknown keys are reported", () => {
  const a = adaptOpenCodeSkillDocument(ID, doc({ name: "x", description: "D", "allowed-tools": "Read", license: "MIT" }), "auto");
  assert.deepEqual(a.document.frontmatter, { name: ID, description: "D" });
  assert.deepEqual(a.dropped, ["allowed-tools", "license"]);
  assert.equal(a.advertised, true);
  assert.equal(a.document.body, "Body.\n");
});

test("manual adds the hide key, merging into existing metadata", () => {
  const none = adaptOpenCodeSkillDocument(ID, doc({ name: "x", description: "D" }), "manual");
  assert.deepEqual(none.document.frontmatter.metadata, { "opencode/autoinvoke": false });
  assert.equal(none.advertised, false);
  const merged = adaptOpenCodeSkillDocument(ID, doc({ name: "x", description: "D", metadata: { a: "1", "opencode/autoinvoke": true } }), "manual");
  assert.deepEqual(merged.document.frontmatter.metadata, { a: "1", "opencode/autoinvoke": false });
  assert.deepEqual(Object.keys(merged.document.frontmatter.metadata as object), ["a", "opencode/autoinvoke"]);
});

test("auto and both remove an upstream hide key; an emptied mapping disappears", () => {
  for (const inv of ["auto", "both"] as const) {
    const a = adaptOpenCodeSkillDocument(ID, doc({ name: "x", description: "D", metadata: { "opencode/autoinvoke": false } }), inv);
    assert.equal("metadata" in a.document.frontmatter, false, inv);
    assert.equal(a.advertised, true);
  }
  const kept = adaptOpenCodeSkillDocument(ID, doc({ name: "x", description: "D", metadata: { a: "1", "opencode/autoinvoke": false } }), "both");
  assert.deepEqual(kept.document.frontmatter.metadata, { a: "1" });
});

test("absent passes upstream posture through and renders an upstream DMI", () => {
  const plain = adaptOpenCodeSkillDocument(ID, doc({ name: "x", description: "D", metadata: { "opencode/autoinvoke": false } }), undefined);
  assert.deepEqual(plain.document.frontmatter.metadata, { "opencode/autoinvoke": false }, "upstream key kept");
  assert.equal(plain.advertised, false);
  const dmi = adaptOpenCodeSkillDocument(ID, doc({ name: "x", description: "D", "disable-model-invocation": true, "user-invocable": false }), undefined);
  assert.deepEqual(dmi.document.frontmatter.metadata, { "opencode/autoinvoke": false });
  assert.deepEqual(dmi.dropped, ["user-invocable"], "a consumed DMI is not a drop");
  assert.deepEqual(dmi.transformations, ["disable-model-invocation: true -> metadata.opencode/autoinvoke: false"]);
});

test("a stated invocation drops upstream Claude invocation keys", () => {
  const a = adaptOpenCodeSkillDocument(ID, doc({ name: "x", description: "D", "disable-model-invocation": true }), "auto");
  assert.deepEqual(a.dropped, ["disable-model-invocation"]);
  assert.equal(a.advertised, true);
});

test("command keeps description; agent keeps description and mode, never name", () => {
  const src = doc({ name: "x", description: "D", model: "opus", color: "info", tools: "Read" });
  assert.deepEqual(adaptOpenCodeCommandDocument(src), { document: { frontmatter: { description: "D" }, body: "Body.\n" }, dropped: ["color", "model", "tools"] });
  const agent = adaptOpenCodeAgentDocument(src);
  assert.deepEqual(agent.document.frontmatter, { description: "D", mode: "subagent" });
  assert.deepEqual(agent.dropped, ["color", "model", "tools"]);
});

test("a hidden item whose metadata is not a mapping is a preflight problem", () => {
  assert.equal(openCodeWouldHide(doc({ metadata: "x" }), "manual"), true);
  assert.equal(openCodeWouldHide(doc({ "disable-model-invocation": true }), undefined), true);
  assert.equal(openCodeWouldHide(doc({}), "both"), false);
});
```

  Add the preflight test (imports: `mkdtempSync`, `writeFileSync` from `node:fs`, `tmpdir` from
  `node:os`, `join` from `node:path`, `type AssembledItem` from `./assemble.ts`):

```ts
test("collectOpenCodeEmissionProblems reports a non-mapping metadata on a hidden skill only", () => {
  const dir = mkdtempSync(join(tmpdir(), "oc-target-"));
  writeFileSync(join(dir, "SKILL.md"), "---\nname: x\ndescription: D\nmetadata: plain\n---\n\nBody.\n");
  const item = (invocation?: "manual" | "auto"): AssembledItem => ({
    plugin: "deniz-process", source: "sp/skills/x", sourceType: "skill", outName: "x", outType: "skill",
    dir, own: false, item: { source: "sp/skills/x", ...(invocation ? { invocation } : {}) },
  });
  const manifests = [{ plugin: { name: "deniz-process", description: "d", version: "0.1.0" }, items: [] }];
  assert.deepEqual(collectOpenCodeEmissionProblems(manifests, [item("manual")]), [
    "deniz-process/x: metadata must be a mapping to carry opencode/autoinvoke",
  ]);
  assert.deepEqual(collectOpenCodeEmissionProblems(manifests, [item("auto")]), []);
});
```

- [ ] **Step 2: Run to verify failure.** `node --test tools/lib/opencode-target.test.ts` -> FAIL
  (functions not exported).

- [ ] **Step 3: Implement** per spec §2 and §3. First narrow `OPENCODE_SKILL_KEYS` in
  `tools/lib/opencode-target.ts` to `name`, `description`, `metadata` (spec Q4) and change the Task 2
  key-set assertion to `["description", "metadata", "name"]`; nothing emits through the adaptation until
  Task 6, so this stays inside the declared step. Key points: copy kept keys in source order; set
  `name` first if absent from source; never mutate the input document; `dropped` sorted with
  `localeCompare`; `isPlainObject(v)` is `typeof v === "object" && v !== null && !Array.isArray(v)`.
  `collectOpenCodeEmissionProblems` reads `join(item.dir, "SKILL.md")` with `parseDoc` for every
  assembled item whose `outType === "skill"` and reports only when `openCodeWouldHide` is true and
  `metadata` is present but not a plain object.

- [ ] **Step 4: Run tests.** PASS; five tooling commands.

- [ ] **Step 5: Commit.**

```bash
git add tools/lib/opencode-target.ts tools/lib/opencode-target.test.ts
git commit -m "feat: adapt documents to OpenCode 2 frontmatter"
```

### Task 6: OpenCode emitter and Module manifest modes

**Files:**
- Modify: `tools/build.ts:95-105` (preflight call), `:500-616` (replace `emitOpenCodeSkill` and
  `emitOpenCode`), `:618-650` (`writeOpenCodeManifests`)
- Modify: `tools/build.test.ts` (tests at 50, 169, 241-271, 301, 339, 418, 472, 506, 547, 591, 1139)
- Modify: `tools/testutil.ts` (add `opencodeIdPath`)

**Interfaces:**
- Consumes: Task 2 helpers, Task 5 adapters.
- Produces: emitted paths `opencode/<m>/skills/<m>.<name>/`, `commands/<m>.<name>.md`,
  `agents/<m>.<name>.md`; report lines `opencode skill <id>: ...`, `opencode command <id>: ...`,
  `opencode agent <id>: ...`; testutil
  `opencodeIdPath(root: string, module: string, kind: "skill" | "command" | "agent", name: string, ...rest: string[]): string`.

- [ ] **Step 1: Add the testutil helper** (no behavior):

```ts
/** An OpenCode artifact at its namespaced Bundle path: skills/<m>.<name>/..., commands|agents/<m>.<name>.md. */
export function opencodeIdPath(root: string, module: string, kind: "skill" | "command" | "agent", name: string, ...rest: string[]): string {
  const id = `${module}.${name}`;
  return kind === "skill"
    ? join(root, "opencode", module, "skills", id, ...rest)
    : join(root, "opencode", module, `${kind}s`, `${id}.md`);
}
```

- [ ] **Step 2: Replace the OpenCode 1 tests** with this invocation test (replacing
  `invocation sets the Claude flags and picks the OpenCode artifact`; keep its Claude and Codex
  assertions verbatim) and delete `a bundled manual command parks its body...`,
  `a bundle-less manual conversion preserves...`, `both preserves the pre-wave skill and command...`,
  `manual bundle links repoint to BODY.md...`, `only a manual conversion reports parked files`:

```ts
  // OpenCode 2: invocation selects frontmatter, never shape.
  const oc = (name: string) => parseDoc(readFileSync(opencodeIdPath(root, "deniz-process", "skill", name, "SKILL.md"), "utf8")).frontmatter;
  assert.equal(oc("alpha").name, "deniz-process.alpha");
  assert.equal("metadata" in oc("alpha"), false, "auto is advertised");
  assert.deepEqual(oc("beta").metadata, { "opencode/autoinvoke": false }, "manual is unadvertised");
  assert.ok(existsSync(opencodeIdPath(root, "deniz-process", "skill", "beta", "references", "notes.md")), "manual keeps its bundle");
  assert.equal("metadata" in oc("delta"), false, "both is one plain skill");
  assert.equal("metadata" in oc("gamma"), false, "absent with no upstream DMI is advertised");
  assert.ok(!existsSync(opencodeModulePath(root, "deniz-process", "commands")), "no command without as: command");
  assert.ok(!existsSync(opencodeModulePath(root, "deniz-process", "skills", "beta", "BODY.md")));
  assert.ok(!existsSync(opencodeModulePath(root, "deniz-process", "skills", "alpha")), "no bare folder");
```

  Add:

```ts
test("as: command and as: agent are the only non-skill OpenCode shapes", () => {
  const root = makeRepo();
  const report = buildAll(root);
  const cmd = parseDoc(readFileSync(opencodeIdPath(root, "deniz-process", "command", "deniz-beta"), "utf8"));
  assert.deepEqual(cmd.frontmatter, { description: "Beta overlay" });
  const agent = parseDoc(readFileSync(opencodeIdPath(root, "deniz-process", "agent", "beta-agent"), "utf8"));
  assert.deepEqual(agent.frontmatter, { description: "Beta upstream", mode: "subagent" });
  assert.ok(report.includes("opencode agent deniz-process.beta-agent: dropped frontmatter keys: model"));
  const own = parseDoc(readFileSync(opencodeIdPath(root, "deniz-process", "skill", "my-own", "SKILL.md"), "utf8"));
  assert.equal(own.frontmatter.name, "deniz-process.my-own", "original skills get the ID too");
});

test("an upstream DMI on an absent item renders the hide key and reports it", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "delta", "SKILL.md"),
    "---\nname: delta\ndescription: Delta upstream\ndisable-model-invocation: true\n---\n\nDelta body.\n",
  );
  const report = buildAll(root);
  const fm = parseDoc(readFileSync(opencodeIdPath(root, "deniz-process", "skill", "delta", "SKILL.md"), "utf8")).frontmatter;
  assert.deepEqual(fm.metadata, { "opencode/autoinvoke": false });
  assert.equal("disable-model-invocation" in fm, false);
  assert.ok(report.includes("opencode skill deniz-process.delta: disable-model-invocation: true -> metadata.opencode/autoinvoke: false"));
});
```

  Update `module manifests inherit executable modes from the plugin tree` (line 245) to read
  `manifest.files["skills/deniz-process.my-own/run.sh"].mode` (expect `100755`),
  `manifest.files["skills/deniz-process.alpha/SKILL.md"].mode` (expect `100644`), and
  `manifest.files["commands/deniz-process.deniz-beta.md"].mode` (expect `100644`). Update every
  other `opencodeModulePath(..., "skills", "<name>", ...)` in this file to `opencodeIdPath`.

- [ ] **Step 3: Run to verify failure.** `node --test tools/build.test.ts` -> FAIL on the new and
  updated tests (bare paths, commands present, `BODY.md` present, mode key missing).

- [ ] **Step 4: Implement** in `tools/build.ts`:
  - after `collectCodexEmissionProblems`, call `collectOpenCodeEmissionProblems(manifests, assembled)`
    and throw with the same header format (`N invalid OpenCode emission documents, nothing deleted:`);
  - replace both functions with:

```ts
/** OpenCode 2: one artifact per item at its `<plugin>.<name>` ID; invocation is frontmatter only (ADR-0005). */
function emitOpenCode(root: string, manifests: CurationManifest[], assembled: AssembledItem[], report: string[]): void {
  for (const manifest of manifests) {
    const module = manifest.plugin.name;
    const moduleRoot = join(root, "opencode", module);
    for (const item of assembled
      .filter((candidate) => candidate.plugin === module)
      .sort((left, right) => left.outName.localeCompare(right.outName))) {
      const id = openCodeId(module, item.outName);
      const neutral = parseDoc(readFileSync(join(item.dir, "SKILL.md"), "utf8"));
      const destination = join(moduleRoot, openCodeBundlePath(item.outType, module, item.outName));
      if (item.outType === "skill") {
        cpSync(item.dir, destination, { recursive: true });
        const adapted = adaptOpenCodeSkillDocument(id, neutral, item.item?.invocation);
        writeFileSync(join(destination, "SKILL.md"), serializeDoc(adapted.document));
        if (adapted.dropped.length) report.push(`opencode skill ${id}: dropped frontmatter keys: ${adapted.dropped.join(", ")}`);
        for (const t of adapted.transformations) report.push(`opencode skill ${id}: ${t}`);
        continue;
      }
      const adapted = item.outType === "command" ? adaptOpenCodeCommandDocument(neutral) : adaptOpenCodeAgentDocument(neutral);
      mkdirSync(dirname(destination), { recursive: true });
      writeFileSync(destination, serializeDoc(adapted.document));
      if (adapted.dropped.length) report.push(`opencode ${item.outType} ${id}: dropped frontmatter keys: ${adapted.dropped.join(", ")}`);
      const bundled = listFiles(item.dir).filter((file) => file !== "SKILL.md");
      if (item.outType === "command" && bundled.length) report.push(`opencode command ${id}: bundled files not emitted: ${bundled.join(", ")}`);
    }
  }
}
```

  - in `writeOpenCodeManifests`, replace the mode callback with:

```ts
(path) => {
  const counterpart = claudeCounterpartPath(m.plugin.name, path);
  return counterpart === undefined ? "100644" : (pluginModes.get(counterpart) ?? "100644");
},
```

  - update the doc comment above `writeOpenCodeManifests` (no parked `BODY.md`) and delete the
    "OpenCode's half of ADR-0005: the dial is which artifact exists" comment with its function.
    Add imports: `dirname` from `node:path`; `adaptOpenCodeAgentDocument`,
    `adaptOpenCodeCommandDocument`, `adaptOpenCodeSkillDocument`, `claudeCounterpartPath`,
    `collectOpenCodeEmissionProblems`, `openCodeBundlePath`, `openCodeId` from
    `./lib/opencode-target.ts`. Remove imports that become unused (`basename` if unused;
    `claudeDocument` stays for Claude).

- [ ] **Step 5: Run tests.** `node --test tools/build.test.ts` -> PASS (rewrite-dependent tests at
  169 and 1139 may still assert OpenCode spelling; update them in Task 7, and mark them
  `{ todo: "Task 7" }` only if they block this step). Then the red-window commands from
  Checkpoints; `tools/validate.test.ts` and `tools/lib/ledger.test.ts` are expected to fail until
  Tasks 8 and 9.

- [ ] **Step 6: Commit** (no generated output).

```bash
git add tools/build.ts tools/build.test.ts tools/testutil.ts
git commit -m "feat: emit OpenCode 2 namespaced artifacts"
```

### Task 7: OpenCode reference rendering and sibling climbs

**Files:**
- Modify: `tools/lib/rewrite.ts`, `tools/lib/rewrite.test.ts`, `tools/build.ts` (`rewriteTree`
  call site, new `rewriteOpenCodeTree`), `tools/build.test.ts:169,1139`

**Interfaces:**
- Consumes: `RewriteTarget` (Task 3), `openCodeId` (Task 2).
- Produces: OpenCode rendering per spec §7;
  `rewriteOpenCodeSiblingClimbs(content: string, depthBelowSkillFolder: number, skillIds: Map<string, string>): string`;
  `openCodeSkillIds(assembled: AssembledItem[]): Map<string, string>` in `tools/lib/opencode-target.ts`.

- [ ] **Step 1: Write the failing tests** in `tools/lib/rewrite.test.ts` (replace the checkpoint
  test's OpenCode line from Task 3):

```ts
test("OpenCode renders dotted IDs and picks the pointer prefix by target kind", () => {
  const m: CurationManifest = {
    plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
    items: [{ source: "sp/skills/brainstorming" }, { source: "sp/skills/tdd", as: "command", name: "handoff" }],
  };
  const map = buildRewriteMap([m], components, own);
  assert.equal(
    rewriteRefs("Use superpowers:brainstorming. Open /superpowers:brainstorming or /superpowers:tdd or /deniz-process:my-own.", map, "opencode"),
    "Use deniz-process.brainstorming. Open @deniz-process.brainstorming or /deniz-process.handoff or @deniz-process.my-own.",
  );
});

test("sibling climbs are respelled only when they land on the shared skills directory", () => {
  const ids = new Map([["aspireify", "deniz-dotnet-aspire.aspireify"], ["requesting-code-review", "deniz-process.requesting-code-review"]]);
  assert.equal(rewriteOpenCodeSiblingClimbs("[a](../aspireify/SKILL.md)", 0, ids), "[a](../deniz-dotnet-aspire.aspireify/SKILL.md)");
  assert.equal(rewriteOpenCodeSiblingClimbs("[a](../../aspireify/SKILL.md)", 1, ids), "[a](../../deniz-dotnet-aspire.aspireify/SKILL.md)");
  assert.equal(rewriteOpenCodeSiblingClimbs("[a](../../aspireify/SKILL.md)", 0, ids), "[a](../../aspireify/SKILL.md)", "wrong depth");
  assert.equal(rewriteOpenCodeSiblingClimbs('"Dispatch (../requesting-code-review/code-reviewer.md)"', 0, ids), '"Dispatch (../deniz-process.requesting-code-review/code-reviewer.md)"', "prose climb");
  assert.equal(rewriteOpenCodeSiblingClimbs('AddCSharpApp("api", "../src/Api")', 1, ids), 'AddCSharpApp("api", "../src/Api")', "not an emitted skill");
  assert.equal(rewriteOpenCodeSiblingClimbs("x/../aspireify/y", 0, ids), "x/../aspireify/y", "inside a longer path");
});
```

  In `tools/build.test.ts`, update `a curated item localizes a guarded original-skill target in both
  harnesses` and `pointer spellings rewrite in both trees` to expect `deniz-process.<name>` model
  edges and `@deniz-process.<name>` pointers in OpenCode, and add:

```ts
test("an OpenCode skill's sibling link points at the sibling's ID folder", () => {
  const root = makeRepo();
  writeFileSync(join(root, "external", "sp", "skills", "delta", "references", "notes.md"), "See [beta](../../beta/SKILL.md).\n");
  writeFileSync(join(root, "external", "sp", "skills", "alpha", "SKILL.md"), "---\nname: alpha\ndescription: A\n---\n\nSee [delta](../delta/SKILL.md).\n");
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n  - source: sp/skills/beta\n  - source: sp/skills/delta\n",
  );
  buildAll(root);
  assert.match(readFileSync(opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"), "utf8"), /\(\.\.\/deniz-process\.delta\/SKILL\.md\)/);
  assert.match(readFileSync(opencodeIdPath(root, "deniz-process", "skill", "delta", "references", "notes.md"), "utf8"), /\(\.\.\/\.\.\/deniz-process\.beta\/SKILL\.md\)/);
  assert.match(readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8"), /\(\.\.\/delta\/SKILL\.md\)/, "Claude unchanged");
});
```

- [ ] **Step 2: Run to verify failure.** `node --test tools/lib/rewrite.test.ts tools/build.test.ts`
  -> FAIL (bare OpenCode rendering; climbs untouched).

- [ ] **Step 3: Implement.** In `render`/`rewriteRefs`, for `opencode`: consume the pointer slash
  (`start = ref.index - 1` when `ref.kind === "pointer"`) and emit
  `${target.kind === "command" ? "/" : "@"}${openCodeId(target.plugin, target.name)}`; model edges
  emit `openCodeId(...)`. Add:

```ts
const CLIMB = /((?:\.\.\/)+)([a-z0-9]+(?:-[a-z0-9]+)*)(?=\/)/g;

export function rewriteOpenCodeSiblingClimbs(content: string, depthBelowSkillFolder: number, skillIds: Map<string, string>): string {
  return content.replace(CLIMB, (match, climb: string, name: string, offset: number) => {
    const before = offset > 0 ? content[offset - 1] : "";
    if (before && /[A-Za-z0-9._/-]/.test(before)) return match;
    if (climb.length / 3 !== depthBelowSkillFolder + 1) return match;
    const id = skillIds.get(name);
    return id === undefined ? match : `${climb}${id}`;
  });
}
```

  In `tools/lib/opencode-target.ts` add `openCodeSkillIds(assembled)` mapping each
  `outType === "skill"` item's `outName` to `openCodeId(item.plugin, item.outName)`. In
  `tools/build.ts`, replace `rewriteTree(join(root, "opencode"), ...)` with
  `rewriteOpenCodeTree(root, manifests, rewriteMap, openCodeSkillIds(assembled))`, which for each
  Module walks `skills/<id>/` computing `depth = relative(skillFolder, dirname(file)).split(sep).filter(Boolean).length`
  and applies `rewriteRefs(text, map, "opencode")` then `rewriteOpenCodeSiblingClimbs(text, depth, ids)`;
  files under `commands/` and `agents/` get only `rewriteRefs`.

- [ ] **Step 4: Run tests.** `node --test tools/lib/rewrite.test.ts tools/build.test.ts` -> PASS; then the red-window commands (Checkpoints).

- [ ] **Step 5: Commit.**

```bash
git add tools/lib/rewrite.ts tools/lib/rewrite.test.ts tools/lib/opencode-target.ts tools/build.ts tools/build.test.ts
git commit -m "feat: render OpenCode 2 references and sibling links"
```

### Task 8: Ledger OpenCode projection

**Files:**
- Modify: `tools/lib/ledger.ts:15-160`, `tools/lib/ledger.test.ts`,
  `docs/agents/reference-audit-playbook.md:55-90` (collection script)

**Interfaces:**
- Consumes: `openCodeId`, `openCodeBundlePath`, adapters (Tasks 2, 5), `RewriteTarget.kind` lookup.
- Produces: ledger `opencode` = `{ artifacts, identity, advertised?, edges, dropped, metadataTransformations? }`
  (spec §8); no `parked`.

- [ ] **Step 1: Write the failing test** in `tools/lib/ledger.test.ts`:

```ts
test("the ledger projects OpenCode 2 identity, advertisement, and dotted edges", () => {
  const root = makeRepo();
  writeFileSync(join(root, "external", "sp", "skills", "alpha", "SKILL.md"), "---\nname: alpha\ndescription: A\n---\n\nUse superpowers:delta. Tell the user /superpowers:beta.\n");
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n    invocation: manual\n    depends_on: [delta]\n  - source: sp/skills/beta\n    invocation: both\n  - source: sp/skills/delta\n    invocation: auto\n",
  );
  buildAll(root);
  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));
  const alpha = ledger["deniz-process/skill/alpha"].opencode;
  assert.deepEqual(alpha.artifacts, ["skill"]);
  assert.equal(alpha.identity, "deniz-process.alpha");
  assert.equal(alpha.advertised, false);
  assert.deepEqual(alpha.edges, { model: ["deniz-process.delta"], pointer: ["@deniz-process.beta"] });
  assert.equal("parked" in alpha, false);
  assert.equal(ledger["deniz-process/skill/delta"].opencode.advertised, true);
});
```

  Update the existing determinism test's expected `opencode` objects to the new shape.

- [ ] **Step 2: Run to verify failure.** `node --test tools/lib/ledger.test.ts` -> FAIL.

- [ ] **Step 3: Implement.** Probe `join(moduleRoot, openCodeBundlePath(kind, plugin, outName))`
  (plus `SKILL.md` for skills); compute `dropped`, `advertised`, `metadataTransformations` with the
  Task 5 adapters on `neutralDoc` and `item.invocation`; respell edges with a
  `kindOf: Map<string, ComponentType>` built from `resolveItem` over all manifests plus original
  skills: model `openCodeId(plugin, name)`, pointer `(kind === "command" ? "/" : "@") + openCodeId(...)`.
  Delete `parked` from the interface and output.
- [ ] **Step 4: Update the playbook script** in `docs/agents/reference-audit-playbook.md`: build
  OpenCode paths as `opencode/${plugin}/skills/${plugin}.${name}` and
  `opencode/${plugin}/${artifact}s/${plugin}.${name}.md`, and delete the `entry.opencode.parked`
  branch. Keep the playbook's Date unless its rules change; the script follows the ledger.
- [ ] **Step 5: Run tests.** `node --test tools/lib/ledger.test.ts` -> PASS; then the red-window commands (Checkpoints); only `tools/validate.test.ts` may still fail.
- [ ] **Step 6: Commit.**

```bash
git add tools/lib/ledger.ts tools/lib/ledger.test.ts docs/agents/reference-audit-playbook.md
git commit -m "feat: record the OpenCode 2 projection in the ledger"
```

### Task 9: Validator — retire OpenCode 1 checks, ID paths, linker state, path rules

**Files:**
- Modify: `tools/validate.ts:147-155` (`openCodeArtifact`), `:378-430` (section 1e),
  `:645-750` (linker), `:763-790` (L6), `:792-905` (L8, R1, R2)
- Modify: `tools/validate.test.ts` (tests at 598, 712, 741, 944, and every bare `opencode/` path)

**Interfaces:**
- Consumes: Task 2 helpers.
- Produces: `openCodeIndex(root, manifests, components): Map<string, { plugin: string; name: string; kind: ComponentType; invocation?: string; hidden: boolean }>`
  keyed by OpenCode ID (local to `validate.ts`); linker causes
  `hidden from the OpenCode model (opencode/autoinvoke)` and `no OpenCode artifact`.

- [ ] **Step 1: Write the failing tests** in `tools/validate.test.ts`:

```ts
// A manual target is already unreachable through Claude, whose cause the linker names first; only an
// absent item whose upstream carries the hide key is Claude-reachable but OpenCode-hidden.
test("linker: a model-edge to an OpenCode-hidden skill names the hide key", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\n\nUse superpowers:delta next.\n",
  );
  writeFileSync(
    join(root, "external", "sp", "skills", "delta", "SKILL.md"),
    "---\nname: delta\ndescription: Delta upstream\nmetadata:\n  opencode/autoinvoke: false\n---\n\nDelta body.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n    depends_on: [delta]\n  - source: sp/skills/delta\n",
  );
  buildAll(root);
  const errors = validateRepo(root).filter((f) => f.level === "error").map((f) => f.message);
  assert.ok(
    errors.some((m) => m.includes("model-edge to a target the model cannot reach: delta (hidden from the OpenCode model (opencode/autoinvoke))")),
    errors.join("\n"),
  );
});

test("linker: a pointer to an auto skill is an error only through Claude", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nSuggest /superpowers:beta to the user.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n  - source: sp/skills/beta\n    invocation: auto\n",
  );
  buildAll(root);
  const errors = validateRepo(root).filter((f) => f.level === "error").map((f) => f.message);
  assert.ok(errors.some((m) => m.includes("user-invocable: false in the Claude tree")), errors.join("\n"));
  assert.ok(!errors.some((m) => m.includes("no OpenCode artifact")), "OpenCode users can attach any skill");
});

test("path rules: an OpenCode climb into another Module resolves through the installed layout", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nSee [delta](../delta/SKILL.md).\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-other.yaml"),
    "plugin:\n  name: deniz-other\n  description: O\n  version: 0.1.0\nitems:\n  - source: sp/skills/delta\n",
  );
  buildAll(root);
  assert.match(
    readFileSync(opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md"), "utf8"),
    /\(\.\.\/deniz-other\.delta\/SKILL\.md\)/,
  );
  const r1 = validateRepo(root).filter((f) => f.message.includes("does not resolve in opencode/"));
  assert.deepEqual(r1, [], "the installed layout shares one skills/ directory across Modules");
});
```

  Import `opencodeIdPath` from `./testutil.ts` and `readFileSync` from `node:fs` if not already
  imported. Delete
  `linker: a parked manual BODY naming a missing parked file is an error` (741) and
  `a sibling path that resolves from the skill copy but not the command copy is a warning` (944; its
  `invocation: both` fixture no longer produces a command copy, and the warning class retires).
  `linker: a model-edge to a manual target is an error in both trees` (598) keeps its cause-agnostic
  assertion; rename it to say the Claude cause fires first. In
  `linker: a pointer to a model-only target is an error` (622) replace the stale fixture comment
  "OpenCode gets no command for beta" (the error now comes from Claude alone), or fold it into the
  new `only through Claude` test above.

- [ ] **Step 2: Run to verify failure.** `node --test tools/validate.test.ts` -> FAIL.

- [ ] **Step 3: Implement.**
  - `openCodeArtifact(root, module, kind, name)` returns
    `join(openCodeModuleRoot(root, module), openCodeBundlePath(kind === "skills" ? "skill" : kind === "commands" ? "command" : "agent", module, name))`.
  - Section 1e: the OpenCode output path is
    `opencode/${m.plugin.name}/skills/${openCodeId(m.plugin.name, outName)}/${rel}`.
  - Linker: build `targetState` with `ocModel` (skill `SKILL.md` exists and its
    `metadata?.["opencode/autoinvoke"] !== false`) and `ocUser` (skill, agent, or command exists).
    Model-edge error when `!modelReachClaude || !ocModel`; pointer error when
    `!userReachClaude || !ocUser`; causes per the interface above.
  - Delete L6 entirely.
  - L8/R1/R2: in the `opencode` tree, `owningItem` maps `p[3]` (folder ID) to its bare name with
    `splitOpenCodeId`; `skillsOf(name)` returns the owner Module's ID folder; before `existsSync(abs)`,
    re-root `opencode/<m>/skills/<m2>.<x>/...` to `opencode/<m2>/skills/<m2>.<x>/...` when `m2 !== m`.
    For a climb in OpenCode, `climb` is the ID segment; map it to the bare name before
    `emitted.has(...)`. Delete the `soundInSkillTree` branch (always `error`) and the R2 `designed`
    branch (always `error`). Delete the `manual` field from `EmittedItem` if unused.
  - Replace the stale comments at the R2 site ("`manual` deliberately withholds the OpenCode
    SKILL.md") and at the section-4 note.

- [ ] **Step 4: Run tests.** PASS; the five tooling commands, with `npm test` at `# fail 0` (the red window closes here).
- [ ] **Step 5: Commit.**

```bash
git add tools/validate.ts tools/validate.test.ts
git commit -m "feat: check OpenCode 2 paths and reachability"
```

### Task 10: Validator — OpenCode ID, handle, and shape checks (O1, O2, O4–O6)

**Files:**
- Modify: `tools/validate.ts` (new section after L4), `tools/validate.test.ts`

**Interfaces:**
- Consumes: `openCodeIndex` (Task 9), `OPENCODE_AGENT_KEYS`, `OPENCODE_AGENT_COLOR`,
  `OPENCODE_HIDE_KEY` (Task 2).
- Produces: `export function scanOpenCodeIds(text: string, plugins: string[]): { prefix: "" | "@" | "/"; id: string; line: number }[]`
  and `export function skillToolHandles(line: string): string[]` (exported from `tools/validate.ts`
  for tests); findings with the messages in spec §9.

- [ ] **Step 1: Write the failing unit tests**:

```ts
test("rendered OpenCode IDs are tokens, not path segments", () => {
  const plugins = ["deniz-process"];
  const ids = (t: string) => scanOpenCodeIds(t, plugins).map((x) => `${x.prefix}${x.id}`);
  assert.deepEqual(ids("Use deniz-process.grilling, @deniz-process.handoff or /deniz-process.wizard."), [
    "deniz-process.grilling", "@deniz-process.handoff", "/deniz-process.wizard",
  ]);
  assert.deepEqual(ids("See ../deniz-process.prototype/SKILL.md and x/deniz-process.y and deniz-process.yaml/"), []);
  assert.deepEqual(ids("deniz-process.a_b"), []);
});

test("skill-tool handles cover the three measured forms", () => {
  assert.deepEqual(skillToolHandles('Call the Skill tool with "deniz-process.grilling".'), ["deniz-process.grilling"]);
  assert.deepEqual(skillToolHandles('Call the Skill tool twice, for "grilling" and "domain-modeling".'), ["grilling", "domain-modeling"]);
  assert.deepEqual(skillToolHandles('a subagent that calls the Skill tool with "research". Use when'), ["research"]);
  assert.deepEqual(skillToolHandles("call the Skill tool for whichever skills the block names"), []);
  assert.deepEqual(
    skillToolHandles('Call the Skill tool with "codebase-design" for the vocabulary ("the interface is the test surface")'),
    ["codebase-design"],
  );
});
```

  Repository fixtures:

```ts
/** alpha carries `body`; extra manifest lines follow the alpha item. Returns error messages. */
function ocErrors(body: string, items: string[], mutate?: (root: string) => void): string[] {
  const root = makeRepo();
  writeFileSync(join(root, "external", "sp", "skills", "alpha", "SKILL.md"), `---\nname: alpha\ndescription: A\n---\n${body}\n`);
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    ["plugin:", "  name: deniz-process", "  description: P", "  version: 0.1.0", "items:", ...items].join("\n") + "\n",
  );
  buildAll(root);
  mutate?.(root);
  return validateRepo(root).filter((f) => f.level === "error").map((f) => f.message);
}

test("O1: rendered IDs must resolve with a prefix that matches the target kind", () => {
  // beta as the makeRepo overlay command deniz-beta: a conversion needs a full-file overlay
  const errors = ocErrors("Open /superpowers:beta.", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/beta",
    "    as: command",
    "    name: deniz-beta",
    "    body: overlay",
  ], (root) => {
    const file = opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md");
    assert.match(readFileSync(file, "utf8"), /Open \/deniz-process\.deniz-beta\./);
    writeFileSync(file, `${readFileSync(file, "utf8")}\nSee @deniz-process.nope and @deniz-process.deniz-beta and /deniz-process.alpha.\n`);
  });
  assert.ok(errors.some((m) => m.includes("rendered OpenCode ID @deniz-process.nope does not name an emitted")), errors.join("\n"));
  assert.ok(errors.some((m) => m.includes("rendered OpenCode ID @deniz-process.deniz-beta")), "a command needs /");
  assert.ok(errors.some((m) => m.includes("rendered OpenCode ID /deniz-process.alpha")), "a skill needs @");
  assert.ok(!errors.some((m) => m.includes("rendered OpenCode ID /deniz-process.deniz-beta")), "the rendered command pointer resolves");
});

test("O2: a bare skill-tool handle fails; a promoted one passes", () => {
  const bare = ocErrors('Call the Skill tool with "beta".', [
    "  - source: sp/skills/alpha", "    invocation: manual", "  - source: sp/skills/beta", "    invocation: auto",
  ]);
  assert.ok(bare.some((m) => m.includes('skill-tool handle "beta" is not an emitted OpenCode skill ID')), bare.join("\n"));
  const promoted = ocErrors('Call the Skill tool with "superpowers:beta".', [
    "  - source: sp/skills/alpha", "    invocation: manual", "    depends_on: [beta]", "  - source: sp/skills/beta", "    invocation: auto",
  ]);
  assert.ok(!promoted.some((m) => m.includes("skill-tool handle")), promoted.join("\n"));
});

test("O4–O6: phantom skills, skill name, and agent keys", () => {
  const errors = ocErrors("Body.", ["  - source: sp/skills/alpha", "  - source: sp/skills/beta", "    as: agent"], (root) => {
    const skills = opencodeModulePath(root, "deniz-process", "skills");
    writeFileSync(join(skills, "stray.md"), "---\nname: stray\ndescription: S\n---\n");
    mkdirSync(join(skills, "deniz-process.alpha", "nested"), { recursive: true });
    writeFileSync(join(skills, "deniz-process.alpha", "nested", "SKILL.md"), "---\nname: n\ndescription: N\n---\n");
    const skill = join(skills, "deniz-process.alpha", "SKILL.md");
    writeFileSync(skill, readFileSync(skill, "utf8").replace("name: deniz-process.alpha", "name: alpha"));
    const agent = opencodeIdPath(root, "deniz-process", "agent", "beta");
    writeFileSync(agent, readFileSync(agent, "utf8").replace("mode: subagent", "mode: subagent\nname: x\ncolor: info"));
  });
  for (const expected of ["skills/stray.md", "nested/SKILL.md", "name alpha does not equal", "agent key name", "color info"]) {
    assert.ok(errors.some((m) => m.includes(expected)), `${expected}\n${errors.join("\n")}`);
  }
});
```

  The O4–O6 message fragments above fix the message wording: O4
  `<path>: phantom OpenCode skill: <reason>`, O5 `<path>: skill name <name> does not equal its ID <id>`,
  O6 `<path>: non-native OpenCode agent key name` and `<path>: OpenCode agent color info is not #rrggbb`.
  The O1 fixture reuses the `makeRepo` overlay and lock for `deniz-beta`, so the conversion stays
  valid.

- [ ] **Step 2: Run to verify failure.** `node --test tools/validate.test.ts` -> FAIL (exports
  missing).

- [ ] **Step 3: Implement** per spec §9. Token regex, built once per run:

```ts
const alternation = plugins.map((p) => p.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|");
const TOKEN = new RegExp(`([@/]?)((?:${alternation})\\.[a-z0-9]+(?:-[a-z0-9]+)*)(?![A-Za-z0-9_/-])`, "g");
// keep a match only when: prefix "@" -> any preceding char not in [A-Za-z0-9._/@-];
// prefix "/" -> char before the slash not in [A-Za-z0-9._/-];
// no prefix -> preceding char not in [A-Za-z0-9._/@-].
const HANDLE = /Skill tool(?: twice,)? (?:with|for) ("[^"\n]+"(?:,? (?:and|or) "[^"\n]+")*)/gi;
```

  The negative lookahead does not exclude `.`, so a standalone `deniz-process.yaml` in prose is a
  token, and O1 reports it unless `yaml` is an emitted name; that is the intended signal. A path
  such as `curation/deniz-process.yaml` is not a token, because the character before its `/` is a
  letter. No committed output contains a `<plugin>.` token today (measured at `9442efa`). Run O1, O2, and O4–O6 over every `opencode/**/*.md` file and the agent and skill folders as
  spec §9 defines.

- [ ] **Step 4: Run tests.** PASS; five tooling commands.
- [ ] **Step 5: Commit.**

```bash
git add tools/validate.ts tools/validate.test.ts
git commit -m "feat: validate OpenCode 2 IDs, handles, and shapes"
```

### Task 11: Installer Destination

**Files:**
- Modify: `tools/lib/opencode-install-state.ts:504-516`, `tools/lib/opencode-install-state.test.ts:41-46,169-190`,
  `tools/install-opencode.ts:70` (only if gate G3 chooses B), `tools/install-opencode.test.ts:471-480`,
  `docs/architecture/distribution-and-installation.md` (Destination paragraph and its
  `resolveDestination` line anchor)

**Interfaces:**
- Produces: `resolveDestination(env, home?)` per spec §10.

- [ ] **Step 1: Wait for gates G3 and G4.**
- [ ] **Step 2: Write the failing tests** (replace `OPENCODE_CONFIG_DIR is refused`):

```ts
test("a non-empty absolute OPENCODE_CONFIG_DIR is the Destination", () => {
  const dir = resolve("/tmp/oc-root");
  assert.equal(resolveDestination({ HOME: join("/home", "test"), XDG_CONFIG_HOME: "/xdg", OPENCODE_CONFIG_DIR: dir }), dir);
});

test("a relative OPENCODE_CONFIG_DIR is refused", () => {
  assert.throws(() => resolveDestination({ HOME: join("/home", "test"), OPENCODE_CONFIG_DIR: "oc" }), /must be an absolute path/);
});
```

  Keep `empty OPENCODE_CONFIG_DIR is not a refusal` (empty falls through to `<home>/.config/opencode`).
  In `tools/install-opencode.test.ts` replace the refusal test with an end-to-end case: set
  `OPENCODE_CONFIG_DIR` to a fresh temp directory, run `install --all --yes`, and assert
  `<dir>/.deniz-skills/install.json` exists and `<xdg>/opencode` was not created.
  If G3 = B, add a test that `tools/install-opencode.ts` derives `home` from `os.homedir()` (inject
  through the existing `io.home` seam) and remove the `HOME`/`USERPROFILE` order. B also needs
  `resolveDestination` itself to stop preferring `env.HOME` over its `home` argument
  (`tools/lib/opencode-install-state.ts:511`); otherwise a Windows shell that sets `HOME` (Git Bash
  does) still wins over `os.homedir()`. Under B, the state tests that pass only `HOME` in `env`
  (for example `empty OPENCODE_CONFIG_DIR is not a refusal`) pass `home` explicitly instead.

- [ ] **Step 3: Run to verify failure.**
  `node --test tools/lib/opencode-install-state.test.ts tools/install-opencode.test.ts` -> FAIL.
- [ ] **Step 4: Implement** the spec §10 body (`isAbsolute` from `node:path`). In
  `docs/architecture/distribution-and-installation.md`, update the `resolveDestination` link's line
  anchor (`#L504-L516`) to the new range, and, if G4 refuses a relative value or G3 chooses B, state
  that refusal and the `<home>` source in the Destination paragraph (canon currently states neither).
- [ ] **Step 5: Run tests.** PASS; five tooling commands.
- [ ] **Step 6: Commit.**

```bash
git add tools/lib/opencode-install-state.ts tools/lib/opencode-install-state.test.ts tools/install-opencode.ts tools/install-opencode.test.ts docs/architecture/distribution-and-installation.md
git commit -m "feat: install into OPENCODE_CONFIG_DIR when it is set"
```

### Task 12: Regenerate, review, and close the step-2 gaps

**Files:**
- Regenerate: `opencode/`, `dist/`, `docs/ledger.json` (and prove no change elsewhere)
- Modify: `docs/ROADMAP.md` (Known Gaps), `docs/architecture/transformation-and-emission.md`
  (`validate` shape checks gain O5), `docs/architecture/references-and-linking.md` (only if
  an implemented detail differs from canon)

- [ ] **Step 1: Run the generated-output gate.**

```text
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
npm run build
npm run inventory
npm run validate
```

  Expected: the first five exit 0; `build` and `inventory` exit 0; `validate` exits nonzero with
  exactly the curation-owned findings of the decision packet (Appendix A2: 16 O2 handle findings on
  12 lines) and no other new error. Any other finding is a defect
  in Tasks 2–11: stop and fix it there. Rerun `npm test` after `build`: the pack tests in
  `tools/install-opencode.test.ts` (around line 998) read the committed `opencode/` and `dist/`, so
  the first run, in CI order, still saw the stale trees.

- [ ] **Step 2: Prove the non-OpenCode trees did not move.**

```text
git diff --exit-code -- plugins codex .claude-plugin .agents/plugins/marketplace.json docs/inventory.md
```

  Expected: no output.

- [ ] **Step 3: Review the OpenCode diff semantically.** Check, and write the counts in the commit
  body:
  - `find opencode -path "*/skills/*" -name SKILL.md | wc -l` -> 115; `ls opencode/*/commands` ->
    none; `ls opencode/*/agents` -> `deniz-dotnet-akka.akka-net-specialist.md`,
    `deniz-dotnet-general.roslyn-incremental-generator-specialist.md`;
    `find opencode -name BODY.md | wc -l` -> 0.
  - `grep -rl "opencode/autoinvoke: false" opencode | wc -l` -> 27, and the files are exactly the
    27 `manual` items of Appendix A1 (no absent item carries an upstream DMI today).
  - Every `skills/<id>/SKILL.md` `name` equals `<id>` (O5 is green).
  - Executable modes: `node -e` over `opencode/deniz-process/manifest.json` lists the seven
    `100755` files at their ID paths.
  - Sibling climbs: `grep -rnoE "\]\((\.\./)+deniz-[a-z-]+\.[a-z0-9-]+/" opencode | wc -l` -> 34, and
    no `(../<bare-emitted-name>/` link remains.
  - Pointers: no `/<plugin>.<name>` remains for a skill target; `@` pointers count equals the
    ledger's pointer edges.
  - Ledger: only `opencode` projections changed (`git diff docs/ledger.json` shows no `claude` or
    `codex` hunk).
  - `dist/`: only the Destination change.
- [ ] **Step 4: Prove idempotence.** Save the diff, rerun, compare:

```bash
git diff --binary > "$SCRATCH/first.diff"
npm run build && npm run inventory
git diff --binary | cmp - "$SCRATCH/first.diff"
```

  (`$SCRATCH` is any directory outside the repository.) Expected: `cmp` prints nothing.
- [ ] **Step 5: Update canon state.** In `docs/ROADMAP.md` delete the Known Gaps entries "OpenCode
  2 emission is not implemented", "OpenCode 2 references are not rewritten", "OpenCode 2 identity
  and validation are not implemented", "OpenCode 2 ledger probes are not implemented", "OpenCode 2
  Destination is not implemented", "Parked-path regex interpolation is unescaped", and the
  `tools/` part of "OpenCode 1 tests and probes remain" except `tools/repository-docs.test.ts`
  (its lab assertion moves in Task 14 and its Release pins in Task 19); keep the `experiments/` part
  for Task 14. Remove the finished compiler sub-step from Next Up item 1. Update the Current State
  bullet that says the checkout does not implement the rules. Keep
  "Converted command paths" until Task 13 decides `as: command` items. Add the O5 bullet (required by
  D3: skill `name` equals its ID) to the shape-check list in `transformation-and-emission.md`. Retarget the canon symbol links the
  implementation moved: `transformation-and-emission.md` names `OPENCODE_SKILL_KEYS` in
  `tools/lib/ledger.ts` (now `tools/lib/opencode-target.ts`) and links `emitOpenCodeSkill` (now
  `emitOpenCode` and `adaptOpenCodeSkillDocument`). In `references-and-linking.md` "Ledger
  semantics", add the new OpenCode projection fields (`identity`, `advertised`,
  `metadataTransformations`) to the projected-state list, which today names only OpenCode drops.
  Move the ROADMAP Date only if it is not already 2026-10-08.
- [ ] **Step 6: Commit** (on the curator's word).

```bash
git add opencode dist docs/ledger.json docs/ROADMAP.md docs/architecture
git commit -m "build: regenerate OpenCode 2 output"
```

---

## Phase B2: W0 correctness (step 3)

The curator settled the W0 decisions on 2026-10-08; they are spec "W0 correctness", decisions 1–7,
and they answer Appendix A. Canon already states the rules
([References and linking](../../architecture/references-and-linking.md): harness phrasing, handoff
templates, harness vocabulary check, path claims; [ADR-0008](../../adr/0008-references-are-symbols.md);
[`curation/SCHEMA.md`](../../../curation/SCHEMA.md#dependencies)); the spec sections 12–16 are the
execution design. Phase B2 replaces Task 13.

Rules for this phase:

- **Order.** W0.1–W0.8 are tooling, W0.9 measures, W0.10–W0.13 are curation, W0.14 is the only
  regeneration, W0.15 merges. Each task starts on the curator's word, and nothing is pushed.
- **Generated trees are regenerated only in W0.14.** Any earlier task may run `npm run build` to
  inspect output, but before it commits, the generated paths must be back at `HEAD`:

```bash
GEN="plugins opencode codex dist .claude-plugin .agents/plugins/marketplace.json docs/inventory.md docs/ledger.json"
git restore --source=HEAD --worktree -- $GEN
git clean -fdq -- plugins opencode codex dist
git status --short -- $GEN   # expected: no output
```

- **Tooling gate.** Each tooling task ends with the five tooling commands (Checkpoints); every
  command exits 0 and `npm test` reports `# fail 0`. `npm run validate` is not part of that gate:
  it is red from W0.5 until W0.12, with exactly the findings W0.9 fixes.
- **Behavior-neutral step.** W0.1 only moves grammar; its build output must be byte-identical.
- **Curation layer.** Comments carry the why beside the item and no names or dates. A new patch uses
  the two-pass `eject --patch`. Extending an existing patch re-cuts it, because `--force` deletes
  the old patch before it lays a pristine working copy:

```bash
SCRATCH=$(mktemp -d)
cp overlays/<plugin>/<item>/overlay.patch "$SCRATCH/<item>.patch"
npm run eject -- <plugin> <item> --patch --force      # pristine upstream working copy
git apply -p1 --directory=overlays/<plugin>/<item> "$SCRATCH/<item>.patch"
# make the new edit in the working copy
npm run eject -- <plugin> <item> --patch              # cut, verify, stamp the lock
```

  A full overlay (`ask-deniz`) is edited in place; its lock stamps upstream bytes, not overlay text,
  so it needs no re-bless.
- **Commits** end with a blank line and the session's `Co-Authored-By` trailer.

### Task W0.1: Move the sibling-climb grammar into `refs.ts` (behavior-neutral)

**Files:**
- Modify: `tools/lib/refs.ts`, `tools/lib/refs.test.ts`, `tools/lib/rewrite.ts` (`CLIMB`,
  `rewriteOpenCodeSiblingClimbs`)

**Interfaces:**
- Produces: `PathClaim` and `scanPathClaims(content: string, depthBelowSkillFolder: number): PathClaim[]`
  in `tools/lib/refs.ts` (landing climbs only in this task; W0.7 adds item-root claims).

- [ ] **Step 1: Write the failing test** in `tools/lib/refs.test.ts`:

```ts
test("landing climbs are path claims only at the depth that reaches the skills directory", () => {
  const claims = (t: string, d: number) => scanPathClaims(t, d).map((c) => `${c.kind}:${c.segment}:${c.path}`);
  assert.deepEqual(claims("[a](../aspireify/SKILL.md)", 0), ["climb:aspireify:../aspireify/SKILL.md"]);
  assert.deepEqual(claims("[a](../../aspireify/SKILL.md)", 1), ["climb:aspireify:../../aspireify/SKILL.md"]);
  assert.deepEqual(claims("[a](../../aspireify/SKILL.md)", 0), [], "wrong depth");
  assert.deepEqual(claims("x/../aspireify/y", 0), [], "inside a longer path");
  assert.deepEqual(claims('AddCSharpApp("api", "../Api")', 0), [], "uppercase is illustrative");
  assert.deepEqual(claims("see `../using-superpowers/references/`.", 0), [
    "climb:using-superpowers:../using-superpowers/references/",
  ]);
  assert.deepEqual(claims("read ../beta/notes.md.", 0), ["climb:beta:../beta/notes.md"], "one trailing dot dropped");
});
```

- [ ] **Step 2: Run to verify failure.** `node --test tools/lib/refs.test.ts` -> FAIL
  (`scanPathClaims` is not exported).
- [ ] **Step 3: Implement.** In `tools/lib/refs.ts`:

```ts
/** A relative path the linker can attribute: a climb onto the shared skills directory, or (W0.7) an item root. */
export interface PathClaim {
  kind: "climb" | "item-root";
  index: number;
  segmentIndex: number;
  segment: string;
  path: string;
}

const CLIMB = /((?:\.\.\/)+)([a-z0-9]+(?:-[a-z0-9]+)*)(?=\/)/g;
const PATH_TAIL = /^\/[^\s"'`)\]]*/;
const CONTINUES_PATH = /[A-Za-z0-9._/-]/;

function claimPath(content: string, start: number, afterSegment: number): string {
  const path = content.slice(start, afterSegment) + (PATH_TAIL.exec(content.slice(afterSegment))?.[0] ?? "");
  return path.endsWith(".") ? path.slice(0, -1) : path;
}

export function scanPathClaims(content: string, depthBelowSkillFolder: number): PathClaim[] {
  const out: PathClaim[] = [];
  for (const m of content.matchAll(CLIMB)) {
    const before = m.index > 0 ? (content[m.index - 1] as string) : "";
    const climb = m[1] as string;
    const segment = m[2] as string;
    if ((before && CONTINUES_PATH.test(before)) || climb.length / 3 !== depthBelowSkillFolder + 1) {
      continue;
    }
    const segmentIndex = m.index + climb.length;
    out.push({ kind: "climb", index: m.index, segmentIndex, segment, path: claimPath(content, m.index, segmentIndex + segment.length) });
  }
  return out;
}
```

  In `tools/lib/rewrite.ts`, delete `CLIMB` and rebuild `rewriteOpenCodeSiblingClimbs` on the scan:

```ts
export function rewriteOpenCodeSiblingClimbs(content: string, depthBelowSkillFolder: number, skillIds: Map<string, string>): string {
  let out = "";
  let cut = 0;
  for (const claim of scanPathClaims(content, depthBelowSkillFolder)) {
    const id = skillIds.get(claim.segment);
    if (id === undefined) {
      continue;
    }
    out += content.slice(cut, claim.segmentIndex) + id;
    cut = claim.segmentIndex + claim.segment.length;
  }
  return out + content.slice(cut);
}
```

- [ ] **Step 4: Run tests.** `node --test tools/lib/refs.test.ts tools/lib/rewrite.test.ts` -> PASS;
  the existing "sibling climbs are respelled only when they land on the shared skills directory"
  test passes unchanged.
- [ ] **Step 5: Prove behavior neutrality.**

```bash
npm run build
git diff --exit-code -- $GEN
```

  Expected: no output, exit 0.
- [ ] **Step 6:** the five tooling commands.
- [ ] **Step 7: Commit.**

```bash
git add tools/lib/refs.ts tools/lib/refs.test.ts tools/lib/rewrite.ts
git commit -m "refactor: move the sibling-climb grammar into refs.ts"
```

### Task W0.2: Skill-tool call grammar

**Files:**
- Modify: `tools/lib/refs.ts`, `tools/lib/refs.test.ts`

**Interfaces:**
- Produces: `SkillToolCall`, `scanSkillToolCalls(content: string): SkillToolCall[]`,
  `straySkillToolMentions(content: string): number[]` (spec section 12).

- [ ] **Step 1: Write the failing tests** in `tools/lib/refs.test.ts`:

```ts
test("skill-tool calls: three forms, four verbs, payloads located", () => {
  const forms = (t: string) =>
    scanSkillToolCalls(t).map(
      (c) => `${c.verb}|${c.form}|${c.payloads.map((p) => `${p.text}@${p.index}`).join(",")}|${t.slice(c.index, c.end)}`,
    );
  assert.deepEqual(forms('Call the Skill tool with "mattpocock-skills:grilling".'), [
    'Call|with|mattpocock-skills:grilling@26|Call the Skill tool with "mattpocock-skills:grilling"',
  ]);
  assert.deepEqual(forms('Always call the Skill tool twice, for "a" and "b", to pin'), [
    'call|twice|a@39,b@47|call the Skill tool twice, for "a" and "b"',
  ]);
  assert.deepEqual(forms('a subagent that calls the Skill tool with "research". Use'), [
    'calls|with|research@43|calls the Skill tool with "research"',
  ]);
  assert.deepEqual(forms('by calling the Skill tool with "prototype". Links'), [
    'calling|with|prototype@32|calling the Skill tool with "prototype"',
  ]);
  assert.deepEqual(forms("should call the Skill tool for."), ["call|generic||call the Skill tool for"]);
  assert.deepEqual(forms("call the Skill tool for whichever skills"), ["call|generic||call the Skill tool for"]);
  assert.deepEqual(forms('call the Skill tool for "x"'), [], "a single for-handle is not a form");
  assert.deepEqual(forms("call the Skill tool format"), []);
});

test("a skill-tool mention outside a recognized call is stray", () => {
  assert.deepEqual(straySkillToolMentions('Call the Skill tool with "a:b". call the Skill tool for.'), []);
  const text = 'Invoke the Skill tool on "x". Use the skill tool.';
  assert.deepEqual(straySkillToolMentions(text), [text.indexOf("Skill tool"), text.indexOf("skill tool.")]);
});
```

  The `@` offsets are the payload's first character inside the quotes; recompute them with
  `indexOf` if a literal changes.
- [ ] **Step 2: Run to verify failure.** `node --test tools/lib/refs.test.ts` -> FAIL (exports missing).
- [ ] **Step 3: Implement** in `tools/lib/refs.ts`:

```ts
export interface SkillToolCall {
  index: number;
  end: number;
  verb: "Call" | "call" | "calls" | "calling";
  form: "with" | "twice" | "generic";
  payloads: { index: number; text: string }[];
}

// The skill-tool call template (references-and-linking.md "Handoff templates"). Closed on purpose:
// a sentence outside these three forms is a stray mention, which validate rejects.
const SKILL_TOOL_CALL =
  /\b(Call|call|calls|calling) the Skill tool(?: with "([^"\n]*)"| twice, for "([^"\n]*)" and "([^"\n]*)"| for(?![A-Za-z]| "))/g;
const SKILL_TOOL_MENTION = /\bskill tool/gi;

export function scanSkillToolCalls(content: string): SkillToolCall[] {
  const out: SkillToolCall[] = [];
  for (const m of content.matchAll(SKILL_TOOL_CALL)) {
    const texts = m[2] !== undefined ? [m[2]] : m[3] !== undefined ? [m[3], m[4] as string] : [];
    let from = m.index;
    const payloads = texts.map((text) => {
      const index = content.indexOf(`"${text}"`, from) + 1;
      from = index + text.length + 1;
      return { index, text };
    });
    out.push({
      index: m.index,
      end: m.index + m[0].length,
      verb: m[1] as SkillToolCall["verb"],
      form: m[2] !== undefined ? "with" : m[3] !== undefined ? "twice" : "generic",
      payloads,
    });
  }
  return out;
}

export function straySkillToolMentions(content: string): number[] {
  const spans = scanSkillToolCalls(content);
  return [...content.matchAll(SKILL_TOOL_MENTION)]
    .map((m) => m.index)
    .filter((index) => !spans.some((s) => index >= s.index && index < s.end));
}
```

- [ ] **Step 4: Run tests.** `node --test tools/lib/refs.test.ts` -> PASS; then the five tooling commands.
- [ ] **Step 5: Commit.**

```bash
git add tools/lib/refs.ts tools/lib/refs.test.ts
git commit -m "feat: scan skill-tool call spans in refs.ts"
```

### Task W0.3: Harness phrasing renderer (pure)

**Files:**
- Modify: `tools/lib/rewrite.ts`, `tools/lib/rewrite.test.ts`

**Interfaces:**
- Consumes: `scanSkillToolCalls` (W0.2).
- Produces: `renderHarnessPhrasing(content, style)`, `localize(content, map, style)`,
  `CLAUDE_ONLY_VOCABULARY`, `claudeOnlyVocabulary(content): { index: number; match: string }[]`.

- [ ] **Step 1: Write the failing tests** in `tools/lib/rewrite.test.ts` (import the four new
  exports):

```ts
const PHRASES = [
  'Call the Skill tool with "sp:a".',
  'Always call the Skill tool twice, for "sp:a" and "sp:b".',
  'a subagent that calls the Skill tool with "sp:a". By calling the Skill tool with "sp:b".',
  "naming which skills the next agent should call the Skill tool for.",
  "Subagent (general-purpose):",
  "dispatch a `general-purpose`\nsubagent. Then send one message with two `Agent` calls, one `Agent` call",
].join("\n");

test("harness phrasing: Claude Code is identity", () => {
  assert.equal(renderHarnessPhrasing(PHRASES, "claude"), PHRASES);
});

test("harness phrasing: OpenCode names its skill and subagent tools", () => {
  assert.equal(
    renderHarnessPhrasing(PHRASES, "opencode"),
    [
      'Call the `skill` tool with "sp:a".',
      'Always call the `skill` tool twice, for "sp:a" and "sp:b".',
      'a subagent that calls the `skill` tool with "sp:a". By calling the `skill` tool with "sp:b".',
      "naming which skills the next agent should call the `skill` tool for.",
      "Subagent (general):",
      "dispatch a `general`\nsubagent. Then send one message with two `subagent` calls, one `subagent` call",
    ].join("\n"),
  );
});

test("harness phrasing: Codex invokes and names no subagent tool or type", () => {
  assert.equal(
    renderHarnessPhrasing(PHRASES, "codex"),
    [
      "Invoke `sp:a`.",
      "Always invoke `sp:a` and `sp:b`.",
      "a subagent that invokes `sp:a`. By invoking `sp:b`.",
      "naming which skills the next agent should invoke.",
      "Subagent:",
      "dispatch a subagent. Then send one message with two subagent calls, one subagent call",
    ].join("\n"),
  );
});

test("localize renders the facts after the phrasing", () => {
  const map = buildRewriteMap([manifest], components);
  const line = 'Call the Skill tool with "superpowers:brainstorming".';
  assert.equal(localize(line, map, "claude"), 'Call the Skill tool with "deniz-process:brainstorming".');
  assert.equal(localize(line, map, "opencode"), 'Call the `skill` tool with "deniz-process.brainstorming".');
  assert.equal(localize(line, map, "codex"), "Invoke `$deniz-process:brainstorming`.");
});

test("every non-Claude rendering is free of Claude-only vocabulary", () => {
  assert.deepEqual(
    claudeOnlyVocabulary(PHRASES).map((hit) => hit.match).sort(),
    ["Skill tool", "Skill tool", "Skill tool", "Skill tool", "Skill tool", "Subagent (general-purpose)", "`Agent`", "`Agent`", "`general-purpose`"].sort(),
  );
  for (const style of ["opencode", "codex"] as const) {
    assert.deepEqual(claudeOnlyVocabulary(renderHarnessPhrasing(PHRASES, style)), [], style);
  }
});
```

- [ ] **Step 2: Run to verify failure.** `node --test tools/lib/rewrite.test.ts` -> FAIL (exports missing).
- [ ] **Step 3: Implement** in `tools/lib/rewrite.ts` (import `scanSkillToolCalls` and
  `SkillToolCall` from `./refs.ts`):

```ts
const CODEX_VERB = { Call: "Invoke", call: "invoke", calls: "invokes", calling: "invoking" } as const;

// Closed dispatch table (references-and-linking.md "Harness phrasing"). Codex names no subagent tool
// or agent type: neither is recorded in this repository's research.
const DISPATCH: { pattern: RegExp; opencode: string; codex: string }[] = [
  { pattern: /Subagent \(general-purpose\)/g, opencode: "Subagent (general)", codex: "Subagent" },
  { pattern: /`general-purpose`(\s+)subagent/g, opencode: "`general`$1subagent", codex: "subagent" },
  { pattern: /`Agent`( calls?)\b/g, opencode: "`subagent`$1", codex: "subagent$1" },
];

/** Claude Code tool words that must not survive in an OpenCode or Codex tree. */
export const CLAUDE_ONLY_VOCABULARY: readonly RegExp[] = [
  /\bskill tool/gi,
  /Subagent \(general-purpose\)/g,
  /`general-purpose`/g,
  /\bgeneral-purpose\s+(?:sub)?agent\b/g,
  /`Agent`/g,
  /\bAgent tool\b/g,
  /\bTask tool\b/g,
  /\bsubagent_type\b/g,
];

export function claudeOnlyVocabulary(content: string): { index: number; match: string }[] {
  return CLAUDE_ONLY_VOCABULARY.flatMap((re) => [...content.matchAll(re)].map((m) => ({ index: m.index, match: m[0] }))).sort(
    (a, b) => a.index - b.index,
  );
}

function renderCall(content: string, call: SkillToolCall, style: "opencode" | "codex"): string {
  const span = content.slice(call.index, call.end);
  if (style === "opencode") {
    return span.replace("the Skill tool", "the `skill` tool");
  }
  const verb = CODEX_VERB[call.verb];
  return call.payloads.length ? `${verb} ${call.payloads.map((p) => `\`${p.text}\``).join(" and ")}` : verb;
}

/** Renders the skill-tool call and dispatch words for one harness; facts stay neutral. */
export function renderHarnessPhrasing(content: string, style: RefStyle): string {
  if (style === "claude") {
    return content;
  }
  let out = "";
  let cut = 0;
  for (const call of scanSkillToolCalls(content)) {
    out += content.slice(cut, call.index) + renderCall(content, call, style);
    cut = call.end;
  }
  out += content.slice(cut);
  for (const row of DISPATCH) {
    out = out.replace(row.pattern, row[style]);
  }
  return out;
}

/** Harness phrasing first, then the facts it left neutral. */
export function localize(content: string, map: Map<string, RewriteTarget>, style: RefStyle): string {
  return rewriteRefs(renderHarnessPhrasing(content, style), map, style);
}
```

- [ ] **Step 4: Run tests.** `node --test tools/lib/rewrite.test.ts` -> PASS; then the five tooling commands.
- [ ] **Step 5: Commit.**

```bash
git add tools/lib/rewrite.ts tools/lib/rewrite.test.ts
git commit -m "feat: render skill-tool calls and dispatch words per harness"
```

### Task W0.4: Localize harness phrasing in every tree

**Files:**
- Modify: `tools/build.ts` (`rewriteTree`, `rewriteOpenCodeTree`: the three `rewriteRefs` call
  sites), `tools/build.test.ts`

- [ ] **Step 1: Write the failing test** in `tools/build.test.ts`:

```ts
test("each harness renders the skill-tool call and dispatch words in its own words", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    '---\nname: alpha\ndescription: A\n---\n\nCall the Skill tool with "superpowers:delta".\n\nSubagent (general-purpose):\n',
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n    depends_on: [delta]\n  - source: sp/skills/delta\n",
  );
  buildAll(root);
  const read = (path: string) => readFileSync(path, "utf8");
  assert.match(
    read(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md")),
    /Call the Skill tool with "deniz-process:delta"\.\n\nSubagent \(general-purpose\):/,
  );
  assert.match(
    read(opencodeIdPath(root, "deniz-process", "skill", "alpha", "SKILL.md")),
    /Call the `skill` tool with "deniz-process\.delta"\.\n\nSubagent \(general\):/,
  );
  assert.match(
    read(codexPluginPath(root, "deniz-process", "skills", "alpha", "SKILL.md")),
    /Invoke `\$deniz-process:delta`\.\n\nSubagent:/,
  );
});
```

- [ ] **Step 2: Run to verify failure.** `node --test tools/build.test.ts` -> FAIL (the OpenCode and
  Codex assertions; Claude already passes).
- [ ] **Step 3: Implement.** In `tools/build.ts`, import `localize` and call it where
  `rewriteTree` and `rewriteOpenCodeTree` call `rewriteRefs` today (Markdown only; the skill-folder
  loop keeps applying `rewriteOpenCodeSiblingClimbs` after it). Update the `rewriteOpenCodeTree` doc
  comment: Markdown gets harness phrasing and the dotted-ID rendering.
- [ ] **Step 4: Run tests.** `node --test tools/build.test.ts` -> PASS; then the five tooling
  commands. Restore the generated paths if you built the real repository.
- [ ] **Step 5: Commit.**

```bash
git add tools/build.ts tools/build.test.ts
git commit -m "feat: localize harness phrasing in every tree"
```

### Task W0.5: Validator — skill-tool payload rule replaces O2; harness vocabulary check

**Files:**
- Modify: `tools/validate.ts` (delete `SKILL_TOOL_HANDLE`, `skillToolHandles`, and the O2 loop in
  the O1/O2 section; add H1 after the linker section and V after L4), `tools/validate.test.ts`
  (delete the tests "skill-tool handles cover the three measured forms" and "O2: a bare skill-tool
  handle fails; a promoted one passes", drop `skillToolHandles` from the import)

**Interfaces:**
- Consumes: `scanSkillToolCalls`, `straySkillToolMentions`, `scanRefs` (`refs.ts`);
  `claudeOnlyVocabulary` (`rewrite.ts`); the existing `ownNs`, `walk`, `pluginsDir`, `ocDir`.
- Produces: the H1 and V messages of spec section 13.

- [ ] **Step 1: Write the failing tests** in `tools/validate.test.ts` (the existing `ocErrors`
  helper builds the fixture and returns error messages):

```ts
test("H1: a skill-tool handle must be a namespaced fact; stray skill-tool prose fails", () => {
  const bareItems = ["  - source: sp/skills/alpha", "    invocation: manual", "  - source: sp/skills/beta", "    invocation: auto"];
  const bare = ocErrors('Call the Skill tool with "beta".', bareItems);
  assert.ok(bare.some((m) => m.includes('skill-tool handle "beta" is not a namespaced fact')), bare.join("\n"));
  assert.ok(!bare.some((m) => m.includes("Claude-only vocabulary")), "a recognized call renders, so it never leaks");
  const promoted = ocErrors('Call the Skill tool with "superpowers:beta".', [
    "  - source: sp/skills/alpha",
    "    invocation: manual",
    "    depends_on: [beta]",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ]);
  assert.ok(!promoted.some((m) => m.includes("skill-tool") || m.includes("Claude-only vocabulary")), promoted.join("\n"));
  const stray = ocErrors("Use the Skill tool to load beta.", bareItems);
  assert.ok(stray.some((m) => m.includes('"skill tool" outside a recognized skill-tool call')), stray.join("\n"));
  assert.equal(stray.filter((m) => m.includes('Claude-only vocabulary "Skill tool"')).length, 2, "opencode/ and codex/");
});

test("V: Claude-only dispatch words in a generated tree are errors", () => {
  const errors = ocErrors("Body.", ["  - source: sp/skills/alpha"], (root) => {
    const file = codexPluginPath(root, "deniz-process", "skills", "alpha", "SKILL.md");
    writeFileSync(file, `${readFileSync(file, "utf8")}\nUse the Task tool with subagent_type set.\n`);
  });
  for (const word of ["Task tool", "subagent_type"]) {
    assert.ok(errors.some((m) => m.includes(`Claude-only vocabulary "${word}" in codex/`)), errors.join("\n"));
  }
});
```

- [ ] **Step 2: Run to verify failure.** `node --test tools/validate.test.ts` -> FAIL (old O2
  message, no H1 or V messages).
- [ ] **Step 3: Implement** in `tools/validate.ts`:

```ts
const lineOf = (text: string, index: number): number => text.slice(0, index).split("\n").length;

// H1: the skill-tool call is a handoff template (references-and-linking.md). Checked once, on the
// canonical tree, where a handle still carries its namespace; the linker and O1 then check the target.
for (const file of existsSync(pluginsDir) ? [...walk(pluginsDir)].filter((f) => f.endsWith(".md")) : []) {
  const rel = relative(root, file).replaceAll("\\", "/");
  const text = readFileSync(file, "utf8");
  for (const call of scanSkillToolCalls(text)) {
    for (const payload of call.payloads) {
      const refs = scanRefs(payload.text);
      const ref = refs[0];
      const fact = refs.length === 1 && ref?.kind === "model" && ref.address === payload.text && ownNs.has(ref.ns);
      if (!fact) {
        findings.push({
          level: "error",
          message: `${rel}:${lineOf(text, payload.index)}: skill-tool handle "${payload.text}" is not a namespaced fact — author it as "ns:${payload.text}" with a matching depends_on`,
        });
      }
    }
  }
  for (const index of straySkillToolMentions(text)) {
    findings.push({
      level: "error",
      message: `${rel}:${lineOf(text, index)}: "skill tool" outside a recognized skill-tool call — reword it into a form in curation/SCHEMA.md Dependencies`,
    });
  }
}

// V: Claude Code tool words must not reach a harness that has no such tool.
for (const tree of ["opencode", "codex"] as const) {
  const dir = join(root, tree);
  for (const file of existsSync(dir) ? [...walk(dir)].filter((f) => f.endsWith(".md")) : []) {
    const rel = relative(root, file).replaceAll("\\", "/");
    const text = readFileSync(file, "utf8");
    for (const hit of claudeOnlyVocabulary(text)) {
      findings.push({
        level: "error",
        message: `${rel}:${lineOf(text, hit.index)}: Claude-only vocabulary "${hit.match}" in ${tree}/ — reword it into a form the harness phrasing renders (curation/SCHEMA.md Dependencies)`,
      });
    }
  }
}
```

  Rename the O1/O2 section comment to O1 and the O4–O6 heading accordingly.
- [ ] **Step 4: Run tests.** `node --test tools/validate.test.ts` -> PASS; then the five tooling
  commands.
- [ ] **Step 5: Commit.**

```bash
git add tools/validate.ts tools/validate.test.ts
git commit -m "feat: check skill-tool handles on the canonical tree and Claude-only words elsewhere"
```

### Task W0.6: Handoff templates

**Files:**
- Modify: `tools/lib/refs.ts`, `tools/lib/refs.test.ts`, `tools/lib/rewrite.ts` (export
  `addressOf`), `tools/validate.ts` (estate names and H2–H4 beside H1), `tools/validate.test.ts`

**Interfaces:**
- Produces: `Handoff`, `scanHandoffs(content: string): Handoff[]`; `estateNames` in
  `validateRepo` (reused by W0.8).

- [ ] **Step 1: Write the failing tests.** In `tools/lib/refs.test.ts`:

```ts
test("handoff templates find bare backticked names in load-bearing forms", () => {
  const hits = (t: string) => scanHandoffs(t).map((h) => `${h.template}:${h.name}`);
  assert.deepEqual(hits("Use the `binlog-generation` skill to generate a log."), ["imperative:binlog-generation"]);
  assert.deepEqual(hits("Follow the complete procedure in the `platform-detection`\nskill. Read props."), [
    "imperative:platform-detection",
  ]);
  assert.deepEqual(hits("Load `filter-syntax` only when filtered."), ["load:filter-syntax"]);
  assert.deepEqual(hits("invoke `test-gap-analysis` and `test-anti-patterns` when available"), [
    "load:test-gap-analysis",
    "load:test-anti-patterns",
  ]);
  assert.deepEqual(hits("Re-invoke `aspireify`; confirm the path"), ["load:aspireify"]);
  assert.deepEqual(hits("- **A merge went sideways** → `resolving-merge-conflicts`."), ["route:resolving-merge-conflicts"]);
  assert.deepEqual(hits("Load `x` skill first."), ["imperative:x"], "one hit when two templates overlap");
  assert.deepEqual(hits("Use the `dotnet-msbuild:binlog-generation` skill."), [], "a fact never matches");
  assert.deepEqual(hits("see the `filter-syntax` skill for details"), [], "a reference stays a candidate");
  assert.deepEqual(hits("- running tests (use `run-tests`)"), [], "a routing hint without 'skill'");
  assert.deepEqual(hits("Use the following\n- the `grilling` skill"), [], "a list item ends the sentence");
  assert.deepEqual(hits("Use it. The `grilling` skill drives it."), [], "a period ends the sentence");
});
```

  In `tools/validate.test.ts`:

```ts
test("H2–H4: a bare estate name in a handoff template fails; a fact and an outside name pass", () => {
  const bare = ocErrors("Load `beta` first. Use the `docker` skill.", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ]);
  assert.ok(bare.some((m) => m.includes("load-bearing handoff names `beta` bare")), bare.join("\n"));
  assert.ok(!bare.some((m) => m.includes("`docker`")), "outside the estate");
  const promoted = ocErrors("Load `superpowers:beta` first.", [
    "  - source: sp/skills/alpha",
    "    depends_on: [beta]",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ]);
  assert.ok(!promoted.some((m) => m.includes("load-bearing handoff")), promoted.join("\n"));
});

test("H2–H4: an excluded name is reported as not emitted", () => {
  const errors = ocErrors("Use the `beta` skill.", ["  - source: sp/skills/alpha", "  - source: sp/skills/beta", "    exclude: true"]);
  assert.ok(errors.some((m) => m.includes("names `beta`, which this estate does not emit")), errors.join("\n"));
});
```

- [ ] **Step 2: Run to verify failure.** `node --test tools/lib/refs.test.ts tools/validate.test.ts`
  -> FAIL.
- [ ] **Step 3: Implement.** In `tools/lib/refs.ts`:

```ts
export interface Handoff {
  template: "imperative" | "load" | "route";
  name: string;
  index: number;
}

const NAME = String.raw`\x60([a-z][a-z0-9]*(?:-[a-z0-9]+)*)\x60`;
// A sentence stops at . ; : ! ? |, a blank line, or a newline that opens a list, table, heading, or quote.
const SENTENCE = String.raw`(?:[^.;:!?|\n]|\n(?![ \t]*(?:\n|[-*+>#|]|\d+\.)))*?`;
const IMPERATIVE = new RegExp(String.raw`\b(?:Load|load|Use|use|Follow|follow|Invoke|invoke|Call|call)\b${SENTENCE}${NAME}\s+skill\b`, "g");
const LOAD = new RegExp(String.raw`\b(?:Load|load|Invoke|invoke)\s+${NAME}`, "g");
const LIST_TAIL = new RegExp(String.raw`^(?:,\s*|\s+and\s+|\s+or\s+)${NAME}`);
const ROUTE = new RegExp(String.raw`→\s*\*{0,2}${NAME}`, "g");

/** Bare backticked names in the load-bearing handoff templates; `index` is the name's opening backtick. */
export function scanHandoffs(content: string): Handoff[] {
  const out: Handoff[] = [];
  const at = (start: number, matched: string, name: string): number => start + matched.lastIndexOf(`\`${name}\``);
  for (const m of content.matchAll(IMPERATIVE)) {
    out.push({ template: "imperative", name: m[1] as string, index: at(m.index, m[0], m[1] as string) });
  }
  for (const m of content.matchAll(LOAD)) {
    out.push({ template: "load", name: m[1] as string, index: at(m.index, m[0], m[1] as string) });
    let cursor = m.index + m[0].length;
    for (let t = LIST_TAIL.exec(content.slice(cursor)); t; t = LIST_TAIL.exec(content.slice(cursor))) {
      out.push({ template: "load", name: t[1] as string, index: at(cursor, t[0], t[1] as string) });
      cursor += t[0].length;
    }
  }
  for (const m of content.matchAll(ROUTE)) {
    out.push({ template: "route", name: m[1] as string, index: at(m.index, m[0], m[1] as string) });
  }
  return out
    .sort((a, b) => a.index - b.index)
    .filter((hit, i, all) => all.findIndex((other) => other.index === hit.index) === i);
}
```

  In `tools/lib/rewrite.ts`, export `addressOf`. In `tools/validate.ts`, after `targetState` is
  built, add:

```ts
// The handoff universe (references-and-linking.md "Handoff templates"): every scanned upstream
// address and frontmatter name, every manifest output name (excluded included), every original skill.
const estateNames = new Set<string>();
for (const c of components) {
  estateNames.add(addressOf(c));
  estateNames.add(c.name);
}
for (const m of manifests) {
  for (const item of m.items) {
    estateNames.add(resolveItem(root, m.plugin.name, item, components).outName);
  }
}
for (const own of ownSkillIdentities(root, manifests)) {
  estateNames.add(own.name);
}
```

  and, in the H1 file loop:

```ts
for (const hit of scanHandoffs(text)) {
  if (!estateNames.has(hit.name)) {
    continue;
  }
  const at = `${rel}:${lineOf(text, hit.index)}`;
  findings.push({
    level: "error",
    message: targetState.has(hit.name)
      ? `${at}: load-bearing handoff names \`${hit.name}\` bare — author it as ns:${hit.name} with depends_on, or /ns:${hit.name} if the human is the audience`
      : `${at}: load-bearing handoff names \`${hit.name}\`, which this estate does not emit — reroute or remove it`,
  });
}
```

- [ ] **Step 4: Run tests.** `node --test tools/lib/refs.test.ts tools/validate.test.ts` -> PASS;
  then the five tooling commands.
- [ ] **Step 5: Commit.**

```bash
git add tools/lib/refs.ts tools/lib/refs.test.ts tools/lib/rewrite.ts tools/validate.ts tools/validate.test.ts
git commit -m "feat: reject bare names in load-bearing handoff templates"
```

### Task W0.7: Item-root claims, bundled text files, and OpenCode path respelling

**Files:**
- Modify: `tools/lib/refs.ts` (`scanPathClaims` gains item-root claims), `tools/lib/refs.test.ts`,
  `tools/lib/rewrite.ts` (`isBundledText`; `rewriteOpenCodePaths` replaces
  `rewriteOpenCodeSiblingClimbs`), `tools/lib/rewrite.test.ts`, `tools/build.ts`
  (`rewriteOpenCodeTree` walks every file of a skill folder), `tools/build.test.ts`

**Interfaces:**
- Produces: `isBundledText(bytes: Buffer): boolean`;
  `rewriteOpenCodePaths(content: string, depthBelowSkillFolder: number, skillIds: Map<string, string>): string`.

- [ ] **Step 1: Write the failing tests.** In `tools/lib/refs.test.ts`:

```ts
test("item-root paths are claims unless they continue a longer path", () => {
  const claims = (t: string) => scanPathClaims(t, 0).filter((c) => c.kind === "item-root").map((c) => `${c.segment}:${c.path}`);
  assert.deepEqual(claims("read `skills/brainstorming/visual-companion.md`"), ["brainstorming:skills/brainstorming/visual-companion.md"]);
  assert.deepEqual(claims("see skills/brainstorming/visual-companion.md."), ["brainstorming:skills/brainstorming/visual-companion.md"]);
  assert.deepEqual(claims(".agents/skills/aspireify/SKILL.md ~/.claude/skills/x/ a/skills/z/ ~skills/y/ $skills/w/"), []);
});
```

  In `tools/lib/rewrite.test.ts` (rename `rewriteOpenCodeSiblingClimbs` to `rewriteOpenCodePaths` in
  the import and the existing climb test, then add):

```ts
test("OpenCode path respelling covers item-root paths and climbs in any text", () => {
  const ids = new Map([["brainstorming", "deniz-process.brainstorming"], ["beta", "deniz-process.beta"]]);
  assert.equal(rewriteOpenCodePaths("`skills/brainstorming/visual-companion.md`", 0, ids), "`skills/deniz-process.brainstorming/visual-companion.md`");
  assert.equal(rewriteOpenCodePaths("cat ../../beta/notes.md", 1, ids), "cat ../../deniz-process.beta/notes.md");
  assert.equal(rewriteOpenCodePaths('cat "$(dirname "$0")/../../beta/notes.md"', 1, ids), 'cat "$(dirname "$0")/../../beta/notes.md"', "inside a longer path");
  assert.equal(rewriteOpenCodePaths("skills/testing/x.md", 0, ids), "skills/testing/x.md", "not an emitted skill");
});

test("bundled text has no NUL byte and survives a UTF-8 round trip", () => {
  assert.equal(isBundledText(Buffer.from("#!/bin/sh\necho ok\n")), true);
  assert.equal(isBundledText(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x00])), false);
  assert.equal(isBundledText(Buffer.from([0x63, 0xff, 0x0a])), false);
});
```

  In `tools/build.test.ts`:

```ts
test("OpenCode respells sibling paths in scripts and item-root paths, and keeps binaries and modes", () => {
  const root = makeRepo();
  const alpha = join(root, "external", "sp", "skills", "alpha");
  mkdirSync(join(alpha, "scripts"), { recursive: true });
  writeFileSync(join(alpha, "SKILL.md"), "---\nname: alpha\ndescription: A\n---\n\nRead `skills/beta/references/notes.md`.\n");
  writeFileSync(join(alpha, "scripts", "run.sh"), "#!/bin/sh\ncat ../../beta/references/notes.md\n");
  chmodSync(join(alpha, "scripts", "run.sh"), 0o755);
  const png = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x00]), Buffer.from("../beta/")]);
  writeFileSync(join(alpha, "logo.png"), png);
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: P\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n  - source: sp/skills/beta\n",
  );
  buildAll(root);
  execFileSync("git", ["init", "-q", "."], { cwd: root });
  execFileSync("git", ["add", "plugins"], { cwd: root, stdio: "ignore" });
  execFileSync("git", ["update-index", "--chmod=+x", "plugins/deniz-process/skills/alpha/scripts/run.sh"], { cwd: root, stdio: "ignore" });
  buildAll(root);
  const oc = (...parts: string[]) => opencodeIdPath(root, "deniz-process", "skill", "alpha", ...parts);
  assert.equal(readFileSync(oc("scripts", "run.sh"), "utf8"), "#!/bin/sh\ncat ../../deniz-process.beta/references/notes.md\n");
  assert.match(readFileSync(oc("SKILL.md"), "utf8"), /`skills\/deniz-process\.beta\/references\/notes\.md`/);
  assert.deepEqual(readFileSync(oc("logo.png")), png, "a binary is never rewritten");
  assert.equal(
    readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "scripts", "run.sh"), "utf8"),
    "#!/bin/sh\ncat ../../beta/references/notes.md\n",
    "Claude keeps the bare folder name",
  );
  const moduleRoot = opencodeModulePath(root, "deniz-process");
  const manifest = JSON.parse(readFileSync(join(moduleRoot, "manifest.json"), "utf8"));
  assert.equal(manifest.files["skills/deniz-process.alpha/scripts/run.sh"].mode, "100755");
  assert.deepEqual(verifyModuleManifest(moduleRoot, manifest), []);
});
```

- [ ] **Step 2: Run to verify failure.** `node --test tools/lib/refs.test.ts tools/lib/rewrite.test.ts tools/build.test.ts`
  -> FAIL.
- [ ] **Step 3: Implement.** In `scanPathClaims`, after the climb loop:

```ts
const ITEM_ROOT = /skills\/([a-z0-9]+(?:-[a-z0-9]+)*)(?=\/)/g;
const CONTINUES_ITEM_ROOT = /[A-Za-z0-9._/~$-]/;

for (const m of content.matchAll(ITEM_ROOT)) {
  const before = m.index > 0 ? (content[m.index - 1] as string) : "";
  if (before && CONTINUES_ITEM_ROOT.test(before)) {
    continue;
  }
  const segment = m[1] as string;
  const segmentIndex = m.index + "skills/".length;
  out.push({ kind: "item-root", index: m.index, segmentIndex, segment, path: claimPath(content, m.index, segmentIndex + segment.length) });
}
return out.sort((a, b) => a.index - b.index);
```

  In `tools/lib/rewrite.ts`, rename `rewriteOpenCodeSiblingClimbs` to `rewriteOpenCodePaths` (the
  W0.1 splice loop already covers both kinds) and add:

```ts
/** A file the path respelling may read: no NUL byte, and bytes that survive a UTF-8 round trip. */
export function isBundledText(bytes: Buffer): boolean {
  return !bytes.includes(0) && Buffer.from(bytes.toString("utf8"), "utf8").equals(bytes);
}
```

  In `tools/build.ts` `rewriteOpenCodeTree`, replace the skill-folder loop body:

```ts
for (const file of listFiles(skillFolder)) {
  const path = join(skillFolder, file);
  const bytes = readFileSync(path);
  if (!isBundledText(bytes)) {
    continue;
  }
  const depth = relative(skillFolder, dirname(path)).split(sep).filter(Boolean).length;
  const before = bytes.toString("utf8");
  const text = path.endsWith(".md") ? localize(before, map, "opencode") : before;
  const after = rewriteOpenCodePaths(text, depth, skillIds);
  if (after !== before) {
    writeFileSync(path, after); // only on change: an unchanged script is not touched
  }
}
```

  Update the function's doc comment (spec section 14).
- [ ] **Step 4: Run tests.** The three files -> PASS; then the five tooling commands.
- [ ] **Step 5: Commit.**

```bash
git add tools/lib/refs.ts tools/lib/refs.test.ts tools/lib/rewrite.ts tools/lib/rewrite.test.ts tools/build.ts tools/build.test.ts
git commit -m "feat: respell OpenCode item-root paths and sibling climbs in every bundled text file"
```

### Task W0.8: Validator — path-claim rule P

**Files:**
- Modify: `tools/validate.ts` (L8 section: rule P; R1 skips landing climbs and gains the
  manual-folder check), `tools/validate.test.ts`

**Interfaces:**
- Consumes: `scanPathClaims`, `isBundledText`, `estateNames` (W0.6), `targetState`, `bareName`,
  `reRootOpenCode`, `skillsOf`, `inside`, `listFiles`.

- [ ] **Step 1: Write the failing test** in `tools/validate.test.ts`:

```ts
test("P: a landing climb fails closed, and a path into a manual folder fails", () => {
  const errors = ocErrors("See `../beta/references/` and `../gone/x.md` and `../delta/references/notes.md`.", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/beta",
    "    invocation: manual",
    '    omit: ["references/**"]',
    "  - source: sp/skills/delta",
  ]);
  const spelled = { plugins: "../beta/references/", opencode: "../deniz-process.beta/references/", codex: "../beta/references/" };
  for (const [tree, path] of Object.entries(spelled)) {
    assert.ok(errors.some((m) => m.includes(`sibling path ${path} does not resolve in ${tree}/`)), `${tree}\n${errors.join("\n")}`);
    assert.ok(errors.some((m) => m.includes(`sibling path ../gone/x.md does not resolve in ${tree}/`)), tree);
  }
  assert.equal(errors.filter((m) => m.includes("lands in manual item beta's folder")).length, 3);
  assert.ok(!errors.some((m) => m.includes("references/notes.md")), "a resolving climb passes");
});

test("P: an item-root path resolves in every tree once OpenCode respells it", () => {
  const errors = ocErrors("Read `skills/delta/references/notes.md` and `skills/delta/missing.md`.", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/delta",
  ]);
  assert.ok(!errors.some((m) => m.includes("skills/delta/references/notes.md") || m.includes("deniz-process.delta/references/notes.md")), errors.join("\n"));
  assert.equal(errors.filter((m) => m.includes("missing.md does not resolve")).length, 3);
});
```

- [ ] **Step 2: Run to verify failure.** `node --test tools/validate.test.ts` -> FAIL.
- [ ] **Step 3: Implement** in the L8 section of `tools/validate.ts`, inside the existing
  `for (const tree of ["plugins", "opencode", "codex"])` loop, before the Markdown-link loop:

```ts
const manualItem = (name: string): boolean => targetState.get(name)?.modelReachClaude === false;
const landed = (abs: string, path: string): boolean => existsSync(abs) && (!path.endsWith("/") || statSync(abs).isDirectory());
for (const unit of readdirSync(treeRoot)) {
  const skillsRoot = join(treeRoot, unit, "skills");
  for (const folder of existsSync(skillsRoot) ? readdirSync(skillsRoot) : []) {
    const skillFolder = join(skillsRoot, folder);
    if (!statSync(skillFolder).isDirectory()) {
      continue;
    }
    const ownName = bareName(tree, folder);
    for (const file of listFiles(skillFolder).map((f) => join(skillFolder, f))) {
      const bytes = readFileSync(file);
      if (!isBundledText(bytes)) {
        continue;
      }
      const text = bytes.toString("utf8");
      const depth = relative(skillFolder, dirname(file)).split(sep).filter(Boolean).length;
      const rel = relative(root, file).replaceAll("\\", "/");
      for (const claim of scanPathClaims(text, depth)) {
        const at = `${rel}:${text.slice(0, claim.index).split("\n").length}`;
        const name = bareName(tree, claim.segment);
        if (name !== ownName && manualItem(name)) {
          findings.push({ level: "error", message: `${at}: path ${claim.path} lands in manual item ${name}'s folder — a path is a read that bypasses Claude's model-invocation block; point the human with /ns:${name} instead` });
        }
        const base = claim.kind === "climb" ? resolve(dirname(file), claim.path) : resolve(join(treeRoot, unit), claim.path);
        const abs = tree === "opencode" ? reRootOpenCode(base) : base;
        if (claim.kind === "climb" && !landed(abs, claim.path)) {
          findings.push({ level: "error", message: `${at}: sibling path ${claim.path} does not resolve in ${tree}/ — the target item was renamed, excluded, omitted, or never existed` });
        } else if (claim.kind === "item-root" && estateNames.has(name) && !landed(abs, claim.path)) {
          findings.push({ level: "error", message: `${at}: item path ${claim.path} does not resolve in ${tree}/` });
        }
      }
    }
  }
}
```

  In the Markdown-link loop: compute the file's depth below `own.dir`, skip a link for which
  `scanPathClaims(link, depth)` returns a climb at index 0 (P judged it), and before
  `if (existsSync(abs)) continue;` report a link that resolves inside `skillsOf(n)` of another
  emitted item `n` with `manualItem(n)`, using the same "lands in manual item" message. Import
  `sep` from `node:path` and `listFiles` already comes from `./lib/overlay.ts`.
- [ ] **Step 4: Run tests.** `node --test tools/validate.test.ts` -> PASS, including the existing
  "a relative path into a sibling item that no longer has the file is an error" test (three
  findings, now from P). Then the five tooling commands.
- [ ] **Step 5: Commit.**

```bash
git add tools/validate.ts tools/validate.test.ts
git commit -m "feat: fail closed on unresolved path claims and paths into manual items"
```

### Task W0.9: Measure the curation-owned findings (no commit)

- [ ] **Step 1: Build and validate the real repository.**

```bash
npm run build
npm run validate > "$SCRATCH/w0-red.txt"; tail -1 "$SCRATCH/w0-red.txt"
grep -c 'skill-tool handle' "$SCRATCH/w0-red.txt"
grep -c 'load-bearing handoff names' "$SCRATCH/w0-red.txt"
grep -c 'sibling path .*using-superpowers/references/ does not resolve' "$SCRATCH/w0-red.txt"
grep -c "lands in manual item using-superpowers's folder" "$SCRATCH/w0-red.txt"
grep -cE 'Claude-only vocabulary|outside a recognized|item path|which this estate does not emit' "$SCRATCH/w0-red.txt"
```

  Expected, in order: `40 error(s), 1 warning(s)` (the warning is the existing
  `elements-of-style` namespace); `16`; `18`; `3`; `3`; `0`. The 16 handle findings and the 18
  handoff findings are exactly the H1 and H2–H4 rows of spec section 13 (`plugins/` paths and
  lines). Any other finding is a defect in W0.2–W0.8, or a hit the spec did not measure: stop and
  report it to the curator.
- [ ] **Step 2: Spot-check the rendered text** (not committed):
  `opencode/deniz-process/skills/deniz-process.brainstorming/SKILL.md` names
  `skills/deniz-process.brainstorming/visual-companion.md`;
  `codex/deniz-process/skills/requesting-code-review/SKILL.md` says "dispatch a subagent" and "two
  subagent calls"; `opencode/deniz-process/skills/deniz-process.dispatching-parallel-agents/SKILL.md`
  says `Subagent (general):`; `codex/deniz-process/skills/handoff/SKILL.md` ends its line 11 with
  "should invoke.".
- [ ] **Step 3: Restore the generated paths** (phase rules). `git status --short` prints nothing.

### Task W0.10: Promote the 16 Process skill-tool handles

**Files:**
- Modify: `curation/deniz-process.yaml`; `overlays/deniz-process/{grill-me,grill-with-docs}/overlay.patch`
  (new), `overlays/deniz-process/{improve-codebase-architecture,wayfinder}/overlay.patch` (re-cut),
  `overlays/overlays.lock.json`

- [ ] **Step 1:** `npm run inventory`, then reread the four upstream bodies under
  `external/mattpocock-skills/skills/` and their bundled files (AGENTS.md rule).
- [ ] **Step 2: New patches.** For `grill-me` and `grill-with-docs`: `npm run eject -- deniz-process
  <item> --patch`, edit the working copy's `SKILL.md`, run the same command again.
  - grill-me: `Call the Skill tool with "grilling".` → `Call the Skill tool with "mattpocock-skills:grilling".`
  - grill-with-docs: `Call the Skill tool twice, for "grilling" and "domain-modeling".` →
    `Call the Skill tool twice, for "mattpocock-skills:grilling" and "mattpocock-skills:domain-modeling".`
- [ ] **Step 3: Re-cut the two existing patches** (phase rules). In each working copy's `SKILL.md`,
  namespace every skill-tool handle with `mattpocock-skills:` and touch nothing else:
  - improve-codebase-architecture: the two `"codebase-design"` handles (`- Call the Skill tool
    with ...` in the vocabulary bullet and the "explore alternative interfaces" bullet),
    `"grilling"` ("Once the user picks a candidate"), `"domain-modeling"` ("Side effects happen
    inline").
  - wayfinder: `"research"` (Research bullet; "Fire the research subagents"), `"prototype"`
    (Prototype bullet), and `"grilling"` and `"domain-modeling"` in the three twice-forms (Grilling
    bullet, "Name the destination", "If in doubt").
  Check the working copy before cutting: `grep -nE 'Skill tool[^"]*"[a-z][a-z0-9-]*"' <SKILL.md>`
  prints nothing.
- [ ] **Step 4: Manifest** (`curation/deniz-process.yaml`):

```yaml
  - source: mattpocock-skills/skills/productivity/grill-me
    invocation: manual # 7-line trigger whose whole body is "Run a /grilling session" — the composition pattern's cleanest example
    body: patch # namespaces the grilling handle, so each harness renders a skill-tool sentence it can follow
    depends_on: [grilling]
    omit: ["agents/**"]
  - source: mattpocock-skills/skills/engineering/grill-with-docs
    invocation: manual # stateful grill (CONTEXT.md + ADRs); entry point, drags domain-modeling (taken)
    body: patch # namespaces both handles, so each harness renders a skill-tool sentence it can follow
    depends_on: [domain-modeling, grilling]
    omit: ["agents/**"]
```

  improve-codebase-architecture: `body: patch # make the load-bearing codebase-design, grilling,
  and domain-modeling edges harness-native` and `depends_on: [codebase-design, domain-modeling,
  grilling]`. wayfinder: `body: patch # localize the explicit setup prerequisite and the research,
  prototype, grilling, and domain-modeling handles through the shared reference grammar` and
  `depends_on: [domain-modeling, grilling, prototype, research]`.
- [ ] **Step 5: Verify.** `npm run build && npm run validate`: last line `24 error(s), 1 warning(s)`;
  `grep -c 'skill-tool handle'` on the output -> 0; no `undeclared dependency` or `stale depends_on`.
  Restore the generated paths.
- [ ] **Step 6: Commit.**

```bash
git add curation/deniz-process.yaml overlays/deniz-process overlays/overlays.lock.json
git commit -m "curate: promote the Process skill-tool handles to facts"
```

### Task W0.11: Close the Process path and route breaks

**Files:**
- Modify: `curation/deniz-process.yaml`, `overlays/deniz-process/executing-plans/overlay.patch`
  (new), `overlays/deniz-process/ask-deniz/SKILL.md`, `overlays/overlays.lock.json`

- [ ] **Step 1:** Reread `external/superpowers/skills/executing-plans/SKILL.md`,
  `external/mattpocock-skills/skills/engineering/ask-matt/SKILL.md`, and the current overlay.
- [ ] **Step 2: executing-plans (new patch).** In the working copy, change
  `Codex CLI, Codex App, Copilot CLI, and Gemini CLI all qualify; see the per-platform tool refs in \`../using-superpowers/references/\`)`
  to `Codex CLI, Codex App, Copilot CLI, and Gemini CLI all qualify)`. Cut it. Manifest:

```yaml
  - source: superpowers/skills/executing-plans
    invocation: both # SDD's no-subagent sibling; writing-plans' handoff menu offers both — skipping it would need a body patch for zero gain
    body: patch # drops the per-platform tool-ref clause: using-superpowers omits references/** and is manual, so the path was dead in every tree and would read a manual body
    depends_on: [finishing-a-development-branch, subagent-driven-development, using-git-worktrees]
```

- [ ] **Step 3: ask-deniz (overlay edit, no re-bless).** In
  `overlays/deniz-process/ask-deniz/SKILL.md`: `- **Something's broken** → \`systematic-debugging\`.`
  becomes `` → `superpowers:systematic-debugging`. ``, and `- **A merge went sideways** →
  \`resolving-merge-conflicts\`.` becomes `` → `mattpocock-skills:resolving-merge-conflicts`. ``.
  Manifest: add `depends_on: [resolving-merge-conflicts, systematic-debugging] # both routes are
  model-reachable facts, so a target deleted upstream fails as a dangling reference`.
- [ ] **Step 4: Verify.** `npm run build && npm run validate`: last line `16 error(s), 1 warning(s)`,
  every one a `load-bearing handoff names` finding in General or Aspire. Restore the generated paths.
- [ ] **Step 5: Commit.**

```bash
git add curation/deniz-process.yaml overlays/deniz-process overlays/overlays.lock.json
git commit -m "curate: drop executing-plans' dead path and make ask-deniz's routes facts"
```

### Task W0.12: Promote the General and Aspire load-bearing handoffs

**Files:**
- Modify: `curation/deniz-dotnet-general.yaml`, `curation/deniz-dotnet-aspire.yaml`;
  new patches `overlays/deniz-dotnet-general/{run-tests,build-perf-baseline,msbuild-antipatterns}/overlay.patch`;
  re-cut `overlays/deniz-dotnet-general/{mtp-hot-reload,check-bin-obj-clash,test-anti-patterns,test-gap-analysis,code-testing-agent}/overlay.patch`
  and `overlays/deniz-dotnet-aspire/aspire-init/overlay.patch`; `overlays/overlays.lock.json`

- [ ] **Step 1:** `npm run inventory`; reread each upstream body below and its bundled files under
  `external/dotnet-agent-skills/plugins/` and `external/aspire-skills/skills/aspire-init/`.
- [ ] **Step 2: Edits** (namespaces: `dotnet-test`, `dotnet-msbuild`, `aspire`; change only the
  backticked name):

| Item | Mechanism | Text → change | `depends_on` after |
|---|---|---|---|
| run-tests | new patch | `` Load `filter-syntax` `` (routing table row and "only when the request is filtered") → `` `dotnet-test:filter-syntax` ``; `` Load `platform-detection` only when `` → `` `dotnet-test:platform-detection` `` | [filter-syntax, platform-detection] |
| build-perf-baseline | new patch | ``Then use the `build-perf-diagnostics` skill`` → `` `dotnet-msbuild:build-perf-diagnostics` `` | [build-perf-diagnostics] |
| msbuild-antipatterns | new patch, target `references/additional-antipatterns.md` | both ``Use the `check-bin-obj-clash` skill`` → `` `dotnet-msbuild:check-bin-obj-clash` `` | [check-bin-obj-clash] |
| mtp-hot-reload | re-cut | ``in the `platform-detection` `` (before the line-wrapped `skill`) → `` `dotnet-test:platform-detection` `` | [platform-detection] |
| check-bin-obj-clash | re-cut | ``Use the `binlog-generation` skill`` → `` `dotnet-msbuild:binlog-generation` `` | [binlog-generation] |
| test-anti-patterns | re-cut | both ``Call the `test-analysis-extensions` skill`` → `` `dotnet-test:test-analysis-extensions` `` | [test-analysis-extensions] |
| test-gap-analysis | re-cut, new target `references/mutation-catalog.md` | SKILL.md ``Invoke`` + `` `test-analysis-extensions` `` and the catalog's ``invoke`` + `` `test-analysis-extensions` `` → `` `dotnet-test:test-analysis-extensions` `` | [test-analysis-extensions] |
| code-testing-agent | re-cut | ``(use the `run-tests` skill)`` → `` `dotnet-test:run-tests` ``; ``invoke `test-gap-analysis` and `test-anti-patterns` `` → `` `dotnet-test:test-gap-analysis` `` and `` `dotnet-test:test-anti-patterns` `` | [run-tests, test-anti-patterns, test-gap-analysis, writing-tunit-tests] |
| aspire-init | re-cut (`references/init-workflow.md` is already a target) | ``Re-invoke `aspireify` `` → ``Re-invoke `aspire:aspireify` `` | unchanged |

- [ ] **Step 3: Comments** (`curation/deniz-dotnet-general.yaml`):
  - Header "Reference posture" paragraph: after "...declared with exact `depends_on`." add "A load
    form (``Load `x` ``, ``Use the `x` skill``, ``Invoke `x` ``) is a handoff template, so it is
    authored as a namespaced fact with its `depends_on`; a "see" mention stays a candidate."
  - run-tests: `body: patch # namespaces its filter-syntax and platform-detection loads so every harness renders a loadable ID`.
  - build-perf-baseline: `body: patch # namespaces its build-perf-diagnostics handoff`.
  - msbuild-antipatterns: `body: patch # the reference file's two check-bin-obj-clash handoffs become namespaced facts`.
  - mtp-hot-reload, check-bin-obj-clash, code-testing-agent: append to the existing `body: patch`
    reason "; its load instruction for <target> is a namespaced fact".
  - test-anti-patterns: replace "sibling references preserve the upstream bare style" with "its two
    test-analysis-extensions loads are namespaced facts".
  - test-gap-analysis: replace "dead redirects stay out while the bundled mutation catalog flows by
    copy" with "dead redirects stay out; SKILL.md and the bundled mutation catalog load
    test-analysis-extensions through a namespaced fact".
- [ ] **Step 4: Verify.** `npm run build && npm run validate`: last line `0 error(s), 1 warning(s)`.
  Restore the generated paths.
- [ ] **Step 5: Commit.**

```bash
git add curation overlays
git commit -m "curate: promote the General and Aspire load-bearing handoffs"
```

### Task W0.13: writing-for-agents, item shapes, stale comments, Module versions

**Files:**
- Modify: `curation/deniz-process.yaml`, `curation/deniz-dotnet-general.yaml`,
  `curation/deniz-dotnet-akka.yaml`, `curation/deniz-dotnet-aspire.yaml`,
  `overlays/deniz-process/writing-for-agents/overlay.patch` (new), `overlays/overlays.lock.json`

- [ ] **Step 1: writing-for-agents (new patch).** Change only the frontmatter `description` line.
  Proposed wording, which the curator confirms before the cut: `description: Writing documents an
  agent consumes. Use when designing or rewording a skill, an AGENTS.md or CLAUDE.md, or an agent
  prompt for how reliably an agent follows it — not for routine content edits to those files.`
  Manifest:

```yaml
  - source: mattpocock-skills/skills/productivity/writing-for-agents
    invocation: both # curator's call now, not upstream's flag: upstream deleted writing-great-skills,
    # rewrote it under this name, broadened it from skills to any agent-consumed document, and dropped
    # its disable-model-invocation. Both, so a skill body can load it (retro's prescription step does
    # once retro is taken). Its upstream trigger fired on "modifying AGENTS.md or CLAUDE.md", which is
    # ordinary canon work in this repo, so the patched description narrows it to designing or
    # rewording such documents. Still the complement, not the substitute, of writing-skills: that one
    # is a TDD-for-skills testing methodology opened by hand, this one is the design vocabulary.
    body: patch # the narrowed description; the lock stamps it, so an upstream rewrite stops the build
    omit: ["agents/**"]
```

- [ ] **Step 2: teach and handoff stay skills.**
  - handoff: `invocation: manual # small, dependency-free, immediately useful session bridge. Stays a skill, not as: command: OpenCode 2 attaches it with @ like every manual item, and its argument-hint stays advisory`.
  - teach: `invocation: manual # upstream's own flag; stateful learning workspace, wanted around. Stays a skill, not as: command: upstream ships /teach as a skill body, and a command would drop its four *-FORMAT.md files in OpenCode`.
- [ ] **Step 3: Appendix A6 comments.**
  - `curation/deniz-dotnet-akka.yaml` (akka-net-specialist): "Native diagnostic agent in both
    harnesses;" → "Native diagnostic agent in Claude Code and OpenCode 2, and a skill in Codex;".
  - `curation/deniz-dotnet-aspire.yaml` (aspire): "in both harnesses" → "in every harness".
  - `curation/deniz-dotnet-general.yaml` (analyzing-dotnet-performance): "forced-manual would hide it
    from the model structurally (Claude) and wall-paste its body (OpenCode)" → "forced-manual would
    block the model from loading it in Claude Code and stop offering it to the model in OpenCode 2
    and Codex". Posture stays `both`.
  - `curation/deniz-process.yaml` (writing-skills patch reason): delete "In OpenCode the omit left
    that item with no bundle at all, so the emitter dropped the husk and the target directory does
    not exist either."
- [ ] **Step 4: Module versions** (`plugin.version`): Process `0.7.0`, General `0.10.0`, Akka
  `0.4.0`, Aspire `0.4.0` (spec decision 6).
- [ ] **Step 5: Verify.** `npm run build && npm run validate` -> `0 error(s), 1 warning(s)`;
  `grep -rl "opencode/autoinvoke: false" opencode | wc -l` -> 26. `npm run validate`'s provenance
  rule reports no curator name or date in the new comments. Restore the generated paths.
- [ ] **Step 6: Commit.**

```bash
git add curation overlays
git commit -m "curate: settle writing-for-agents, item shapes, stale comments, and Module versions"
```

### Task W0.14: Regenerate, review all three trees, prove idempotence

**Files:**
- Regenerate: `plugins/`, `opencode/`, `codex/`, `docs/ledger.json` (and prove no change elsewhere)
- Modify: `docs/ROADMAP.md`, and `docs/architecture/references-and-linking.md` only if an
  implemented detail differs from canon

- [ ] **Step 1: Generated-output gate.** The five tooling commands, then `npm run build`,
  `npm run inventory`, `npm run validate`, then `npm test` again (the pack tests read the committed
  trees). Expected: every command exits 0; `validate` prints `0 error(s), 1 warning(s)`.
- [ ] **Step 2: Unmoved surfaces.**
  `git diff --exit-code -- dist .claude-plugin .agents/plugins/marketplace.json docs/inventory.md`
  -> no output.
- [ ] **Step 3: Semantic review of all three trees.** Run each, write the counts in the commit body:

```bash
grep -rn "Skill tool" plugins | wc -l                                   # 13, identity
grep -rnE 'Skill tool[^"]*"[a-z][a-z0-9-]*"' plugins                    # nothing: every handle is a fact
grep -rn "Subagent (general-purpose)" plugins | wc -l                   # 9, identity
grep -rni "skill tool" opencode codex | grep -v '`skill` tool'          # nothing
grep -rn '`skill` tool' opencode | wc -l                                # 13
grep -rn "Subagent (general)" opencode | wc -l                          # 9
grep -rn "Subagent:" codex | wc -l                                      # 9
grep -rn 'Invoke `\$deniz-process:grilling`' codex/deniz-process/skills/grill-me/SKILL.md   # 1
grep -rn "skills/deniz-process.brainstorming/visual-companion.md" opencode | wc -l          # 1
grep -rn "using-superpowers/references" plugins opencode codex                              # nothing
grep -rl "opencode/autoinvoke: false" opencode | wc -l                  # 26
find opencode -path "*/skills/*" -name SKILL.md | wc -l                 # 115
ls codex/deniz-process/skills/writing-for-agents/agents/openai.yaml     # absent
grep -h '"version"' plugins/*/.claude-plugin/plugin.json codex/*/.codex-plugin/plugin.json opencode/*/manifest.json
```

  The versions are 0.4.0 (Akka), 0.4.0 (Aspire), 0.10.0 (General), 0.7.0 (Process) in each tree.
  Then read every changed hunk in `git diff -- plugins opencode codex`: each rendered skill-tool
  sentence, dispatch line, and promoted handoff must read as grammatical, native text in its
  harness. The ledger diff (`git diff docs/ledger.json`) may show only the spec section 16 changes:
  `depends_on` and model edges for the items in spec section 15, `body` for the seven new patches,
  and `writing-for-agents`' invocation, flags, advertisement, Codex policy, and description.
  Anything else is a defect.
- [ ] **Step 4: Idempotence** as in Task 12 Step 4: `cmp` prints nothing.
- [ ] **Step 5: Close W0 state in `docs/ROADMAP.md`.** Delete the Known Gaps entries "W0 rendering
  and checks are canon but not implemented" and "Curation comments describe OpenCode 1 or two
  harnesses". In Current State, update the Module versions (Process 0.7.0, General 0.10.0, Akka
  0.4.0, Aspire 0.4.0) in the estate bullet and in the General, Aspire, and Process sentences that
  name a version. Remove the finished W0 sub-step from Next Up item 1 and renumber. Move the Date.
  Compare the implemented names and messages with `references-and-linking.md` and correct canon
  where it drifted.
- [ ] **Step 6: Commit.**

```bash
git add plugins opencode codex docs/ledger.json docs/ROADMAP.md docs/architecture
git commit -m "build: regenerate W0 output"
```

### Task W0.15: Merge

- [ ] **Step 1:** With `npm run validate` at 0 errors on the branch head and the curator's word,
  merge `opencode-2-target` into `master` per gate G2. Pushing is a separate curator decision.

---

## Phase C: Curation pass (step 3)

### Task 13: Present the decision packet and apply only the curator's decisions

Replaced by Phase B2. The curator answered Appendix A on 2026-10-08 (spec "W0 correctness",
decisions 1–7): no `as: command`, every `manual` and `both` item stays a skill, `writing-for-agents`
becomes `both`, the 16 handles are promoted, A6 comments are refreshed, and every Module takes a
minor bump. W0.10–W0.13 apply the decisions, W0.14 regenerates, and W0.15 merges.

---

## Phase D: Measurements (step 4)

### Task 14: Retire the OpenCode 1 probes and port a discovery check

**Files:**
- Delete: `experiments/harness-invocation/stub-command-smoke.ps1`
- Create: `experiments/harness-invocation/oc2-discovery.ps1`
- Modify: retire the OpenCode legs of `matrix.ps1`, `variants.ps1`, `intent-matrix.ps1`,
  `ocprobe.ps1`, and `verify.ps1`; port the OpenCode lab in `common.ps1` and `lab.ps1` only as far as
  the discovery check and Task 16 need; `selftest.ps1`; `protocol.md` (Isolate,
  Prove installer composition, Probe cheaply, Traps); `runbook.md`; `README.md` (experiments);
  `tools/repository-docs.test.ts` (`OpenCode lab describes installer composition`)
- Keep unchanged: every record under `records/` (append-only evidence)

**Interfaces:**
- Produces: `oc2-discovery.ps1 -Lab <dir> [-Port <n>] [-DryRun]`. A real run prints one JSON object
  `{ opencodeVersion, skills: [{id, advertised}], commands: [id], agents: [id] }` from
  `GET /api/skill`, `/api/command`, `/api/agent` of an isolated `opencode serve`. `-DryRun` starts
  nothing and prints `{ env: { <name>: <value> }, command: "opencode serve --hostname 127.0.0.1 --port <n> --print-logs" }`.

- [ ] **Step 1: Write the failing selftest assertions** in `selftest.ps1`, before the
  `Exit-Selftest` call, in the existing `Test-That` style:

```powershell
Write-Host "OpenCode 2 discovery check"
$ocLab = Join-Path ([IO.Path]::GetTempPath()) "oc2-selftest-lab"
Test-That "oc2-discovery.ps1 exists" { Test-Path (Join-Path $PSScriptRoot "oc2-discovery.ps1") }
Test-That "oc2-discovery dry run isolates every OpenCode 2 root under the lab" {
    $plan = & pwsh -NoProfile -File (Join-Path $PSScriptRoot "oc2-discovery.ps1") -Lab $ocLab -DryRun | ConvertFrom-Json
    $vars = 'OPENCODE_CONFIG_DIR','OPENCODE_DISABLE_PROJECT_CONFIG','HOME','USERPROFILE','OPENCODE_TEST_HOME',
            'XDG_CONFIG_HOME','XDG_DATA_HOME','XDG_STATE_HOME','XDG_CACHE_HOME','OPENCODE_DB'
    $missing = $vars | Where-Object { -not $plan.env.$_ }
    $outside = $vars | Where-Object { $_ -ne 'OPENCODE_DISABLE_PROJECT_CONFIG' -and -not "$($plan.env.$_)".StartsWith($ocLab) }
    if ($missing) { "missing: $missing" } elseif ($outside) { "outside lab: $outside" }
    elseif ($plan.command -notmatch '^opencode serve --hostname 127\.0\.0\.1 --port \d+ --print-logs$') { "command: $($plan.command)" }
    else { $true }
}
Test-That "the OpenCode 1 stub probe is retired" { -not (Test-Path (Join-Path $PSScriptRoot "stub-command-smoke.ps1")) }
Test-That "no runner calls OpenCode 1 introspection" {
    $hits = Get-ChildItem $PSScriptRoot -Filter *.ps1 | Where-Object Name -ne 'selftest.ps1' |
        Select-String -Pattern 'debug skill', 'run --command', 'models --verbose' -SimpleMatch
    if ($hits) { ($hits | ForEach-Object { "$($_.Filename):$($_.LineNumber)" }) -join ', ' } else { $true }
}
Test-That "protocol no longer describes OPENCODE_CONFIG_DIR as additive or BODY.md checks" {
    $p = Get-Content (Join-Path $PSScriptRoot "protocol.md") -Raw
    if ($p -match 'only \*\*adds\*\* a search location' -or $p -match 'BODY\.md') { "stale OpenCode 1 text" } else { $true }
}
```

  Remove the OpenCode markers (`'debug skill'`, `'debug config'`, `'customize-opencode'`) from the
  `verify.ps1` entry of the runner-shape table at line 449, keep markers of its Claude Code leg,
  drop the table entries of files that retire whole (`ocprobe.ps1`, `matrix.ps1`, and
  `variants.ps1` hold no other harness leg), adjust the OpenCode markers of `intent-matrix.ps1`
  (for example `--format`) and `lab.ps1` (`Start-OpenCodeLab`) to whatever those files keep, and
  every
  selftest assertion that expects the installer to refuse `OPENCODE_CONFIG_DIR`.
- [ ] **Step 2: Run to verify failure.**
  `pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab` -> FAIL on the new
  assertions.
- [ ] **Step 3: Implement** `oc2-discovery.ps1`: create the lab tree, write
  `<lab>/config/service.json` `{"disabled":true}`, start `opencode serve` with Basic auth from a
  random password, poll `GET /api/skill` until non-empty or 20 seconds (the first request races the
  location load), query the three routes with
  `location[directory]=<lab project>`, stop the process in `finally`, and emit the JSON. Advertised
  is `metadata["opencode/autoinvoke"] !== false` from the returned skill. Per the curator's decision,
  only the discovery check and the Task 16 model record are ported: retire the OpenCode legs of
  `matrix.ps1`, `variants.ps1`, `intent-matrix.ps1`, `ocprobe.ps1`, and `verify.ps1` (the OpenCode 1
  CLI matrices) rather than porting them, and record each retirement in the commit body. Change
  `common.ps1` and `lab.ps1` only as far as the discovery check and Task 16 need an OpenCode 2 lab
  (installer composition into `OPENCODE_CONFIG_DIR`, isolated `serve`); porting any other OpenCode
  leg is a new decision for the curator.
  Rewrite the protocol's OpenCode isolation table and steps to OpenCode 2 (research note §2): the
  variable replaces the root, `--standalone` or isolated `serve`,
  `OPENCODE_DISABLE_PROJECT_CONFIG`, always-on `~/.claude/skills` and `~/.agents/skills`. Retarget
  the `repository-docs.test.ts` lab assertion to the ported `lab.ps1` wording (keep the invariant:
  installer composition, not a mounted build tree).
- [ ] **Step 4: Run.** `selftest.ps1 -SkipLab` -> PASS; `npm test` -> `# fail 0`; public-safety passes.
- [ ] **Step 5: Update ROADMAP**: remove the `experiments/` remainder of "OpenCode 1 tests and
  probes remain".
- [ ] **Step 6: Commit.**

```bash
git add experiments tools/repository-docs.test.ts docs/ROADMAP.md
git commit -m "test: port OpenCode experiments to OpenCode 2"
```

### Task 15: Discovery record (tier 1), Windows and Linux

**Files:**
- Create: `experiments/harness-invocation/records/2026-MM-DD-opencode2-discovery.md`
- Modify: `docs/architecture/distribution-and-installation.md` (Other current limits: discovery no
  longer unmeasured), `docs/research/opencode-2-target.md` (link the record only)

- [ ] **Step 1: Prepare the lab** per `experiments/harness-invocation/protocol.md` (Build a lab,
  Isolate). Record the OpenCode version (`opencode --version`, expect `v2.0.23` or later on the
  2.0.x line) on both machines.
- [ ] **Step 2: Install the checkout output into the lab** with
  `OPENCODE_CONFIG_DIR=<lab config>` and `npm run install:opencode -- install --all --yes`.
- [ ] **Step 3: Run** `pwsh -NoProfile -File experiments/harness-invocation/oc2-discovery.ps1 -Lab <lab>`.
  Expected: the post-Task-13 estate from `docs/ledger.json` (before curation: 115 skills, the 27
  manual IDs `advertised: false`, 0 commands, 2 agents; an `as: command` decision moves an item from
  the skill count to the command count), every ID dotted, every agent `mode: subagent`, built-in
  commands aside.
- [ ] **Step 4: On the Linux host, measure Q3**: start the isolated `serve` once with
  `OPENCODE_CONFIG_DIR=` (empty) and once unset; record the config root each reports
  (`opencode debug paths` inside the same environment). If an empty value makes OpenCode use an
  empty root, stop and return Q3 to the curator before Task 18.
- [ ] **Step 5: Write the record** per `records/README.md` (frontmatter `record_id`, `date`,
  `repo_head`, `kind: structural`, `summary`, `isolation_ok: true`), sanitized; then link it from
  the research note and update the distribution limit.
- [ ] **Step 6:** `npm run check:public-safety`; commit
  `test: record OpenCode 2 discovery of the Bundles`.

### Task 16: Model record (tier 2): manual skill posture

**Files:**
- Create: `experiments/harness-invocation/records/2026-MM-DD-opencode2-manual-skill.md`

- [ ] **Step 1: Fixture.** Use the protocol's nonsense-trigger fixture with one `manual` skill
  `deniz-fixture.zorblat` (hidden by the metadata key) and one `auto` control.
- [ ] **Step 2: Probes** (protocol "Probe cheaply", repeated per its repetition rule):
  1. the model's skill list (`/api/skill` plus the session's system skill list) omits the manual ID;
  2. the user attaches `@deniz-fixture.zorblat` and the body is loaded;
  3. when the prompt names `deniz-fixture.zorblat`, the model calls the skill tool with that exact
     ID and the load succeeds (not offered to the model, but still loadable).
- [ ] **Step 3: Record** with `kind: model-panel`, `fixture_sha`, `harness_name: opencode`,
  `harness_version`, `runner_revision`, the per-attempt table, and sanitized event excerpts (tier 2,
  required because the claim backs ADR-0005's OpenCode meaning).
- [ ] **Step 4:** public-safety; commit `test: record OpenCode 2 manual skill posture`.

### Task 17: Windows bulk-Apply measurement for `anomalyco/opencode#47505`

**Files:**
- Create: `experiments/harness-invocation/records/2026-MM-DD-opencode2-bulk-apply-windows.md`
- Modify: `docs/architecture/distribution-and-installation.md` (Target OpenCode runtime: replace
  "Until that record exists ... unmeasured" with the result and a record link)

- [ ] **Step 1:** On the Windows workstation, in an isolated profile, start a running OpenCode 2
  service bound to the lab config root (isolated `serve`, not the managed shared service).
- [ ] **Step 2:** Run `install --all --yes` (the full estate, about 115 skills), then `remove --all --yes`, then
  `install --all --yes` again, while polling `/api/skill` every second and capturing the service log.
- [ ] **Step 3:** Record whether the service stayed alive, the time to a consistent list after each
  Apply, and any watcher error. Repeat three times.
- [ ] **Step 4:** If the service terminates, record it, keep the Release gate closed, and return to
  the curator with options (stop the service before Apply in the documented procedure, or wait for
  an upstream fix).
- [ ] **Step 5:** public-safety; commit `test: measure Windows bulk Apply on OpenCode 2`.

---

## Phase E: Profiles and Release (step 5)

### Task 18: One-off migration of the two real profiles

**Files:**
- Create: `experiments/harness-invocation/records/2026-MM-DD-opencode2-profile-migration.md`

Each machine needs the curator's explicit go (gate G6) immediately before Step 3 on that machine.
This is a recorded one-off, not a product path; the installer gains nothing for it.

- [ ] **Step 1: Inventory, read-only.** On the machine: `opencode --version`; `opencode debug paths`
  (config root); whether `OPENCODE_CONFIG_DIR` and `XDG_CONFIG_HOME` are set; the schema of
  `<root>/.deniz-skills/install.json`; `deniz-skills status` with the `installer-v0.3.0` Package;
  a listing with SHA-256 of every file under `<root>/skills`, `<root>/commands`, `<root>/agents`
  and of `<root>/opencode.json(c)`. `opencode debug paths` reports the CLI's own environment, while
  a running managed service keeps the environment it started with, so also confirm the service reads
  the same root (for example, its `/api/skill` lists exactly the skills found under that root's
  `skills/`; this step stays read-only). Stop if the
  OpenCode config root differs from the v0.3.0 installer's Destination (the old installer refuses
  `OPENCODE_CONFIG_DIR`).
- [ ] **Step 2: Back up** the whole config root to a dated directory outside it.
- [ ] **Step 3: Stop OpenCode** on the machine (managed service included) unless Task 17 showed bulk
  Apply is safe on that OS.
- [ ] **Step 4: Remove with the schema-1 installer**, Plan then Apply. `$package030` is the
  local path of the `installer-v0.3.0` Package, downloaded and digest-checked with the README's
  current Release recipe before any execution:

```powershell
npm exec --yes --package $package030 -- deniz-skills remove --all
npm exec --yes --package $package030 -- deniz-skills remove --all --yes
npm exec --yes --package $package030 -- deniz-skills status
```

  Expected: empty Selection, no owned files, no pending Recovery.
- [ ] **Step 5: Remove the then-empty schema-1 state.** Confirm `<root>/.deniz-skills/` holds only
  `install.json` with no modules and no files, and no transaction or journal directory; then delete
  `<root>/.deniz-skills/`.
- [ ] **Step 6: Install schema-2 output from the checkout**, Plan then Apply:

```bash
npm run install:opencode -- install --all
npm run install:opencode -- install --all --yes
npm run install:opencode -- status
```

  Expected: Plan without Collisions; Apply succeeds; status shows four Modules current.
- [ ] **Step 7: Verify.** Start OpenCode again if Step 3 stopped it (a running OpenCode 2 service
  hot-reloads its config folders otherwise); `/api/skill` (or the TUI `@` picker) lists the dotted
  IDs; unrelated files from Step 1's listing are byte-identical.
- [ ] **Step 8: Record** both machines (machine described only as "Windows workstation" and "Linux
  host"; no paths, hostnames, or usernames). Remove the ROADMAP Known Gaps entry "Real profiles
  hold schema-1 state". Commit `test: record the one-off OpenCode 2 profile migration`.

### Task 19: Next Package Release

**Files:**
- Modify: `package.json` (`version`), `package-lock.json` (root version fields),
  `tools/repository-docs.test.ts` (Release test pins), `README.md` (capability summary lines 64-68,
  "OpenCode from a Release Package" recipe and prose, the "refuses alternate config-dir mounts"
  sentence), `docs/architecture/distribution-and-installation.md` (Bundle and Package identity:
  current Release name and schema), `docs/ROADMAP.md`
- Create: `experiments/harness-invocation/records/2026-MM-DD-opencode-installer-v<version>.md`

- [ ] **Step 1: Gates.** G7 answered; Task 17 record shows no service termination or documents the
  mitigation; Tasks 13–18 committed; `validate` clean on `master`.
- [ ] **Step 2: Write the failing guard first.** In `tools/repository-docs.test.ts` change the
  Release test to the new tag, asset name, and a placeholder digest constant; add
  `installer-v0\.3\.0|deniz-agent-skills-0\.3\.0\.tgz` to the `doesNotMatch` list.
  Run `npm test` -> FAIL (README still names v0.3.0).
- [ ] **Step 3: Bump** `package.json` and `package-lock.json` to the G7 version.
- [ ] **Step 4: Build the candidate** with the manual `release-package` workflow on Linux
  (`publish: false`, `source_ref: <exact sha>`), download its artifact, and run
  `npm run verify:package -- <package.tgz>`. Record the SHA-256.
- [ ] **Step 5: Update README**: the capability bullet (OpenCode receives one artifact per item at
  its `<plugin>.<name>` ID, a skill unless curation chose `as: command` or `as: agent`; `manual`
  skills hidden by `opencode/autoinvoke`; `@` attach; no parked bodies; describe the estate the
  W0 curation in Phase B2 produced), the
  Release recipe (tag, asset, digest), and replace "refuses alternate config-dir mounts" with the
  `OPENCODE_CONFIG_DIR` Destination rule and the OpenCode 2 floor. Put the real digest into the test
  constant. Run `npm test` -> PASS.
- [ ] **Step 6: Publish** on the curator's go: create the GitHub Release and its tag at the exact
  SHA (the workflow's `release_tag` input must name an existing Release), run the workflow with
  `publish: true` and that `release_tag`, re-download the asset, and verify the digest. Whether the
  workflow's `release_tag` default (`installer-v0.3.0`) moves with the Release is the curator's
  call; Step 4's build-only run passes `release_tag` explicitly either way.
- [ ] **Step 7: Record** the Release (asset identity, workflow runs, isolated Plan/Apply/status,
  re-download) and update distribution canon and ROADMAP (Current State Release bullet; remove
  "Public Release surface lags the decision"; keep "Public schema-1 Release has no upgrade path" as
  a historical boundary or reword it per the curator).
- [ ] **Step 8: Full gate** (generated-output row plus Release row of quality gates), then commit
  `release: OpenCode 2 installer Package`.

### Task 20: Closeout

- [ ] **Step 1:** Move any durable statement still only in the spec or plan to its owner.
- [ ] **Step 2:** Remove the finished Next Up item 1 from `docs/ROADMAP.md`.
- [ ] **Step 3:** Delete `docs/superpowers/specs/2026-10-08-opencode-2-target-design.md` and this
  plan.
- [ ] **Step 4:** Documentation gate (dates, links, owners, relays), `npm test`,
  `npm run check:public-safety`; commit `docs: close OpenCode 2 migration`.

---

## Contradictions to clear (from the align report)

| Contradiction | Cleared by |
|---|---|
| `README.md` lines 64-68: OpenCode "receives a skill, a command, or both"; parked bodies | Task 19 Step 5 |
| `README.md` line 256: installer "refuses alternate config-dir mounts" | Task 19 Step 5 |
| `experiments/harness-invocation/protocol.md` lines 13, 47, 57, 92, 98, 121, 135, 173, 188 and `runbook.md`: additive `OPENCODE_CONFIG_DIR`, the refusal, `BODY.md` checks | Task 14 |
| "both harnesses" comments: `curation/deniz-dotnet-akka.yaml:31`, `curation/deniz-dotnet-aspire.yaml:13` | W0.13 (A6) |
| OpenCode wall-paste reason: `curation/deniz-dotnet-general.yaml:220` | W0.13 (A6) |
| OpenCode husk reason: `curation/deniz-process.yaml:293` | W0.13 (A6) |
| `tools/build.ts:501` comment "the dial is which artifact exists" | Task 6 Step 4 |
| `tools/validate.ts:895` comment about parked files | Task 9 Step 3 |
| Research note file name vs topic-name rule | Done before Task 1 (renamed to `opencode-2-target.md`) |
| Writer questions: absent DMI, Iteration 2 example, empty `OPENCODE_CONFIG_DIR`, `license`/`compatibility` | Spec Q1–Q4; Q3 measured in Task 15 Step 4 |
| `docs/adr/README.md` supersede wording vs in-place revisions | No change (align report: not a conflict) |

## Stop conditions

Stop and return to the curator instead of guessing when:

- regeneration changes any byte under `plugins/`, `codex/`, either marketplace, or
  `docs/inventory.md` before Phase B2, or W0.1 changes any generated byte;
- `validate` after Task 12 reports anything beyond Appendix A2, or W0.9 reports anything beyond the
  40 findings it lists;
- a W0 curation task meets a handoff, path, or wording choice that spec section 15 does not settle;
- W0.14 shows a ledger change outside spec section 16, or the idempotence `cmp` prints anything;
- a measured OpenCode 2 behavior contradicts the research note (hiding key, dotted IDs, `@` attach,
  `OPENCODE_CONFIG_DIR` as the root);
- the Windows bulk-Apply measurement terminates the service;
- a real profile's config root differs between OpenCode and either installer;
- an `as: command` decision needs bundled files in OpenCode.

---

## Appendix A: Curation decision packet (for Task 13; data measured at `9442efa`)

The curator answered this packet on 2026-10-08 (spec "W0 correctness", decisions 1–7); Phase B2
applies the answers. The questions below are kept as the record of what was asked.

### A1. The 27 `manual` and 11 `both` items

Columns: posture, item, upstream source, OpenCode 1 output today, bundled files beside `SKILL.md`,
argument hint. No item uses `$ARGUMENTS` today. Only `handoff` and `teach` carry an
`argument-hint`. Under OpenCode 2 every item below becomes one skill (manual hidden) unless the
curator gives it `as: command`. An `as: command` item emits a single file in OpenCode and loses its
bundled files there.

| Posture | Item | Source | OC1 today | Bundled | Argument hint |
|---|---|---|---|---:|---|
| both | deniz-dotnet-general/analyzing-dotnet-performance | dotnet-agent-skills/plugins/dotnet-diag/skills/analyzing-dotnet-performance | skill+command | 7 | |
| both | deniz-dotnet-general/dotnet-devcert-trust | dotnet-skills/skills/dotnet-devcert-trust | skill+command | 0 | |
| both | deniz-dotnet-general/dotnet-slopwatch | dotnet-skills/skills/slopwatch | skill+command | 0 | |
| both | deniz-process/brainstorming | superpowers/skills/brainstorming | skill+command | 7 | |
| both | deniz-process/executing-plans | superpowers/skills/executing-plans | skill+command | 0 | |
| both | deniz-process/finishing-a-development-branch | superpowers/skills/finishing-a-development-branch | skill+command | 0 | |
| both | deniz-process/prototype | mattpocock-skills/skills/engineering/prototype | skill+command | 2 | |
| both | deniz-process/requesting-code-review | superpowers/skills/requesting-code-review | skill+command | 1 | |
| both | deniz-process/research | mattpocock-skills/skills/engineering/research | skill+command | 0 | |
| both | deniz-process/subagent-driven-development | superpowers/skills/subagent-driven-development | skill+command | 6 | |
| both | deniz-process/writing-plans | superpowers/skills/writing-plans | skill+command | 1 | |
| manual | deniz-dotnet-general/code-testing-agent | dotnet-agent-skills/plugins/dotnet-test/skills/code-testing-agent | stub+park | 1 | |
| manual | deniz-dotnet-general/convert-to-cpm | dotnet-agent-skills/plugins/dotnet-nuget/skills/convert-to-cpm | stub+park | 6 | |
| manual | deniz-dotnet-general/dotnet-aot-compat | dotnet-agent-skills/plugins/dotnet-upgrade/skills/dotnet-aot-compat | stub+park | 1 | |
| manual | deniz-dotnet-general/dotnet-trace-collect | dotnet-agent-skills/plugins/dotnet-diag/skills/dotnet-trace-collect | stub+park | 5 | |
| manual | deniz-dotnet-general/dump-collect | dotnet-agent-skills/plugins/dotnet-diag/skills/dump-collect | stub+park | 3 | |
| manual | deniz-dotnet-general/generate-testability-wrappers | dotnet-agent-skills/plugins/dotnet-test/skills/generate-testability-wrappers | inline command | 0 | |
| manual | deniz-dotnet-general/migrate-nullable-references | dotnet-agent-skills/plugins/dotnet-upgrade/skills/migrate-nullable-references | stub+park | 5 | |
| manual | deniz-dotnet-general/migrate-static-to-wrapper | dotnet-agent-skills/plugins/dotnet-test/skills/migrate-static-to-wrapper | inline command | 0 | |
| manual | deniz-dotnet-general/thread-abort-migration | dotnet-agent-skills/plugins/dotnet-upgrade/skills/thread-abort-migration | inline command | 0 | |
| manual | deniz-process/ask-deniz | mattpocock-skills/skills/engineering/ask-matt | stub+park | 1 | |
| manual | deniz-process/grill-me | mattpocock-skills/skills/productivity/grill-me | inline command | 0 | |
| manual | deniz-process/grill-with-docs | mattpocock-skills/skills/engineering/grill-with-docs | inline command | 0 | |
| manual | deniz-process/handoff | mattpocock-skills/skills/productivity/handoff | inline command | 0 | What will the next session be used for? |
| manual | deniz-process/implement | mattpocock-skills/skills/engineering/implement | inline command | 0 | |
| manual | deniz-process/improve-codebase-architecture | mattpocock-skills/skills/engineering/improve-codebase-architecture | stub+park | 1 | |
| manual | deniz-process/setup-matt-pocock-skills | mattpocock-skills/skills/engineering/setup-matt-pocock-skills | stub+park | 5 | |
| manual | deniz-process/teach | mattpocock-skills/skills/productivity/teach | stub+park | 4 | What would you like to learn about? |
| manual | deniz-process/to-questionnaire | mattpocock-skills/skills/productivity/to-questionnaire | inline command | 0 | |
| manual | deniz-process/to-spec | mattpocock-skills/skills/engineering/to-spec | inline command | 0 | |
| manual | deniz-process/to-tickets | mattpocock-skills/skills/engineering/to-tickets | inline command | 0 | |
| manual | deniz-process/triage | mattpocock-skills/skills/engineering/triage | stub+park | 2 | |
| manual | deniz-process/using-superpowers | superpowers/skills/using-superpowers | inline command | 0 | |
| manual | deniz-process/wait-what | mattpocock-skills/skills/productivity/wait-what | inline command | 0 | |
| manual | deniz-process/wayfinder | mattpocock-skills/skills/engineering/wayfinder | inline command | 0 | |
| manual | deniz-process/wizard | mattpocock-skills/skills/engineering/wizard | stub+park | 1 | |
| manual | deniz-process/writing-for-agents | mattpocock-skills/skills/productivity/writing-for-agents | stub+park | 1 | |
| manual | deniz-process/writing-skills | superpowers/skills/writing-skills | stub+park | 6 | |

Questions per item: keep the posture as a skill, or `as: command`? For `teach` (4 bundled
`*-FORMAT.md` files), `as: command` would drop those files from OpenCode and reopen the
"Converted command paths" gap. Re-derive the posture columns from the ledger before presenting,
because curation may have moved since `9442efa`:

```bash
node -e 'const l=require("./docs/ledger.json");for(const[k,v]of Object.entries(l))if(v.invocation==="manual"||v.invocation==="both")console.log(v.invocation,k,v.source)'
```

The "OC1 today" column describes the pre-Task-12 tree and is history once Task 12 lands.

### A2. The 12 bare skill-tool handle lines (16 handles)

All in `deniz-process` Claude output at `9442efa` (same text in OpenCode and Codex). Promotion means:
author the handle as `mattpocock-skills:<name>` through a `body: patch`, and add the target to
`depends_on`. All five targets are model-reachable (`grilling`, `domain-modeling`,
`codebase-design` are `auto`; `research`, `prototype` are `both`).

| File:line | Handles | Current `depends_on` |
|---|---|---|
| `grill-me/SKILL.md:7` | grilling | none |
| `grill-with-docs/SKILL.md:8` | grilling, domain-modeling | none |
| `improve-codebase-architecture/SKILL.md:14` | codebase-design | [codebase-design] |
| `improve-codebase-architecture/SKILL.md:65` | grilling | |
| `improve-codebase-architecture/SKILL.md:67` | domain-modeling | |
| `improve-codebase-architecture/SKILL.md:72` | codebase-design | |
| `wayfinder/SKILL.md:79` | research | none |
| `wayfinder/SKILL.md:80` | prototype | |
| `wayfinder/SKILL.md:81` | grilling, domain-modeling | |
| `wayfinder/SKILL.md:113` | grilling, domain-modeling | |
| `wayfinder/SKILL.md:117` | research | |
| `wayfinder/SKILL.md:126` | grilling, domain-modeling (the "If in doubt" clause) | |

Questions: promote all 16? Codex renders a promoted handle as `"$deniz-process:grilling"` inside a
"Skill tool" sentence that Codex has no tool for; accept, or reword per harness (there is no
per-harness body seam)? Not detected by O2 and left as prose: `handoff/SKILL.md:11` and
`wayfinder/SKILL.md:126` "call the Skill tool for whichever skills".

Answered: promote all 16 (W0.10), and render the whole skill-tool sentence per harness, generic
form included (spec section 12).

### A3. User pointers to `manual` items

The pointers in `csharp-nullable-reference-types` (to `migrate-nullable-references`) and
`requesting-code-review` (to `setup-matt-pocock-skills`) stay as validated user pointers.

### A4. Related text the rules do not check (for information)

- Path climb into a manual item: `deniz-process/skills/executing-plans/SKILL.md:15` (`both`) names
  `../using-superpowers/references/`; `using-superpowers` is `manual` and omits `references/**`, so
  the path is already dead in every harness (spec Q5). Answered: a path into a manual folder is now
  an error, and W0.11 drops the clause.
- Absent items with an upstream `disable-model-invocation: true`: none.

### A5. Module versions and Release version (gate G7)

Every Module's OpenCode bytes change (paths, digests). Checkout versions: Process 0.6.0, General
0.9.1, Akka 0.3.1, Aspire 0.3.3. A `plugin.version` bump also changes the Claude and Codex Plugin
manifests and marketplaces. Question: bump which Modules, to what? Package version: recommend
`0.4.0` (spec Q9).

### A6. Comments and reasons written against OpenCode 1 or two harnesses

- `curation/deniz-dotnet-akka.yaml:31` "Native diagnostic agent in both harnesses".
- `curation/deniz-dotnet-aspire.yaml:13` "in both harnesses".
- `curation/deniz-dotnet-general.yaml:217-221` (`analyzing-dotnet-performance`): "forced-manual would
  ... wall-paste its body (OpenCode)".
- `curation/deniz-process.yaml:288-293` (`writing-skills` patch reason): "In OpenCode the omit left
  that item with no bundle at all, so the emitter dropped the husk".
- `setup-matt-pocock-skills` prefers writing `CLAUDE.md`, which OpenCode 2 never loads; the ROADMAP
  assigns this to the `mattpocock-skills` sync wave, so no action here unless the curator moves it.

Question per comment: new wording (curator's).
