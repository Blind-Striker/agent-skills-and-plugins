# Reference-Model Wave Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **EXECUTION NOT STARTED.** This plan is written; the user decides when and how it executes.
> Transient document (AGENTS.md, Documentation Hygiene): delete it in the change that completes
> the wave. The contract this plan implements is [ADR-0008](../../adr/0008-references-are-symbols.md)
> and the `merged_from` section of [ADR-0001](../../adr/0001-submodule-manifest-overlay-architecture.md);
> where this plan and those ADRs disagree, the ADRs win.

**Goal:** One reference model (facts/candidates, model-edge/user-pointer), a linker in `validate`,
a committed `docs/ledger.json`, `merged_from` blessing for merge sources, and a semantic `sync` —
so every dependency of every shipped skill is machine-checked in both harness trees.

**Architecture:** A new `tools/lib/refs.ts` is the only reference scanner. The build keeps its
pipeline (omit → overlay/patch → frontmatter → invocation → emit OpenCode → rewrite per tree) and
gains a ledger writer at the end. `validate` gains a linking pass that reads built output —
effective flags, not manifest intent. `overlays.lock.json` gains `mergeSources`. `sync` classifies
upstream changes instead of listing paths.

**Tech Stack:** Node ≥24, TypeScript 7 (`tsc --noEmit`), `node --test` + `node:assert/strict`,
Biome 2.x (120 cols, `useBlockStatements`), `yaml` as the only runtime dependency. No new
dependencies. The five npm commands stay the five npm commands (ADR-0004).

## Global Constraints

- Never hand-edit `external/`, `plugins/`, `opencode/` — output regenerates via `npm run build`.
- Gates on every commit: `npm test`, `npm run typecheck`, `npm run lint`, `npm run format:check`,
  `npm run build`, `npm run validate`. Committed output must be regenerated in the same commit
  (CI fails on stale output; `docs/ledger.json` joins that contract in Task 2).
- RED first: every behaviour lands as a failing test before its implementation.
- Fixtures: use `makeRepo()` from `tools/testutil.ts` (fixture submodule dir `external/sp/`,
  namespace `superpowers`, plugin `deniz-process`), extend it per test by writing files into the
  returned root — the pattern at `tools/build.test.ts:78-110`.
- Reference spellings (ADR-0008): neutral/authored text spells targets by upstream address —
  `superpowers:x` model-edge, `/superpowers:x` user-pointer. Manifest `depends_on` speaks output
  names. Never write `deniz-process:*` into an overlay or patch.
- Curation decisions are the user's alone. Tasks 4 and 9 contain explicit user checkpoints —
  do not proceed past them on your own judgment.
- Commit messages: lowercase `feat:` / `test:` / `docs:` / `fix:`, present tense, like the
  existing history.

---

## Phase 1 — space (Tasks 1–5)

### Task 1: The reference scanner — `tools/lib/refs.ts`

**Files:**
- Create: `tools/lib/refs.ts`
- Create: `tools/lib/refs.test.ts`
- Modify: `tools/build.test.ts` (one new test locking slash-form rewriting)

**Interfaces:**
- Produces: `type RefKind = "model" | "pointer"`,
  `interface Ref { kind: RefKind; ns: string; name: string; address: string }`,
  `extractRefs(content: string): Ref[]`,
  `candidateHits(content: string, names: Iterable<string>): string[]`.
  Later tasks (2, 3, 10) import exactly these.

- [ ] **Step 1: Write the failing tests**

Create `tools/lib/refs.test.ts`:

```ts
import assert from "node:assert/strict";
import { test } from "node:test";
import { candidateHits, extractRefs } from "./refs.ts";

test("extractRefs finds namespaced references and classifies their kind by the leading slash", () => {
  const body = [
    "Use the superpowers:test-driven-development skill at a correct seam.",
    "When stuck, suggest opening /superpowers:brainstorming to the user.",
    "Plain prose with no references.",
  ].join("\n");
  const refs = extractRefs(body);
  assert.deepEqual(refs, [
    { kind: "model", ns: "superpowers", name: "test-driven-development", address: "superpowers:test-driven-development" },
    { kind: "pointer", ns: "superpowers", name: "brainstorming", address: "superpowers:brainstorming" },
  ]);
});

test("extractRefs ignores lookalikes: longer tokens, chained colons, URLs", () => {
  const body = [
    "not-superpowers:foo is inside a longer token",           // char before ns is [a-z0-9-]
    "a:b:c is a chain, not a reference",                      // ':' on either side
    "https://example.com/skills/x carries no ns:name shape",
    "C:\\Users\\deniz is a Windows path",                     // uppercase ns never matches
  ].join("\n");
  assert.deepEqual(extractRefs(body), []);
});

test("extractRefs keeps duplicates in order — callers decide about uniqueness", () => {
  const refs = extractRefs("superpowers:tdd then superpowers:tdd again");
  assert.equal(refs.length, 2);
});

test("candidateHits matches known names as standalone words only", () => {
  const names = ["research", "tdd", "writing-plans"];
  const body = [
    "Run /tdd where possible.",                // slash prose — a hit
    "invoke the writing-plans skill",          // bare name — a hit
    "researching is not the research skill",   // 'researching' must not hit; bare 'research' does
    "superpowers:tdd",                         // fact spelling — colon boundary, no candidate hit
  ].join("\n");
  assert.deepEqual(candidateHits(body, names), ["research", "tdd", "writing-plans"]);
});

test("candidateHits returns nothing when no name appears", () => {
  assert.deepEqual(candidateHits("nothing here", ["tdd"]), []);
});
```

- [ ] **Step 2: Run them to verify they fail**

Run: `npm test`
Expected: FAIL — `Cannot find module '.../tools/lib/refs.ts'`

- [ ] **Step 3: Implement `tools/lib/refs.ts`**

```ts
/**
 * The one reference scanner (ADR-0008). Facts are namespaced spellings; the leading slash is the
 * kind: `ns:name` is a model-edge (the model invokes the target), `/ns:name` is a user-pointer
 * (the human is told what to open). Boundaries mirror rewriteRefs, so everything the rewrite
 * would touch is exactly what this extracts.
 */
export type RefKind = "model" | "pointer";

export interface Ref {
  kind: RefKind;
  ns: string;
  name: string;
  /** `ns:name` as written, without the pointer slash. */
  address: string;
}

const REF = /([a-z][a-z0-9-]*):([a-z][a-z0-9-]*)/g;

export function extractRefs(content: string): Ref[] {
  const out: Ref[] = [];
  for (const m of content.matchAll(REF)) {
    const before = m.index > 0 ? (content[m.index - 1] as string) : "";
    const after = content[m.index + m[0].length] ?? "";
    // Inside a longer token, or part of an a:b:c chain — not a reference. The after-side needs
    // only the colon check: REF already consumed every trailing [a-z0-9-].
    if (/[a-z0-9-]/.test(before) || before === ":" || after === ":") {
      continue;
    }
    out.push({
      kind: before === "/" ? "pointer" : "model",
      ns: m[1] as string,
      name: m[2] as string,
      address: m[0],
    });
  }
  return out;
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Candidate tier: known names appearing as standalone words. Heuristic by design — upstream names
 * are ordinary words — so hits are surfaced for human reading and never become build state.
 * The colon in the boundary class keeps a fact spelling from double-counting as its own candidate.
 */
export function candidateHits(content: string, names: Iterable<string>): string[] {
  const hits: string[] = [];
  for (const n of names) {
    const re = new RegExp(`(^|[^a-z0-9-:])${escapeRegExp(n)}($|[^a-z0-9-:])`, "m");
    if (re.test(content)) {
      hits.push(n);
    }
  }
  return hits.sort();
}
```

- [ ] **Step 4: Run the tests and make sure they pass**

Run: `npm test`
Expected: PASS (all suites; 5 new tests green)

- [ ] **Step 5: Lock the slash-form rewrite with a test (RED would be green — this is a regression lock)**

`rewriteRefs` bounds its key with `(?<![a-z0-9-])`, and `/` is outside that class, so
`/superpowers:x` already rewrites. Lock it so no future boundary change breaks pointers. Append to
`tools/build.test.ts`:

```ts
// ADR-0008: a user-pointer `/ns:name` must localize exactly like a model-edge — `/deniz-process:x`
// in the Claude tree, `/x` in the OpenCode one — because each is the form its harness lets a user type.
test("pointer spellings rewrite in both trees", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nWhen unsure, suggest /superpowers:beta to the user.\n",
  );
  buildAll(root);
  const claude = readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8");
  const oc = readFileSync(join(root, "opencode", "skills", "alpha", "SKILL.md"), "utf8");
  assert.match(claude, /\/deniz-process:beta/);
  assert.match(oc, /suggest \/beta to the user/);
});
```

