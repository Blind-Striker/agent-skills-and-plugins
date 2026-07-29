# Multi-Harness Skill/Plugin Repo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the curation repo from the approved spec: git submodules of upstream skill repos + YAML curation manifests + overlays, compiled by a small TypeScript toolchain into `deniz-*` Claude Code plugins (personal marketplace) and OpenCode output.

**Architecture:** `external/` submodules are read-only inputs. `curation/*.yaml` declares what to take and how to modify it; `overlays/` holds full-file replacements; `skills/` holds original skills. `npm run build` compiles everything into committed `plugins/` and `opencode/` trees, rewrites cross-references, and regenerates `marketplace.json`. Supporting commands: `inventory`, `eject`, `sync`, `validate`.

**Tech Stack:** Node ≥ 24 (runs `.ts` natively via type stripping — no bundler, no tsx), ESM, `node:test` for tests, single runtime dependency: `yaml`.

**Spec:** `docs/superpowers/specs/2026-07-29-skills-plugin-repo-design.md`

## Global Constraints

- Node ≥ 24 (dev machine has v24.13.0; npm 11.6.2). All scripts run as `node tools/<name>.ts` — no compile step.
- Runtime dependencies: **only `yaml`**. Dev dependencies: only `@types/node`, `typescript` (^7.x — Go-native TS7, used solely for `tsc --noEmit`), `@biomejs/biome` (format + lint). No test framework (use `node:test`), no CLI framework, no schema library.
- ESM everywhere (`"type": "module"`); relative imports include the `.ts` extension (required by Node type stripping).
- Plugin names exactly: `deniz-process`, `deniz-dotnet-general`, `deniz-dotnet-aspire`, `deniz-dotnet-akka`. Marketplace name: `deniz-skills`.
- npm scripts exactly: `build`, `inventory`, `eject`, `sync`, `validate`, `test`, `typecheck`, `format`, `format:check`, `lint`. (`test` is `node --test "tools/**/*.test.ts"` — a bare directory arg breaks on Node 24.)
- Biome config (`biome.json`) excludes `external/`, `plugins/`, `opencode/` — tooling never touches submodules or build output.
- `external/`, `plugins/`, `opencode/` are NEVER hand-edited. `plugins/` and `opencode/` are build outputs but ARE committed.
- Keep tooling small (spec: "over-engineering yok"). If a feature isn't needed by a task below, don't build it. `hooks.include` non-empty → build throws "not implemented yet".
- Code and comments in English. Windows dev machine: always build paths with `node:path.join`, never hardcode `/` or `\` in joins (forward slashes OK inside string *values* like `sourcePath`).
- Commit after every task (steps include exact commands). Shell commands below are for Git Bash / POSIX sh.

## File Structure

```
package.json / tsconfig.json / .gitignore          — Task 1
tools/lib/frontmatter.ts (+ .test.ts)              — Task 2: SKILL.md frontmatter parse/serialize
tools/lib/manifest.ts   (+ .test.ts)               — Task 3: curation YAML types + loader
tools/lib/scan.ts       (+ .test.ts)               — Task 4: discover skills/commands/agents in a submodule
external/{superpowers,mattpocock-skills,dotnet-skills,aspire-skills,dotnet-agent-skills}  — Task 5 (submodules)
tools/inventory.ts                                 — Task 6: writes docs/inventory.md
tools/lib/rewrite.ts    (+ .test.ts)               — Task 7: reference map + string rewrite
tools/build.ts          (+ .test.ts)               — Task 8 core, Task 9 adds OpenCode emission
tools/validate.ts       (+ .test.ts)               — Task 10
tools/eject.ts, tools/sync.ts (+ sync report test) — Task 11
curation/*.yaml (4 starter manifests)              — Task 12 (+ first committed build output)
.github/workflows/validate.yml, README.md          — Task 13
```

---

### Task 1: Repo scaffold (package.json, tsconfig, .gitignore)

**Files:**
- Create: `package.json`, `tsconfig.json`, `.gitignore`

**Interfaces:**
- Produces: npm scripts `build|inventory|eject|sync|validate|test|typecheck`; dependency `yaml` importable from `tools/`.

- [ ] **Step 1: Write `package.json`**

```json
{
  "name": "deniz-agent-skills",
  "private": true,
  "type": "module",
  "engines": { "node": ">=24" },
  "scripts": {
    "build": "node tools/build.ts",
    "inventory": "node tools/inventory.ts",
    "eject": "node tools/eject.ts",
    "sync": "node tools/sync.ts",
    "validate": "node tools/validate.ts",
    "test": "node --test \"tools/**/*.test.ts\"",
    "typecheck": "tsc --noEmit"
  },
  "dependencies": { "yaml": "^2.6.0" },
  "devDependencies": { "@types/node": "^24.0.0", "typescript": "^5.8.0" }
}
```

- [ ] **Step 2: Write `tsconfig.json`**

```json
{
  "compilerOptions": {
    "module": "nodenext",
    "moduleResolution": "nodenext",
    "target": "es2023",
    "strict": true,
    "noEmit": true,
    "allowImportingTsExtensions": true,
    "rewriteRelativeImportExtensions": false,
    "erasableSyntaxOnly": true,
    "types": ["node"],
    "skipLibCheck": true
  },
  "include": ["tools"]
}
```

`erasableSyntaxOnly` guarantees every construct we write survives Node's type stripping (no enums, no namespaces, no parameter properties).

- [ ] **Step 3: Write `.gitignore`**

```
node_modules/
```

- [ ] **Step 4: Install and verify**

Run: `npm install && node -e "import('yaml').then(() => console.log('yaml ok'))" && npx tsc --noEmit`
Expected: `yaml ok`, tsc exits 0 (no input files yet is fine; if tsc errors with "No inputs were found", that is acceptable at this stage).

- [ ] **Step 5: Commit**

```bash
git add package.json package-lock.json tsconfig.json .gitignore
git commit -m "chore: scaffold Node/TypeScript tooling"
```

---

### Task 2: Frontmatter library

**Files:**
- Create: `tools/lib/frontmatter.ts`
- Test: `tools/lib/frontmatter.test.ts`

**Interfaces:**
- Produces:
  - `interface ParsedDoc { frontmatter: Record<string, unknown>; body: string }`
  - `parseDoc(content: string): ParsedDoc`
  - `serializeDoc(doc: ParsedDoc): string` — emits `---\n<yaml>\n---\n\n<body>`

- [ ] **Step 1: Write the failing test**

```ts
// tools/lib/frontmatter.test.ts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseDoc, serializeDoc } from './frontmatter.ts';

test('parseDoc splits frontmatter and body', () => {
  const doc = parseDoc('---\nname: foo\ndescription: Bar baz\n---\n\nBody text\n');
  assert.equal(doc.frontmatter.name, 'foo');
  assert.equal(doc.frontmatter.description, 'Bar baz');
  assert.equal(doc.body, 'Body text\n');
});

test('parseDoc handles CRLF line endings', () => {
  const doc = parseDoc('---\r\nname: foo\r\n---\r\nBody');
  assert.equal(doc.frontmatter.name, 'foo');
  assert.equal(doc.body, 'Body');
});

test('parseDoc without frontmatter returns empty object', () => {
  const doc = parseDoc('Just body');
  assert.deepEqual(doc.frontmatter, {});
  assert.equal(doc.body, 'Just body');
});

