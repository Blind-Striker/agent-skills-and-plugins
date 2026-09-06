# Module Selection Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reject incomplete final Module Selections using version-bound requirement metadata, without automatic Selection changes or compatibility workarounds.

**Architecture:** Derive direct Module requirements during compilation, bind them into schema-2 Bundle identity, and persist them with each selected Module. A shared pure closure check serves Package verification, Plan, and actual-state status; the existing transaction engine carries the new state bytes unchanged.

**Tech Stack:** Node.js 24+, TypeScript run directly by Node, `node:test`, `node:assert/strict`, the existing YAML library, and compiled JavaScript under `dist/`. No new dependencies.

Date: 2026-09-06

Approved spec: [iteration-1 design](../specs/2026-09-06-module-selection-validation-design.md).
Baseline: `50d94324c610f5e1a5c8034f94f8782ddc029977`.

## Global Constraints

- Module manifests use `schemaVersion: 2` and mandatory `requiredModules: string[]`.
- Install state uses `schemaVersion: 2`; each Module records `version`, `digest`, and `requiredModules`.
- Digest payload field order is `schemaVersion`, `requiredModules`, `files`; file identity field order is `sha256`, `mode`. Requirement names and file paths use ordinal ordering.
- Module name and curator-facing version remain outside the content/dependency digest.
- No backward-compatibility readers, migration paths, legacy digest matching, metadata adoption, or automatic profile cleanup.
- No automatic dependency installation, cascade remove, version-range resolution, or `original_skills` declaration. Those are iteration 2.
- Check required Module presence, not cross-version item/API compatibility. Do not broaden the public guarantee.
- Never manually edit real `external/`, `plugins/`, `opencode/`, `dist/`, marketplace, inventory, or ledger output. Temporary test fixtures are not the real upstream worktrees.
- Preserve the unrelated `external/dotnet-agent-skills/tests/dotnet-msbuild/item-management/Constants.g.cs` edit. Do not stage its submodule.
- Preserve current file-integrity, exact-byte Recovery, locking, zero-write Plan, and failure assertions. No new warning suppression, skipped tests, permissive defaults, or fallback code.
- Module-manifest/Install-state schema versions are independent from `TransactionJournal.schemaVersion`. Keep the unchanged journal envelope at version 1; its old/new state evidence now contains version-2 Install state. Version-1 state evidence remains unsupported.
- No real-profile installation or cleanup and no new public Package/Release. Do not alter existing Release assets or the verified v0.3.0 recipe/digest.
- Suggested commits below are checkpoints, not independent Git authorization. Commit/push only when the user authorizes them; never amend, force-push, or stage unrelated paths.
- Use the approved execution workspace, retaining these uncommitted planning inputs. If isolation is needed, use `using-git-worktrees` at execution time, not a reset or checkout of the shared working tree.

---

## File Structure and Ordering

| File | Responsibility in this feature |
|---|---|
| `tools/lib/resolve.ts` | Build-only projection from declared output targets to owning Modules |
| `tools/lib/module-requirements.test.ts` (new) | Pure projection tests, without changing the shared repository fixture |
| `tools/lib/opencode-bundle.ts` | Schema-2 metadata validation, canonical payload digest, shared missing-requirement query |
| `tools/build.ts`, `tools/build.test.ts` | Pre-delete derivation, final-byte manifest emission, focused integration/preflight tests |
| `tools/validate.ts`, `tools/validate.test.ts` | Recompute requirements from authored inputs; verify emitted metadata matches them |
| `tools/verify-package.ts`, `tools/verify-package.test.ts` | Mirror schema-2 validation and check required targets in exact tar contents |
| `tools/lib/opencode-install-state.ts`, `.test.ts` | Strict new-format state parsing, canonical serialization, requirement persistence |
| `tools/lib/opencode-install-plan.ts`, `.test.ts` | Select correct requirement versions and block incomplete final Selections |
| `tools/install-opencode.ts`, `.test.ts` | Verify complete Package graph, separate actual/proposed status findings, prove CLI no-write behavior |
| `tools/lib/opencode-install-apply.test.ts` | Update valid state fixtures and prove metadata-only transaction/Recovery behavior |
| `tools/lib/opencode-install-apply.ts` | Change only if a new-format safety test exposes a real issue; no general refactor |
| `curation/deniz-*.yaml` | Version bumps only, when Bundle identities change; no item intent changes |
| `CONTEXT.md`, `docs/architecture/`, `README.md`, `docs/ROADMAP.md` | Current guarantees, format break, proof limits, and scope closeout |
| `.github/workflows/release-package.yml`, `docs/engineering/quality-gates.md` | Keep development build defaults and verifier-format guidance consistent with the clean break |

Tasks are sequential. Task 1 is a tested build-only projection with no emission change. Task 2
atomically changes the format, producers, readers, and fixtures; it does not yet claim Selection
enforcement. Task 3 adds that enforcement and status semantics. Task 4 supplies the final safety and
distribution evidence. These are four reviewable commit boundaries, not four parallel writers.

The shared `makeRepo()` in `tools/testutil.ts` has no declared `depends_on`. Keep it unchanged: adding
another default Module changes marketplace ordering and many unrelated assertions. Create additional
Modules only in the tests that need them. Keep existing formal-reference fixture debt visible; do
not enlarge the `FIXTURE_DEBT` allowlist in `tools/validate.test.ts`.

## Baseline Check

- [ ] Inspect `git status --short`, `git diff`, and `git log --oneline -10`; verify the execution workspace and protect unrelated changes.
- [ ] Run the following against the unmodified implementation and retain the results outside committed docs. A failure is a blocker to investigate, not a reason to update an assertion blindly.

```powershell
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
```

The last measured baseline had 418 tests: 415 passing and three POSIX-only Windows skips. New tests
increase that total. Do not use the old count as a substitute for reading the new run's results.

