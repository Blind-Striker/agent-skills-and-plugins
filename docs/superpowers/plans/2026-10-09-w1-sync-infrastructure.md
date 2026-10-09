# W1 Sync Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use deniz-process:subagent-driven-development (recommended) or deniz-process:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Date: 2026-10-09

**Goal:** Implement the W1 rules that canon already states: `eject --patch --rebase` with a
bless that checks the patch applies (X5), the unknown-source rule for sources deleted upstream (X7),
the recorded-absent drift predicate and one bless ceremony (X10), the ledger-history name universe,
and path-claim punctuation. W1 is tooling only; generated output stays byte-identical.

**Architecture:** `tools/lib/overlay.ts` owns the git plumbing (`gitEnv`, `gitApply`, `cutPatch`,
`cutResidue`, `copyUpstream`, `replayPatch`, `fileStamp`, `ejectCommand`); `tools/eject.ts` owns the
command contract and its messages; `tools/build.ts`, `tools/lib/assemble.ts`, `tools/sync.ts`, and
`tools/validate.ts` only change their messages and the name universe; `tools/lib/ledger.ts` gains
`everListedNames`; `tools/lib/refs.ts` changes one character class.

**Tech Stack:** Node 24 (`node --test`, native TypeScript stripping), TypeScript 7
(`tsc --noEmit`), Biome, git 2.55 (`apply --3way`, alternates, `--path-format=absolute`).