Note: `makeRepo()`'s default manifest curates `sp/skills/alpha` and `sp/skills/beta` (see
`tools/testutil.ts`); if the default yaml this test inherits differs, write the two-item manifest
explicitly the way `tools/build.test.ts:78-92` does.

- [ ] **Step 6: One address computation — fix the `.agent.md` double extension (ROADMAP known gap)**

`addressOf` in `tools/lib/rewrite.ts` strips only `.md`, so an upstream agent file `zeta.agent.md`
gets map key `ns:zeta.agent` while references spell `ns:zeta` — never rewritten. Test first,
append to `tools/build.test.ts`:

```ts
test("an upstream agent named zeta.agent.md is addressed as ns:zeta", () => {
  const root = makeRepo();
  mkdirSync(join(root, "external", "sp", "agents"), { recursive: true });
  writeFileSync(join(root, "external", "sp", "agents", "zeta.agent.md"), "---\nname: zeta\ndescription: Z\n---\nZ.\n");
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nDispatch superpowers:zeta for this.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "  - source: sp/agents/zeta.agent.md",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const alpha = readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8");
  assert.match(alpha, /deniz-process:zeta/);
});
```

Run `npm test` → FAIL (the body keeps `superpowers:zeta`). Then fix `addressOf` in
`tools/lib/rewrite.ts`:

```ts
function addressOf(c: ComponentInfo): string {
  return c.type === "skill" ? basename(c.sourcePath) : basename(c.sourcePath, ".md").replace(/\.agent$/, "");
}
```

Run `npm test` → PASS.

- [ ] **Step 7: Run tests, typecheck, lint**

Run: `npm test && npm run typecheck && npm run lint && npm run format:check`
Expected: all PASS

- [ ] **Step 8: Commit**

```bash
git add tools/lib/refs.ts tools/lib/refs.test.ts tools/lib/rewrite.ts tools/build.test.ts
git commit -m "feat: one reference scanner - facts, kinds, candidates (ADR-0008)"
```

### Task 2: The ledger — `docs/ledger.json` emitted by the build

**Files:**
- Create: `tools/lib/ledger.ts`
- Create: `tools/lib/ledger.test.ts`
- Modify: `tools/build.ts` (call the writer at the end of `buildAll`, thread report data)
- Generated: `docs/ledger.json` (committed, like `plugins/` and `opencode/`)

**Interfaces:**
- Consumes: `extractRefs` from Task 1; `resolveItem` (`tools/lib/resolve.ts`); the built trees.
- Produces: `writeLedger(root: string, manifests: CurationManifest[], components: ComponentInfo[]): void`
  writing `docs/ledger.json` with the shape below. Tasks 3 and 10 read this file's shape.

Ledger shape (one entry per non-excluded manifest item; keys sorted; arrays sorted; one fact per line):

```json
{
  "deniz-process/systematic-debugging": {
    "source": "superpowers/skills/systematic-debugging",
    "invocation": "auto",
    "body": "overlay",
    "dependsOn": ["test-driven-development", "verification-before-completion"],
    "description": "Use when encountering any bug, test failure, performance regression, or unexpected behavior, before proposing fixes",
    "claude": {
      "artifacts": ["skill"],
      "edges": { "model": ["deniz-process:test-driven-development", "deniz-process:verification-before-completion"], "pointer": [] }
    },
    "opencode": {
      "artifacts": ["skill"],
      "edges": { "model": ["test-driven-development", "verification-before-completion"], "pointer": [] },
      "dropped": ["user-invocable"],
      "parked": []
    }
  }
}
```

- [ ] **Step 1: Write the failing test**

Create `tools/lib/ledger.test.ts`:

```ts
import assert from "node:assert/strict";
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "../build.ts";
import { makeRepo } from "../testutil.ts";

test("the build writes a deterministic ledger describing each item's resolved state per harness", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha", // fixture body references superpowers:beta
      "    invocation: auto",
      "  - source: sp/skills/beta",
      "    invocation: manual",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"));

  const alpha = ledger["deniz-process/alpha"];
  assert.equal(alpha.source, "sp/skills/alpha");
  assert.equal(alpha.invocation, "auto");
  assert.deepEqual(alpha.claude.artifacts, ["skill"]);
  assert.deepEqual(alpha.claude.edges.model, ["deniz-process:beta"]);
  assert.deepEqual(alpha.opencode.edges.model, ["beta"]);

  const beta = ledger["deniz-process/beta"];
  assert.deepEqual(beta.claude.artifacts, ["skill"]); // Claude: manual is still a skill, flagged
  assert.deepEqual(beta.opencode.artifacts, ["command"]); // OpenCode: manual is a command, no skill
  assert.deepEqual(alpha.opencode.dropped, ["user-invocable"]); // auto's Claude flag has no OpenCode home
  assert.deepEqual(beta.opencode.dropped, ["disable-model-invocation", "name"]); // command keeps description only

  // determinism: a second build produces byte-identical content
  const first = readFileSync(join(root, "docs", "ledger.json"), "utf8");
  buildAll(root);
  assert.equal(readFileSync(join(root, "docs", "ledger.json"), "utf8"), first);
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npm test`
Expected: FAIL — `ENOENT ... docs/ledger.json`

- [ ] **Step 3: Implement `tools/lib/ledger.ts`**

```ts
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parseDoc } from "./frontmatter.ts";
import type { CurationManifest } from "./manifest.ts";
import { extractRefs, type RefKind } from "./refs.ts";
import { resolveItem } from "./resolve.ts";
import type { ComponentInfo } from "./scan.ts";
import { listFiles } from "./overlay.ts";

/** Skill frontmatter OpenCode recognises (moved here from build.ts — build imports it back). */
export const OPENCODE_SKILL_KEYS = new Set(["name", "description", "license", "compatibility", "metadata"]);

interface HarnessState {
  artifacts: string[];
  edges: Record<RefKind, string[]>;
  dropped?: string[];
  parked?: string[];
}
interface LedgerEntry {
  source: string;
  invocation?: string;
  body?: string;
  mergedFrom?: string[];
  dependsOn?: string[];
  description: string;
  claude: HarnessState;
  opencode: HarnessState;
}

function sortedUnique(xs: string[]): string[] {
  return [...new Set(xs)].sort();
}

/** Facts in one built artifact set, filtered to our own output namespaces, spelled as found. */
function edgesIn(files: string[], ownNs: Set<string>): Record<RefKind, string[]> {
  const model: string[] = [];
  const pointer: string[] = [];
  for (const f of files) {
    for (const r of extractRefs(readFileSync(f, "utf8"))) {
      if (ownNs.has(r.ns)) {
        (r.kind === "model" ? model : pointer).push(r.address);
      }
    }
  }
  return { model: sortedUnique(model), pointer: sortedUnique(pointer) };
}

export function writeLedger(root: string, manifests: CurationManifest[], components: ComponentInfo[]): void {
  const ownNs = new Set(manifests.map((m) => m.plugin.name));
  const ledger: Record<string, LedgerEntry> = {};
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { outName, outType, id } = resolveItem(root, m.plugin.name, item, components);
      const claudeDir = join(root, "plugins", m.plugin.name, `${outType}s`, outType === "skill" ? outName : `${outName}.md`);
      const claudeFiles =
        outType === "skill"
          ? listFiles(claudeDir).filter((f) => f.endsWith(".md")).map((f) => join(claudeDir, f))
          : [claudeDir];
      const ocSkill = join(root, "opencode", "skills", outName, "SKILL.md");
      const ocCommand = join(root, "opencode", "commands", `${outName}.md`);
      const ocAgent = join(root, "opencode", "agents", `${outName}.md`);
      const ocArtifacts = [
        ...(existsSync(ocSkill) ? ["skill"] : []),
        ...(existsSync(ocCommand) ? ["command"] : []),
        ...(existsSync(ocAgent) ? ["agent"] : []),
      ];
      const parkedDir = join(root, "opencode", "skills", outName);
      const parked = !existsSync(ocSkill) && existsSync(parkedDir) ? listFiles(parkedDir) : [];
      const doc = parseDoc(readFileSync(outType === "skill" ? join(claudeDir, "SKILL.md") : claudeDir, "utf8"));
      const claudeEdges = edgesIn(claudeFiles, ownNs);
      // OpenCode text is bare — respell the Claude facts through the known mapping instead of
      // parsing bare words back (ADR-0008: detection never runs on rendered output).
      const respell = (xs: string[]) => xs.map((a) => a.split(":")[1] as string).sort();
      // Mirrors each emitter's drop policy, derived from output alone: an emitted skill keeps the
      // recognised keys; a command/agent keeps description (+ its forced fields).
      const ocDropped = existsSync(ocSkill)
        ? Object.keys(doc.frontmatter).filter((k) => !OPENCODE_SKILL_KEYS.has(k)).sort()
        : ocArtifacts.length
          ? Object.keys(doc.frontmatter).filter((k) => k !== "description").sort()
          : [];
      const entry: LedgerEntry = {
        source: item.source,
        ...(item.invocation ? { invocation: item.invocation } : {}),
        ...(item.body ? { body: item.body } : {}),
        ...(item.merged_from ? { mergedFrom: [...item.merged_from].sort() } : {}),
        ...(item.depends_on ? { dependsOn: [...item.depends_on].sort() } : {}),
        description: String(doc.frontmatter.description ?? ""),
        claude: { artifacts: [outType], edges: claudeEdges },
        opencode: {
          artifacts: ocArtifacts,
          edges: { model: respell(claudeEdges.model), pointer: respell(claudeEdges.pointer) },
          dropped: ocDropped,
          parked,
        },
      };
      ledger[id] = entry;
    }
  }
  const sorted = Object.fromEntries(Object.entries(ledger).sort(([a], [b]) => a.localeCompare(b)));
  mkdirSync(join(root, "docs"), { recursive: true });
  writeFileSync(join(root, "docs", "ledger.json"), `${JSON.stringify(sorted, null, 2)}\n`);
}
```