test('serializeDoc round-trips', () => {
  const out = serializeDoc({ frontmatter: { name: 'x', description: 'd' }, body: 'B\n' });
  assert.equal(out, '---\nname: x\ndescription: d\n---\n\nB\n');
  assert.deepEqual(parseDoc(out).frontmatter, { name: 'x', description: 'd' });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node --test tools/lib/frontmatter.test.ts`
Expected: FAIL — `Cannot find module ... frontmatter.ts`

- [ ] **Step 3: Implement**

```ts
// tools/lib/frontmatter.ts
import { parse as parseYaml, stringify as stringifyYaml } from 'yaml';

export interface ParsedDoc {
  frontmatter: Record<string, unknown>;
  body: string;
}

export function parseDoc(content: string): ParsedDoc {
  const m = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?/.exec(content);
  if (!m) return { frontmatter: {}, body: content };
  const frontmatter = (parseYaml(m[1]) as Record<string, unknown> | null) ?? {};
  return { frontmatter, body: content.slice(m[0].length).replace(/^\r?\n/, '') };
}

export function serializeDoc(doc: ParsedDoc): string {
  return `---\n${stringifyYaml(doc.frontmatter).trimEnd()}\n---\n\n${doc.body}`;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `node --test tools/lib/frontmatter.test.ts`
Expected: 4 passing.

- [ ] **Step 5: Commit**

```bash
git add tools/lib/frontmatter.ts tools/lib/frontmatter.test.ts
git commit -m "feat: frontmatter parse/serialize"
```

---

### Task 3: Curation manifest library

**Files:**
- Create: `tools/lib/manifest.ts`
- Test: `tools/lib/manifest.test.ts`

**Interfaces:**
- Produces:
  - `type ComponentType = 'skill' | 'command' | 'agent'`
  - `interface CurationItem { source: string; exclude?: boolean; name?: string; as?: ComponentType; frontmatter?: Record<string, unknown>; body?: 'overlay' }`
  - `interface CurationManifest { plugin: { name: string; description: string; version: string }; items: CurationItem[]; hooks?: { include: string[] } }`
  - `loadManifest(path: string): CurationManifest` — throws with the file path in the message on missing required fields.

- [ ] **Step 1: Write the failing test**

```ts
// tools/lib/manifest.test.ts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { loadManifest } from './manifest.ts';

function write(content: string): string {
  const p = join(mkdtempSync(join(tmpdir(), 'manifest-')), 'x.yaml');
  writeFileSync(p, content);
  return p;
}

test('loads a valid manifest', () => {
  const m = loadManifest(write(
    'plugin:\n  name: deniz-process\n  description: Process skills\n  version: 0.1.0\n' +
    'items:\n  - source: superpowers/skills/brainstorming\n    name: brainstorm\n    as: command\n',
  ));
  assert.equal(m.plugin.name, 'deniz-process');
  assert.equal(m.items[0].source, 'superpowers/skills/brainstorming');
  assert.equal(m.items[0].as, 'command');
});

test('items defaults to empty array', () => {
  const m = loadManifest(write('plugin:\n  name: p\n  description: d\n  version: 0.1.0\n'));
  assert.deepEqual(m.items, []);
});

test('missing plugin.name throws with path', () => {
  const p = write('plugin:\n  description: d\n  version: 0.1.0\n');
  assert.throws(() => loadManifest(p), new RegExp('plugin\\.name'));
});

test('item without source throws', () => {
  const p = write('plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - name: x\n');
  assert.throws(() => loadManifest(p), /source/);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node --test tools/lib/manifest.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```ts
// tools/lib/manifest.ts
import { readFileSync } from 'node:fs';
import { parse as parseYaml } from 'yaml';

export type ComponentType = 'skill' | 'command' | 'agent';

export interface CurationItem {
  source: string;
  exclude?: boolean;
  name?: string;
  as?: ComponentType;
  frontmatter?: Record<string, unknown>;
  body?: 'overlay';
}

export interface CurationManifest {
  plugin: { name: string; description: string; version: string };
  items: CurationItem[];
  hooks?: { include: string[] };
}

export function loadManifest(path: string): CurationManifest {
  const raw = parseYaml(readFileSync(path, 'utf8')) as CurationManifest | null;
  if (!raw?.plugin?.name) throw new Error(`${path}: plugin.name is required`);
  if (!raw.plugin.description) throw new Error(`${path}: plugin.description is required`);
  if (!raw.plugin.version) throw new Error(`${path}: plugin.version is required`);
  raw.items ??= [];
  for (const item of raw.items) {
    if (!item.source) throw new Error(`${path}: every item needs a source`);
  }
  return raw;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `node --test tools/lib/manifest.test.ts`
Expected: 4 passing.

- [ ] **Step 5: Commit**

```bash
git add tools/lib/manifest.ts tools/lib/manifest.test.ts
git commit -m "feat: curation manifest loader"
```

---

### Task 4: Submodule scanner

**Files:**
- Create: `tools/lib/scan.ts`
- Test: `tools/lib/scan.test.ts`

**Interfaces:**
- Consumes: `parseDoc` from `tools/lib/frontmatter.ts`.
- Produces:
  - `interface ComponentInfo { submodule: string; namespace: string; type: ComponentType; name: string; description: string; sourcePath: string; files: number; bytes: number }`
    - `sourcePath` is repo-relative with forward slashes, e.g. `superpowers/skills/brainstorming` (a **directory** for skills, a **file** like `superpowers/commands/foo.md` for commands/agents). This exact string is what curation `source:` fields must match.
    - `namespace` is the upstream plugin name from the nearest `.claude-plugin/plugin.json` walking up, falling back to the submodule dir name.
  - `scanSubmodule(externalDir: string, submodule: string): ComponentInfo[]`

- [ ] **Step 1: Write the failing test**

```ts
// tools/lib/scan.test.ts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { scanSubmodule } from './scan.ts';

function makeFixture(): string {
  const ext = mkdtempSync(join(tmpdir(), 'scan-'));
  mkdirSync(join(ext, 'sp', '.claude-plugin'), { recursive: true });
  writeFileSync(join(ext, 'sp', '.claude-plugin', 'plugin.json'), JSON.stringify({ name: 'superpowers' }));
  mkdirSync(join(ext, 'sp', 'skills', 'foo', 'references'), { recursive: true });
  writeFileSync(join(ext, 'sp', 'skills', 'foo', 'SKILL.md'), '---\nname: foo\ndescription: Foo skill\n---\n\nBody');
  writeFileSync(join(ext, 'sp', 'skills', 'foo', 'references', 'extra.md'), 'ref');
  mkdirSync(join(ext, 'sp', 'commands'), { recursive: true });
  writeFileSync(join(ext, 'sp', 'commands', 'bar.md'), '---\ndescription: Bar cmd\n---\n\nDo bar');
  mkdirSync(join(ext, 'sp', 'agents'), { recursive: true });
  writeFileSync(join(ext, 'sp', 'agents', 'helper.md'), '---\nname: helper\ndescription: Helps\n---\n\nYou help.');
  return ext;
}

test('finds skills, commands, agents with namespace from plugin.json', () => {
  const comps = scanSubmodule(makeFixture(), 'sp');
  const skill = comps.find((c) => c.type === 'skill');
  assert.ok(skill);
  assert.equal(skill.name, 'foo');
  assert.equal(skill.namespace, 'superpowers');
  assert.equal(skill.sourcePath, 'sp/skills/foo');
  assert.equal(skill.files, 2);
  assert.ok(skill.bytes > 0);
  const cmd = comps.find((c) => c.type === 'command');
  assert.ok(cmd);
  assert.equal(cmd.name, 'bar');
  assert.equal(cmd.sourcePath, 'sp/commands/bar.md');
  const agent = comps.find((c) => c.type === 'agent');
  assert.ok(agent);
  assert.equal(agent.name, 'helper');
});

test('falls back to submodule dir name when no plugin.json', () => {
  const ext = mkdtempSync(join(tmpdir(), 'scan-'));
  mkdirSync(join(ext, 'raw', 'skills', 'x'), { recursive: true });
  writeFileSync(join(ext, 'raw', 'skills', 'x', 'SKILL.md'), '---\nname: x\ndescription: X\n---\n\nB');
  const comps = scanSubmodule(ext, 'raw');
  assert.equal(comps[0].namespace, 'raw');
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node --test tools/lib/scan.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```ts
// tools/lib/scan.ts
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { basename, dirname, join, relative } from 'node:path';
import { parseDoc } from './frontmatter.ts';
import type { ComponentType } from './manifest.ts';

export interface ComponentInfo {
  submodule: string;
  namespace: string;
  type: ComponentType;
  name: string;
  description: string;
  sourcePath: string;
  files: number;
  bytes: number;
}

function* walk(dir: string): Generator<string> {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === '.git' || e.name === 'node_modules') continue;
    const p = join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else yield p;
  }
}

function findNamespace(startDir: string, stopDir: string): string {
  let dir = startDir;
  while (true) {
    const pj = join(dir, '.claude-plugin', 'plugin.json');
    if (existsSync(pj)) return (JSON.parse(readFileSync(pj, 'utf8')) as { name: string }).name;
    if (dir === stopDir) return basename(stopDir);
    dir = dirname(dir);
  }
}

function rel(root: string, p: string): string {
  return relative(root, p).replaceAll('\\', '/');
}

export function scanSubmodule(externalDir: string, submodule: string): ComponentInfo[] {
  const root = join(externalDir, submodule);
  const out: ComponentInfo[] = [];
  for (const file of walk(root)) {
    const r = rel(root, file);
    if (basename(file) === 'SKILL.md') {
      const dir = dirname(file);
      const { frontmatter } = parseDoc(readFileSync(file, 'utf8'));
      const all = [...walk(dir)];
      out.push({
        submodule,
        namespace: findNamespace(dir, root),
        type: 'skill',
        name: String(frontmatter.name ?? basename(dir)),
        description: String(frontmatter.description ?? ''),
        sourcePath: `${submodule}/${rel(root, dir)}`,
        files: all.length,
        bytes: all.reduce((s, f) => s + statSync(f).size, 0),
      });
    } else if (/(^|\/)(commands|agents)\/[^/]+\.md$/.test(r)) {
      const type: ComponentType = /(^|\/)commands\//.test(r) ? 'command' : 'agent';
      const { frontmatter } = parseDoc(readFileSync(file, 'utf8'));
      out.push({
        submodule,
        namespace: findNamespace(dirname(file), root),
        type,
        name: String(frontmatter.name ?? basename(file, '.md')),
        description: String(frontmatter.description ?? ''),
        sourcePath: `${submodule}/${r}`,
        files: 1,
        bytes: statSync(file).size,
      });
    }
  }
  return out;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `node --test tools/lib/scan.test.ts`
Expected: 2 passing.

- [ ] **Step 5: Commit**

```bash
git add tools/lib/scan.ts tools/lib/scan.test.ts
git commit -m "feat: submodule component scanner"
```

---

### Task 5: Add the five upstream submodules

**Files:**
- Create: `.gitmodules`, `external/*` (5 submodules)

**Interfaces:**
- Produces: `external/superpowers`, `external/mattpocock-skills`, `external/dotnet-skills`, `external/aspire-skills`, `external/dotnet-agent-skills` — the directory names that all `source:` fields and `scanSubmodule` calls use.

- [ ] **Step 1: Add submodules (HTTPS URLs so CI can clone)**

```bash
git submodule add https://github.com/obra/superpowers.git external/superpowers
git submodule add https://github.com/mattpocock/skills.git external/mattpocock-skills
git submodule add https://github.com/Aaronontheweb/dotnet-skills.git external/dotnet-skills
git submodule add https://github.com/microsoft/aspire-skills.git external/aspire-skills
git submodule add https://github.com/dotnet/skills.git external/dotnet-agent-skills
```

- [ ] **Step 2: Smoke-check the scanner against real data**

Run: `node -e "const {scanSubmodule}=await import('./tools/lib/scan.ts'); const c=scanSubmodule('external','superpowers'); console.log(c.length, c.slice(0,3).map(x=>x.sourcePath))" --input-type=module`
Expected: a count > 5 and paths like `superpowers/skills/brainstorming`. If superpowers keeps skills elsewhere, note the real paths — Task 12 uses `docs/inventory.md` for exact paths, so nothing else hardcodes them.

- [ ] **Step 3: Commit**

```bash
git add .gitmodules external
git commit -m "chore: add upstream skill repos as submodules"
```

---

### Task 6: Inventory command

**Files:**
- Create: `tools/inventory.ts`
- Create (generated): `docs/inventory.md`

**Interfaces:**
- Consumes: `scanSubmodule`, `loadManifest`.
- Produces: `docs/inventory.md` — the catalog that curation sessions and Task 12 read for exact `source` paths. Spec rule: no skill-by-skill curation decision without this catalog.

- [ ] **Step 1: Implement (no unit test — thin composition of tested libs; verified against real data below)**

```ts
// tools/inventory.ts
import { existsSync, mkdirSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { scanSubmodule } from './lib/scan.ts';
import { loadManifest } from './lib/manifest.ts';

const root = process.cwd();
const curated = new Map<string, string>();
const curationDir = join(root, 'curation');
if (existsSync(curationDir)) {
  for (const f of readdirSync(curationDir).filter((f) => f.endsWith('.yaml'))) {
    const m = loadManifest(join(curationDir, f));
    for (const item of m.items) curated.set(item.source, item.exclude ? 'excluded' : m.plugin.name);
  }
}

let md = '# Skill Inventory\n\nGenerated by `npm run inventory`. Do not edit by hand.\n';
for (const sub of readdirSync(join(root, 'external')).sort()) {
  const comps = scanSubmodule(join(root, 'external'), sub).sort((a, b) =>
    a.type.localeCompare(b.type) || a.name.localeCompare(b.name),
  );
  md += `\n## ${sub} — ${comps.length} components\n\n| Name | Type | Curated | Description | Source | Files | KB |\n|---|---|---|---|---|---|---|\n`;
  for (const c of comps) {
    const desc = c.description.replaceAll('|', '\\|').replaceAll('\n', ' ').slice(0, 140);
    md += `| ${c.name} | ${c.type} | ${curated.get(c.sourcePath) ?? '—'} | ${desc} | \`${c.sourcePath}\` | ${c.files} | ${Math.ceil(c.bytes / 1024)} |\n`;
  }
}
mkdirSync(join(root, 'docs'), { recursive: true });
writeFileSync(join(root, 'docs', 'inventory.md'), md);
console.log('Wrote docs/inventory.md');
```

- [ ] **Step 2: Run against real submodules**

Run: `npm run inventory && head -30 docs/inventory.md`
Expected: `Wrote docs/inventory.md`; file has one `##` section per submodule with populated tables (superpowers section should list `brainstorming`, `writing-plans`, etc.).

- [ ] **Step 3: Commit**

```bash
git add tools/inventory.ts docs/inventory.md
git commit -m "feat: inventory catalog generator"
```

---

### Task 7: Reference rewrite library

**Files:**
- Create: `tools/lib/rewrite.ts`
- Test: `tools/lib/rewrite.test.ts`

**Interfaces:**
- Consumes: `CurationManifest`, `ComponentInfo`.
- Produces:
  - `buildRewriteMap(manifests: CurationManifest[], components: ComponentInfo[]): Map<string, string>` — e.g. `"superpowers:brainstorming" → "deniz-process:brainstorming"`; excluded items and unmatched sources are skipped.
  - `rewriteRefs(content: string, map: Map<string, string>): string` — plain `replaceAll`, longest keys first (so `sp:foo-bar` is replaced before `sp:foo`).

- [ ] **Step 1: Write the failing test**

```ts
// tools/lib/rewrite.test.ts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildRewriteMap, rewriteRefs } from './rewrite.ts';
import type { CurationManifest } from './manifest.ts';
import type { ComponentInfo } from './scan.ts';

const comp = (over: Partial<ComponentInfo>): ComponentInfo => ({
  submodule: 'sp', namespace: 'superpowers', type: 'skill', name: 'x',
  description: '', sourcePath: 'sp/skills/x', files: 1, bytes: 1, ...over,
});
const manifest: CurationManifest = {
  plugin: { name: 'deniz-process', description: 'd', version: '0.1.0' },
  items: [
    { source: 'sp/skills/brainstorming' },
    { source: 'sp/skills/tdd', name: 'deniz-tdd' },
    { source: 'sp/skills/dropped', exclude: true },
  ],
};
const components = [
  comp({ name: 'brainstorming', sourcePath: 'sp/skills/brainstorming' }),
  comp({ name: 'tdd', sourcePath: 'sp/skills/tdd' }),
  comp({ name: 'dropped', sourcePath: 'sp/skills/dropped' }),
];

test('map covers included items with renames, skips excluded', () => {
  const map = buildRewriteMap([manifest], components);
  assert.equal(map.get('superpowers:brainstorming'), 'deniz-process:brainstorming');
  assert.equal(map.get('superpowers:tdd'), 'deniz-process:deniz-tdd');
  assert.equal(map.has('superpowers:dropped'), false);
});

test('rewriteRefs replaces longest keys first', () => {
  const map = new Map([['sp:foo', 'p:foo'], ['sp:foo-bar', 'p:foo-bar']]);
  assert.equal(rewriteRefs('use sp:foo-bar then sp:foo', map), 'use p:foo-bar then p:foo');
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node --test tools/lib/rewrite.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```ts
// tools/lib/rewrite.ts
import type { CurationManifest } from './manifest.ts';
import type { ComponentInfo } from './scan.ts';

export function buildRewriteMap(
  manifests: CurationManifest[],
  components: ComponentInfo[],
): Map<string, string> {
  const bySource = new Map(components.map((c) => [c.sourcePath, c]));
  const map = new Map<string, string>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) continue;
      const c = bySource.get(item.source);
      if (!c) continue;
      map.set(`${c.namespace}:${c.name}`, `${m.plugin.name}:${item.name ?? c.name}`);
    }
  }
  return map;
}