### Task 1: Derive Direct Module Requirements

**Files:** Modify `tools/lib/resolve.ts`; create `tools/lib/module-requirements.test.ts`.

**Interfaces: Consumes** existing `resolveItem(root, plugin, item, components): ResolvedItem`,
`CurationManifest`, `ComponentInfo`, and `OwnSkillIdentity` from `tools/lib/own-skills.ts`.

**Interfaces: Produces** this build-only function; all other tasks use this exact signature:

```typescript
export function deriveModuleRequirements(
  root: string,
  manifests: CurationManifest[],
  components: ComponentInfo[],
  ownSkills: OwnSkillIdentity[],
): Map<string, string[]>;
```

- [ ] Add the new test file with the existing Node test/assert imports, type imports from `manifest.ts`, and the new function import. Use this small data fixture; `resolveItem` needs no filesystem access for explicitly named items:

```typescript
function moduleManifest(name: string, items: CurationItem[]): CurationManifest {
  return { plugin: { name, description: name, version: "1.0.0" }, items };
}

test("requirements: derive owning Modules, not item names", () => {
  const manifests = [
    moduleManifest("consumer", [
      { source: "source/consumer", name: "entry", depends_on: ["renamed", "local", "renamed", "own"] },
      { source: "source/local", name: "local" },
    ]),
    moduleManifest("provider", [{ source: "source/old", name: "renamed" }]),
    moduleManifest("originals", []),
  ];
  const result = deriveModuleRequirements(".", manifests, [], [
    { plugin: "originals", name: "own", address: "originals:own" },
  ]);
  assert.deepEqual([...result], [
    ["consumer", ["originals", "provider"]],
    ["originals", []],
    ["provider", []],
  ]);
});

test("requirements: reject unknown owners", () => {
  const manifests = [moduleManifest("consumer", [
    { source: "source/entry", name: "entry", depends_on: ["missing"] },
  ])];
  assert.throws(() => deriveModuleRequirements(".", manifests, [], []), /unknown.*missing/);
});

test("requirements: reject cross-kind ambiguity rather than choosing an owner", () => {
  const manifests = [
    moduleManifest("consumer", [{ source: "s/entry", name: "entry", depends_on: ["shared"] }]),
    moduleManifest("a", [{ source: "s/a", name: "shared", as: "skill" }]),
    moduleManifest("b", [{ source: "s/b", name: "shared", as: "agent" }]),
  ];
  assert.throws(() => deriveModuleRequirements(".", manifests, [], []), /ambiguous.*shared/);
});
```

- [ ] Run `node --test tools/lib/module-requirements.test.ts`. Confirm it fails because the export is absent, not because of a syntax error or missing dependency.
- [ ] Implement the projection in `resolve.ts`, importing `OwnSkillIdentity` as a type. Use a `Map<string, Set<string>>` so multiple owners are preserved rather than overwritten. The implementation is:

```typescript
export function deriveModuleRequirements(
  root: string,
  manifests: CurationManifest[],
  components: ComponentInfo[],
  ownSkills: OwnSkillIdentity[],
): Map<string, string[]> {
  const owners = new Map<string, Set<string>>();
  const addOwner = (name: string, module: string): void => {
    const modules = owners.get(name) ?? new Set<string>();
    modules.add(module);
    owners.set(name, modules);
  };
  for (const manifest of manifests) {
    for (const item of manifest.items) {
      if (!item.exclude) {
        addOwner(resolveItem(root, manifest.plugin.name, item, components).outName, manifest.plugin.name);
      }
    }
  }
  for (const own of ownSkills) addOwner(own.name, own.plugin);

  const result = new Map<string, string[]>();
  for (const manifest of [...manifests].sort((a, b) => ordinalCompare(a.plugin.name, b.plugin.name))) {
    const required = new Set<string>();
    for (const item of manifest.items) {
      if (item.exclude) continue;
      for (const target of item.depends_on ?? []) {
        const candidates = owners.get(target);
        if (!candidates?.size) throw new Error(`${manifest.plugin.name}: unknown dependency target ${target}`);
        if (candidates.size !== 1) throw new Error(`${manifest.plugin.name}: ambiguous dependency target ${target}`);
        for (const owner of candidates) {
          if (owner !== manifest.plugin.name) required.add(owner);
        }
      }
    }
    result.set(manifest.plugin.name, [...required].sort(ordinalCompare));
  }
  return result;
}
```

- [ ] Add separate pure cases for excluded sources, excluded-only targets, an empty Module, an intra-Module cycle, a cross-Module cycle, and reversed input order. Assert exact maps, not just counts. Names appearing only in descriptions/frontmatter must not create an edge.
- [ ] Rerun the focused test, then the five baseline tooling commands. Review before the suggested commit `feat: derive Module requirements from curation`. Do not connect the helper to emission yet; this checkpoint has no generated diff.

### Task 2: Atomically Adopt Schema-2 Identity and State

**Files:** Modify `tools/lib/opencode-bundle.ts`, `tools/lib/opencode-bundle.test.ts`,
`tools/lib/opencode-install-state.ts`, `tools/lib/opencode-install-state.test.ts`,
`tools/lib/opencode-install-plan.ts`, `tools/lib/opencode-install-plan.test.ts`,
`tools/install-opencode.ts`, `tools/install-opencode.test.ts`,
`tools/lib/opencode-install-apply.test.ts`, `tools/build.ts`, `tools/build.test.ts`,
`tools/validate.ts`, `tools/validate.test.ts`, `tools/verify-package.ts`,
`tools/verify-package.test.ts`, the four `curation/deniz-*.yaml` versions, and the identity/format
sections of `CONTEXT.md`, architecture canon, and README.