**Spec:** [`docs/superpowers/specs/2026-10-09-w1-sync-infrastructure-design.md`](../specs/2026-10-09-w1-sync-infrastructure-design.md),
cited as "spec §N". Canon owners: [`workflow.md`](../../engineering/workflow.md#manifest-and-body-edit-flow),
[`curation/SCHEMA.md`](../../../curation/SCHEMA.md),
[`references-and-linking.md`](../../architecture/references-and-linking.md).

## Global Constraints

- Branch `master`. Never push. Never move a submodule pin: `git submodule status` at the end equals
  the baseline. Fetch only inside the scratchpad clones.
- Never hand-edit `external/`, `plugins/`, `opencode/`, `codex/`, `dist/`, either marketplace,
  `docs/inventory.md`, or `docs/ledger.json`.
- Generated output must be byte-identical after every task, and `npm run validate` output must
  equal the W1.1 baseline after every task. Every W1 task changes a module that the build or
  validate loads (`overlay.ts` and `assemble.ts` sit on the build path), so the quality-gates
  matrix requires the generated-output row each time, not only the tooling row.
- Every task is red, then green, then one commit. Behavior-neutral tasks (W1.2) prove neutrality
  with the existing suite and change no assertion.
- Per-commit gate ("full gate" below), the tooling row and then the generated-output row of
  `docs/engineering/quality-gates.md`: `npm test`, `npm run typecheck`, `npm run lint`,
  `npm run format:check`, `npm run check:public-safety`, each exiting 0 with `npm test` ending in
  `ℹ fail 0`; then

```bash
npm run build && npm run inventory
git status --porcelain -- plugins opencode codex dist .claude-plugin .agents/plugins/marketplace.json docs/inventory.md docs/ledger.json
node tools/validate.ts > "$SP/w1-validate-now.txt" 2>&1; echo "exit=$?" >> "$SP/w1-validate-now.txt"
diff "$SP/w1-validate0.txt" "$SP/w1-validate-now.txt" && echo VALIDATE-IDENTICAL
```

  The `git status` line prints nothing and the diff prints `VALIDATE-IDENTICAL`.
- Commit messages end with a blank line, then
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Write them with `git commit -F -` and
  a heredoc.
- Test fixtures use the identity `-c user.name=t -c user.email=t@example.test` and
  `-c core.autocrlf=false` on every git call they make.
- No backward compatibility: old message wording and the old `--force` recut recipe are not kept.

## Decision record

The curator-approved direction fixes the choices; the spec §3 table records why each alternative
lost. Two accepted risks need no further decision: a rebase cut stamps merge-source drift after the
replay printed it (spec §11), and a deliberately authored line that is a diff3 marker (seven `<`,
`|`, or `>`, then a space or the line end) is refused by the cut.

## File structure

| File | Tasks |
|---|---|
| `tools/lib/overlay.ts` | W1.2, W1.3, W1.7, W1.9, W1.10 |
| `tools/lib/overlay.merge.test.ts` | W1.3 |
| `tools/lib/overlay.replay.test.ts` (new) | W1.9, W1.10 |
| `tools/eject.ts`, `tools/eject.test.ts` | W1.2, W1.3, W1.7, W1.8, W1.9, W1.11 |
| `tools/build.ts`, `tools/build.test.ts` | W1.5, W1.7 |
| `tools/lib/assemble.ts` | W1.5, W1.7 |
| `tools/lib/assemble.test.ts` | W1.7 |
| `tools/sync.ts`, `tools/sync.test.ts` | W1.5, W1.6, W1.7 |
| `tools/validate.ts`, `tools/validate.test.ts` | W1.4, W1.5, W1.6 |
| `tools/lib/manifest.ts` | W1.5 |
| `tools/lib/ledger.ts`, `tools/lib/ledger.test.ts` | W1.6 |
| `tools/lib/refs.ts`, `tools/lib/refs.test.ts` | W1.4 |
| `docs/…`, `README.md` | W1.12, W1.13 |

---

### Task W1.1: Baseline (no commit)

- [ ] **Step 1:** `git status --porcelain` -> empty; `git log -1 --format=%h` -> the design commit.
- [ ] **Step 2:** Save the baseline into the session scratchpad (`$SP`, never inside the repository):

```bash
git submodule status > "$SP/w1-submodules.txt"
npm run build > "$SP/w1-build0.txt" 2>&1
npm run inventory > "$SP/w1-inventory0.txt" 2>&1
node tools/validate.ts > "$SP/w1-validate0.txt" 2>&1; echo "exit=$?" >> "$SP/w1-validate0.txt"
git status --porcelain
```

  Expected: build, inventory, and validate exit as on `master`; the final `git status --porcelain`
  prints nothing (generation is current).

### Task W1.2: Extract `cutPatch` and widen `gitApply` (behavior-neutral)

**Files:** Modify `tools/lib/overlay.ts`, `tools/eject.ts`.

- [ ] **Step 1:** In `overlay.ts`, change the private `gitApply(dir, patch, check: boolean)` to
  `gitApply(dir: string, patch: string, extra: string[], env: NodeJS.ProcessEnv = {})`: replace
  `...(check ? ["--check"] : [])` with `...extra`, and the call's `env` with
  `{ ...process.env, GIT_CONFIG_NOSYSTEM: "1", ...env }`. `checkPatch` passes `["--check"]`,
  `applyPatch` passes `[]`. Public signatures do not change.
- [ ] **Step 2:** Move eject phase 2's diff (eject.ts lines 215-250) into `overlay.ts`, unchanged in
  format and comments:

```ts
const noSymlinks = (s: string): boolean => !lstatSync(s).isSymbolicLink();

/**
 * The patch that turns `upstreamDir` into `workDir`: two snapshots side by side, because a commit
 * plus copy could not see an added (untracked) or deleted (a copy never removes) file. The a/ and
 * b/ names are exactly what -p1 strips. Returns "" when the trees are equal.
 */
export function cutPatch(upstreamDir: string, workDir: string): string {
  const tmp = mkdtempSync(join(tmpdir(), "eject-"));
  try {
    cpSync(upstreamDir, join(tmp, "a"), { recursive: true, filter: noSymlinks });
    cpSync(workDir, join(tmp, "b"), { recursive: true, filter: noSymlinks });
    try {
      // Pin the format: diff.noprefix or diff.mnemonicPrefix would break the -p1 contract, and
      // diff.context would silently narrow the anchor a patch is matched on.
      execFileSync(
        "git",
        ["-c", "core.autocrlf=false", "-c", "core.eol=lf", "diff", "--no-ext-diff", "--no-textconv",
         "--no-prefix", "-U8", "--no-index", "--", "a", "b"],
        { cwd: tmp, encoding: "utf8", env: { ...process.env, GIT_CONFIG_NOSYSTEM: "1" } },
      );
      return "";
    } catch (e) {
      // --no-index exits 1 when the trees differ, which is the normal outcome.
      return (e as { stdout?: string }).stdout ?? "";
    }
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
}
```

  `eject.ts` phase 2 becomes `const patch = cutPatch(src, dest);`. Keep eject's own `noSymlinks`
  for phase 1 and the full-file eject until W1.9.
- [ ] **Step 3:** `npm test` -> `ℹ fail 0` with no test edited; the four other gate commands exit 0.
- [ ] **Step 4:** Commit `refactor: move the patch cut into overlay.ts and widen gitApply`.

### Task W1.3: `fileStamp` — the drift scan honours the recorded-absent stamp (X10)

**Files:** Modify `tools/lib/overlay.ts`, `tools/eject.ts`, `tools/lib/overlay.merge.test.ts`.

- [ ] **Step 1: Failing tests** appended to `tools/lib/overlay.merge.test.ts` (add `mkdirSync`,
  `rmSync` and `driftedFiles`, `stampFiles` to the imports):

```ts
test("a directory at a recorded-absent merge name is not drift", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const entry: LockEntry = {
    source: "sp/skills/alpha",
    files: {},
    mergeSources: { "sp/skills/beta": stampMergeFiles(dir, ["SKILL.md", "notes.md"]) },
  };
  mkdirSync(join(dir, "notes.md"));
  assert.deepEqual(driftedMergeSources(root, entry), []);
});

test("a stamped file replaced by a directory is drift, not a hash-object error", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const entry: LockEntry = {
    source: "sp/skills/beta",
    files: stampFiles(dir, ["SKILL.md"]),
    mergeSources: { "sp/skills/beta": stampMergeFiles(dir, ["SKILL.md"]) },
  };
  rmSync(join(dir, "SKILL.md"));
  mkdirSync(join(dir, "SKILL.md"));
  assert.deepEqual(driftedFiles(dir, entry), ["SKILL.md"]);
  assert.deepEqual(driftedMergeSources(root, entry), ["sp/skills/beta: SKILL.md"]);
});

test("drift of a fresh stamp is empty for a file, an absent name, and a directory", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const entry: LockEntry = {
    source: "sp/skills/beta",
    files: stampFiles(dir, ["SKILL.md", "references"]),
    mergeSources: { "sp/skills/beta": stampMergeFiles(dir, ["SKILL.md", "absent.md", "references"]) },
  };
  assert.deepEqual(entry.mergeSources?.["sp/skills/beta"]?.references, null);
  assert.deepEqual(driftedFiles(dir, entry), []);
  assert.deepEqual(driftedMergeSources(root, entry), []);
});
```

- [ ] **Step 2:** `node --test tools/lib/overlay.merge.test.ts` -> FAIL: the first reports
  `notes.md (appeared upstream)`, the second throws from `git hash-object`.
- [ ] **Step 3: Implement** in `overlay.ts`:

```ts
/** What a stamp records for a path: the blob SHA of a regular file, null for anything else. */
export function fileStamp(p: string): string | null {
  return existsSync(p) && lstatSync(p).isFile() ? blobSha(p) : null;
}
```

  - `stampFiles`: `const sha = fileStamp(join(upstreamDir, f)); if (sha !== null) stamped[f] = sha;`
  - `driftedFiles`: `.filter(([f, sha]) => fileStamp(join(upstreamDir, f)) !== sha)`
  - `stampMergeFiles`: `out[f] = fileStamp(join(dir, f));`
  - `driftedMergeSources` loop body:

```ts
const now = fileStamp(join(dir, f));
if (now !== sha) {
  msgs.push(sha === null ? `${addr}: ${f} (appeared upstream)` : `${addr}: ${f}`);
}
```

  - `eject.ts` `showDrift`: replace `if (!existsSync(now))` with `if (fileStamp(now) === null)`.
- [ ] **Step 4:** Targeted file -> PASS; full gate.
- [ ] **Step 5:** Commit `fix: stamp and drift-check paths through one file predicate`.

### Task W1.4: Path claims drop one trailing `,`, `;`, or `:`

**Files:** Modify `tools/lib/refs.ts`, `tools/lib/refs.test.ts`, `tools/validate.test.ts`.

- [ ] **Step 1: Failing tests.** In `refs.test.ts`, beside the existing "one trailing dot dropped"
  assertion (line 112):

```ts
for (const p of [",", ";", ":"]) {
  assert.deepEqual(claims(`read ../beta/notes.md${p} then`, 0), ["climb:beta:../beta/notes.md"], `trailing ${p}`);
}
assert.deepEqual(claims("see ../beta/, then", 0), ["climb:beta:../beta/"], "a directory claim before a comma");
assert.deepEqual(claims("read ../beta/notes.md..", 0), ["climb:beta:../beta/notes.md."], "only one character");
```

  In `validate.test.ts`, after the P tests:

```ts
test("P: a claim followed by , ; or : resolves", () => {
  const errors = ocErrors("See ../beta/SKILL.md, then ../beta/SKILL.md; and ../beta/SKILL.md: done. Also ../gone/x.md; end.", [
    "  - source: sp/skills/alpha",
    "  - source: sp/skills/beta",
    "    invocation: auto",
  ]);
  assert.ok(!errors.some((m) => m.includes("../beta/SKILL.md")), errors.join("\n"));
  assert.ok(errors.some((m) => m.includes("sibling path ../gone/x.md does not resolve")), errors.join("\n"));
});
```

- [ ] **Step 2:** `node --test tools/lib/refs.test.ts tools/validate.test.ts` -> FAIL on the new
  cases (`../beta/notes.md,` is the claimed path).
- [ ] **Step 3: Implement** `claimPath`'s return as
  `return /[.,;:]$/.test(path) ? path.slice(0, -1) : path;` and update the `scanPathClaims` doc
  comment ("minus one trailing `.`, `,`, `;`, or `:`").
- [ ] **Step 4:** PASS; full gate, whose validate comparison proves that no shipped claim changed
  verdict.
- [ ] **Step 5:** Commit `fix: let a path claim end before , ; or :`.

### Task W1.5: One unknown-source rule (X7)

**Files:** Modify `tools/lib/manifest.ts`, `tools/validate.ts`, `tools/build.ts`,
`tools/lib/assemble.ts`, `tools/sync.ts`, `tools/validate.test.ts`, `tools/build.test.ts`,
`tools/sync.test.ts`.

- [ ] **Step 1: Failing tests.**
  - `validate.test.ts`:

```ts
test("an unknown source, taken or excluded, names the deleted-source rule", () => {
  for (const extra of [[], ["    exclude: true"]]) {
    const root = makeRepo();
    const p = join(root, "curation", "deniz-process.yaml");
    writeFileSync(p, `${readFileSync(p, "utf8")}  - source: sp/skills/vanished\n${extra.join("\n")}${extra.length ? "\n" : ""}`);
    const msg = validateRepo(root).find((f) => f.message.includes("unknown source sp/skills/vanished"));
    assert.ok(msg, "reported");
    assert.match(msg.message, /remove the item from the manifest — an exclude is not a tombstone/);
    assert.match(msg.message, /in that item's comment/);
    assert.match(msg.message, /lands with or after that pin move/);
  }
});
```

  - `build.test.ts`: `assert.throws(() => buildAll(root), /source not found in external\/: sp\/skills\/vanished — the current pin has no such source/)`
    for the same taken fixture.
  - `sync.test.ts`, in "a deleted source is reported as a build stop": add
    `assert.match(alpha, /an exclude is not a tombstone/);`; in "a deleted source under an excluded
    item": replace nothing, add `assert.match(gone, /validate rejects an unknown source/)` and
    `assert.match(gone, /an exclude is not a tombstone/)`.
- [ ] **Step 2:** Run the three files -> FAIL on the new assertions.
- [ ] **Step 3: Implement.** In `manifest.ts`:

```ts
/**
 * What an unknown manifest source means and what to do about it (workflow.md "Upstream sync
 * flow"). validate, build, the assemble mirror, and sync all say this sentence, so a pin move that
 * deletes a source gets one instruction wherever it is first noticed.
 */
export const UNKNOWN_SOURCE_RULE =
  "the current pin has no such source. If upstream deleted it, remove the item from the manifest — an exclude is not a tombstone — and give the reason in the commit message and, when another item succeeds it, in that item's comment. If upstream moved it, repoint source:. An exclude for a source that only a later pin has lands with or after that pin move.";
```

  - `validate.ts` section 1: `` message: `${m.plugin.name}: unknown source ${item.source} — ${UNKNOWN_SOURCE_RULE}` ``.
  - `build.ts` `collectProblems` and `assemble.ts` (the mirror throw):
    `` `${plugin}: source not found in external/: ${item.source} — ${UNKNOWN_SOURCE_RULE}` ``.
  - `sync.ts` `goneLine`:

```ts
return item.exclude
  ? `${plugin}: ${item.source} SOURCE GONE${where} — the build ignores it (excluded), but validate rejects an unknown source: ${UNKNOWN_SOURCE_RULE}`
  : `${plugin}: ${item.source} SOURCE GONE${where} — nothing auto-updates: the next build stops with "source not found in external/"; ${UNKNOWN_SOURCE_RULE}`;
```

  Update `goneLine`'s doc comment ("its entry is a record validate rejects").
- [ ] **Step 4:** PASS; full gate. `git grep -n "repoint or drop it" tools/sync.ts` -> only the
  MERGE SOURCE GONE line remains.
- [ ] **Step 5:** Commit `feat: say what a source deleted upstream requires`.

### Task W1.6: The ledger-history name universe

**Files:** Modify `tools/lib/ledger.ts`, `tools/validate.ts`, `tools/sync.ts`,
`tools/lib/ledger.test.ts`, `tools/validate.test.ts`, `tools/sync.test.ts`.

- [ ] **Step 1: Failing tests.**
  - `ledger.test.ts` (imports: `execFileSync`, `mkdirSync`, `mkdtempSync`, `writeFileSync`,
    `tmpdir`, `everListedNames`):

```ts
test("everListedNames reads every top-level key any committed ledger listed", () => {
  const root = mkdtempSync(join(tmpdir(), "ledger-"));
  const g = (...a: string[]) =>
    execFileSync("git", ["-c", "user.name=t", "-c", "user.email=t@example.test", "-c", "core.autocrlf=false", ...a], { cwd: root, stdio: "ignore" });
  mkdirSync(join(root, "docs"));
  const write = (o: object) => writeFileSync(join(root, "docs", "ledger.json"), `${JSON.stringify(o, null, 2)}\n`);
  assert.deepEqual(everListedNames(root), [], "outside Git");
  g("init", "-q");
  write({ "deniz-process/old-shape": { source: "x" }, "deniz-process/skill/gone": { claude: { "deniz-process/skill/nested": { a: 1 } } } });
  g("add", "-A"); g("commit", "-qm", "one");
  write({ "deniz-process/skill/kept": { source: "y" } });
  g("add", "-A"); g("commit", "-qm", "two");
  assert.deepEqual(everListedNames(root), ["gone", "kept", "old-shape"], "nested keys never count");
});
```

  - `validate.test.ts`:

```ts
test("H2–H4: a name a committed ledger once listed stays in the estate", () => {
  const history = (root: string) => {
    const g = (...a: string[]) =>
      execFileSync("git", ["-c", "user.name=t", "-c", "user.email=t@example.test", "-c", "core.autocrlf=false", ...a], { cwd: root, stdio: "ignore" });
    const ledger = join(root, "docs", "ledger.json");
    const built = readFileSync(ledger, "utf8");
    g("init", "-q");
    writeFileSync(ledger, `${JSON.stringify({ "deniz-process/skill/gone": { source: "sp/skills/gone" } }, null, 2)}\n`);
    g("add", "docs/ledger.json"); g("commit", "-qm", "gone listed");
    writeFileSync(ledger, built);
    g("add", "docs/ledger.json"); g("commit", "-qm", "gone deleted");
  };
  const items = ["  - source: sp/skills/alpha"];
  assert.ok(
    ocErrors("Use the `gone` skill.", items, history).some((m) => m.includes("names `gone`, which this estate does not emit")),
  );
  assert.ok(!ocErrors("Use the `gone` skill.", items).some((m) => m.includes("`gone`")), "no history, no name");
});
```

  - `sync.test.ts`: `assert.ok(syncCandidateNames({}, [], ["gone"]).includes("gone"));`
- [ ] **Step 2:** Run -> FAIL (`everListedNames` missing; no `gone` error; arity).
- [ ] **Step 3: Implement** in `ledger.ts` (import `execFileSync`):

```ts
/**
 * Every output name any committed docs/ledger.json has listed (references-and-linking.md "Handoff
 * templates"), so a name a pin move deleted stays known after its manifest item leaves. writeLedger
 * writes JSON.stringify(…, 2), so an item is a two-space top-level key line; a format change must
 * move this pattern with it. Keys were once `<plugin>/<name>` and are now `<plugin>/<kind>/<name>`;
 * the last segment is the name in both. Outside Git, or without the history, the set is smaller.
 */
export function everListedNames(root: string): string[] {
  let log: string;
  try {
    log = execFileSync(
      "git",
      ["-C", root, "log", "--format=", "-p", "-U0", "--no-ext-diff", "--no-textconv", "--", "docs/ledger.json"],
      { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"], maxBuffer: 1 << 28 },
    );
  } catch {
    return [];
  }
  const names = new Set<string>();
  for (const m of log.matchAll(/^\+ {2}"([^"]+)": \{$/gm)) {
    names.add((m[1] as string).split("/").at(-1) as string);
  }
  return [...names].sort();
}
```

  Add `// everListedNames reads this exact two-space key shape back from Git history.` above the
  `JSON.stringify(sorted, null, 2)` line in `writeLedger`.
  - `validate.ts`: after the own-skills loop of the estate-name block, add
    `for (const n of everListedNames(root)) { estateNames.add(n); }` and extend its comment with
    "and every output name a committed ledger has listed".
  - `sync.ts`: `syncCandidateNames(ledger, ownSkills, everListed: string[])` unions `everListed`;
    `main()` passes `everListedNames(root)`.
- [ ] **Step 4:** PASS; full gate, whose validate comparison proves that the 10 extra names raise
  no new finding.
- [ ] **Step 5:** Commit `feat: keep every name a committed ledger listed in the handoff universe`.

### Task W1.7: One eject command helper and one bless ceremony

**Files:** Modify `tools/lib/overlay.ts`, `tools/build.ts`, `tools/lib/assemble.ts`, `tools/sync.ts`,
`tools/build.test.ts`, `tools/sync.test.ts`.

- [ ] **Step 1: Failing tests.**
  - `build.test.ts`: in "a patch that no longer applies aborts before any output is deleted"
    (line 860) add
    `assert.throws(() => buildAll(root), /overlay\.patch no longer applies to sp\/skills\/gamma — run: npm run eject -- deniz-process gamma --patch --rebase/);`.
    In the merge-source tests at lines 1162, 1227, and 1263 add
    `assert.throws(() => buildAll(root), (e: Error) => /npm run eject -- deniz-process \S+ --bless/.test(e.message) && !e.message.includes("--yes"));`.
  - A body-patch item whose overlay directory is missing reports
    `run: npm run eject -- deniz-process gamma --patch` from both sides: a new `build.test.ts` case
    for `collectProblems` (`buildAll`), and a new `tools/lib/assemble.test.ts` case that calls
    `assembleItems` directly, beside its existing "body is overlay.*missing" assertion.
  - `sync.test.ts`: the OVERLAY assertion (line 130) becomes
    `/OVERLAY — review: git -C external\/sp diff <old> <new> -- skills\/beta; then npm run build names the eject step/`;
    the PATCH assertion (line 189) becomes
    `/PATCH — a change to a file overlay\.patch edits stops the build; changes elsewhere in the item flow through/`;
    a MERGE SOURCE change line matches `/then npm run build names the eject step/`; no sync line
    matches `/npm run eject/`.
- [ ] **Step 2:** Run -> FAIL.
- [ ] **Step 3: Implement.** `overlay.ts`:

```ts
export type EjectStep = "overlay" | "patch" | "bless" | "rebase";
const EJECT_FLAGS: Record<EjectStep, string> = {
  overlay: "",
  patch: " --patch",
  bless: " --bless",
  rebase: " --patch --rebase",
};
/** The one spelling of every eject step a message names (workflow.md "Manifest and body-edit flow"). */
export function ejectCommand(plugin: string, name: string, step: EjectStep): string {
  return `npm run eject -- ${plugin} ${name}${EJECT_FLAGS[step]}`;
}
```

  `build.ts`:
  - `overlayDrift(root, item, comp, plugin, outName, overlayDir, lock, next: string)`: delete the
    local `bless`; every return uses `next`; drop the three ` --yes` suffixes; the unguarded line
    ends `Fix merged_from, then: ${next}`.
  - `collectProblems`, patch branch: compute
    `const err = checkPatch(upstreamBase(root, item, comp), join(overlayDir, PATCH_FILE));` before
    `overlayDrift`, pass `ejectCommand(m.plugin.name, outName, err ? "rebase" : "bless")`, and push
    `` `${id}: ${PATCH_FILE} no longer applies to ${item.source} — run: ${ejectCommand(m.plugin.name, outName, "rebase")}\n${err}` ``.
    Problem order is unchanged (drift, omit, apply). Overlay branch passes `"bless"`.
  - Both missing-overlay messages use `ejectCommand(m.plugin.name, outName, item.body === "patch" ? "patch" : "overlay")`;
    `assemble.ts`'s mirror uses the same expression.
  `sync.ts`: `const NEXT_STEP = "then npm run build names the eject step";` and

```ts
// OVERLAY
tag = `OVERLAY — review: ${review}; ${NEXT_STEP}`;
// PATCH: stamped targets stop the build on any change to a file the patch edits.
tag = `PATCH — a change to a file ${PATCH_FILE} edits stops the build; changes elsewhere in the item flow through. Review: ${review}; ${NEXT_STEP}`;
// MERGE SOURCE changed
`${m.plugin.name}: ${item.source} MERGE SOURCE ${msrc} changed upstream — review: git -C external/${sub} diff <old> <new> -- ${mrel || "."}; ${NEXT_STEP}`
```

  Update the comments that say "re-bless" or "absorbed".
- [ ] **Step 4:** PASS; full gate; `git grep -n -- "--bless --yes\| --yes\`" tools/build.ts tools/sync.ts` -> no match.
- [ ] **Step 5:** Commit `fix: name one eject step, without --yes, in every overlay message`.

### Task W1.8: `--bless` refuses a patch that no longer applies

**Files:** Modify `tools/eject.ts`, `tools/eject.test.ts`.

- [ ] **Step 1: Failing test** in `eject.test.ts`:

```ts
test("eject --bless refuses a patch that no longer applies, even with --yes", () => {
  const root = makeRepo();
  const up = join(root, "external", "sp", "skills", "gamma", "SKILL.md");
  writeFileSync(up, readFileSync(up, "utf8").replace("Replace this line.", "Replaced upstream."));
  const lockFile = join(root, "overlays", "overlays.lock.json");
  const before = readFileSync(lockFile, "utf8");
  const r = eject(root, ["deniz-process", "gamma", "--bless", "--yes"]);
  assert.equal(r.ok, false);
  assert.match(r.out, /overlay\.patch no longer applies to sp\/skills\/gamma/);
  assert.match(r.out, /npm run eject -- deniz-process gamma --patch --rebase/);
  assert.doesNotMatch(r.out, /DRIFTED/, "refused before any drift display");
  assert.equal(readFileSync(lockFile, "utf8"), before);
});
```

- [ ] **Step 2:** `node --test tools/eject.test.ts` -> FAIL (bless stamps).
- [ ] **Step 3: Implement** right after the "Nothing to bless" guard:

```ts
// A stamp says "reviewed against this upstream". Recording it for a patch the build cannot apply
// would bless a state the next build still refuses, so the rebase comes first.
if (existsSync(patchFile)) {
  const bad = checkPatch(base, patchFile);
  if (bad) {
    console.error(
      `overlays/${plugin}/${name}/${PATCH_FILE} no longer applies to ${item.source} — blessing would record a patch the build cannot apply. Rebase it: ${ejectCommand(plugin, name, "rebase")}`,
    );
    console.error(bad);
    process.exit(1);
  }
}
```

- [ ] **Step 4:** PASS (the far-region bless tests in `eject.test.ts` line 219 and `build.test.ts`
  line 843 stay green); full gate.
- [ ] **Step 5:** Commit `fix: refuse to bless a patch that no longer applies`.

### Task W1.9: Cut hygiene — upstream copy filter, old patch, residue, check before replace

**Files:** Modify `tools/lib/overlay.ts`, `tools/eject.ts`; create `tools/lib/overlay.replay.test.ts`;
modify `tools/eject.test.ts`.

- [ ] **Step 1: Failing tests.**
  - `overlay.replay.test.ts` (new):

```ts
import assert from "node:assert/strict";
import { test } from "node:test";
import { cutResidue } from "./overlay.ts";

const chunk = (file: string, lines: string[]) =>
  [`diff --git a/${file} b/${file}`, `--- a/${file}`, `+++ b/${file}`, "@@ -1,2 +1,2 @@", ...lines, ""].join("\n");

test("cutResidue names an added marker line and an added .rej file", () => {
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" keep", "+<<<<<<< ours"])), ["SKILL.md: conflict marker"]);
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" keep", "+||||||| base"])), ["SKILL.md: conflict marker"]);
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" keep", "+>>>>>>>"])), ["SKILL.md: conflict marker"]);
  const rej = ["diff --git a/SKILL.md.rej b/SKILL.md.rej", "new file mode 100644", "--- /dev/null", "+++ b/SKILL.md.rej", "@@ -0,0 +1 @@", "+x", ""].join("\n");
  assert.deepEqual(cutResidue(rej), ["SKILL.md.rej: reject file"]);
});

test("cutResidue ignores markers in context, removed lines, and setext underlines", () => {
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" <<<<<<< HEAD", "->>>>>>> theirs", "+=======", "+Title"])), []);
});
```

  - `eject.test.ts`:

```ts
test("a cut never carries the old overlay.patch into the new one", () => {
  const root = makeRepo();
  const dir = join(root, "overlays", "deniz-process", "gamma");
  cpSync(join(root, "external", "sp", "skills", "gamma"), dir, { recursive: true });
  const file = join(dir, "SKILL.md");
  writeFileSync(file, readFileSync(file, "utf8").replace("Replace this line.", "Patched line."));
  const r = eject(root, ["deniz-process", "gamma", "--patch"]);
  assert.ok(r.ok, r.out);
  assert.doesNotMatch(readFileSync(join(dir, "overlay.patch"), "utf8"), /overlay\.patch/);
});

test("a cut refuses replay residue and leaves the old patch byte-identical", () => {
  for (const residue of ["marker", "rej"]) {
    const root = makeRepo();
    const dir = join(root, "overlays", "deniz-process", "gamma");
    const old = readFileSync(join(dir, "overlay.patch"), "utf8");
    cpSync(join(root, "external", "sp", "skills", "gamma"), dir, { recursive: true });
    const file = join(dir, "SKILL.md");
    if (residue === "marker") {
      writeFileSync(file, readFileSync(file, "utf8").replace("Replace this line.", "<<<<<<< ours\nA\n=======\nB\n>>>>>>> theirs"));
    } else {
      writeFileSync(join(dir, "SKILL.md.rej"), "@@ -1 +1 @@\n");
    }
    const r = eject(root, ["deniz-process", "gamma", "--patch"]);
    assert.equal(r.ok, false, residue);
    assert.match(r.out, residue === "marker" ? /SKILL\.md: conflict marker/ : /SKILL\.md\.rej: reject file/);
    assert.equal(readFileSync(join(dir, "overlay.patch"), "utf8"), old);
    assert.ok(existsSync(file), "the working copy stays");
  }
});

test("eject never copies an upstream root .git gitfile", () => {
  const root = makeRepo();
  const delta = join(root, "external", "sp", "skills", "delta");
  writeFileSync(join(delta, ".git"), "gitdir: ../../.git/modules/delta\n");
  assert.ok(eject(root, ["deniz-process", "delta", "--patch"]).ok);
  assert.ok(!existsSync(join(root, "overlays", "deniz-process", "delta", ".git")));
  rmSync(join(root, "overlays", "deniz-process", "delta"), { recursive: true });
  assert.ok(eject(root, ["deniz-process", "delta"]).ok);
  assert.ok(!(".git" in (loadLock(root)[lockKey("deniz-process", "delta")]?.files ?? {})));
});
```

  (Add `cpSync` to the `node:fs` import.)
- [ ] **Step 2:** Run both files -> FAIL (`cutResidue` missing; the old patch appears as an added
  file; markers are cut; `.git` is copied and stamped).
- [ ] **Step 3: Implement** in `overlay.ts`:

```ts
/**
 * Copy filter for every upstream copy: no symlinks (ADR-0001: a copied link absolutizes its target)
 * and no root `.git`, because a root item's gitfile would point git commands in the copy at the
 * real submodule (assemble.ts omits it for the same reason).
 */
export function copyUpstream(src: string): (s: string) => boolean {
  const gitfile = join(src, ".git");
  return (s) => s !== gitfile && !lstatSync(s).isSymbolicLink();
}

/** Splits a patch into one chunk per file at its column-0 `diff --git ` headers. */
export function splitPatch(text: string): string[] {
  const starts = [...text.matchAll(/^diff --git /gm)].map((m) => m.index);
  return starts.map((s, i) => text.slice(s, starts[i + 1] ?? text.length));
}

/** The item-relative path a chunk writes: its `+++ b/` side, else `--- a/`, else the header. */
export function chunkTarget(chunk: string): string {
  return (
    /^\+\+\+ b\/(.+?)\s*$/m.exec(chunk)?.[1] ??
    /^--- a\/(.+?)\s*$/m.exec(chunk)?.[1] ??
    /^diff --git a\/(\S+)/.exec(chunk)?.[1] ??
    ""
  );
}

// diff3 markers only. `=======` is left out because it is also a Markdown setext underline.
const ADDED_MARKER = /^\+(?:<{7}|\|{7}|>{7})(?: |$)/m;

/** Replay residue a cut must never ship: added conflict-marker lines and added `.rej` files. */
export function cutResidue(patch: string): string[] {
  const out: string[] = [];
  for (const chunk of splitPatch(patch)) {
    const target = chunkTarget(chunk);
    if (target.endsWith(".rej")) {
      out.push(`${target}: reject file`);
    } else if (ADDED_MARKER.test(chunk)) {
      out.push(`${target}: conflict marker`);
    }
  }
  return out;
}
```

  `cutPatch`: copy the upstream side with `copyUpstream(upstreamDir)` and the working side with
  `(s) => s !== join(workDir, PATCH_FILE) && !lstatSync(s).isSymbolicLink()`.
  `eject.ts`: replace `noSymlinks` with `{ filter: copyUpstream(src) }` in phase 1 and in the
  full-file eject (`isDir` branch). Phase 2 becomes:

```ts
const patch = cutPatch(src, dest);
if (!patch.trim()) { /* unchanged "No edits found" refusal */ }
const residue = cutResidue(patch);
if (residue.length) {
  console.error(`overlays/${plugin}/${name}/ still holds replay residue — nothing was written:`);
  for (const r of residue) console.error(`  ${r}`);
  console.error(`Resolve each conflict, delete each .rej, then: ${ejectCommand(plugin, name, "patch")}`);
  process.exit(1);
}
// Check before anything is replaced: during a rebase overlay.patch still holds the old edits.
const tmp = mkdtempSync(join(tmpdir(), "cut-"));
const candidate = join(tmp, PATCH_FILE);
writeFileSync(candidate, patch);
const bad = checkPatch(base, candidate);
rmSync(tmp, { recursive: true, force: true });
if (bad) {
  console.error(`Cut a patch that does not apply back to ${item.source}. Your working copy and ${PATCH_FILE} are untouched.`);
  console.error(bad);
  process.exit(1);
}
mkdirSync(dest, { recursive: true });
writeFileSync(patchFile, patch);
// … the existing working-copy deletion, stamp(), and "Cut N patch lines" report.
```

- [ ] **Step 4:** PASS; full gate (the existing `--force` recut and add/delete tests stay green).
- [ ] **Step 5:** Commit `fix: keep the old patch, residue, and gitfiles out of a cut`.

### Task W1.10: `replayPatch` in an isolated git environment

**Files:** Modify `tools/lib/overlay.ts`, `tools/lib/overlay.replay.test.ts`.

- [ ] **Step 1: Failing tests** appended to `overlay.replay.test.ts` (imports: `execFileSync`,
  `cpSync`, `existsSync`, `mkdirSync`, `mkdtempSync`, `readFileSync`, `rmSync`, `writeFileSync`,
  `tmpdir`, `dirname`, `join`, plus `copyUpstream`, `cutPatch`, `replayPatch`):

```ts
const lines = (n: number) => Array.from({ length: n }, (_, i) => `line ${i + 1}`);
const git = (cwd: string, ...a: string[]) =>
  execFileSync("git", ["-c", "user.name=t", "-c", "user.email=t@example.test", "-c", "core.autocrlf=false", ...a], { cwd, stdio: "ignore" });

function put(dir: string, files: Record<string, string>): void {
  for (const [f, c] of Object.entries(files)) {
    mkdirSync(dirname(join(dir, f)), { recursive: true });
    writeFileSync(join(dir, f), c);
  }
}

/** An upstream item committed as the old pin, the old patch cut from an edited copy, then upstream moved. */
function scenario(opts: { commit: boolean; edit: (f: string[]) => void; move: (up: string) => void }) {
  const up = mkdtempSync(join(tmpdir(), "up-"));
  put(up, { "SKILL.md": `${lines(30).join("\n")}\n`, "notes.md": "n1\nn2\n" });
  if (opts.commit) { git(up, "init", "-q"); git(up, "add", "-A"); git(up, "commit", "-qm", "pin"); }
  const edited = mkdtempSync(join(tmpdir(), "ed-"));
  cpSync(up, edited, { recursive: true, filter: copyUpstream(up) });
  const skill = lines(30); opts.edit(skill); put(edited, { "SKILL.md": `${skill.join("\n")}\n`, "notes.md": "n1\nN2 ours\n" });
  const patch = cutPatch(up, edited);
  opts.move(up);
  const work = join(mkdtempSync(join(tmpdir(), "rp-")), "w");
  cpSync(up, work, { recursive: true, filter: copyUpstream(up) });
  return { work, results: replayPatch(work, up, patch) };
}
const at = (up: string, n: number, text: string) => {
  const f = join(up, "SKILL.md"); const l = readFileSync(f, "utf8").split("\n"); l[n - 1] = text; writeFileSync(f, l.join("\n"));
};

test("replay: far change applies, near change merges, same line conflicts", () => {
  const edit = (f: string[]) => { f[14] = "line 15 ours"; };
  const far = scenario({ commit: true, edit, move: (up) => at(up, 1, "line 1 upstream") });
  assert.deepEqual(far.results.map((r) => `${r.file}:${r.status}`), ["SKILL.md:applied", "notes.md:applied"]);
  const near = scenario({ commit: true, edit, move: (up) => at(up, 10, "line 10 upstream") });
  assert.equal(near.results[0]?.status, "merged");
  assert.match(readFileSync(join(near.work, "SKILL.md"), "utf8"), /line 10 upstream[\s\S]*line 15 ours/);
  const same = scenario({ commit: true, edit, move: (up) => at(up, 15, "line 15 upstream") });
  assert.deepEqual(same.results[0], { file: "SKILL.md", status: "conflict", regions: 1 });
  assert.equal(same.results[1]?.status, "applied", "one file's conflict never blocks another");
  assert.match(readFileSync(join(same.work, "SKILL.md"), "utf8"), /^<<<<<<< ours$[\s\S]*^\|\|\|\|\|\|\| base$/m);
  assert.ok(!existsSync(join(same.work, ".git")), "the seed repository is removed");
});

test("replay: no base blob rejects; a deleted target is gone with its chunk in .rej", () => {
  const edit = (f: string[]) => { f[14] = "line 15 ours"; };
  const nobase = scenario({ commit: false, edit, move: (up) => at(up, 10, "line 10 upstream") });
  assert.equal(nobase.results[0]?.status, "rejected");
  assert.match(nobase.results[0]?.detail ?? "", /^1 of 1 hunks/);
  assert.ok(existsSync(join(nobase.work, "SKILL.md.rej")));
  const gone = scenario({ commit: true, edit, move: (up) => rmSync(join(up, "notes.md")) });
  assert.deepEqual(gone.results.map((r) => r.status), ["applied", "gone"]);
  assert.match(readFileSync(join(gone.work, "notes.md.rej"), "utf8"), /^\+N2 ours$/m);
});

test("replay: an ignore rule in the item never hides a file from the seed index", () => {
  const r = scenario({
    commit: true,
    edit: (f) => { f[14] = "line 15 ours"; },
    move: (up) => { at(up, 10, "line 10 upstream"); put(up, { ".gitignore": "SKILL.md\n" }); },
  });
  assert.equal(r.results[0]?.status, "merged");
});

test("replay never writes through an inherited GIT_INDEX_FILE or GIT_DIR", () => {
  const decoy = mkdtempSync(join(tmpdir(), "decoy-"));
  const saved = { GIT_INDEX_FILE: process.env.GIT_INDEX_FILE, GIT_DIR: process.env.GIT_DIR };
  process.env.GIT_INDEX_FILE = join(decoy, "index");
  process.env.GIT_DIR = join(decoy, "repo.git");
  try {
    scenario({ commit: false, edit: (f) => { f[14] = "x"; }, move: () => {} });
  } finally {
    for (const [k, v] of Object.entries(saved)) { if (v === undefined) delete process.env[k]; else process.env[k] = v; }
  }
  assert.ok(!existsSync(join(decoy, "index")) && !existsSync(join(decoy, "repo.git")));
});
```

  Note the third test's `scenario` with `commit: false` calls `git` only through `replayPatch` and
  `cutPatch`; if a fixture helper itself needs git under the decoy, run it before setting the
  variables.
- [ ] **Step 2:** `node --test tools/lib/overlay.replay.test.ts` -> FAIL (`replayPatch` missing).
  Once it exists, dropping `--force` from the seed `add` must turn the ignore-rule case `rejected`;
  check that once so the test is known to guard the flag.
- [ ] **Step 3: Implement** in `overlay.ts`:

```ts
const INHERITED_REPOSITORY = ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY",
  "GIT_COMMON_DIR", "GIT_ALTERNATE_OBJECT_DIRECTORIES"];

/**
 * The environment of every git call in the overlay plumbing: no system config, and no repository a
 * caller (a hook, an IDE) left in the environment — those would make an init or add in a throwaway
 * directory act on a real repository. `extra` may set alternates back on purpose.
 */
export function gitEnv(extra: NodeJS.ProcessEnv = {}): NodeJS.ProcessEnv {
  const env: NodeJS.ProcessEnv = { ...process.env, GIT_CONFIG_NOSYSTEM: "1" };
  for (const k of INHERITED_REPOSITORY) delete env[k];
  return { ...env, ...extra };
}
```

  - `toplevel`, `gitApply`, and `cutPatch` use `gitEnv(...)`; `gitApply`'s config prefix gains
    `"-c", "apply.whitespace=nowarn"` (a host `apply.whitespace=fix` would rewrite content).
  - The replay:

```ts
export type ReplayStatus = "applied" | "merged" | "conflict" | "rejected" | "gone";
export interface ReplayResult { file: string; status: ReplayStatus; regions?: number; reject?: string; detail?: string }

function seedGit(work: string, args: string[]): string {
  return execFileSync("git", ["-c", "core.autocrlf=false", "-c", "core.eol=lf", "-c", "core.longpaths=true", ...args],
    { cwd: work, encoding: "utf8", stdio: ["ignore", "pipe", "pipe"], env: gitEnv() });
}

/** Absolute object directory of the repository holding `dir`; a relative one silently disables --3way. */
function objectsOf(dir: string): string | null {
  try {
    return execFileSync("git", ["-C", dir, "rev-parse", "--path-format=absolute", "--git-path", "objects"],
      { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"], env: gitEnv() }).trim();
  } catch {
    return null;
  }
}

const firstLine = (s: string | null): string => (s ?? "").split("\n").find((l) => l.trim()) ?? "";
const hunks = (s: string): number => (s.match(/^@@ /gm) ?? []).length;

/**
 * Replays an old patch onto `work`, a copy of the current upstream item, one file at a time
 * (workflow.md "Manifest and body-edit flow"). The old pin's blobs live in the submodule, so the
 * throwaway repository borrows its objects; status is read from the index and the files, never from
 * git's wording.
 */
export function replayPatch(work: string, upstreamDir: string, patchText: string): ReplayResult[] {
  seedGit(work, ["init", "-q"]);
  mkdirSync(join(work, ".git", "info"), { recursive: true });
  writeFileSync(join(work, ".git", "info", "attributes"), "* -text\n");
  seedGit(work, ["config", "merge.conflictStyle", "diff3"]);
  // --force: an item's own .gitignore, info/exclude, or the host's core.excludesFile must not keep
  // a file out of the index, or its --3way merge fails as "does not exist in index".
  seedGit(work, ["add", "-A", "--force"]);
  const objects = objectsOf(upstreamDir);
  const alternates = objects ? { GIT_ALTERNATE_OBJECT_DIRECTORIES: objects } : {};
  const scratch = mkdtempSync(join(tmpdir(), "replay-"));
  try {
    return splitPatch(patchText).map((chunk, i): ReplayResult => {
      const file = chunkTarget(chunk);
      const p = join(scratch, `${i}.patch`);
      writeFileSync(p, chunk);
      if (gitApply(work, p, ["--check"]) === null && gitApply(work, p, []) === null) {
        return { file, status: "applied" };
      }
      const three = gitApply(work, p, ["--3way"], alternates);
      if (seedGit(work, ["ls-files", "-u", "--", file]).trim()) {
        const regions = (readFileSync(join(work, file), "utf8").match(/^<<<<<<< /gm) ?? []).length;
        return { file, status: "conflict", regions };
      }
      if (three === null) {
        return { file, status: "merged" };
      }
      const reject = `${file}.rej`;
      const rejected = gitApply(work, p, ["--reject"]);
      if (existsSync(join(work, reject))) {
        const k = hunks(readFileSync(join(work, reject), "utf8"));
        return { file, status: "rejected", reject, detail: `${k} of ${hunks(chunk)} hunks: ${firstLine(three)}` };
      }
      mkdirSync(dirname(join(work, reject)), { recursive: true });
      writeFileSync(join(work, reject), chunk);
      return existsSync(join(work, file))
        ? { file, status: "rejected", reject, detail: `whole file: ${firstLine(rejected)}` }
        : { file, status: "gone", reject };
    });
  } finally {
    rmSync(scratch, { recursive: true, force: true });
    rmSync(join(work, ".git"), { recursive: true, force: true });
  }
}
```

  (`seedGit` throws on a non-zero exit; `ls-files -u` exits 0. The `three === null` branch is
  reached only when `--check` failed, so it is a real merge.)
- [ ] **Step 4:** PASS; full gate. If the "near change merges" case reports `applied`, widen the
  distance so the moved line sits inside the 8 context lines but outside the hunk's changed line —
  do not change the algorithm.
- [ ] **Step 5:** Commit `feat: replay an old patch onto moved upstream one file at a time`.

### Task W1.11: `eject --patch --rebase`

**Files:** Modify `tools/eject.ts`, `tools/eject.test.ts`.

- [ ] **Step 1: Failing tests** in `eject.test.ts`. Add helpers:

```ts
function commitUpstream(root: string): void {
  const sp = join(root, "external", "sp");
  for (const a of [["init", "-q", "."], ["add", "-A"], ["commit", "-qm", "pin"]]) {
    execFileSync("git", ["-c", "user.name=t", "-c", "user.email=t@example.test", "-c", "core.autocrlf=false", ...a], { cwd: sp, stdio: "ignore" });
  }
}
function patchDelta(root: string, edits: Record<string, [string, string]>): string {
  const dir = join(root, "overlays", "deniz-process", "delta");
  assert.ok(eject(root, ["deniz-process", "delta", "--patch"]).ok);
  for (const [f, [from, to]] of Object.entries(edits)) {
    writeFileSync(join(dir, f), readFileSync(join(dir, f), "utf8").replace(from, to));
  }
  assert.ok(eject(root, ["deniz-process", "delta", "--patch"]).ok);
  const p = join(root, "curation", "deniz-process.yaml");
  writeFileSync(p, readFileSync(p, "utf8").replace("  - source: sp/skills/delta", "  - source: sp/skills/delta\n    body: patch"));
  return readFileSync(join(dir, "overlay.patch"), "utf8");
}
const changes = (p: string) => p.split("\n").filter((l) => /^[+-](?![+-]{2} )/.test(l)).sort();
const upstreamDelta = (root: string, f: string) => join(root, "external", "sp", "skills", "delta", f);
```

  Tests (names are the assertions):
  - **R0 reopening a patch on unmoved upstream recuts it byte-identically.** `commitUpstream`;
    `old = patchDelta(root, { "SKILL.md": ["Delta body.", "Our delta body."] })`; save the lock
    text; `eject delta --patch --rebase` -> ok, out matches `/applied\s+SKILL\.md/`; with no edit,
    `eject delta --patch` -> ok; `overlay.patch` equals `old` byte for byte and the lock text is
    unchanged. This is the spec §10 recut invariant and the flow that replaces the old `--force`
    recut recipe.
  - **R1 context-only move rebases clean and recuts the same edit.** `commitUpstream`;
    `old = patchDelta(root, { "SKILL.md": ["Delta body.", "Our delta body."] })`; upstream
    `description: Delta upstream` -> `description: Delta moved`; `buildAll` throws
    `/no longer applies[\s\S]*--patch --rebase/`; `eject delta --patch --rebase` -> ok, out matches
    `/merged\s+SKILL\.md/` and `/Next: review the working copy, then: npm run eject -- deniz-process delta --patch/`;
    the working SKILL.md holds `Delta moved` and `Our delta body.`; `overlay.patch` still equals
    `old`; `eject delta --patch` -> ok; `changes(newPatch)` deep-equals `changes(old)`; the lock's
    `files["SKILL.md"]` equals `blobSha(upstreamDelta(root, "SKILL.md"))`; `buildAll` passes and the
    emitted `plugins/deniz-process/skills/delta/SKILL.md` holds both changes.
  - **R2 a real conflict stops, keeps the old patch, and blocks the cut until resolved.** As R1, but
    upstream `Delta body.` -> `Delta body, upstream.`; rebase exits 1 with
    `/CONFLICT\s+SKILL\.md \(1 region\)/`, the legend `/<<<<<<< ours = current upstream/`, and
    `/delete each \.rej, review, then: npm run eject -- deniz-process delta --patch/`; the lock and
    `overlay.patch` are byte-identical; `eject delta --patch` exits 1 with
    `/SKILL\.md: conflict marker/`; write a resolved SKILL.md (upstream text with
    `Our delta body, upstream.`); the cut succeeds and `buildAll` passes.
  - **R3 one file's conflict leaves the other file's edit in place.** Edits to `SKILL.md` and
    `references/notes.md`; upstream conflicts `SKILL.md` only; the report has
    `/applied\s+references\/notes\.md/` and the working notes file holds the edit.
  - **R4 a patch with no base blob falls back to .rej.** No `commitUpstream`; use makeRepo's
    `gamma` patch (no index line); upstream `Replace this line.` -> `Replace this line, upstream.`;
    rebase exits 1 with `/REJECTED\s+SKILL\.md -> SKILL\.md\.rej \(1 of 1 hunks/`; the cut exits 1
    with `/SKILL\.md\.rej: reject file/`.
  - **R5 a target deleted upstream is GONE and drops out after resolution.** Edits to `SKILL.md`
    and `references/notes.md`; upstream deletes `references/notes.md`; rebase exits 1 with
    `/GONE\s+references\/notes\.md -> references\/notes\.md\.rej/`; delete the `.rej`; the cut
    succeeds; the lock's `files` keys are `["SKILL.md"]`; `buildAll` passes.
  - **R6 exec mode survives replay and cut** (`{ skip: process.platform === "win32" }`): upstream
    `scripts/run.sh` mode `0o755` with 20 lines, committed; a patch edits line 12; rewrite every
    `index` line's trailing mode in `overlay.patch` to `100644` (a Windows-cut patch); upstream
    changes line 6; after rebase `statSync(work run.sh).mode & 0o777` is `0o755`; after the cut the
    patch matches neither `/^old mode/m` nor `/^new mode/m`.
  - **R7 a non-Markdown file rebases like Markdown.** As R1 on `scripts/check.ps1` (20 lines).
  - **R8 merge-source drift is shown, and the cut stamps it.** Write the manifest first: `delta`
    `body: patch`, `merged_from: [sp/skills/beta]` (so the first cut stamps the merge source);
    `commitUpstream`; cut a patch with two `eject delta --patch` passes; change upstream
    `beta/SKILL.md`; rebase exits 0 and prints `/DRIFTED sp\/skills\/beta: SKILL\.md/` and
    `/^\+Beta body, moved upstream\.$/m`; the cut sets
    `mergeSources["sp/skills/beta"]["SKILL.md"]` to the new blob; `buildAll` passes.
  - **R9 refusals.** `eject delta --rebase` (no `--patch`), `--patch --rebase --force`, rebase with
    no `overlay.patch` (message names `--patch`), rebase with a working copy present, and rebase of
    a `body: overlay` skill (message names `--bless`) each exit 1 and leave the overlay directory
    and lock byte-identical.
  - **R10 the phase-1 refusal names the rebase.** `eject gamma --patch` on makeRepo's existing patch
    exits 1 with `/reopen it on current upstream: npm run eject -- deniz-process gamma --patch --rebase/`
    and `/--force/`.
  - **R11 upstream adopting every edit ends the patch instead of looping.** As R1, but upstream
    `Delta body.` -> `Our delta body.`; the rebase reports `merged` or `applied`; `eject delta --patch`
    exits 1 with `/already carries every edit/` and names `overlays/deniz-process/delta/`, its lock
    entry, and `body: patch`; `overlay.patch` and the lock are byte-identical.
- [ ] **Step 2:** `node --test tools/eject.test.ts` -> FAIL (unknown flag behaves as phase 1).
- [ ] **Step 3: Implement** in `eject.ts`:
  - Usage: `npm run eject -- <plugin> <name> [--patch [--rebase]] [--bless [--yes]] [--force]`;
    after the usage check, refuse `--rebase` without `--patch` with
    `--rebase reopens an overlay.patch: ${ejectCommand(plugin, name, "rebase")}` (place it after
    `plugin`/`name` are bound).
  - Extract the bless branch's loop into `printDrift(prior: LockEntry): number` (prints the
    DRIFTED lines and `showDrift` diffs, returns the count); the bless branch calls it.
  - Inside the `--patch` branch, after the skill-only check and before the `--force` handling:

```ts
if (flags.has("--rebase")) {
  if (flags.has("--force")) {
    console.error("--rebase keeps the old edits and --force discards them — pass one");
    process.exit(1);
  }
  if (!existsSync(patchFile)) {
    console.error(
      item.body === "overlay"
        ? `overlays/${plugin}/${name}/ is a full-file overlay — review upstream with: ${ejectCommand(plugin, name, "bless")}`
        : `Nothing to rebase: overlays/${plugin}/${name}/${PATCH_FILE} does not exist — start a patch with: ${ejectCommand(plugin, name, "patch")}`,
    );
    process.exit(1);
  }
  if (listFiles(dest).length) {
    console.error(
      `overlays/${plugin}/${name}/ already holds a working copy — cut it with: ${ejectCommand(plugin, name, "patch")}, or delete its files (${PATCH_FILE} stays) to replay again`,
    );
    process.exit(1);
  }
  // The cut stamps everything, so the review comes first — the same show-then-record as --bless.
  const prior = loadLock(root)[lockKey(plugin, name)];
  if (prior) {
    printDrift(prior);
  }
  const tmp = mkdtempSync(join(tmpdir(), "rebase-"));
  const work = join(tmp, "w");
  cpSync(src, work, { recursive: true, filter: copyUpstream(src) });
  const results = replayPatch(work, base, readFileSync(patchFile, "utf8"));
  // The old patch stays: deleting the working-copy files abandons the rebase.
  cpSync(work, dest, { recursive: true });
  rmSync(tmp, { recursive: true, force: true });
  console.log(`Replayed overlays/${plugin}/${name}/${PATCH_FILE} onto ${item.source}:`);
  for (const r of results) {
    console.log(`  ${replayLine(r)}`);
  }
  const open = results.filter((r) => r.status !== "applied" && r.status !== "merged");
  if (open.some((r) => r.status === "conflict")) {
    console.log("Markers: <<<<<<< ours = current upstream, ||||||| base = what the patch was cut against, >>>>>>> theirs = the old patch's result.");
  }
  console.log(
    open.length
      ? `Next: resolve every conflict and .rej, delete each .rej, review, then: ${ejectCommand(plugin, name, "patch")}`
      : `Next: review the working copy, then: ${ejectCommand(plugin, name, "patch")}`,
  );
  process.exit(open.length ? 1 : 0);
}
```

  with