Implementation notes for the engineer:
- `item.merged_from` / `item.depends_on` do not exist on `CurationItem` yet — Task 3 adds
  `depends_on`, Task 7 adds `merged_from`. Until then leave those two spread lines out (add them in
  the task that adds the field; the tests of those tasks assert the ledger picks them up).
- `OPENCODE_SKILL_KEYS` moves here from `tools/build.ts` (exported); `build.ts` deletes its local
  copy and imports it back — one direction only, since build already imports the ledger.
- The commands/agents path join for `claudeDir` builds `commands/<name>.md` — note the
  `${outType}s` pluralization matches the existing emit layout (`build.ts` `kindDir`).

- [ ] **Step 4: Wire it into `buildAll`**

In `tools/build.ts`, add the import and call it as the last step of `buildAll` (after both
`rewriteTree` calls, before `return report`):

```ts
import { writeLedger } from "./lib/ledger.ts";
// ... at the end of buildAll():
  rewriteTree(join(root, "plugins"), buildRewriteMap(manifests, components, "claude"));
  rewriteTree(join(root, "opencode"), buildRewriteMap(manifests, components, "opencode"));
  writeLedger(root, manifests, components);
  return report;
```

- [ ] **Step 5: Run the tests**

Run: `npm test && npm run typecheck`
Expected: PASS

- [ ] **Step 6: Rebuild the real repo and commit the generated ledger**

Run: `npm run build && npm run validate && git status --porcelain`
Expected: validate `0 error(s), 0 warning(s)`; new file `docs/ledger.json`; no other tree changes.

```bash
git add tools/lib/ledger.ts tools/lib/ledger.test.ts tools/build.ts docs/ledger.json
git commit -m "feat: the build ledgers its resolved output (ADR-0008)"
```

### Task 3: The linker — `validate` checks resolution, reachability and `depends_on`

**Files:**
- Modify: `tools/lib/manifest.ts` (add `depends_on?: string[]` to `CurationItem`)
- Modify: `tools/validate.ts` (replace the two-tree leftover scan; add the linking pass)
- Modify: `tools/validate.test.ts` (new rule tests)

**Interfaces:**
- Consumes: `extractRefs` (Task 1); built trees; `parseDoc`.
- Produces: linker findings with these stable message prefixes (Task 4 and CI depend on the
  wording): `dangling reference`, `model-edge to a target the model cannot reach`,
  `pointer to a target the user cannot reach`, `undeclared dependency`, `stale depends_on`,
  `output namespace leaked into opencode/`.

Rules (all errors, per ADR-0008; the existing unrewritten-upstream-reference scan becomes
plugins/-only, retiring the doubled-warning gap):

| # | Rule | Reads |
|---|---|---|
| L1 | every `<plugin>:<name>` fact in `plugins/` resolves to a built output name | plugins/ |
| L2 | model-edge target: built SKILL.md must not carry `disable-model-invocation: true`, and `opencode/skills/<name>/SKILL.md` must exist | both trees |
| L3 | pointer target: built SKILL.md must not carry `user-invocable: false`, and `opencode/commands/<name>.md` must exist | both trees |
| L4 | no `<plugin>:` spelling anywhere in `opencode/` | opencode/ |
| L5 | per item: derived model-edge target set == `depends_on` set (both directions) | ledger inputs |
| L6 | a command body naming a parked file that does not exist | opencode/ |
| L7 | an own skill sharing a curated item's name in the same plugin (emit-time overwrite) | skills/ + manifests |

- [ ] **Step 1: Add the manifest field**

In `tools/lib/manifest.ts`, extend `CurationItem` after the `invocation` field:

```ts
  /**
   * Output names of this item's model-edge targets (ADR-0008). Enforced both ways by validate:
   * a declared name with no matching fact in the shipped body is stale, a fact with no
   * declaration is undeclared — both are errors.
   */
  depends_on?: string[];
```

- [ ] **Step 2: Write the failing rule tests**

Append to `tools/validate.test.ts` (fixture pattern: build first, then assert findings; helper
style as in the existing tests there):

```ts
test("linker: a model-edge to a manual target is an error in both trees", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha", // body says: use the superpowers:beta skill (model-edge)
      "    depends_on: [beta]",
      "  - source: sp/skills/beta",
      "    invocation: manual",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("model-edge to a target the model cannot reach")),
    JSON.stringify(findings, null, 2),
  );
});

test("linker: a pointer to a model-only target is an error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\nSuggest /superpowers:beta to the user.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "  - source: sp/skills/beta",
      "    invocation: auto", // OpenCode gets no command for beta -> nothing for a user to type
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("pointer to a target the user cannot reach")));
});

test("linker: depends_on is enforced in both directions", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha", // body has a model-edge to beta, but declares gamma instead
      "    depends_on: [gamma]",
      "  - source: sp/skills/beta",
      "  - source: sp/skills/gamma",
    ].join("\n")}\n`,
  );
  mkdirSync(join(root, "external", "sp", "skills", "gamma"), { recursive: true });
  writeFileSync(
    join(root, "external", "sp", "skills", "gamma", "SKILL.md"),
    "---\nname: gamma\ndescription: Gamma upstream\n---\nBody.\n",
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("undeclared dependency: beta")));
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("stale depends_on: gamma")));
});

test("linker: an output namespace leaking into opencode/ is an error", () => {
  const root = makeRepo();
  buildAll(root);
  // simulate the historical bug: a hand-authored output-space reference surviving into opencode
  writeFileSync(
    join(root, "opencode", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: x\n---\nUse the deniz-process:beta skill.\n",
  );
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("output namespace leaked into opencode/")));
});

test("linker: a dangling namespaced reference is an error", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: x\n---\nUse the deniz-process:nonexistent skill.\n",
  );
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("dangling reference")));
});

test("linker: a command body naming a missing parked file is an error", () => {
  const root = makeRepo();
  writeFileSync(join(root, "external", "sp", "skills", "beta", "notes.md"), "bundled\n");
  writeFileSync(
    join(root, "external", "sp", "skills", "beta", "SKILL.md"),
    "---\nname: beta\ndescription: Beta upstream\n---\nRead skills/beta/other.md first.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/beta",
      "    invocation: manual",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("skills/beta/other.md")));
});

test("linker: an own skill colliding with a curated item in the same plugin is an error", () => {
  const root = makeRepo();
  mkdirSync(join(root, "skills", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(join(root, "skills", "deniz-process", "alpha", "SKILL.md"), "---\nname: alpha\ndescription: own\n---\nOwn.\n");
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("own skill") && f.message.includes("alpha")));
});
```

- [ ] **Step 3: Run to verify the new tests fail**

Run: `npm test`
Expected: the five new tests FAIL (no linker yet); every existing test still PASSES.

- [ ] **Step 4: Implement the linking pass in `tools/validate.ts`**

Changes, in order:

4a. Narrow the existing leftover scan (section "3. portability … + 4. leftover upstream
references") to `plugins/` only: change `for (const outDir of ["plugins", "opencode"])` to
`for (const outDir of ["plugins"])` and keep the symlink walk for both trees by hoisting it:

```ts
  for (const outDir of ["plugins", "opencode"]) {
    const dir = join(root, outDir);
    if (!existsSync(dir)) {
      continue;
    }
    for (const link of walkSymlinks(dir)) {
      findings.push({
        level: "error",
        message: `committed build output must not contain symlinks: ${relative(root, link).replaceAll("\\", "/")}`,
      });
    }
  }