**Interfaces: Consumes** Task 1's projection and existing `ownSkillIdentities(root, manifests)`.

**Interfaces: Produces** these definitions in `opencode-bundle.ts`:

```typescript
export interface ModuleManifest {
  schemaVersion: 2;
  module: string;
  version: string;
  digest: Sha256;
  requiredModules: string[];
  files: Record<string, FileIdentity>;
}
export interface MissingModuleRequirement {
  module: string;
  requiredModule: string;
}
export function requiredModulesError(module: string, value: unknown): string | null;
export function digestModulePayload(files: Record<string, FileIdentity>, requiredModules: readonly string[]): Sha256;
export function findMissingModuleRequirements(
  modules: Readonly<Record<string, { requiredModules: readonly string[] }>>,
): MissingModuleRequirement[];
export function createModuleManifest(
  root: string, module: string, version: string,
  resolveMode: (path: string) => FileMode, requiredModules: readonly string[],
): ModuleManifest;
```

`ModuleState` in `opencode-install-state.ts` becomes
`{ version: string; digest: Sha256; requiredModules: string[] }`. `InstallState` and
`EMPTY_INSTALL_STATE` use schema 2; their Ownership shape and exported names remain unchanged.

- [ ] Add identity and requirement tests before implementation. Keep imports tied to the exact new function names:

```typescript
test("requirements: metadata participates in Bundle identity", () => {
  const files = { "commands/a.md": { sha256: hashBytes("a"), mode: "100644" as const } };
  assert.notEqual(digestModulePayload(files, []), digestModulePayload(files, ["provider"]));
  assert.equal(
    digestModulePayload(files, ["z", "a"]),
    digestModulePayload(files, ["a", "z"]),
  );
  const expected = `{"schemaVersion":2,"requiredModules":["provider"],"files":{"commands/a.md":{"sha256":"${hashBytes("a")}","mode":"100644"}}}`;
  assert.equal(digestModulePayload(files, ["provider"]), hashBytes(expected));
});

test("requirements: distinguish missing, malformed, duplicate, and self requirements", () => {
  for (const value of [undefined, null, "provider", [1], [""], ["a/b"], ["a\\b"], ["a\0b"], ["a", "a"], ["consumer"]]) {
    assert.notEqual(requiredModulesError("consumer", value), null);
  }
  assert.equal(requiredModulesError("consumer", []), null);
  assert.equal(requiredModulesError("consumer", ["provider"]), null);
});

test("requirements: closure is deterministic and allows a complete cycle", () => {
  assert.deepEqual(findMissingModuleRequirements({
    b: { requiredModules: ["a"] }, a: { requiredModules: ["b"] },
  }), []);
  assert.deepEqual(findMissingModuleRequirements({ a: { requiredModules: ["toString"] } }), [
    { module: "a", requiredModule: "toString" },
  ]);
  assert.deepEqual(findMissingModuleRequirements({
    z: { requiredModules: ["b", "a"] }, a: { requiredModules: [] },
  }), [{ module: "z", requiredModule: "b" }]);
});
```

- [ ] Run `node --test tools/lib/opencode-bundle.test.ts` and record the expected missing-export failures. Implement the helpers below and replace, rather than wrap, the old `digestFileMap` contract:

```typescript
export function requiredModulesError(module: string, value: unknown): string | null {
  if (!Array.isArray(value)) return "requiredModules must be an array";
  const seen = new Set<string>();
  for (const name of value) {
    if (typeof name !== "string" || name.length === 0 || /[\\/\0]/.test(name)) {
      return "requiredModules must contain nonempty Module names without path separators or NUL";
    }
    if (name === module) return "requiredModules must not contain the owning Module";
    if (seen.has(name)) return `requiredModules contains duplicate ${name}`;
    seen.add(name);
  }
  return null;
}

export function digestModulePayload(
  files: Record<string, FileIdentity>, requiredModules: readonly string[],
): Sha256 {
  const canonicalFiles = Object.entries(files).sort(([a], [b]) => ordinalCompare(a, b))
    .map(([path, value]) => `${JSON.stringify(path)}:${JSON.stringify({ sha256: value.sha256, mode: value.mode })}`)
    .join(",");
  const requirements = JSON.stringify([...requiredModules].sort(ordinalCompare));
  return hashBytes(`{"schemaVersion":2,"requiredModules":${requirements},"files":{${canonicalFiles}}}`);
}

export function findMissingModuleRequirements(
  modules: Readonly<Record<string, { requiredModules: readonly string[] }>>,
): MissingModuleRequirement[] {
  const missing: MissingModuleRequirement[] = [];
  for (const module of Object.keys(modules).sort(ordinalCompare)) {
    const entry = modules[module];
    if (!entry) continue;
    for (const requiredModule of [...entry.requiredModules].sort(ordinalCompare)) {
      if (!Object.hasOwn(modules, requiredModule)) missing.push({ module, requiredModule });
    }
  }
  return missing;
}
```

The digest function consumes already-validated names; it does not silently deduplicate invalid
metadata. Production readers and writers must call the validator first. Building the digest's file
object in sorted entry order avoids JavaScript's numeric-property reordering; add an ordering case
with relative paths `10` and `2`. Use the same canonical file-identity reconstruction in manifest
normalization so extra input property order cannot alter the serialized identity shape.

- [ ] Wire the Bundle reader/writer together: change the schema guard, validate the Module name and `requiredModules`, copy/sort the list in `normalizedManifest` and `requireValidManifest`, require the fifth creation argument, and calculate only the schema-2 digest. Missing metadata and schema 1 must fail with specific errors. Add a loader test that creates a valid temporary Bundle, changes only the serialized schema to 1, and asserts `/schemaVersion.*2/`; add a stale-digest requirement-tamper case.
- [ ] Connect derivation before either generated directory is deleted, then pass the resulting map to `writeOpenCodeManifests`:

```typescript
const ownSkills = ownSkillIdentities(root, manifests);
const moduleRequirements = deriveModuleRequirements(root, manifests, components, ownSkills);
// Existing rewrite-map construction and emission remain in their current order.
// At final manifest emission:
writeOpenCodeManifests(root, manifests, moduleRequirements);
```

Change the private writer signature to accept `Map<string, string[]>`. Each entry must exist; throw
an internal error if it does not, never substitute `[]`. Pass that entry as the fifth
`createModuleManifest` argument after the existing mode resolver. Do not change Native-tree bodies
or reread ledger output.

- [ ] Add build integration coverage using `makeRepo()` plus a test-local provider manifest. Import `loadManifest` and YAML `stringify` if not present. This preserves the shared fixture while producing a real guarded cross-Module edge:

```typescript
const root = makeRepo();
const path = join(root, "curation", "deniz-process.yaml");
const consumer = loadManifest(path);
const alpha = consumer.items.find((item) => item.source === "sp/skills/alpha");
const delta = consumer.items.find((item) => item.source === "sp/skills/delta");
assert.ok(alpha && delta);
alpha.depends_on = ["provider"];
delta.exclude = true;
writeFileSync(path, stringify(consumer));
writeFileSync(join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
  "---\nname: alpha\ndescription: Alpha\n---\nUse superpowers:delta.\n");
writeFileSync(join(root, "curation", "deniz-provider.yaml"), stringify({
  plugin: { name: "deniz-provider", description: "Provider", version: "1.0.0" },
  items: [{ source: "sp/skills/delta", name: "provider", invocation: "auto" }],
}));
buildAll(root);
const generated = loadModuleManifest(join(root, "opencode", "deniz-process", "manifest.json"));
assert.deepEqual(generated.requiredModules, ["deniz-provider"]);
```

Extend this test-local setup to check that changing the dependency to an unknown target throws
before deleting the previously generated skill bytes. In `validate.test.ts`, build the valid
cross-Module estate, remove its generated requirement, recompute its digest, and require a
`required Modules do not match curation` error. Re-signing metadata must not defeat the authored-input
comparison. Keep existing reference mismatch tests and their original failure reasons.

- [ ] In `validateRepo`, derive expected lists with the same function and `ownSkillIdentities`. Convert derivation exceptions into error findings without skipping other checks. Compare each loaded manifest's normalized list against its expected map entry. Also collect loaded manifests into a prototype-safe record and report `findMissingModuleRequirements` results. Existing target-root and integrity checks remain.
- [ ] In `verify-package.ts`, update its separate Buffer-based shape parser to require schema 2 and call `requiredModulesError`. Compute the new digest, collecting valid parsed manifests by actual Module identity. After the loop, run `findMissingModuleRequirements` and report absent Package targets. Keep exact tar file-set, byte, mode, bin, and no-runtime-dependency checks. In CLI `loadVerifiedBundles`, append the same whole-Package graph findings after per-Bundle verification; this must happen before Destination work.
- [ ] Extend `validEntries()` in `verify-package.test.ts` to emit schema 2 and explicit empty requirements. Add this re-signed missing-provider negative case, then add a valid provider entry to prove the positive case:

```typescript
const entries = validEntries();
const manifestEntry = entries.find((item) => item.path === "package/opencode/deniz-process/manifest.json");
assert.ok(manifestEntry);
const manifest = JSON.parse(manifestEntry.content.toString("utf8")) as ModuleManifest;
manifest.requiredModules = ["deniz-provider"];
manifest.digest = digestModulePayload(manifest.files, manifest.requiredModules);
manifestEntry.content = Buffer.from(JSON.stringify(manifest));
assert.ok(verifyPackageEntries(entries).some((message) => /deniz-process.*requires.*deniz-provider/.test(message)));
const provider: ModuleManifest = {
  schemaVersion: 2, module: "deniz-provider", version: "1.0.0",
  requiredModules: [], files: {}, digest: digestModulePayload({}, []),
};
entries.push(entry("opencode/deniz-provider/manifest.json", JSON.stringify(provider)));
assert.deepEqual(verifyPackageEntries(entries), []);
```

- [ ] Add schema-2 state tests before changing the state parser. This proves the format boundary and that structurally valid but incomplete Selection can reach diagnostics:

```typescript
test("requirements: state preserves requirements without assuming closure", () => {
  const state = parseInstallState(JSON.stringify({
    schemaVersion: 2,
    modules: { consumer: { version: "1.0.0", digest: HASH_A, requiredModules: ["z", "a"] } },
    files: {},
  }));
  assert.deepEqual(state.modules.consumer?.requiredModules, ["a", "z"]);
  const bytes = serializeInstallState(state);
  assert.equal(stateDigest(state), hashBytes(bytes));
  assert.deepEqual(parseInstallState(bytes), state);
  assert.throws(() => parseInstallState(JSON.stringify({ schemaVersion: 1, modules: {}, files: {} })), /schemaVersion.*2/);
});
```

Run `node --test --test-name-pattern="requirements:" tools/lib/opencode-install-state.test.ts` red.
Then change only the state format/type fields, allowed Module keys, metadata validation, and
normalization. Preserve the unique-member JSON parser and exact hashing. Normalize Module entries
in field order `version`, `digest`, `requiredModules`; require metadata even for an empty list.
Update `buildNextState` now so it copies the new field from both requested Bundle and unaffected
recorded entries; Selection validation is Task 3.

```typescript
modules[name] = {
  version: moduleManifest.version,
  digest: moduleManifest.digest,
  requiredModules: [...moduleManifest.requiredModules],
};
// The unaffected-entry branch copies existing.version, existing.digest,
// and [...existing.requiredModules], never the current Package's entry.
```