export function rewriteRefs(content: string, map: Map<string, string>): string {
  const keys = [...map.keys()].sort((a, b) => b.length - a.length);
  let out = content;
  for (const key of keys) out = out.replaceAll(key, map.get(key)!);
  return out;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `node --test tools/lib/rewrite.test.ts`
Expected: 2 passing.

- [ ] **Step 5: Commit**

```bash
git add tools/lib/rewrite.ts tools/lib/rewrite.test.ts
git commit -m "feat: cross-reference rewrite map"
```

---

### Task 8: Build command (core)

**Files:**
- Create: `tools/build.ts`, `tools/testutil.ts` (shared fixture — NOT named `*.test.ts`, so the test runner never executes it directly)
- Test: `tools/build.test.ts`

**Interfaces:**
- Consumes: everything from Tasks 2-4, 7.
- Produces:
  - `buildAll(root: string): string[]` — compiles the whole repo, returns report lines. Deletes and regenerates `plugins/` (and later `opencode/`).
  - Output layout per plugin: `plugins/<name>/.claude-plugin/plugin.json`, `plugins/<name>/skills/<skill>/...`, `plugins/<name>/commands/<cmd>.md`, `plugins/<name>/agents/<agent>.md`.
  - `.claude-plugin/marketplace.json` at repo root: `{ name: 'deniz-skills', owner: { name: 'Deniz Irgin', email: 'denizirgin@gmail.com' }, plugins: [{ name, source: './plugins/<name>', description }] }`.
  - CLI entry guarded by `import.meta.url === pathToFileURL(process.argv[1] ?? '').href` so tests can import without side effects. Task 9 extends this file; Task 10 validates its output.

- [ ] **Step 1: Write the shared fixture helper**

```ts
// tools/testutil.ts
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

export function makeRepo(): string {
  const root = mkdtempSync(join(tmpdir(), 'build-'));
  // upstream submodule 'sp' with namespace 'superpowers'
  mkdirSync(join(root, 'external', 'sp', '.claude-plugin'), { recursive: true });
  writeFileSync(join(root, 'external', 'sp', '.claude-plugin', 'plugin.json'), JSON.stringify({ name: 'superpowers' }));
  mkdirSync(join(root, 'external', 'sp', 'skills', 'alpha'), { recursive: true });
  writeFileSync(
    join(root, 'external', 'sp', 'skills', 'alpha', 'SKILL.md'),
    '---\nname: alpha\ndescription: Alpha upstream\n---\n\nUse superpowers:beta next.\n',
  );
  mkdirSync(join(root, 'external', 'sp', 'skills', 'beta'), { recursive: true });
  writeFileSync(
    join(root, 'external', 'sp', 'skills', 'beta', 'SKILL.md'),
    '---\nname: beta\ndescription: Beta upstream\n---\n\nBeta body.\n',
  );
  // curation manifest
  mkdirSync(join(root, 'curation'), { recursive: true });
  writeFileSync(
    join(root, 'curation', 'deniz-process.yaml'),
    [
      'plugin:', '  name: deniz-process', '  description: Process skills', '  version: 0.1.0',
      'items:',
      '  - source: sp/skills/alpha',
      '    frontmatter:', '      description: Alpha curated',
      '  - source: sp/skills/beta',
      '    as: command', '    name: deniz-beta',
      '    body: overlay',
    ].join('\n') + '\n',
  );
  // overlay for beta
  mkdirSync(join(root, 'overlays', 'deniz-process', 'deniz-beta'), { recursive: true });
  writeFileSync(
    join(root, 'overlays', 'deniz-process', 'deniz-beta', 'SKILL.md'),
    '---\nname: beta\ndescription: Beta overlay\n---\n\nOverlay body.\n',
  );
  // own skill
  mkdirSync(join(root, 'skills', 'deniz-process', 'my-own'), { recursive: true });
  writeFileSync(
    join(root, 'skills', 'deniz-process', 'my-own', 'SKILL.md'),
    '---\nname: my-own\ndescription: Mine\n---\n\nMine.\n',
  );
  return root;
}
```

- [ ] **Step 2: Write the failing integration test**

```ts
// tools/build.test.ts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, writeFileSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { buildAll } from './build.ts';
import { parseDoc } from './lib/frontmatter.ts';
import { makeRepo } from './testutil.ts';

test('buildAll compiles plugins with overrides, overlays, conversions, rewrites', () => {
  const root = makeRepo();
  const report = buildAll(root);
  assert.ok(report.length > 0);

  // skill copied with frontmatter override + reference rewrite (beta excluded from skills, so alpha's ref maps to the command name)
  const alpha = parseDoc(readFileSync(join(root, 'plugins', 'deniz-process', 'skills', 'alpha', 'SKILL.md'), 'utf8'));
  assert.equal(alpha.frontmatter.description, 'Alpha curated');
  assert.match(alpha.body, /deniz-process:deniz-beta/);
  assert.doesNotMatch(alpha.body, /superpowers:beta/);

  // skill -> command conversion with overlay body
  const cmd = parseDoc(readFileSync(join(root, 'plugins', 'deniz-process', 'commands', 'deniz-beta.md'), 'utf8'));
  assert.equal(cmd.frontmatter.description, 'Beta overlay');
  assert.match(cmd.body, /Overlay body/);

  // own skill copied
  assert.ok(existsSync(join(root, 'plugins', 'deniz-process', 'skills', 'my-own', 'SKILL.md')));

  // plugin.json + marketplace.json
  const pj = JSON.parse(readFileSync(join(root, 'plugins', 'deniz-process', '.claude-plugin', 'plugin.json'), 'utf8'));
  assert.equal(pj.name, 'deniz-process');
  const mp = JSON.parse(readFileSync(join(root, '.claude-plugin', 'marketplace.json'), 'utf8'));
  assert.equal(mp.plugins[0].source, './plugins/deniz-process');
});

test('missing overlay throws a helpful error', () => {
  const root = makeRepo();
  writeFileSync(
    join(root, 'curation', 'deniz-process.yaml'),
    'plugin:\n  name: deniz-process\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n    body: overlay\n',
  );
  assert.throws(() => buildAll(root), /overlay/);
});

test('non-empty hooks.include throws not-implemented', () => {
  const root = makeRepo();
  writeFileSync(
    join(root, 'curation', 'deniz-process.yaml'),
    'plugin:\n  name: deniz-process\n  description: d\n  version: 0.1.0\nitems: []\nhooks:\n  include: [x]\n',
  );
  assert.throws(() => buildAll(root), /not implemented/);
});
```

- [ ] **Step 3: Run test to verify it fails**

Run: `node --test tools/build.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 4: Implement**

```ts
// tools/build.ts
import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { loadManifest, type CurationItem, type CurationManifest } from './lib/manifest.ts';
import { parseDoc, serializeDoc } from './lib/frontmatter.ts';
import { scanSubmodule, type ComponentInfo } from './lib/scan.ts';
import { buildRewriteMap, rewriteRefs } from './lib/rewrite.ts';

export function buildAll(root: string): string[] {
  const report: string[] = [];
  const manifests = readdirSync(join(root, 'curation'))
    .filter((f) => f.endsWith('.yaml'))
    .sort()
    .map((f) => loadManifest(join(root, 'curation', f)));
  const components = readdirSync(join(root, 'external')).flatMap((s) =>
    scanSubmodule(join(root, 'external'), s),
  );

  rmSync(join(root, 'plugins'), { recursive: true, force: true });
  rmSync(join(root, 'opencode'), { recursive: true, force: true });

  for (const m of manifests) {
    if (m.hooks?.include?.length) {
      throw new Error(`${m.plugin.name}: hooks.include is not implemented yet — keep it empty (YAGNI)`);
    }
    for (const item of m.items) emitItem(root, m, item, components, report);
    emitOwnSkills(root, m, report);
    const pluginDir = join(root, 'plugins', m.plugin.name);
    mkdirSync(join(pluginDir, '.claude-plugin'), { recursive: true });
    writeFileSync(
      join(pluginDir, '.claude-plugin', 'plugin.json'),
      JSON.stringify(m.plugin, null, 2) + '\n',
    );
  }

  writeMarketplace(root, manifests);
  rewriteTree(join(root, 'plugins'), buildRewriteMap(manifests, components));
  return report;
}

function emitItem(
  root: string,
  m: CurationManifest,
  item: CurationItem,
  components: ComponentInfo[],
  report: string[],
): void {
  if (item.exclude) return;
  const comp = components.find((c) => c.sourcePath === item.source);
  if (!comp) throw new Error(`${m.plugin.name}: source not found in external/: ${item.source}`);
  const outName = item.name ?? comp.name;
  const outType = item.as ?? comp.type;
  const srcPath = join(root, 'external', item.source);
  const overlayDir = join(root, 'overlays', m.plugin.name, outName);
  const pluginDir = join(root, 'plugins', m.plugin.name);

  if (item.body === 'overlay' && !existsSync(overlayDir)) {
    throw new Error(
      `${m.plugin.name}/${outName}: body is overlay but overlays/${m.plugin.name}/${outName}/ is missing — run: npm run eject ${m.plugin.name} ${outName}`,
    );
  }

  if (outType === 'skill') {
    if (comp.type !== 'skill') throw new Error(`${item.source}: ${comp.type} -> skill conversion not supported`);
    const destDir = join(pluginDir, 'skills', outName);
    cpSync(srcPath, destDir, { recursive: true });
    if (item.body === 'overlay') cpSync(overlayDir, destDir, { recursive: true, force: true });
    const skillMd = join(destDir, 'SKILL.md');
    const doc = parseDoc(readFileSync(skillMd, 'utf8'));
    doc.frontmatter = { ...doc.frontmatter, name: outName, ...item.frontmatter };
    writeFileSync(skillMd, serializeDoc(doc));
    report.push(`${m.plugin.name}: skill ${outName} <- ${item.source}${item.body === 'overlay' ? ' (overlay)' : ''}`);
  } else {
    const srcFile = comp.type === 'skill' ? join(srcPath, 'SKILL.md') : srcPath;
    let doc = parseDoc(readFileSync(srcFile, 'utf8'));
    if (item.body === 'overlay') {
      doc = parseDoc(readFileSync(join(overlayDir, basename(srcFile)), 'utf8'));
    }
    if (comp.type === 'skill') {
      const extras = readdirSync(srcPath).filter((f) => f !== 'SKILL.md');
      if (extras.length) {
        report.push(`WARN ${m.plugin.name}/${outName}: dropped in skill->${outType} conversion: ${extras.join(', ')}`);
      }
    }
    const base: Record<string, unknown> =
      outType === 'command'
        ? { description: String(doc.frontmatter.description ?? '') }
        : { name: outName, description: String(doc.frontmatter.description ?? '') };
    doc = { frontmatter: { ...base, ...item.frontmatter }, body: doc.body };
    const kindDir = outType === 'command' ? 'commands' : 'agents';
    mkdirSync(join(pluginDir, kindDir), { recursive: true });
    writeFileSync(join(pluginDir, kindDir, `${outName}.md`), serializeDoc(doc));
    report.push(`${m.plugin.name}: ${outType} ${outName} <- ${item.source}`);
  }
}

function emitOwnSkills(root: string, m: CurationManifest, report: string[]): void {
  const ownDir = join(root, 'skills', m.plugin.name);
  if (!existsSync(ownDir)) return;
  for (const name of readdirSync(ownDir)) {
    if (!statSync(join(ownDir, name)).isDirectory()) continue;
    cpSync(join(ownDir, name), join(root, 'plugins', m.plugin.name, 'skills', name), { recursive: true });
    report.push(`${m.plugin.name}: skill ${name} <- skills/ (own)`);
  }
}

function writeMarketplace(root: string, manifests: CurationManifest[]): void {
  const marketplace = {
    name: 'deniz-skills',
    owner: { name: 'Deniz Irgin', email: 'denizirgin@gmail.com' },
    plugins: manifests.map((m) => ({
      name: m.plugin.name,
      source: `./plugins/${m.plugin.name}`,
      description: m.plugin.description,
    })),
  };
  mkdirSync(join(root, '.claude-plugin'), { recursive: true });
  writeFileSync(join(root, '.claude-plugin', 'marketplace.json'), JSON.stringify(marketplace, null, 2) + '\n');
}

function rewriteTree(dir: string, map: Map<string, string>): void {
  if (!existsSync(dir)) return;
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) rewriteTree(p, map);
    else if (e.name.endsWith('.md')) writeFileSync(p, rewriteRefs(readFileSync(p, 'utf8'), map));
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  for (const line of buildAll(process.cwd())) console.log(line);
  console.log('Build complete.');
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `node --test tools/build.test.ts`
Expected: 3 passing.

- [ ] **Step 6: Run the full suite and typecheck**

Run: `npm test && npm run typecheck`
Expected: all tests pass, tsc clean.

- [ ] **Step 7: Commit**

```bash
git add tools/build.ts tools/build.test.ts tools/testutil.ts
git commit -m "feat: build pipeline core (emit, overlays, conversions, marketplace, rewrites)"
```

---

### Task 9: OpenCode emission in build

**Files:**
- Modify: `tools/build.ts` (add `emitOpenCode`, call it at the end of `buildAll` after `rewriteTree`)
- Test: extend `tools/build.test.ts`

**Interfaces:**
- Produces: `opencode/skill/<name>/...` (verbatim copies of built skills — SKILL.md is an open standard OpenCode reads natively), `opencode/command/<name>.md` (frontmatter reduced to `{ description }`), `opencode/agent/<name>.md` (frontmatter reduced to `{ description, mode: 'subagent' }`). Dropped frontmatter keys are reported — no silent loss.

- [ ] **Step 1: Add failing test to `tools/build.test.ts`**

```ts
test('buildAll emits opencode tree and reports dropped keys', () => {
  const root = makeRepo();
  const report = buildAll(root);
  assert.ok(existsSync(join(root, 'opencode', 'skill', 'alpha', 'SKILL.md')));
  assert.ok(existsSync(join(root, 'opencode', 'skill', 'my-own', 'SKILL.md')));
  const cmd = parseDoc(readFileSync(join(root, 'opencode', 'command', 'deniz-beta.md'), 'utf8'));
  assert.equal(cmd.frontmatter.description, 'Beta overlay');
  // references were rewritten before opencode emission
  const alpha = readFileSync(join(root, 'opencode', 'skill', 'alpha', 'SKILL.md'), 'utf8');
  assert.match(alpha, /deniz-process:deniz-beta/);
  assert.ok(Array.isArray(report));
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node --test tools/build.test.ts`
Expected: new test FAILS (`opencode` dir missing); prior 3 still pass.

- [ ] **Step 3: Implement — append to `tools/build.ts` and call `emitOpenCode(root, report)` as the last line of `buildAll` before `return report;`**

```ts
function emitOpenCode(root: string, report: string[]): void {
  const pluginsDir = join(root, 'plugins');
  if (!existsSync(pluginsDir)) return;
  for (const plugin of readdirSync(pluginsDir)) {
    const skillsDir = join(pluginsDir, plugin, 'skills');
    if (existsSync(skillsDir)) {
      for (const name of readdirSync(skillsDir)) {
        cpSync(join(skillsDir, name), join(root, 'opencode', 'skill', name), { recursive: true });
      }
    }
    for (const kind of ['commands', 'agents'] as const) {
      const dir = join(pluginsDir, plugin, kind);
      if (!existsSync(dir)) continue;
      const outKind = kind === 'commands' ? 'command' : 'agent';
      mkdirSync(join(root, 'opencode', outKind), { recursive: true });
      for (const f of readdirSync(dir)) {
        const doc = parseDoc(readFileSync(join(dir, f), 'utf8'));
        const kept: Record<string, unknown> = { description: doc.frontmatter.description };
        if (outKind === 'agent') kept.mode = 'subagent';
        const dropped = Object.keys(doc.frontmatter).filter((k) => k !== 'description' && k !== 'name');
        if (dropped.length) report.push(`opencode ${outKind} ${f}: dropped frontmatter keys: ${dropped.join(', ')}`);
        writeFileSync(join(root, 'opencode', outKind, f), serializeDoc({ frontmatter: kept, body: doc.body }));
      }
    }
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `node --test tools/build.test.ts && npm run typecheck`
Expected: 4 passing, tsc clean.

- [ ] **Step 5: Commit**

```bash
git add tools/build.ts tools/build.test.ts
git commit -m "feat: opencode output emission"
```

---

### Task 10: Validate command

**Files:**
- Create: `tools/validate.ts`
- Test: `tools/validate.test.ts`

**Interfaces:**
- Consumes: `loadManifest`, `scanSubmodule`, `parseDoc`, and the on-disk output of `buildAll`.
- Produces:
  - `interface Finding { level: 'error' | 'warn'; message: string }`
  - `validateRepo(root: string): Finding[]` — checks: (1) every manifest `source` exists; (2) every built skill has non-empty `name`+`description`, every command/agent has `description`; (3) duplicate output names across plugins = error (they collide in flat `opencode/`); (4) leftover upstream namespace references (`superpowers:x` etc.) in `plugins/` or `opencode/` = warn; (5) `marketplace.json` plugins exactly match `plugins/` dirs; (6) Windows-hostile file names (`<>:"|?*`) = error, repo-relative path > 200 chars = warn.
  - CLI prints findings and exits 1 if any error.

- [ ] **Step 1: Write the failing test**

```ts
// tools/validate.test.ts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { buildAll } from './build.ts';
import { makeRepo } from './testutil.ts';
import { validateRepo } from './validate.ts';

test('clean build validates without errors', () => {
  const root = makeRepo();
  buildAll(root);
  const findings = validateRepo(root);
  assert.deepEqual(findings.filter((f) => f.level === 'error'), []);
});

test('unknown manifest source is an error', () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, 'curation', 'deniz-broken.yaml'),
    'plugin:\n  name: deniz-broken\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/nope\n',
  );
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === 'error' && f.message.includes('sp/skills/nope')));
});

test('leftover upstream reference is a warning', () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, 'plugins', 'deniz-process', 'skills', 'alpha', 'SKILL.md'),
    '---\nname: alpha\ndescription: d\n---\n\nStill uses superpowers:some-skill here.\n',
  );
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === 'warn' && f.message.includes('superpowers:some-skill')));
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node --test tools/validate.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```ts
// tools/validate.ts
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { basename, join, relative } from 'node:path';
import { pathToFileURL } from 'node:url';
import { loadManifest } from './lib/manifest.ts';
import { parseDoc } from './lib/frontmatter.ts';
import { scanSubmodule } from './lib/scan.ts';

export interface Finding {
  level: 'error' | 'warn';
  message: string;
}

function* walk(dir: string): Generator<string> {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === '.git') continue;
    const p = join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else yield p;
  }
}