```

4b. Append the linking pass after it (imports: `extractRefs` from `./lib/refs.ts`):

```ts
  // 4b. reference linking (ADR-0008). plugins/ carries the canonical namespaced text; opencode/
  // is derived from it, so facts are read once and each tree is checked in its own address space.
  interface TargetState {
    modelReachClaude: boolean;
    userReachClaude: boolean;
    ocSkill: boolean;
    ocCommand: boolean;
  }
  const targetState = new Map<string, TargetState>();
  for (const plugin of existsSync(pluginsDir) ? readdirSync(pluginsDir) : []) {
    const skillsDir = join(pluginsDir, plugin, "skills");
    for (const name of existsSync(skillsDir) ? readdirSync(skillsDir) : []) {
      const doc = parseDoc(readFileSync(join(skillsDir, name, "SKILL.md"), "utf8"));
      targetState.set(name, {
        modelReachClaude: doc.frontmatter["disable-model-invocation"] !== true,
        userReachClaude: doc.frontmatter["user-invocable"] !== false,
        ocSkill: existsSync(join(root, "opencode", "skills", name, "SKILL.md")),
        ocCommand: existsSync(join(root, "opencode", "commands", `${name}.md`)),
      });
    }
    for (const kind of ["commands", "agents"] as const) {
      const dir = join(pluginsDir, plugin, kind);
      for (const f of existsSync(dir) ? readdirSync(dir) : []) {
        const name = basename(f, ".md");
        targetState.set(name, {
          modelReachClaude: kind === "commands", // agents are dispatched, not skill-invoked
          userReachClaude: true,
          ocSkill: false,
          ocCommand: existsSync(join(root, "opencode", "commands", `${name}.md`)),
        });
      }
    }
  }

  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { outName, outType, id } = resolveItem(root, m.plugin.name, item, components);
      const dir = join(pluginsDir, m.plugin.name, outType === "skill" ? join("skills", outName) : join(`${outType}s`, `${outName}.md`));
      if (!existsSync(dir)) {
        continue;
      }
      const files = outType === "skill" ? [...walk(dir)].filter((f) => f.endsWith(".md")) : [dir];
      const derived = new Set<string>();
      for (const file of files) {
        const rel = relative(root, file).replaceAll("\\", "/");
        for (const ref of extractRefs(readFileSync(file, "utf8"))) {
          if (!ownNs.has(ref.ns)) {
            continue; // upstream-namespace leftovers stay section 4's warning, not the linker's
          }
          const t = targetState.get(ref.name);
          if (!t) {
            findings.push({ level: "error", message: `${rel}: dangling reference ${ref.address} — no built output has that name` });
            continue;
          }
          if (ref.kind === "model") {
            derived.add(ref.name);
            if (!t.modelReachClaude || !t.ocSkill) {
              findings.push({
                level: "error",
                message: `${rel}: model-edge to a target the model cannot reach: ${ref.name} (${!t.modelReachClaude ? "disable-model-invocation in the Claude tree" : "no opencode/skills entry"}) — make the target auto/both, or spell the reference /${ref.address} if the human is the audience`,
              });
            }
          } else {
            if (!t.userReachClaude || !t.ocCommand) {
              findings.push({
                level: "error",
                message: `${rel}: pointer to a target the user cannot reach: ${ref.name} (${!t.userReachClaude ? "user-invocable: false in the Claude tree" : "no opencode/commands entry"}) — make the target manual/both, or drop the slash if the model is the audience`,
              });
            }
          }
        }
      }
      const declared = new Set(item.depends_on ?? []);
      for (const d of derived) {
        if (!declared.has(d)) {
          findings.push({ level: "error", message: `${id}: undeclared dependency: ${d} — add it to depends_on in curation/${m.plugin.name}.yaml` });
        }
      }
      for (const d of declared) {
        if (!derived.has(d)) {
          findings.push({ level: "error", message: `${id}: stale depends_on: ${d} — no model-edge in the shipped body references it` });
        }
      }
    }
  }

  // L4: output namespaces must never reach the OpenCode tree — it has no plugin concept.
  const ocDir = join(root, "opencode");
  for (const file of existsSync(ocDir) ? [...walk(ocDir)].filter((f) => f.endsWith(".md")) : []) {
    for (const ref of extractRefs(readFileSync(file, "utf8"))) {
      if (ownNs.has(ref.ns)) {
        findings.push({
          level: "error",
          message: `${relative(root, file).replaceAll("\\", "/")}: output namespace leaked into opencode/: ${ref.address}`,
        });
      }
    }
  }

  // L6: a command body naming a parked file that is not there (omitted, renamed) ships a dead path.
  const cmdsDir = join(root, "opencode", "commands");
  for (const f of existsSync(cmdsDir) ? readdirSync(cmdsDir) : []) {
    const name = basename(f, ".md");
    const parkedDir = join(root, "opencode", "skills", name);
    if (!existsSync(parkedDir)) {
      continue;
    }
    const parkedFiles = new Set(listFiles(parkedDir));
    const body = readFileSync(join(cmdsDir, f), "utf8");
    for (const hit of body.matchAll(new RegExp(`skills/${name}/([A-Za-z0-9._/-]+)`, "g"))) {
      const target = (hit[1] as string).replace(/[).,:;'"]+$/, "");
      if (!parkedFiles.has(target)) {
        findings.push({
          level: "error",
          message: `opencode/commands/${f}: references skills/${name}/${target}, which is not among the parked files`,
        });
      }
    }
  }

  // L7 (place in section 1's per-manifest loop): an own skill overwrites a curated item of the
  // same name at emit time — own skills are copied last, and the cross-plugin duplicate check
  // never sees a same-plugin overwrite because only one directory survives.
  //   const ownDir = join(root, "skills", m.plugin.name);
  //   const own = existsSync(ownDir)
  //     ? readdirSync(ownDir).filter((n) => statSync(join(ownDir, n)).isDirectory())
  //     : [];
  //   ...per item:
  //   if (!item.exclude && own.includes(outName)) {
  //     findings.push({
  //       level: "error",
  //       message: `${m.plugin.name}/${outName}: own skill skills/${m.plugin.name}/${outName}/ silently overwrites this curated item — rename one`,
  //     });
  //   }
```

Wiring notes:
- `ownNs` = `new Set(manifests.map((m) => m.plugin.name))`, declared once near the existing
  `upstreamNs` set.
- The existing section-4 leftover warning keeps firing for upstream namespaces, now from
  `plugins/` only.
- Reuse the existing `walk` generator and `listFiles` from `./lib/overlay.ts`; `pluginsDir`
  already exists in scope. L7's snippet is commented because it lives inside the existing
  section-1 loop rather than the linking pass — lift it there verbatim.

- [ ] **Step 5: Run the tests**

Run: `npm test && npm run typecheck && npm run lint`
Expected: PASS — including the five new rule tests.

- [ ] **Step 6: Run validate against the real repo — expect exactly the known reds, commit nothing**

Run: `npm run build && npm run validate`
Expected: ERRORS, all of one family — `undeclared dependency` findings for every namespaced fact
in `deniz-process` (systematic-debugging, writing-plans, subagent-driven-development,
executing-plans, research, using-superpowers at minimum), plus `model-edge to a target the model
cannot reach: brainstorming` from using-superpowers. This red is the linker catching the real
debt — the proof that Task 3 works — and it is Task 4's input.

**No commit in this task (user's ruling, 2026-07-31): Tasks 3 and 4 land as ONE commit.** The
rule and the data that satisfies it are one logical change — the same atomicity the inventory
gate enforces — and every commit keeps its gates green. Leave the Task 3 changes in the working
tree; Task 4's final step commits both halves together.

### Task 4: The data — transcription, the pointer reword, first green link

**Files:**
- Modify: `curation/deniz-process.yaml` (add `depends_on` per item — **user reviews first**)
- Modify: `overlays/deniz-process/using-superpowers/overlay.patch` (re-cut: pointer reword)
- Modify: `overlays/overlays.lock.json` (re-stamp of using-superpowers via eject)
- Generated: `plugins/`, `opencode/`, `docs/ledger.json` (rebuild)

**Interfaces:**
- Consumes: linker findings (Task 3), ledger edges (Task 2).
- Produces: a green `npm run validate`; the declared dependency map.

- [ ] **Step 1: Pre-flight probe — pointer wording (human at TUI, ~1 hour)**

Per `docs/agents/harness-probing.md`: isolated homes, one fixture skill whose body carries a
pointer sentence (`When the user asks to build, suggest opening /lab:ceremony — do not run it
yourself.`) and a control model-edge. Observe on both harnesses: does the model relay the pointer
instead of attempting invocation? Record the observation in the lab's RESULTS.md; the outcome
decides the *wording template* used in Step 2 (the mechanism does not change either way).

- [ ] **Step 2: Reword the using-superpowers line to a pointer**

The shipped line (Skill Priority section) reads:

```
- "Let's build X" → deniz-process:brainstorming first, then implementation skills.
- "Fix this bug" → deniz-process:systematic-debugging first, then domain skills.
```

In the *patch working copy* these spell `superpowers:...` (neutral space). Re-cut the patch:

```bash
npm run eject -- deniz-process using-superpowers --patch --force   # lays down the working copy
# edit overlays/deniz-process/using-superpowers/SKILL.md — apply the existing patch's edits
# (1% block out, Red Flags out, platform section out; see git show HEAD:overlays/deniz-process/using-superpowers/overlay.patch)
# and change the two lines to (template subject to Step 1's probe):
#   - "Let's build X" → suggest opening /superpowers:brainstorming first; implementation skills follow it.
#   - "Fix this bug" → the superpowers:systematic-debugging skill first, then domain skills.
npm run eject -- deniz-process using-superpowers --patch           # cuts the new overlay.patch, re-stamps the lock
```

Note the asymmetry, which is the point: brainstorming is `manual` (the human opens the ceremony —
pointer), systematic-debugging is `auto` (the model engages it — model-edge stays).

- [ ] **Step 3: Generate the transcription draft and STOP FOR USER REVIEW**

```bash
npm run build   # refresh docs/ledger.json (red validate is fine here)
node -e "const l=require('./docs/ledger.json');for(const [id,e] of Object.entries(l)){const m=e.claude.edges.model.map(a=>a.split(':')[1]);if(m.length)console.log(id,'->',m.join(', '))}"
```

Present the table to the user (expected at time of writing — verify against the live output):

| item | depends_on draft |
|---|---|
| writing-plans | subagent-driven-development, executing-plans, using-git-worktrees |
| subagent-driven-development | *(whatever the grep shows — likely executing-plans, finishing-a-development-branch, requesting-code-review, using-git-worktrees)* |
| executing-plans | *(likely finishing-a-development-branch, using-git-worktrees)* |
| systematic-debugging | test-driven-development, verification-before-completion |
| research | dispatching-parallel-agents |
| using-superpowers | systematic-debugging *(brainstorming is now a pointer — not declared)* |

**USER CHECKPOINT: the user rules on every row (and on the pointer wording) before the manifest
is touched. Curation decisions are his alone.**

- [ ] **Step 4: Write the approved map into `curation/deniz-process.yaml`**

Per approved row, add beside the item (comment the why where the edge is non-obvious), e.g.:

```yaml
  - source: superpowers/skills/systematic-debugging
    invocation: auto
    depends_on: [test-driven-development, verification-before-completion] # Phase 6 invokes both
```

- [ ] **Step 5: Rebuild, validate green, gates, commit**

Run: `npm run build && npm run validate`
Expected: `0 error(s), 0 warning(s)`

Run: `npm test && npm run typecheck && npm run lint && npm run format:check && npm run inventory && git status --porcelain`
Expected: all green; inventory unchanged (depends_on moves no Curated column — if it did change,
commit it here, same-commit rule).

```bash
git add tools/lib/manifest.ts tools/validate.ts tools/validate.test.ts curation/deniz-process.yaml overlays/ plugins/ opencode/ docs/ledger.json
git commit -m "feat: validate links references and the tree satisfies it - first green link"
```

(One commit for Tasks 3+4 — user's ruling; see Task 3 Step 6.)

### Task 5: Phase-1 docs

**Files:**
- Modify: `AGENTS.md` (Sources of Truth table gains the ledger row)
- Modify: `docs/ROADMAP.md` (item 2: phase 1 lines move from "next" to "Current State"; the three
  retired Known Gaps entries are deleted)

- [ ] **Step 1: AGENTS.md — add to the Sources of Truth table:**

```markdown
| Resolved output state (invocation, artifacts, edges) per item × harness | `docs/ledger.json` (generated — regenerate, don't edit) |
```

- [ ] **Step 2: ROADMAP — move phase 1 into Current State (one line: reference model + linker +
ledger live, ADR-0008), delete the doubled-warnings / own-skill-collision / `.agent.md` Known
Gaps entries, keep phase 2 as the open half of item 2.**

- [ ] **Step 3: Commit**

```bash
git add AGENTS.md docs/ROADMAP.md
git commit -m "docs: the roadmap and sources of truth catch up with phase 1"
```

---

## Phase 2 — time (Tasks 6–11)

### Task 6: Lock schema — `mergeSources`

**Files:**
- Modify: `tools/lib/overlay.ts` (schema + two functions)
- Create: `tools/lib/overlay.merge.test.ts`

**Interfaces:**
- Produces:
  `LockEntry.mergeSources?: Record<string, Record<string, string | null>>` (address → file → sha,
  `null` = recorded absent),
  `stampMergeFiles(dir: string, files: string[]): Record<string, string | null>`,
  `driftedMergeSources(root: string, entry: LockEntry): string[]` (human-readable
  `"<address>: <file>[ (appeared upstream)]"` lines). Tasks 7–9 consume all three.

- [ ] **Step 1: Write the failing tests**

Create `tools/lib/overlay.merge.test.ts`:

```ts
import assert from "node:assert/strict";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { makeRepo } from "../testutil.ts";
import { driftedMergeSources, stampMergeFiles, type LockEntry } from "./overlay.ts";

test("stampMergeFiles records a sha per present file and null per absent one", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const stamped = stampMergeFiles(dir, ["SKILL.md", "not-there.md"]);
  assert.match(stamped["SKILL.md"] as string, /^[0-9a-f]{40}$/);
  assert.equal(stamped["not-there.md"], null);
});

test("driftedMergeSources reports edits, deletions, and files that appeared over a null stamp", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const entry: LockEntry = {
    source: "sp/skills/alpha",
    files: {},
    mergeSources: { "sp/skills/beta": stampMergeFiles(dir, ["SKILL.md", "notes.md"]) },
  };
  assert.deepEqual(driftedMergeSources(root, entry), []); // clean right after stamping

  writeFileSync(join(dir, "SKILL.md"), "---\nname: beta\ndescription: changed\n---\nMoved.\n");
  writeFileSync(join(dir, "notes.md"), "appeared later\n");
  const drifted = driftedMergeSources(root, entry);
  assert.deepEqual(drifted, ["sp/skills/beta: SKILL.md", "sp/skills/beta: notes.md (appeared upstream)"]);
});
```

- [ ] **Step 2: Run to verify failure** — `npm test` → FAIL (`stampMergeFiles` not exported).

- [ ] **Step 3: Implement in `tools/lib/overlay.ts`**

Extend `LockEntry` and add below `driftedFiles`:

```ts
export interface LockEntry {
  source: string;
  /** upstream file name -> git blob SHA of its content when the overlay was written */
  files: Record<string, string>;
  /** merge source address -> same-filename stamps; null records "absent when blessed". */
  mergeSources?: Record<string, Record<string, string | null>>;
}