- [ ] Update existing valid fixture constructors and literals atomically. Append `requiredModules: string[] = []` only to test-helper inputs (`writeBundle`, `manifest`, `installed`, and state helpers), then emit explicit metadata and call the mandatory production signature. This is valid test-data construction, not a production default. Migrate all `digestFileMap` imports/calls to `digestModulePayload(files, requiredModules)` and remove the old export. Relevant callers are in Bundle, planner, CLI, Apply, and Package-verifier tests. Inspect every `schemaVersion: 1` occurrence: change Module manifests and Install states, leave journal envelopes at 1, and retain deliberately unsupported-format cases. Change the existing unknown-state-version test to use 3 and add a separate version-1 rejection test. Update exact serialized JSON assertions rather than removing them.
- [ ] Before emitting new Bundle identities, bump the four unchanged item surfaces by a patch version: Process `0.5.0 -> 0.5.1`, General `0.9.0 -> 0.9.1`, Akka `0.3.0 -> 0.3.1`, Aspire `0.3.2 -> 0.3.3`. Do not alter item `depends_on` intent or take/skip decisions. Regenerate once to bootstrap matching compiled code and fixtures, then run the full gate below. Inspect that Native skill/command/agent bytes did not change; expected output changes are Bundle metadata and Plugin/marketplace version metadata, with inventory changes only if its projection includes them.

```powershell
npm run build
npm run inventory
node --test tools/lib/opencode-bundle.test.ts tools/lib/opencode-install-state.test.ts tools/verify-package.test.ts
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
npm run build
npm run inventory
npm run validate
git diff --check
```

- [ ] Compare the two generation results exactly, including untracked generated paths. Update the identity/format sections of `CONTEXT.md`, architecture canon, and checkout README in this commit, but do not claim Selection enforcement yet. Distinguish the new checkout format from the still-public schema-1 v0.3.0 Package; keep that Release recipe intact. Review before the suggested commit `feat: bind Module requirements to schema-2 identity`.

### Task 3: Validate Final Selection and Separate Status Scopes

**Files:** Modify `tools/lib/opencode-install-plan.ts`, `tools/lib/opencode-install-plan.test.ts`,
`tools/install-opencode.ts`, `tools/install-opencode.test.ts`, and the Plan/status sections of
`docs/architecture/distribution-and-installation.md`.

**Interfaces: Consumes** schema-2 Module/state entries and
`findMissingModuleRequirements(modules): MissingModuleRequirement[]` from Task 2.

**Interfaces: Produces** existing `planReconcile(...)` plus a new `missing_dependency` member of
`PlanFinding.code` and optional structured `requiredModule?: string`. No new CLI flags or mutation
commands. The private `renderStatus` gains a final `MissingModuleRequirement[]` argument.

- [ ] Add a focused planner regression using the Task-2 fixture signatures. Append requirements as the fourth `manifest` argument and an optional `requiredModules` property in `installed` entries:

```typescript
test("dependency: install cannot create an incomplete Selection", () => {
  const current = EMPTY_INSTALL_STATE;
  const manifests = {
    consumer: manifest("consumer", {}, "1.0.0", ["provider"]),
    provider: manifest("provider", {}),
  };
  const plan = planReconcile(current, manifests, {}, request("install", { modules: ["consumer"] }));
  assert.deepEqual(plan.findings.map(({ code, module, requiredModule }) => ({ code, module, requiredModule })), [
    { code: "missing_dependency", module: "consumer", requiredModule: "provider" },
  ]);
  assert.deepEqual(plan.operations, []);
  assert.deepEqual(plan.transfers, []);
  assert.equal(plan.nextState, current);
  const repaired = planReconcile(current, manifests, {}, request("install", { modules: ["consumer", "provider"] }));
  assert.deepEqual(repaired.findings, []);
  assert.deepEqual(Object.keys(repaired.nextState.modules), ["consumer", "provider"]);
});
```

- [ ] Run `node --test --test-name-pattern="dependency:" tools/lib/opencode-install-plan.test.ts` red. Before sorting/returning findings in `planReconcile`, construct the proposed state once using the existing `buildNextState` and add direct missing edges:

```typescript
const nextState = buildNextState(current, manifests, request, selection, affected, final);
for (const { module, requiredModule } of findMissingModuleRequirements(nextState.modules)) {
  findings.push({
    code: "missing_dependency", module, requiredModule,
    message: `${module} requires selected Module ${requiredModule}; change the Selection explicitly`,
  });
}
```

Return this `nextState` only on the existing finding-free path. Keep the failure branch's exact
current-state reference, empty operations, and empty transfers. Extend `compareFindings` with an
ordinal `requiredModule` tie-breaker after `module`; preserve the other finding ordering.

- [ ] Add a table of planner cases using empty-file manifests, so graph verdicts cannot be masked by filesystem findings. Assert exact missing pairs and resulting selected names:

| Case | Setup/request | Expected |
|---|---|---|
| Transitive | `a -> b`, `b -> c`, explicitly install a+b | Missing b/c; no mutation |
| Complete cycle | `a -> b`, `b -> a`, install both | Success |
| Partial cycle removal | Both selected, remove a | Missing b/a |
| Whole cycle removal | Both selected, remove both | Empty successful Selection |
| Persisted requirements | Installed a requires b; current Package a requires c; remove b | Missing a/b, never a/c |
| Targeted install | Same versions as above, install unrelated d | Keep a's recorded requirement b |
| Whole update | Same versions as above, update a+b with c available but unselected | Missing a/c; c is not automatically added |
| Explicit repair | Current a requires absent b; install b | Success, preserve a's requirements |
| Absent source on removal | Selected removable Module absent from Package | Preserve existing explicit-removal behavior using stored state |