```ts
function replayLine(r: ReplayResult): string {
  switch (r.status) {
    case "applied":
      return `applied   ${r.file}`;
    case "merged":
      return `merged    ${r.file} (upstream moved inside the patched region — read the merge)`;
    case "conflict":
      return `CONFLICT  ${r.file} (${r.regions} region${r.regions === 1 ? "" : "s"})`;
    case "rejected":
      return `REJECTED  ${r.file} -> ${r.reject} (${r.detail})`;
    case "gone":
      return `GONE      ${r.file} -> ${r.reject} (upstream deleted the file)`;
  }
}
```

  - The phase-1 refusal for an existing patch becomes
    `` `overlays/${plugin}/${name}/${PATCH_FILE} already exists — reopen it on current upstream: ${ejectCommand(plugin, name, "rebase")}, or pass --force to discard it` ``.
  - The cut's empty-patch refusal splits on whether `overlay.patch` exists. Without one it keeps
    "No edits found …". With one, the working copy equals current upstream, so the build's rebase
    hint would only lead back here:
    `` `Upstream already carries every edit in overlays/${plugin}/${name}/${PATCH_FILE} — nothing is left to patch. Delete overlays/${plugin}/${name}/, its ${lockKey(plugin, name)} entry in overlays/${LOCK_FILE}, and body: patch on the item.` ``
    Nothing is written.
  - `showDrift` and the cut's git calls use `gitEnv()`.