/** Same-filename rule for a merge source: stamp what exists, record what does not. */
export function stampMergeFiles(dir: string, files: string[]): Record<string, string | null> {
  const out: Record<string, string | null> = {};
  for (const f of [...files].sort()) {
    const p = join(dir, f);
    out[f] = existsSync(p) && lstatSync(p).isFile() ? blobSha(p) : null;
  }
  return out;
}

/** Merge-source files that no longer match their stamp — including ones that appeared over a null. */
export function driftedMergeSources(root: string, entry: LockEntry): string[] {
  const msgs: string[] = [];
  for (const [addr, files] of Object.entries(entry.mergeSources ?? {})) {
    const dir = join(root, "external", addr);
    for (const [f, sha] of Object.entries(files)) {
      const p = join(dir, f);
      if (sha === null) {
        if (existsSync(p)) {
          msgs.push(`${addr}: ${f} (appeared upstream)`);
        }
      } else if (!existsSync(p) || blobSha(p) !== sha) {
        msgs.push(`${addr}: ${f}`);
      }
    }
  }
  return msgs.sort();
}
```

- [ ] **Step 4: Run tests** — `npm test && npm run typecheck` → PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/lib/overlay.ts tools/lib/overlay.merge.test.ts
git commit -m "feat: the lock learns merge sources - same-filename stamps, absent recorded (ADR-0001)"
```

### Task 7: `merged_from` — manifest field, build drift, validate wiring

**Files:**
- Modify: `tools/lib/manifest.ts` (add `merged_from?: string[]`)
- Modify: `tools/build.ts` (`overlayDrift` checks merge sources; `collectProblems` message)
- Modify: `tools/validate.ts` (contradiction rules)
- Modify: `tools/build.test.ts`, `tools/validate.test.ts`
- Modify: `tools/lib/ledger.ts` (emit `mergedFrom` — the spread line deferred from Task 2)

**Interfaces:**
- Consumes: Task 6's lock functions.
- Produces: build failure message
  `"<id>: merge source changed under the overlay (<address>: <file>) — review the diff, then: npm run eject -- <plugin> <name> --bless --yes"`;
  validate errors `merged_from without body:` and `merged_from source not found in external/`.

- [ ] **Step 1: Failing tests**

Append to `tools/build.test.ts`:

```ts
test("a drifted merge source stops the build, naming the source that moved", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    body: overlay",
      "    merged_from: [sp/skills/beta]",
      "  - source: sp/skills/beta",
      "    exclude: true",
    ].join("\n")}\n`,
  );
  // eject-equivalent fixture setup: overlay dir + lock entry with mergeSources
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(join(root, "overlays", "deniz-process", "alpha", "SKILL.md"), "---\nname: alpha\ndescription: merged\n---\nMerged body.\n");
  const lock = {
    "deniz-process/alpha": {
      source: "sp/skills/alpha",
      files: stampFiles(join(root, "external", "sp", "skills", "alpha"), ["SKILL.md"]),
      mergeSources: { "sp/skills/beta": stampMergeFiles(join(root, "external", "sp", "skills", "beta"), ["SKILL.md"]) },
    },
  };
  writeFileSync(join(root, "overlays", "overlays.lock.json"), `${JSON.stringify(lock, null, 2)}\n`);
  buildAll(root); // clean: both stamps current

  writeFileSync(join(root, "external", "sp", "skills", "beta", "SKILL.md"), "---\nname: beta\ndescription: moved\n---\nMoved.\n");
  assert.throws(() => buildAll(root), /merge source changed under the overlay \(sp\/skills\/beta: SKILL\.md\)/);
});

test("merged_from declared but not blessed stops the build", () => {
  const root = makeRepo();
  // same manifest as above, but the lock has no mergeSources
  // ... (same fixture minus the mergeSources key)
  assert.throws(() => buildAll(root), /merge sources are not blessed .* --bless/);
});
```

(`stampFiles` and `stampMergeFiles` come from `./lib/overlay.ts`; the second test's fixture
repeats the first minus the `mergeSources` key — write it out fully, no sharing, tests read
standalone.)

Append to `tools/validate.test.ts`:

```ts
test("merged_from on an item with no body: is an error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    merged_from: [sp/skills/beta]",
      "  - source: sp/skills/beta",
      "    exclude: true",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("merged_from without body:")));
});
```

- [ ] **Step 2: Run to verify failure** — `npm test` → new tests FAIL.

- [ ] **Step 3: Implement**

`tools/lib/manifest.ts`, after `body`:

```ts
  /**
   * Upstream addresses whose content this item's body merges in (ADR-0001). Each is blessed like
   * the primary under the same-filename rule; drift in any source stops the build.
   */
  merged_from?: string[];
```

`tools/build.ts` — extend `overlayDrift` (after the primary `driftedFiles` check):

```ts
  const declaredSources = new Set(item.merged_from ?? []);
  const blessedSources = new Set(Object.keys(entry.mergeSources ?? {}));
  const sameSources =
    declaredSources.size === blessedSources.size && [...declaredSources].every((s) => blessedSources.has(s));
  if ((declaredSources.size || blessedSources.size) && !sameSources) {
    return [
      `${id}: merge sources are not blessed (declared: ${[...declaredSources].join(", ") || "none"}; lock has: ${[...blessedSources].join(", ") || "none"}) — run: ${bless} --yes`,
    ];
  }
  const mergeDrift = driftedMergeSources(root, entry);
  if (mergeDrift.length) {
    return [`${id}: merge source changed under the overlay (${mergeDrift.join("; ")}) — review the diff, then: ${bless} --yes`];
  }
```

(The size-or-size guard also catches the inverse rot: a lock still carrying `mergeSources` after
the manifest dropped `merged_from`. `overlayDrift` gains a `root` parameter; both call sites pass
it already-in-scope.)

`tools/validate.ts` — in section 1's per-item loop:

```ts
      if (item.merged_from?.length && !item.body) {
        findings.push({
          level: "error",
          message: `${m.plugin.name}/${outName}: merged_from without body: — a merge is a body edit; declare body: overlay|patch or drop merged_from`,
        });
      }
      for (const src of item.merged_from ?? []) {
        if (!components.some((c) => c.sourcePath === src)) {
          findings.push({ level: "error", message: `${m.plugin.name}/${outName}: merged_from source not found in external/: ${src}` });
        }
      }
```

`tools/lib/ledger.ts` — activate the deferred spread line: `...(item.merged_from ? { mergedFrom: [...item.merged_from].sort() } : {})`.

- [ ] **Step 4: Run tests** — `npm test && npm run typecheck && npm run lint` → PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/lib/manifest.ts tools/build.ts tools/validate.ts tools/lib/ledger.ts tools/build.test.ts tools/validate.test.ts
git commit -m "feat: merged_from - merge sources bless like the primary, drift stops the build (ADR-0001)"
```

### Task 8: `eject --bless` — all sources, diff shown, `--yes` to accept

**Files:**
- Modify: `tools/eject.ts` (`stamp()` gains merge sources; `--bless` shows diffs and requires `--yes` when anything drifted)
- Create: `tools/eject.test.ts` (if absent; otherwise extend)

**Interfaces:**
- Consumes: Task 6/7.
- Produces: CLI contract — `npm run eject -- <plugin> <name> --bless` prints per-file diffs and
  exits 1 when stamps would change; `--bless --yes` writes them. No drift → stamps silently
  (unchanged behaviour).

- [ ] **Step 1: Failing test** (drive the CLI the way `npm` would):

```ts
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { stampFiles, stampMergeFiles } from "./lib/overlay.ts";
import { makeRepo } from "./testutil.ts";

function eject(root: string, args: string[]): { code: number; out: string } {
  try {
    const out = execFileSync(process.execPath, [join(import.meta.dirname, "eject.ts"), ...args], {
      cwd: root,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
    });
    return { code: 0, out };
  } catch (e) {
    const err = e as { status?: number; stdout?: string; stderr?: string };
    return { code: err.status ?? 1, out: `${err.stdout ?? ""}${err.stderr ?? ""}` };
  }
}

test("--bless shows what moved and refuses to stamp without --yes", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    body: overlay",
      "    merged_from: [sp/skills/beta]",
      "  - source: sp/skills/beta",
      "    exclude: true",
    ].join("\n")}\n`,
  );
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: merged\n---\nMerged body.\n",
  );
  const lock = {
    "deniz-process/alpha": {
      source: "sp/skills/alpha",
      files: stampFiles(join(root, "external", "sp", "skills", "alpha"), ["SKILL.md"]),
      mergeSources: { "sp/skills/beta": stampMergeFiles(join(root, "external", "sp", "skills", "beta"), ["SKILL.md"]) },
    },
  };
  writeFileSync(join(root, "overlays", "overlays.lock.json"), `${JSON.stringify(lock, null, 2)}\n`);
  writeFileSync(join(root, "external", "sp", "skills", "beta", "SKILL.md"), "---\nname: beta\ndescription: moved\n---\nMoved.\n");
  const before = readFileSync(join(root, "overlays", "overlays.lock.json"), "utf8");
  const dry = eject(root, ["deniz-process", "alpha", "--bless"]);
  assert.equal(dry.code, 1);
  assert.match(dry.out, /sp\/skills\/beta: SKILL\.md/);
  assert.match(dry.out, /--yes/);
  assert.equal(readFileSync(join(root, "overlays", "overlays.lock.json"), "utf8"), before, "lock untouched");

  const wet = eject(root, ["deniz-process", "alpha", "--bless", "--yes"]);
  assert.equal(wet.code, 0);
  assert.match(readFileSync(join(root, "overlays", "overlays.lock.json"), "utf8"), /"mergeSources"/);
});
```

(`eject.ts` is argv-driven at module top level — the CLI test is the honest seam. If
`process.execPath` needs a TS loader flag in this repo's Node setup, mirror however
`tools/check-test-discovery.ts` invokes tools, or extract eject's body into an exported function
with an argv parameter and test that — smallest honest refactor wins.)

- [ ] **Step 2: Run to verify failure**, **Step 3: Implement:**

In `tools/eject.ts` — `stamp()` and the `--bless` branch:

```ts
function stamp(): void {
  const lock = loadLock(root);
  const entry: LockEntry = { source: item.source, files: stampFiles(base, stampedNames()) };
  if (item.merged_from?.length) {
    entry.mergeSources = {};
    for (const addr of item.merged_from) {
      entry.mergeSources[addr] = stampMergeFiles(join(root, "external", addr), stampedNames());
    }
  }
  lock[lockKey(plugin, name)] = entry;
  saveLock(root, lock);
}