- [ ] Add a CLI no-write test using `makeCliFixture` and the extended test-only `writeBundle(packageRoot, module, files, version, requiredModules)` helper:

```typescript
test("dependency: blocked CLI Plan and Apply leave Destination absent", async () => {
  const fixture = makeCliFixture({ "deniz-provider": { "skills/provider/SKILL.md": "provider\n" } });
  writeBundle(fixture.io.packageRoot, "deniz-process", { "skills/alpha/SKILL.md": "alpha skill\n" }, "0.2.0", ["deniz-provider"]);
  for (const args of [
    ["install", "--module", "deniz-process"],
    ["install", "--module", "deniz-process", "--yes"],
  ]) {
    const result = await runInstallCli(args, fixture.io);
    assert.equal(result.exitCode, 1);
    assert.match(result.stdout, /missing_dependency/);
    assert.match(result.stdout, /deniz-provider/);
    assert.equal(existsSync(fixture.destination), false);
  }
});
```

- [ ] Add this status regression before editing `runStatus`. It tests actual metadata versus a blocked future Update without mutating Install state:

```typescript
test("dependency: status separates recorded Selection from proposed Update", async () => {
  const fixture = makeCliFixture({ "deniz-provider": { "skills/provider/SKILL.md": "provider\n" } });
  assert.equal((await runInstallCli(["install", "--module", "deniz-process", "--yes"], fixture.io)).exitCode, 0);
  const statePath = join(fixture.destination, ".deniz-skills", "install.json");
  const before = readFileSync(statePath);
  writeBundle(fixture.io.packageRoot, "deniz-process", { "skills/alpha/SKILL.md": "alpha skill\n" }, "0.3.0", ["deniz-provider"]);
  const status = await runInstallCli(["status"], fixture.io);
  assert.equal(status.exitCode, 1); // Preserve the existing nonzero result for a blocked proposed Update.
  assert.doesNotMatch(status.stdout, /Selection dependency findings:/);
  assert.match(status.stdout, /Proposed Update dependency findings:/);
  assert.ok(readFileSync(statePath).equals(before));
});
```

For the opposite case, create a valid schema-2 state whose recorded consumer requires an unselected
provider, while the current Package consumer requires nothing. Serialize this intentional diagnostic
fixture canonically, call status, assert `Selection dependency findings:` and nonzero exit, and assert
that bytes are unchanged. The proposed Update may repair the requirements; status must not claim
the current Selection is valid just because that proposal is valid.

- [ ] Implement status using the same pure query on `current.modules`. Pass its results to `renderStatus`, retaining file, currency, lock, and Recovery observations. Partition the proposed Plan's findings so dependency errors have an explicit scope:

```typescript
const currentDependencies = findMissingModuleRequirements(current.modules);
// In renderStatus:
appendSection(lines, "Selection dependency findings:", currentDependencies.map(
  ({ module, requiredModule }) => `  missing_dependency ${module} requires selected Module ${requiredModule}`,
));
const proposed = plan?.findings ?? [];
appendSection(lines, "Proposed Update dependency findings:", proposed
  .filter((item) => item.code === "missing_dependency").map((item) => `  ${formatFinding(item)}`));
appendSection(lines, "Findings:", proposed
  .filter((item) => item.code !== "missing_dependency").map((item) => `  ${formatFinding(item)}`));
// In runStatus, retain the existing blocked-Recovery and proposed-Plan conditions:
const blocked = currentDependencies.length > 0 || recovery?.kind === "blocked" ||
  (plan !== null && plan.findings.length > 0);
```

- [ ] Run planner and CLI test files, then the full tooling/generated gate. Update the current Plan/status canon and checkout instructions without changing historical Release claims. Review before the suggested commit `feat: reject incomplete Module Selections`.

### Task 4: Prove Metadata Transactions and Distribution

**Files:** Modify `tools/lib/opencode-install-apply.test.ts`, `tools/install-opencode.test.ts`,
`docs/ROADMAP.md`, `README.md`, applicable architecture canon, `.github/workflows/release-package.yml`,
and `docs/engineering/quality-gates.md`. Regenerate `dist/` through build only. Add an experiment
record only after a new measured smoke has actually run.

**Interfaces: Consumes** the schema-2 types, finding-free Plan, `applyPlan`, `applyRecovery`,
`inspectRecovery`, and the existing `ApplyOptions.crashAfterSyscall` / `forceWindowsStateReplace`.

**Interfaces: Produces** acceptance evidence; no new runtime interface or transaction abstraction.

- [ ] Add a test-only metadata fixture in `opencode-install-apply.test.ts` using the existing `makeInstallFixture`. It keeps Native bytes and versions stable, changes only a requirement, and has the provider already selected:

```typescript
function makeMetadataFixture() {
  const fixture = makeInstallFixture();
  const processBundle = fixture.bundles.get("deniz-process");
  assert.ok(processBundle);
  writeFileSync(join(processBundle.root, "commands", "alpha.md"), fixture.oldBytes);
  const providerRoot = join(processBundle.root, "..", "deniz-provider");
  mkdirSync(providerRoot, { recursive: true });
  const provider = createModuleManifest(providerRoot, "deniz-provider", "0.1.0", () => "100644", []);
  fixture.oldState.modules["deniz-provider"] = {
    version: provider.version, digest: provider.digest, requiredModules: [],
  };
  writeFileSync(join(fixture.destination, ".deniz-skills", "install.json"), serializeInstallState(fixture.oldState));
  const consumer = createModuleManifest(processBundle.root, "deniz-process", "0.1.0", () => "100644", ["deniz-provider"]);
  const bundles = new Map<string, ModuleBundle>([
    ["deniz-process", { root: processBundle.root, manifest: consumer }],
    ["deniz-provider", { root: providerRoot, manifest: provider }],
  ]);
  const plan = requireFindingFree(planReconcile(
    fixture.oldState, { "deniz-process": consumer, "deniz-provider": provider },
    observeOwnedPaths(fixture.destination, fixture.oldState, consumer),
    { kind: "update", modules: [], all: false, platform: "posix" },
  ));
  assert.deepEqual(plan.operations, []);
  assert.notEqual(stateDigest(plan.currentState), stateDigest(plan.nextState));
  return { ...fixture, bundles, plan };
}
```