- [ ] **Step 4:** `node --test tools/eject.test.ts` -> PASS (R6 skipped on Windows, runs on CI
  Linux); full gate.
- [ ] **Step 5:** Commit `feat: eject --patch --rebase replays an old patch onto current upstream`.

### Task W1.12: Canon closeout

**Files:** `docs/architecture/transformation-and-emission.md`, `docs/architecture/references-and-linking.md`,
`docs/engineering/workflow.md`, `curation/SCHEMA.md`, `README.md`, `docs/ROADMAP.md`.

- [ ] **Step 1:** Re-read the canon written by the design commit against the implementation; fix any
  wording the code proved wrong (message text, statuses) in the owner, not in a relay.
- [ ] **Step 2:** Refresh the `overlay.ts` line anchors in `transformation-and-emission.md`
  ("Current limits": `blobSha`, `stampFiles`, `gitApply`) to the new line ranges
  (`grep -n "export function blobSha\|export function stampFiles\|^function gitApply" tools/lib/overlay.ts`).
- [ ] **Step 3:** `README.md` command table: the eject row reads
  `` `npm run eject -- <plugin> <name> [--patch [--rebase]] [--bless]` `` | "Copy an item to `overlays/` for body editing, reopen a patch on current upstream, or bless reviewed upstream drift".