if (flags.has("--bless")) {
  if (!existsSync(dest)) {
    console.error(`Nothing to bless: overlays/${plugin}/${name}/ does not exist`);
    process.exit(1);
  }
  const lock = loadLock(root);
  const prior = lock[lockKey(plugin, name)];
  const moved: string[] = prior ? [...driftedFiles(base, prior).map((f) => `${item.source}: ${f}`), ...driftedMergeSources(root, prior)] : [];
  for (const line of moved) {
    console.log(`DRIFTED ${line}`);
    const [addr, file] = line.replace(" (appeared upstream)", "").split(": ") as [string, string];
    const sha = addr === item.source ? prior?.files[file] : prior?.mergeSources?.[addr]?.[file];
    if (sha) {
      try {
        const old = execFileSync("git", ["-C", join(root, "external", addr.split("/")[0] as string), "cat-file", "blob", sha], { encoding: "utf8" });
        const tmp = join(tmpdir(), `bless-${basename(file)}`);
        writeFileSync(tmp, old);
        try {
          execFileSync("git", ["diff", "--no-index", "--", tmp, join(root, "external", addr, file)], { stdio: "inherit" });
        } catch {
          /* --no-index exits 1 on difference — the diff has been printed */
        }
      } catch {
        console.log(`  (recorded blob ${sha.slice(0, 7)} unavailable locally — blessing records current content)`);
      }
    }
  }
  if (moved.length && !flags.has("--yes")) {
    console.error(`Re-blessing records the content above as reviewed. Re-run with --yes to accept.`);
    process.exit(1);
  }
  stamp();
  console.log(`Blessed overlays/${plugin}/${name}/ against current upstream (${item.source}${item.merged_from?.length ? ` + ${item.merged_from.join(", ")}` : ""}).`);
  process.exit(0);
}
```

(Imports to add at top: `driftedFiles`, `driftedMergeSources`, `stampMergeFiles`, `LockEntry`
from `./lib/overlay.ts`; `tmpdir` from `node:os`.)

- [ ] **Step 4: Run tests + all gates**, **Step 5: Commit**

```bash
git add tools/eject.ts tools/eject.test.ts
git commit -m "feat: --bless shows the drift it accepts and stamps every merge source"
```

### Task 9: Retrofit systematic-debugging

**Files:**
- Modify: `curation/deniz-process.yaml` (systematic-debugging gains `merged_from`; the
  "glance at diagnosing-bugs on sync bumps" comment line is deleted — the machine does it now)
- Modify: `overlays/overlays.lock.json` (via `--bless --yes`)
- Generated: `docs/ledger.json` (gains `mergedFrom`)

- [ ] **Step 1: Manifest edit** — on the systematic-debugging item:

```yaml
    body: overlay
    merged_from: [mattpocock-skills/skills/engineering/diagnosing-bugs]
```

and delete the comment line `# upstream improvements no longer flow — glance at diagnosing-bugs on sync bumps`
(exact current wording: `... Known cost: matt-side upstream improvements no longer flow — glance at diagnosing-bugs on sync bumps.` — trim the clause, keep the rest of the comment).

- [ ] **Step 2: Bless (user runs or approves — it stamps a curation artifact):**

```bash
npm run eject -- deniz-process systematic-debugging --bless --yes
```

Expected: lock entry gains `"mergeSources": { "mattpocock-skills/skills/engineering/diagnosing-bugs": { "SKILL.md": "<sha>" } }`.

- [ ] **Step 3: Rebuild + gates + commit**

```bash
npm run build && npm run validate && npm test && npm run typecheck
git add curation/deniz-process.yaml overlays/overlays.lock.json docs/ledger.json plugins/ opencode/
git commit -m "feat: systematic-debugging's matt source is blessed, not glanced at"
```

(plugins/opencode likely unchanged — include only what `git status` shows.)

### Task 10: Semantic sync — posture drift, merge-source tags, candidate diff

**Files:**
- Modify: `tools/sync.ts` (three additions to `syncReport` + a posture helper)
- Modify: `tools/sync.test.ts` (if absent, create — `syncReport` is a pure function today; keep
  the new logic pure the same way: inputs in, lines out)

**Interfaces:**
- Consumes: `candidateHits` (Task 1), `docs/ledger.json` (Task 2), `parseDoc`.
- Produces: `syncReport(sub, changed, manifests, opts)` where
  `opts: { readFile: (rev: "old" | "new", rel: string) => string | null; ledgerNames: string[] }`
  — the CLI wires `readFile` to `git -C external/<sub> show <rev>:<rel>`; tests wire it to
  fixtures. Output line prefixes: `POSTURE`, `MERGE SOURCE`, `CANDIDATE EDGES`.

- [ ] **Step 1: Failing tests** (pure-function style):

```ts
import assert from "node:assert/strict";
import { test } from "node:test";
import { syncReport } from "./sync.ts";

const manifests = [
  {
    plugin: { name: "deniz-process", description: "x", version: "0.1.0" },
    items: [
      { source: "sp/skills/alpha" }, // passthrough: no invocation stated
      { source: "sp/skills/host", body: "overlay" as const, merged_from: ["mp/skills/beta"] },
    ],
  },
];

test("a passthrough item's invocation flip is reported as posture drift", () => {
  const lines = syncReport("sp", ["skills/alpha/SKILL.md"], manifests, {
    readFile: (rev, rel) =>
      rel === "skills/alpha/SKILL.md"
        ? rev === "old"
          ? "---\nname: alpha\ndescription: a\n---\nB.\n"
          : "---\nname: alpha\ndescription: a\ndisable-model-invocation: true\n---\nB.\n"
        : null,
    ledgerNames: [],
  });
  assert.ok(lines.some((l) => l.includes("POSTURE") && l.includes("disable-model-invocation") && l.includes("undefined -> true")));
});

test("a pin move touching a merge source tags the merged item even across submodules", () => {
  const lines = syncReport("mp", ["skills/beta/SKILL.md"], manifests, { readFile: () => null, ledgerNames: [] });
  assert.ok(lines.some((l) => l.includes("MERGE SOURCE") && l.includes("sp/skills/host") && l.includes("mp/skills/beta")));
});

test("candidate edges that appeared or vanished in a changed body are reported", () => {
  const lines = syncReport("sp", ["skills/alpha/SKILL.md"], manifests, {
    readFile: (rev, rel) =>
      rel === "skills/alpha/SKILL.md"
        ? rev === "old"
          ? "---\nname: alpha\ndescription: a\n---\nRun /tdd here.\n"
          : "---\nname: alpha\ndescription: a\n---\nSee writing-plans instead.\n"
        : null,
    ledgerNames: ["tdd", "writing-plans"],
  });
  assert.ok(lines.some((l) => l.includes("CANDIDATE EDGES") && l.includes("+writing-plans") && l.includes("-tdd")));
});
```

- [ ] **Step 2: Run to verify failure**, **Step 3: Implement in `tools/sync.ts`:**

```ts
import { parseDoc } from "./lib/frontmatter.ts";
import { candidateHits } from "./lib/refs.ts";

const POSTURE_KEYS = ["disable-model-invocation", "user-invocable", "description"] as const;

export interface SyncIO {
  /** file content at the old/new pin, or null when unreadable (added/deleted/binary). */
  readFile: (rev: "old" | "new", rel: string) => string | null;
  /** output names in the current ledger — the candidate universe. */
  ledgerNames: string[];
}

export function syncReport(sub: string, changed: string[], manifests: CurationManifest[], io: SyncIO): string[] {
  const lines: string[] = [];
  for (const m of manifests) {
    for (const item of m.items) {
      // merge sources may live in a DIFFERENT submodule than the item's primary — check first,
      // outside the primary-source guard.
      for (const msrc of item.merged_from ?? []) {
        if (!msrc.startsWith(`${sub}/`)) {
          continue;
        }
        const mrel = msrc.slice(sub.length + 1);
        if (changed.some((c) => c === mrel || c.startsWith(`${mrel}/`))) {
          lines.push(
            `${m.plugin.name}: ${item.source} MERGE SOURCE ${msrc} changed upstream — review, then re-bless: git -C external/${sub} diff <old> <new> -- ${mrel}`,
          );
        }
      }
      if (!item.source.startsWith(`${sub}/`)) {
        continue;
      }
      const rel = item.source.slice(sub.length + 1);
      const hit = changed.some((c) => c === rel || c.startsWith(`${rel}/`));
      if (!hit) {
        continue;
      }
      // ... existing tag block unchanged ...
      const skillRel = `${rel}/SKILL.md`;
      if (changed.includes(skillRel)) {
        const before = io.readFile("old", skillRel);
        const after = io.readFile("new", skillRel);
        if (before !== null && after !== null) {
          if (!item.invocation) {
            const fa = parseDoc(before).frontmatter;
            const fb = parseDoc(after).frontmatter;
            for (const k of POSTURE_KEYS) {
              if (JSON.stringify(fa[k]) !== JSON.stringify(fb[k])) {
                lines.push(
                  `${m.plugin.name}: ${item.source} POSTURE ${k}: ${JSON.stringify(fa[k]) ?? "undefined"} -> ${JSON.stringify(fb[k]) ?? "undefined"} (passthrough item — this flows straight into output)`,
                );
              }
            }
          }
          const na = new Set(candidateHits(before, io.ledgerNames));
          const nb = new Set(candidateHits(after, io.ledgerNames));
          const appeared = [...nb].filter((x) => !na.has(x)).map((x) => `+${x}`);
          const vanished = [...na].filter((x) => !nb.has(x)).map((x) => `-${x}`);
          if (appeared.length || vanished.length) {
            lines.push(`${m.plugin.name}: ${item.source} CANDIDATE EDGES ${[...appeared, ...vanished].join(" ")} — read the hits before believing them (they over-report)`);
          }
        }
      }
    }
  }
  return lines;
}
```