- [ ] First test successful metadata-only Apply: acquire/release the lock in `try/finally`, call `applyPlan`, assert exact next-state bytes, unchanged target bytes, and `inspectRecovery(...) === null`. Repeat with the same state and require the existing no-op behavior. A failure to persist metadata is a real failing regression, not a reason to insert a dummy file operation.
- [ ] Add precommit/postcommit crash cases using actual existing injection points rather than `after-place`, which a zero-file-operation Plan might never reach:

```typescript
for (const point of ["state-aside", "state-commit"] as const) {
  test(`requirements: metadata-only Recovery after ${point}`, () => {
    const fixture = makeMetadataFixture();
    const lock = acquireInstallerLock(fixture.destination);
    try {
      assert.throws(() => applyPlan(lock, fixture.destination, fixture.plan, fixture.bundles, {
        forceWindowsStateReplace: true, crashAfterSyscall: point,
      }), /injected crash/);
    } finally { lock.release(); }
    const recovery = inspectRecovery(fixture.destination);
    assert.ok(recovery && recovery.kind !== "blocked");
    assert.equal(recovery.kind, point === "state-aside" ? "rollback" : "finalize");
    const recoveryLock = acquireInstallerLock(fixture.destination, { recover: true });
    try { applyRecovery(recoveryLock, fixture.destination, recovery); }
    finally { recoveryLock.release(); }
    const expected = point === "state-aside" ? fixture.oldState : fixture.plan.nextState;
    assert.equal(readFileSync(join(fixture.destination, ".deniz-skills", "install.json"), "utf8"), serializeInstallState(expected));
    assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
    assert.equal(inspectRecovery(fixture.destination), null);
  });
}
```

- [ ] Add an evidence-tamper case to the crash setup above: change only the saved new state's requirements without re-signing its journal evidence, then check the exact rejection and unchanged installed bytes:

```typescript
const recovery = inspectRecovery(fixture.destination);
assert.ok(recovery && recovery.kind !== "blocked");
const statePath = join(fixture.destination, ".deniz-skills", "install.json");
const installedBefore = readFileSync(statePath);
const evidencePath = join(recovery.transactionDir, "new-state.json");
const altered = JSON.parse(readFileSync(evidencePath, "utf8")) as InstallState;
const recorded = altered.modules["deniz-process"];
assert.ok(recorded);
recorded.requiredModules = [];
writeFileSync(evidencePath, serializeInstallState(altered));
const blocked = inspectRecovery(fixture.destination);
assert.ok(blocked && blocked.kind === "blocked");
assert.match(blocked.message, /digest/);
assert.ok(readFileSync(statePath).equals(installedBefore));
assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
```

Use the `state-commit` crash setup for this case so the installed state file exists. Add a separate
old-format rejection test using existing `writeLeftoverTransaction`, whose signature is
`(destination, journal, { oldStateBytes, newStateBytes }) => string`:

```typescript
const fixture = makeMetadataFixture();
const oldFormat = {
  schemaVersion: 1,
  modules: Object.fromEntries(Object.entries(fixture.oldState.modules)
    .map(([name, value]) => [name, { version: value.version, digest: value.digest }])),
  files: fixture.oldState.files,
};
const oldBytes = `${JSON.stringify(oldFormat, null, 2)}\n`;
const statePath = join(fixture.destination, ".deniz-skills", "install.json");
writeFileSync(statePath, oldBytes);
writeLeftoverTransaction(fixture.destination, {
  schemaVersion: 1, transactionId: "unsupported-state", phase: "prepared",
  oldStateDigest: hashBytes(oldBytes), newStateDigest: stateDigest(fixture.plan.nextState),
  operations: [], applied: [],
}, { oldStateBytes: oldBytes, newStateBytes: serializeInstallState(fixture.plan.nextState) });
const recovery = inspectRecovery(fixture.destination);
assert.ok(recovery && recovery.kind === "blocked");
assert.match(recovery.message, /schemaVersion/);
assert.equal(readFileSync(statePath, "utf8"), oldBytes);
assert.equal(readFileSync(fixture.target, "utf8"), fixture.oldBytes);
```

The raw old-state hash is valid; this rejection must come from its unsupported format, not a hash
mismatch. These tests may pass on the existing transaction engine once Task 2 carries the new
fields. Do not manufacture a failure or change working transaction code merely to create a red run.
- [ ] Run `node --test tools/lib/opencode-install-apply.test.ts tools/install-opencode.test.ts`. Existing transaction tests must still reach their intended failure points with schema-2 state; journal version stays 1. Only fix production Apply/Recovery if a new test demonstrates a real defect. Keep current lock, syscall intent, state-aside, rollback, and finalization semantics.
- [ ] Prove lock precedence and recomputation at the CLI boundary, retaining the existing competing-owner tests and without a production test hook. First, a held mutation lock must win over a dependency finding:

```typescript
test("dependency: mutation checks Selection only after acquiring its lock", async () => {
  const fixture = makeCliFixture({ "deniz-provider": { "skills/provider/SKILL.md": "provider\n" } });
  writeBundle(fixture.io.packageRoot, "deniz-process", { "skills/alpha/SKILL.md": "alpha skill\n" }, "0.2.0", ["deniz-provider"]);
  assert.equal((await runInstallCli(["install", "--all", "--yes"], fixture.io)).exitCode, 0);
  const lock = acquireInstallerLock(fixture.destination);
  try {
    const result = await runInstallCli(["remove", "--module", "deniz-provider", "--yes"], fixture.io);
    assert.equal(result.exitCode, 1);
    assert.match(result.stderr, /Active installer lock/);
    assert.doesNotMatch(result.stdout, /missing_dependency/);
  } finally { lock.release(); }
  const result = await runInstallCli(["remove", "--module", "deniz-provider", "--yes"], fixture.io);
  assert.equal(result.exitCode, 1);
  assert.match(result.stdout, /missing_dependency/);
});

test("dependency: Apply does not reuse a previously valid printed Plan", async () => {
  const fixture = makeCliFixture({ "deniz-provider": { "skills/provider/SKILL.md": "provider\n" } });
  assert.equal((await runInstallCli(["install", "--all", "--yes"], fixture.io)).exitCode, 0);
  assert.equal((await runInstallCli(["remove", "--module", "deniz-provider"], fixture.io)).exitCode, 0);
  writeBundle(fixture.io.packageRoot, "deniz-process", { "skills/alpha/SKILL.md": "alpha skill\n" }, "0.3.0", ["deniz-provider"]);
  assert.equal((await runInstallCli(["update", "--yes"], fixture.io)).exitCode, 0);
  const statePath = join(fixture.destination, ".deniz-skills", "install.json");
  const before = readFileSync(statePath);
  const result = await runInstallCli(["remove", "--module", "deniz-provider", "--yes"], fixture.io);
  assert.equal(result.exitCode, 1);
  assert.match(result.stdout, /missing_dependency/);
  assert.ok(readFileSync(statePath).equals(before));
});
```
- [ ] Update documentation and the development workflow without inventing a new Release: the checkout installer now requires schema 2; public `installer-v0.3.0` stays an internally consistent schema-1 historical source snapshot. Preserve its download/digest recipe. Change only `workflow_dispatch.inputs.source_ref.default` from the old schema-1 SHA to `master`, keeping `publish: false` and `replace_existing: false`. Evidence runs must still pass an exact SHA explicitly. In quality-gates, clarify that a selected source must use a format supported by the workflow revision's verifier; matching historical workflow revisions, not a runtime compatibility shim, are how historical evidence is reproduced. Do not edit the old release record.
- [ ] Run the full verification sequence and inspect warnings rather than suppressing them:

```powershell
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
npm run build
npm run inventory
npm run validate
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab
git diff --check
```

Preserve the first generated diff, run `npm run build` and `npm run inventory` again, and compare
exactly, including untracked paths. Existing expected validation warnings concern the optional
`elements-of-style` namespace and the converted `subagent-driven-development` relative path; any
other finding requires investigation. Curation surface/counts did not change, so the experiment's
hand-verified ledger oracle must not need another baseline change for this feature.

- [ ] Obtain a read-only Standards/Spec review against the approved spec and the complete feature diff, with a reviewer from another model lineage. Resolve findings without weakening tests. Use the fourth suggested commit `test: verify dependency-aware installation safety` for the reviewed safety/docs closeout when commits are authorized. Do not claim Linux Package verification before it runs.
- [ ] After an explicitly authorized commit/push, run the existing Linux workflow in build-only mode on that exact commit. The release tag below remains only the workflow's unused build-only input; there is no upload, replacement, or Release edit:

```powershell
$source = (git rev-parse HEAD).Trim()
$branch = (git branch --show-current).Trim()
if (-not $branch) { throw "Dispatch from the authorized pushed feature branch, not detached HEAD" }
gh workflow run release-package.yml --repo Blind-Striker/agent-skills-and-plugins --ref $branch -f source_ref=$source -f release_tag=installer-v0.3.0 -f publish=false -f replace_existing=false
gh run list --repo Blind-Striker/agent-skills-and-plugins --workflow release-package.yml --commit $source --limit 5 --json databaseId,status,conclusion,url
```

Select the returned run for this dispatch, wait for its conclusion, and inspect its build job. It
must cover the full source gate, two clean generations, exact tar/Bundle verification, isolated
zero-write Plan, Apply-all, and status. If push or a Linux runner is unavailable, report that proof as
blocked, not completed; local Windows checks do not replace it. Store only measured artifact/run
identity in a new dated record, never predicted IDs or a digest copied from the old Release.
The workflow revision must also contain the schema-2 verifier, which is why its ref is the pushed
feature branch rather than an assumed `master`. A post-proof documentation commit may be needed to
record the measured run and consume planning files; it is not another implementation phase. Do not
amend the already-verified source commit to hide that evidence chronology.

- [ ] Move implemented guarantees into canonical docs, remove iteration 1 from unfinished work, retain iteration 2, and consume this plan/spec and their temporary roadmap relay according to the documentation lifecycle. Update operational memory, report actual test results/skips/warnings and the supported proof boundary, and preserve unrelated worktree changes. No real-profile operation follows automatically.

## Spec Coverage and Handoff

| Spec section | Implementing task |
|---|---|
| Compile-time derivation | Task 1 logic; Task 2 build/validation integration |
| Bundle identity and whole-Package closure | Task 2 |
| Strict state, clean format rejection | Task 2; Task 4 rejected old Recovery evidence |
| Final Selection and mixed versions | Task 3 |
| Actual versus proposed status | Task 3 |
| Metadata-only Apply and exact Recovery | Task 4 |
| Distribution, canon, safety, and non-goals | Global constraints and Task 4 |

Plan self-review must check this table against every acceptance row, verify that all later signatures
match the interfaces above, and scan for incomplete instructions. The plan itself is not passing
implementation evidence. Execute with subagent-driven-development or inline executing-plans only
after the user selects the execution mode.