- [ ] **Step 4:** `docs/ROADMAP.md`: delete the Known Gaps "Deleted names leave the handoff
  universe", "Bless hints disagree", and "Recorded-absent merge directories"; add the gaps W1
  leaves:
  - **Full-file overlays have no replay:** an upstream move under a full-file overlay is reviewed
    through the `--bless` diff and ported by hand (`ask-deniz` would replay with 6 conflicts).
  - **Ledger-history names need Git history:** a shallow or archived checkout knows only the names
    its history holds; a name deleted before any ledger listed it is unknown; a renamed taken
    item keeps only its output name.
  - **`path:line` claims:** `../x/SKILL.md:42` is read as one path and does not resolve.
  Replace the W1 entry's pending-implementation list with "landed; closeout in W1.13".
- [ ] **Step 5:** Docs-only check (dates, links, one owner per claim); `npm test` (guarded prose);
  commit `docs: close the W1 canon against the implementation`.
- [ ] **Step 6:** Update Memorizer how-to `1af9e8b7` ("re-cut an existing overlay.patch with eject
  --patch --force"): the recipe is superseded by `eject --patch --rebase`; keep the probe facts and
  link the W1 measurement record `d8dbee25`.

### Task W1.13: Full gate, byte-identical proof, dry run, close

- [ ] **Step 1: Full gate.** `npm test`, `npm run typecheck`, `npm run lint`, `npm run format:check`,
  `npm run check:public-safety`, then `npm run build`, `npm run inventory`, `npm run validate`.
  Expected: all exit as at baseline; `npm test` -> `ℹ fail 0`.
- [ ] **Step 2: Byte-identical proof.**

```bash
git status --porcelain -- plugins opencode codex dist .claude-plugin .agents/plugins/marketplace.json docs/inventory.md docs/ledger.json
npm run build && npm run inventory
git status --porcelain -- plugins opencode codex dist .claude-plugin .agents/plugins/marketplace.json docs/inventory.md docs/ledger.json
node tools/validate.ts > "$SP/w1-validate-final.txt" 2>&1; echo "exit=$?" >> "$SP/w1-validate-final.txt"
diff "$SP/w1-validate0.txt" "$SP/w1-validate-final.txt" && echo VALIDATE-IDENTICAL
git submodule status | diff "$SP/w1-submodules.txt" - && echo PINS-UNCHANGED
```

  Expected: both `git status` lines print nothing; `VALIDATE-IDENTICAL`; `PINS-UNCHANGED`. The
  recut invariant (an unchanged working copy recuts byte-identically) is proven by R0, not here.
- [ ] **Step 3: Dry run against a temporary upstream head.** Everything below lives in the session
  scratchpad; the real checkout and its submodules are only read. `R` is the repository root, `SP`
  the scratchpad.

```bash
S="$SP/w1-demo"; rm -rf "$S"
git clone -q --no-hardlinks "$R" "$S"
for sub in asd-ste100 aspire-skills dotnet-agent-skills dotnet-skills mattpocock-skills superpowers; do
  git clone -q --shared --no-checkout "$R/.git/modules/external/$sub" "$S/external/$sub"
  git -C "$S/external/$sub" checkout -q --detach "$(git -C "$R" rev-parse "HEAD:external/$sub")"
done
git -C "$S/external/aspire-skills" fetch -q "$SP/clones/microsoft_aspire-skills.git" '+refs/heads/*:refs/remotes/head/*'
git -C "$S/external/aspire-skills" checkout -q --detach da1230cabd8bb9908b31d369afa5b93673bfc897
(cd "$S" && node "$R/tools/build.ts" 2>&1 | grep -E "aspire-(orchestration|deployment)")
(cd "$S" && node "$R/tools/eject.ts" deniz-dotnet-aspire aspire-deployment --bless --yes; echo "exit=$?")
(cd "$S" && cp overlays/deniz-dotnet-aspire/aspire-orchestration/overlay.patch "$SP/w1-orch-old.patch" \
  && node "$R/tools/eject.ts" deniz-dotnet-aspire aspire-orchestration --patch --rebase; echo "exit=$?")
(cd "$S" && node "$R/tools/eject.ts" deniz-dotnet-aspire aspire-orchestration --patch; echo "exit=$?")
pm() { grep -E '^[+-]' "$1" | grep -vE '^(\+\+\+|---) ' | sort; }
diff <(pm "$SP/w1-orch-old.patch") <(pm "$S/overlays/deniz-dotnet-aspire/aspire-orchestration/overlay.patch") && echo SAME-EDITS
(cd "$S" && node "$R/tools/eject.ts" deniz-dotnet-aspire aspire-deployment --patch --rebase; echo "exit=$?")
(cd "$S" && node "$R/tools/eject.ts" deniz-dotnet-aspire aspire-deployment --patch; echo "exit=$?")
git -C "$R" status --porcelain; git -C "$R" submodule status | diff "$SP/w1-submodules.txt" - && echo PINS-UNCHANGED
rm -rf "$S"
```

  Expected (spec §2 measurement):
  - build: `deniz-dotnet-aspire/aspire-orchestration: overlay.patch no longer applies to aspire-skills/skills/aspire-orchestration — run: npm run eject -- deniz-dotnet-aspire aspire-orchestration --patch --rebase`
    and the same for `aspire-deployment`; their drift lines end with `--patch --rebase`; no line
    contains `--yes`.
  - bless: `exit=1`, "no longer applies … Rebase it: npm run eject -- deniz-dotnet-aspire aspire-deployment --patch --rebase".
  - orchestration rebase: every line `applied` or `merged`, `exit=0`; cut `exit=0`; `SAME-EDITS`.
  - deployment rebase: `CONFLICT  SKILL.md (1 region)`, every references file `applied` or
    `merged`, the marker legend, `exit=1`; the cut: `SKILL.md: conflict marker`, `exit=1`.
  - The real checkout: `git status --porcelain` empty; `PINS-UNCHANGED`.
  If any expectation differs, stop and report the actual output; do not adjust the expectation.
- [ ] **Step 4: Close.** Delete this plan and the spec
  ([documentation.md](../../engineering/documentation.md#handovers-and-plans)); remove W1 from
  ROADMAP Next Up (renumber; W2 to W6 become item 1); record nothing else there. Commit
  `docs: close W1 sync infrastructure` with the dry-run outcome summarized in the message body.
  Update the Memorizer project state (W1 landed, the next wave is W2).