CLI wiring in the main block: `readFile: (rev, rel) => { try { return execFileSync("git", ["-C", dir, "show", `${rev === "old" ? before : after}:${rel}`], { encoding: "utf8" }); } catch { return null; } }`
and `ledgerNames: Object.keys(JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8"))).map((k) => k.split("/")[1] as string)`
(guard with `existsSync` — an empty universe is fine before the first build).
`JSON.stringify(undefined)` is `undefined` (the value) — the `?? "undefined"` renders it; keep it.

- [ ] **Step 4: Run tests + gates**, **Step 5: Commit**

```bash
git add tools/sync.ts tools/sync.test.ts
git commit -m "feat: sync reports meaning - posture drift, merge sources, candidate edges"
```

### Task 11: Close the wave

**Files:**
- Modify: `docs/ROADMAP.md` (item 2 leaves Next Up; one Current State line: reference model,
  linker, ledger, merged_from, semantic sync live; delete the retired Known Gaps entries:
  "--bless shows nothing", and re-check the sync-mislabel entry — if the deleted/renamed-source
  case is still unfixed, it stays)
- Delete: `docs/superpowers/plans/2026-07-31-reference-model-wave.md` (this file — transient)
- Modify: `docs/agents/handover-prompts/` — write the next session-pickup prompt only if the
  session ends here (template: `session-pickup-template.md`)

- [ ] **Step 1: ROADMAP shrink + gap deletions**
- [ ] **Step 2: Delete this plan**
- [ ] **Step 3: Final gates on a clean tree**

Run: `npm test && npm run typecheck && npm run lint && npm run format:check && npm run build && npm run validate && git status --porcelain`
Expected: all green, tree clean after commit.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs: the reference-model wave lands; the plan retires"
```

### Task 12: The provenance rule — the curation layer stamps no names, no dates

**Files:**
- Modify: `tools/validate.ts` (new rule section)
- Modify: `tools/validate.test.ts` (three rule tests)
- Modify: `AGENTS.md` (Hard Rules bullet gains the sentence)

Rule (user's ruling): comments in `curation/*.yaml` (only the segment after `#` on each line —
values like `description:` keep their branding), every text file under `overlays/` except the
lock (patch files: **added** lines only — context lines are upstream's, not ours), and
`skills/**` must not contain the curator's name (`\bDeniz\b`, `\bIrgin\b` — case-sensitive, so
`deniz-process` never matches) or an ISO date (`\b20\d{2}-\d{2}-\d{2}\b`). Each hit is an
**error**: git carries who and when; inline stamps are noise.

- [ ] **Step 1: Failing tests** (append to `tools/validate.test.ts`):

```ts
test("provenance: a name or a date in a manifest comment is an error; a description keeps its branding", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "# reviewed by Deniz on 2026-07-31",
      "plugin:",
      "  name: deniz-process",
      '  description: "Deniz curated set"', // a value, not a comment — exempt
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "  - source: sp/skills/beta",
    ].join("\n")}\n`,
  );
  buildAll(root);
  const hits = validateRepo(root).filter((f) => f.level === "error" && f.message.includes("stamps no names"));
  assert.equal(hits.length, 2, JSON.stringify(hits, null, 2)); // the name and the date, nothing for the value
});

test("provenance: overlay bodies are scanned; the lock is exempt", () => {
  const root = makeRepo();
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: x\n---\nRewritten by Deniz on 2026-07-31.\n",
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    body: overlay",
      "  - source: sp/skills/beta",
    ].join("\n")}\n`,
  );
  const lock = {
    "deniz-process/alpha": {
      source: "sp/skills/alpha",
      files: stampFiles(join(root, "external", "sp", "skills", "alpha"), ["SKILL.md"]),
    },
  };
  writeFileSync(join(root, "overlays", "overlays.lock.json"), `${JSON.stringify(lock, null, 2)}\n`);
  const hits = validateRepo(root).filter((f) => f.level === "error" && f.message.includes("stamps no names"));
  assert.equal(hits.length, 2); // name + date in the overlay body; the lock's own content never scanned
});

test("provenance: a patch's context lines are upstream's — only added lines are ours", () => {
  const root = makeRepo();
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "overlay.patch"),
    ["diff --git a/SKILL.md b/SKILL.md", "--- a/SKILL.md", "+++ b/SKILL.md", "@@ -1,3 +1,3 @@", " Deniz appears upstream here", "-old line", "+new line, no stamps"].join("\n"),
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    body: patch",
      "  - source: sp/skills/beta",
    ].join("\n")}\n`,
  );
  const hits = validateRepo(root).filter((f) => f.level === "error" && f.message.includes("stamps no names"));
  assert.equal(hits.length, 0, JSON.stringify(hits, null, 2));
});
```

(`stampFiles` from `./lib/overlay.ts`; these tests call `validateRepo` without a build where the
scan needs no output tree — the unrelated overlay-wiring findings those fixtures also produce are
filtered out by the message filter.)

- [ ] **Step 2: Run to verify the three fail** — `npm test` → FAIL (no provenance findings).

- [ ] **Step 3: Implement in `tools/validate.ts`** — new section after the marketplace check:

```ts
  // 7. provenance: the curation layer stamps no names, no dates — git carries who and when.
  // Scope is the authored layer only: yaml COMMENT segments (values keep their branding),
  // overlay bodies, a patch's added lines (context lines are upstream's), own skills.
  const BANNED = [/\bDeniz\b/, /\bIrgin\b/, /\b20\d{2}-\d{2}-\d{2}\b/];
  const provenance = (text: string, where: string): void => {
    for (const re of BANNED) {
      const m = re.exec(text);
      if (m) {
        findings.push({
          level: "error",
          message: `${where}: the curation layer stamps no names or dates — git carries provenance (found "${m[0]}")`,
        });
      }
    }
  };
  for (const f of readdirSync(join(root, "curation")).filter((n) => n.endsWith(".yaml"))) {
    readFileSync(join(root, "curation", f), "utf8")
      .split("\n")
      .forEach((line, i) => {
        const hash = line.indexOf("#");
        if (hash >= 0) {
          provenance(line.slice(hash), `curation/${f}:${i + 1}`);
        }
      });
  }
  for (const base of ["overlays", "skills"]) {
    const dir = join(root, base);
    if (!existsSync(dir)) {
      continue;
    }
    for (const file of walk(dir)) {
      const rel = relative(root, file).replaceAll("\\", "/");
      if (rel.endsWith(LOCK_FILE)) {
        continue;
      }
      const text = readFileSync(file, "utf8");
      if (rel.endsWith(".patch")) {
        text.split("\n").forEach((line, i) => {
          if (line.startsWith("+") && !line.startsWith("+++")) {
            provenance(line, `${rel}:${i + 1}`);
          }
        });
      } else {
        provenance(text, rel);
      }
    }
  }
```

- [ ] **Step 4: AGENTS.md Hard Rules** — extend the secrets bullet:

```markdown
- Never commit secrets, tokens, or machine-specific paths. The curation layer (manifest
  comments, `overlays/`, `skills/`) stamps no curator names and no dates — git carries
  provenance; `validate` errors on both.
```

- [ ] **Step 5: All gates + real-repo validate 0/0** (the manifests were scrubbed before this
  task landed — if validate finds a leftover stamp, that is the rule working: fix the stamp, not
  the rule).

- [ ] **Step 6: Commit**

```bash
git add tools/validate.ts tools/validate.test.ts AGENTS.md
git commit -m "feat: the curation layer stamps no names, no dates - validate enforces it"
```

---

## Post-wave verification (event-driven, not CI)

Behaviour is probed, not unit-tested (ADR-0008 consequences; method:
`docs/agents/harness-probing.md`). After the wave — and again after each merge pass — one short
TUI round on the real output: the reworded using-superpowers pointer relays instead of invoking;
a model-edge (systematic-debugging → test-driven-development) still composes; `opencode debug
skill` lists exactly the ledger's opencode skill set. Durable observations graduate to
`docs/research/skill-invocation-across-harnesses.md` with the harness version.

## Explicit non-goals (decided 2026-07-31)

- No per-harness overlays — the known body-level divergences are emitter-policy classes, and no
  concrete item demands one yet (ADR-0006 keeps the door).
- No notification machinery — `git diff docs/ledger.json` is the channel.
- No TUI automation in CI; no own-skill neutral addressing until an own skill is referenced.
- No new npm commands, no new dependencies.