export function validateRepo(root: string): Finding[] {
  const findings: Finding[] = [];
  const manifests = readdirSync(join(root, 'curation'))
    .filter((f) => f.endsWith('.yaml'))
    .sort()
    .map((f) => loadManifest(join(root, 'curation', f)));
  const components = readdirSync(join(root, 'external')).flatMap((s) =>
    scanSubmodule(join(root, 'external'), s),
  );
  const sources = new Set(components.map((c) => c.sourcePath));

  // 1. manifest sources exist
  for (const m of manifests) {
    for (const item of m.items) {
      if (!sources.has(item.source)) {
        findings.push({ level: 'error', message: `${m.plugin.name}: unknown source ${item.source}` });
      }
    }
  }

  const pluginsDir = join(root, 'plugins');
  const outputNames = new Map<string, string>(); // "type:name" -> plugin
  const upstreamNs = new Set(components.map((c) => c.namespace));
  for (const m of manifests) upstreamNs.delete(m.plugin.name);

  for (const dir of existsSync(pluginsDir) ? readdirSync(pluginsDir) : []) {
    for (const file of walk(join(pluginsDir, dir))) {
      const rel = relative(root, file).replaceAll('\\', '/');
      // 6. windows-hostile names / length
      if (/[<>:"|?*]/.test(basename(file))) findings.push({ level: 'error', message: `${rel}: invalid character for Windows` });
      if (rel.length > 200) findings.push({ level: 'warn', message: `${rel}: path longer than 200 chars` });
      if (!file.endsWith('.md')) continue;
      const doc = parseDoc(readFileSync(file, 'utf8'));
      // 2. required frontmatter
      if (basename(file) === 'SKILL.md') {
        if (!doc.frontmatter.name || !doc.frontmatter.description) {
          findings.push({ level: 'error', message: `${rel}: SKILL.md missing name or description` });
        }
        const key = `skill:${String(doc.frontmatter.name)}`;
        if (outputNames.has(key) && outputNames.get(key) !== dir) {
          findings.push({ level: 'error', message: `duplicate skill name across plugins: ${String(doc.frontmatter.name)} (${outputNames.get(key)} and ${dir})` });
        }
        outputNames.set(key, dir);
      } else if (/\/(commands|agents)\//.test(rel)) {
        if (!doc.frontmatter.description) {
          findings.push({ level: 'error', message: `${rel}: missing description` });
        }
        const kind = rel.includes('/commands/') ? 'command' : 'agent';
        const key = `${kind}:${basename(file, '.md')}`;
        if (outputNames.has(key) && outputNames.get(key) !== dir) {
          findings.push({ level: 'error', message: `duplicate ${kind} name across plugins: ${basename(file, '.md')} (${outputNames.get(key)} and ${dir})` });
        }
        outputNames.set(key, dir);
      }
    }
  }

  // 4. leftover upstream references in built output
  const refPattern = /([a-z][a-z0-9-]*):([a-z][a-z0-9-]*)/g;
  for (const outDir of ['plugins', 'opencode']) {
    const dir = join(root, outDir);
    if (!existsSync(dir)) continue;
    for (const file of walk(dir)) {
      if (!file.endsWith('.md')) continue;
      const content = readFileSync(file, 'utf8');
      for (const m of content.matchAll(refPattern)) {
        if (upstreamNs.has(m[1])) {
          findings.push({ level: 'warn', message: `${relative(root, file).replaceAll('\\', '/')}: unrewritten upstream reference ${m[0]} — include it in a manifest or eject and edit the reference out` });
        }
      }
    }
  }

  // 5. marketplace consistency
  const mpPath = join(root, '.claude-plugin', 'marketplace.json');
  if (!existsSync(mpPath)) {
    findings.push({ level: 'error', message: '.claude-plugin/marketplace.json missing — run npm run build' });
  } else {
    const mp = JSON.parse(readFileSync(mpPath, 'utf8')) as { plugins: { name: string }[] };
    const listed = new Set(mp.plugins.map((p) => p.name));
    const built = new Set(existsSync(pluginsDir) ? readdirSync(pluginsDir) : []);
    for (const p of listed) if (!built.has(p)) findings.push({ level: 'error', message: `marketplace lists ${p} but plugins/${p} does not exist` });
    for (const p of built) if (!listed.has(p)) findings.push({ level: 'error', message: `plugins/${p} exists but is not in marketplace.json` });
  }

  return findings;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const findings = validateRepo(process.cwd());
  for (const f of findings) console.log(`${f.level.toUpperCase()}: ${f.message}`);
  const errors = findings.filter((f) => f.level === 'error').length;
  console.log(`${errors} error(s), ${findings.length - errors} warning(s)`);
  if (errors) process.exit(1);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `node --test tools/validate.test.ts && npm test && npm run typecheck`
Expected: all passing, tsc clean.

- [ ] **Step 5: Commit**

```bash
git add tools/validate.ts tools/validate.test.ts
git commit -m "feat: validate command (sources, frontmatter, collisions, refs, marketplace, windows paths)"
```

---

### Task 11: Eject and sync commands

**Files:**
- Create: `tools/eject.ts`, `tools/sync.ts`
- Test: `tools/sync.test.ts` (for the pure report function)

**Interfaces:**
- Consumes: `loadManifest`, `CurationManifest`.
- Produces:
  - `eject.ts` CLI: `npm run eject <plugin> <name>` — copies `external/<source>` into `overlays/<plugin>/<name>/`, prints next steps. Matching rule: item whose output name (`item.name ?? last path segment of source, minus .md`) equals `<name>`.
  - `sync.ts` CLI: `npm run sync [submodule]` — updates submodule(s) via `git submodule update --remote`, prints per-item impact report. Exposes pure `syncReport(sub: string, changed: string[], manifests: CurationManifest[]): string[]`.

- [ ] **Step 1: Write the failing test for the report function**

```ts
// tools/sync.test.ts
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { syncReport } from './sync.ts';
import type { CurationManifest } from './lib/manifest.ts';

const manifests: CurationManifest[] = [{
  plugin: { name: 'deniz-process', description: 'd', version: '0.1.0' },
  items: [
    { source: 'sp/skills/alpha' },
    { source: 'sp/skills/beta', body: 'overlay' },
    { source: 'sp/skills/gone', exclude: true },
    { source: 'other/skills/x' },
  ],
}];

test('reports only changed curated items with overlay flag', () => {
  const lines = syncReport('sp', ['skills/alpha/SKILL.md', 'skills/beta/SKILL.md', 'skills/unrelated/SKILL.md'], manifests);
  assert.equal(lines.length, 2);
  assert.match(lines[0], /alpha/);
  assert.match(lines[0], /auto-updated/);
  assert.match(lines[1], /beta/);
  assert.match(lines[1], /OVERLAY/);
});

test('no changes yields empty report', () => {
  assert.deepEqual(syncReport('sp', [], manifests), []);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `node --test tools/sync.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `tools/sync.ts`**

```ts
// tools/sync.ts
import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { loadManifest, type CurationManifest } from './lib/manifest.ts';

export function syncReport(sub: string, changed: string[], manifests: CurationManifest[]): string[] {
  const lines: string[] = [];
  for (const m of manifests) {
    for (const item of m.items) {
      if (!item.source.startsWith(`${sub}/`)) continue;
      const rel = item.source.slice(sub.length + 1);
      const hit = changed.some((c) => c === rel || c.startsWith(`${rel}/`));
      if (!hit) continue;
      const tag = item.exclude
        ? 'excluded — no action'
        : item.body === 'overlay'
          ? `OVERLAY — review: git -C external/${sub} diff <old> <new> -- ${rel}`
          : 'auto-updated on next build';
      lines.push(`${m.plugin.name}: ${item.source} changed upstream (${tag})`);
    }
  }
  return lines;
}

function git(args: string[], cwd?: string): string {
  return execFileSync('git', args, { cwd, encoding: 'utf8' }).trim();
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const root = process.cwd();
  const only = process.argv[2];
  const subs = only ? [only] : readdirSync(join(root, 'external'));
  const manifests = readdirSync(join(root, 'curation'))
    .filter((f) => f.endsWith('.yaml'))
    .map((f) => loadManifest(join(root, 'curation', f)));

  for (const sub of subs) {
    const dir = join(root, 'external', sub);
    if (!existsSync(dir)) {
      console.error(`No such submodule: ${sub}`);
      process.exit(1);
    }
    const before = git(['rev-parse', 'HEAD'], dir);
    execFileSync('git', ['submodule', 'update', '--remote', '--', `external/${sub}`], { cwd: root, stdio: 'inherit' });
    const after = git(['rev-parse', 'HEAD'], dir);
    if (before === after) {
      console.log(`${sub}: up to date`);
      continue;
    }
    console.log(`${sub}: ${before.slice(0, 7)} -> ${after.slice(0, 7)}`);
    const changed = git(['diff', '--name-only', before, after], dir).split('\n').filter(Boolean);
    const lines = syncReport(sub, changed, manifests);
    for (const l of lines) console.log(`  ${l.replace('<old>', before.slice(0, 7)).replace('<new>', after.slice(0, 7))}`);
    if (!lines.length) console.log('  no curated items affected');
  }
  console.log('Next: npm run build && npm run validate, review git diff, then commit.');
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `node --test tools/sync.test.ts`
Expected: 2 passing.

- [ ] **Step 5: Implement `tools/eject.ts` (CLI only — verified manually in Task 12)**

```ts
// tools/eject.ts
import { cpSync, mkdirSync, statSync } from 'node:fs';
import { basename, join } from 'node:path';
import { loadManifest } from './lib/manifest.ts';

const [plugin, name] = process.argv.slice(2);
if (!plugin || !name) {
  console.error('Usage: npm run eject <plugin> <name>');
  process.exit(1);
}
const root = process.cwd();
const manifest = loadManifest(join(root, 'curation', `${plugin}.yaml`));
const item = manifest.items.find(
  (i) => (i.name ?? basename(i.source, '.md')) === name,
);
if (!item) {
  console.error(`No item with output name '${name}' in curation/${plugin}.yaml`);
  process.exit(1);
}
const src = join(root, 'external', item.source);
const dest = join(root, 'overlays', plugin, name);
if (statSync(src).isDirectory()) {
  cpSync(src, dest, { recursive: true });
} else {
  mkdirSync(dest, { recursive: true });
  cpSync(src, join(dest, basename(src)));
}
console.log(`Ejected ${item.source} -> overlays/${plugin}/${name}/`);
console.log(`Next: 1) add 'body: overlay' to that item in curation/${plugin}.yaml  2) edit the overlay  3) npm run build`);
```

- [ ] **Step 6: Run full suite and commit**

Run: `npm test && npm run typecheck`
Expected: all passing.

```bash
git add tools/sync.ts tools/sync.test.ts tools/eject.ts
git commit -m "feat: sync and eject commands"
```

---

### Task 12: Starter manifests + first real build

**Files:**
- Create: `curation/deniz-process.yaml`, `curation/deniz-dotnet-general.yaml`, `curation/deniz-dotnet-aspire.yaml`, `curation/deniz-dotnet-akka.yaml`
- Create (generated): `plugins/**`, `opencode/**`, `.claude-plugin/marketplace.json`, refreshed `docs/inventory.md`

**Interfaces:**
- Consumes: `docs/inventory.md` for **exact** `source` paths — do not guess paths; open the inventory and copy the `Source` column verbatim.
- Produces: a working end-to-end marketplace with one starter item per plugin. These starter picks are deliberately minimal pipeline-proof items; real curation happens later in dedicated sessions with the user (spec rule).

- [ ] **Step 1: Regenerate inventory and pick starter items**

Run: `npm run inventory`
Then open `docs/inventory.md` and find the exact `Source` values for these intended picks (names may differ slightly — trust the inventory, and if a pick doesn't exist choose the closest equivalent from the same submodule):
- deniz-process → superpowers' `systematic-debugging` skill
- deniz-dotnet-general → dotnet-skills' `csharp-coding-standards` skill
- deniz-dotnet-aspire → aspire-skills' main `aspire` skill (or the closest general Aspire skill)
- deniz-dotnet-akka → dotnet-skills' `akka-best-practices` skill

- [ ] **Step 2: Write the four manifests (adjust `source` to inventory values)**

```yaml
# curation/deniz-process.yaml
plugin:
  name: deniz-process
  description: "Deniz's curated process skills (brainstorming, planning, debugging)"
  version: 0.1.0
items:
  - source: superpowers/skills/systematic-debugging
```

```yaml
# curation/deniz-dotnet-general.yaml
plugin:
  name: deniz-dotnet-general
  description: "Deniz's curated general .NET skills"
  version: 0.1.0
items:
  - source: dotnet-skills/skills/csharp-coding-standards
```

```yaml
# curation/deniz-dotnet-aspire.yaml
plugin:
  name: deniz-dotnet-aspire
  description: "Deniz's curated .NET Aspire skills"
  version: 0.1.0
items:
  - source: aspire-skills/skills/aspire
```

```yaml
# curation/deniz-dotnet-akka.yaml
plugin:
  name: deniz-dotnet-akka
  description: "Deniz's curated Akka.NET skills"
  version: 0.1.0
items:
  - source: dotnet-skills/skills/akka-best-practices
```

- [ ] **Step 3: Build and validate**

Run: `npm run build && npm run validate && npm run inventory`
Expected: build report lists 4 plugins with 1 skill each; validate prints `0 error(s)` (unrewritten-reference warnings are acceptable — note them for future curation); inventory now shows `Curated` values.

- [ ] **Step 4: Inspect output structure**

Run: `find plugins opencode -maxdepth 3 | sort` (Git Bash)
Expected: 4 plugin dirs each with `.claude-plugin/plugin.json` + `skills/<name>/`, plus `opencode/skill/<name>/` entries.

- [ ] **Step 5: Commit**

```bash
git add curation plugins opencode .claude-plugin docs/inventory.md
git commit -m "feat: starter manifests and first built marketplace"
```

- [ ] **Step 6 (manual, user-run): local smoke test**

Ask the user to run `/plugin marketplace add E:\repos\my-projects\agent-skills-and-plugings` in Claude Code and install `deniz-process` to confirm the marketplace loads. Not blocking for remaining tasks.

---

### Task 13: CI workflow + README

**Files:**
- Modify: `tsconfig.json` (strictness hardening)
- Create: `.github/workflows/validate.yml`, `README.md`

**Interfaces:**
- Consumes: npm scripts from all prior tasks.
- Produces: CI that fails when tests/validate/lint/format fail or when committed `plugins/`/`opencode/` output is stale relative to sources.

- [ ] **Step 0: tsconfig strictness hardening (deferred from Biome adoption)**

Add to `tsconfig.json` compilerOptions: `"noUncheckedIndexedAccess": true`, `"exactOptionalPropertyTypes": true`, `"noImplicitReturns": true`, `"noFallthroughCasesInSwitch": true`, `"forceConsistentCasingInFileNames": true`. Run `npm run typecheck`; fix every resulting error in `tools/` with minimal, behavior-preserving changes (e.g. guard indexed accesses that are now `T | undefined`). Run `npm test` (all passing) after fixes. Commit as `chore: enable strict tsconfig hardening flags`.

- [ ] **Step 1: Write the workflow**

```yaml
# .github/workflows/validate.yml
name: validate
on:
  push:
    branches: [main]
  pull_request:
jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          submodules: recursive
      - uses: actions/setup-node@v4
        with:
          node-version: 24
      - run: npm ci
      - run: npm test
      - run: npm run typecheck
      - run: npm run lint
      - run: npm run format:check
      - run: npm run build
      - run: npm run validate
      - name: Built output must be committed and fresh
        run: git diff --exit-code plugins opencode .claude-plugin
```

- [ ] **Step 2: Write `README.md`**

```markdown
# deniz-skills

Personal multi-harness skill/plugin marketplace. Upstream skill repos live as
submodules in `external/`; curation manifests in `curation/*.yaml` select and
customize what gets packaged into the `deniz-*` Claude Code plugins in
`plugins/` and the OpenCode output in `opencode/`.

Design: `docs/superpowers/specs/2026-07-29-skills-plugin-repo-design.md`

## Rules

- Never hand-edit `external/`, `plugins/`, or `opencode/`.
- Your world: `curation/` (what to take, how to tweak), `overlays/` (full-file
  body edits), `skills/` (original skills).
- No curation decision without the catalog: `npm run inventory` →
  `docs/inventory.md`.

## Commands

| Command | Purpose |
|---|---|
| `npm run build` | Compile manifests + overlays + own skills into `plugins/` and `opencode/` |
| `npm run inventory` | Regenerate `docs/inventory.md` catalog |
| `npm run eject <plugin> <name>` | Copy an item to `overlays/` for body editing |
| `npm run sync [submodule]` | Update submodule(s), report impact on curated items |
| `npm run validate` | Check sources, frontmatter, collisions, dangling refs, marketplace |
| `npm test` | Run the tooling test suite |
| `npm run lint` / `npm run format` | Biome lint / format (submodules and build output excluded) |

## Consuming

Claude Code: `/plugin marketplace add <this repo>` then install `deniz-*`
plugins. Once a `deniz-*` plugin covers an upstream source, uninstall the
upstream plugin (avoid duplicate similar skills).

OpenCode: point OpenCode at the `opencode/` tree (skills are the open
SKILL.md standard; commands/agents are OpenCode markdown).
```

- [ ] **Step 3: Full verification**

Run: `npm test && npm run typecheck && npm run build && npm run validate && git status --short`
Expected: everything passes; `git status` shows only the two new files (build output unchanged — proves determinism).

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/validate.yml README.md
git commit -m "chore: CI validation workflow and README"
```

---

### Task 14: Docs structure (AGENTS.md, ADRs, research, ROADMAP)

**Files:**
- Create: `AGENTS.md`, `CLAUDE.md`, `docs/ROADMAP.md`, `docs/adr/0001-submodule-manifest-overlay-architecture.md`, `docs/adr/0002-multi-harness-output.md`, `docs/research/README.md`
- Modify: `README.md` (link the new docs)

**Interfaces:**
- Consumes: the design spec (`docs/superpowers/specs/2026-07-29-skills-plugin-repo-design.md`) as the source for ADR content.
- Produces: the repo's canonical agent contract. Pattern adapted (simplified) from the user's discount-service repo: evergreen vs operational split, ADRs, CLAUDE.md as relay. OpenCode reads AGENTS.md natively.

- [ ] **Step 1: Write `AGENTS.md`** (keep it SHORT — target under 70 lines; this is the essence, not the discount-service original)

```markdown
# Agent Instructions

Operating rules for LLM/code agents working in this repository.

This document is canonical and evergreen: it holds only what stays true. Current state, in-flight
work and next steps live in `docs/ROADMAP.md`.

## Purpose

Personal multi-harness skill/plugin marketplace. Upstream skill repos are vendored as git submodules
in `external/`; curation manifests in `curation/*.yaml` select and customize what gets packaged into
the `deniz-*` Claude Code plugins (`plugins/`) and the OpenCode output (`opencode/`).

## Hard Rules

- `external/`, `plugins/` and `opencode/` are never hand-edited. `external/` belongs to upstream;
  the other two are build output (committed, regenerated by `npm run build`).
- Your world: `curation/` (what to take, how to tweak), `overlays/` (full-file body edits),
  `skills/` (original skills), `tools/` (the build toolchain).
- No skill-by-skill curation decision without the catalog: `npm run inventory` → `docs/inventory.md`.
- Once a `deniz-*` plugin covers an upstream source, uninstall the upstream plugin from the harness —
  two similar skills confuse trigger selection.
- Never commit secrets, tokens, or machine-specific paths.

## Sources of Truth

| Fact | Source |
|---|---|
| Commands | `package.json` scripts |
| What is curated, and how | `curation/*.yaml` |
| What upstream offers | `docs/inventory.md` (generated — regenerate, don't edit) |
| Architecture decisions and why | `docs/adr/` |
| Harness research and notes | `docs/research/` |
| Status and next steps | `docs/ROADMAP.md` (operational — expected to change) |

## Documentation Hygiene

Evergreen (`AGENTS.md`, `docs/adr/`, `docs/research/`) answers "how it works and why". Operational
(`docs/ROADMAP.md`) answers "what is done, what is next" and shrinks as work lands. A sentence that
needs rewriting when a task completes is operational. Keep documentation current in the same change
as the code it describes. Documents describe the status quo, not their own history — no amendment
notes, no renumbering; git carries history. Every document carries a `Date:` line.

## Working Style

- Prefer small correct changes over broad refactors.
- When something is ambiguous, lay out options with a recommendation and ask — don't resolve it
  silently.
- Verify before claiming: "builds" and "works" are different words. After changing `tools/`, run
  `npm test` and `npm run typecheck`; after changing `curation/` or `overlays/`, run
  `npm run build` and `npm run validate`.
- Intended behaviour changes get an ADR; a change whose purpose is not to change behaviour must not
  change behaviour.

## Harness Independence

`AGENTS.md` is the canonical contract; `CLAUDE.md` is a relay only. Harness-specific notes live in
`docs/research/`. OpenCode reads `AGENTS.md` natively; keep it short and harness-neutral.
```

- [ ] **Step 2: Write `CLAUDE.md`** (relay only)

```markdown
# CLAUDE.md

The canonical agent contract for Claude Code and other LLM assistants lives in [AGENTS.md](AGENTS.md).

Read [AGENTS.md](AGENTS.md) first.

This file intentionally stays as a relay only.
```

- [ ] **Step 3: Write `docs/ROADMAP.md`** (operational; seed with current reality — check the progress ledger and git log, list at minimum: per-module curation sessions pending, GitHub publishing/visibility decision pending, hooks.include not implemented, OpenCode consumption not yet wired on a real machine)

- [ ] **Step 4: Write the two ADRs** (condense from the design spec, one decision per file, `Date:` line each):
  - `docs/adr/0001-submodule-manifest-overlay-architecture.md` — context (curate external skill repos with controlled upstream tracking), decision (submodules + YAML manifests + full-file overlays + committed build output; alternatives considered: vendor+3-way-merge, quilt patches), consequences (indirection via build step; clean upstream diffs; eject workflow).
  - `docs/adr/0002-multi-harness-output.md` — context (Claude Code primary, OpenCode secondary; SKILL.md is an open standard), decision (single source → harness-native artifacts; skills pass through, commands/agents transformed, unmappable features dropped with a build report), consequences (no lowest-common-denominator translation; per-harness adapters stay small).

- [ ] **Step 5: Write `docs/research/README.md`** — 5-10 lines: this folder holds harness research and integration notes (e.g. OpenCode consumption, future Codex/Cursor investigation); one topic per file; `Date:` line per file.

- [ ] **Step 6: Update `README.md`** — add a short "Docs" section linking AGENTS.md, docs/adr/, docs/research/, docs/ROADMAP.md.

- [ ] **Step 7: Verify and commit**

Run: `npm run format:check` (new .md files are excluded or clean), then commit all new/changed docs:

```bash
git add AGENTS.md CLAUDE.md docs/ROADMAP.md docs/adr docs/research README.md
git commit -m "docs: canonical agent contract, ADRs, research folder, roadmap"
```

---

## Out of Scope (per spec)

- Skill-by-skill curation lists — separate sessions with the user, driven by `docs/inventory.md`.
- `hooks.include` implementation (build throws if non-empty).
- OpenCode agent permission mapping; Codex/Cursor/Gemini outputs.
- Automated/scheduled sync — `npm run sync` is always manual.
- Publishing to GitHub (visibility decision pending) — repo works locally; pushing is a user action.
