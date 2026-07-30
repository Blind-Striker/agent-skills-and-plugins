import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "./build.ts";
import { loadLock, lockKey, saveLock } from "./lib/overlay.ts";
import { makeRepo } from "./testutil.ts";
import { validateRepo } from "./validate.ts";

test("clean build validates without errors", () => {
  const root = makeRepo();
  buildAll(root);
  const findings = validateRepo(root);
  assert.deepEqual(
    findings.filter((f) => f.level === "error"),
    [],
  );
});

test("unknown manifest source is an error", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "curation", "deniz-broken.yaml"),
    "plugin:\n  name: deniz-broken\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/nope\n",
  );
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "error" && f.message.includes("sp/skills/nope")));
});

test("leftover upstream reference is a warning", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: d\n---\n\nStill uses superpowers:some-skill here.\n",
  );
  const findings = validateRepo(root);
  assert.ok(findings.some((f) => f.level === "warn" && f.message.includes("superpowers:some-skill")));
});

// Two plugins can each curate the same upstream skill without either manifest looking wrong, but
// Claude Code addresses a skill by name alone, so only one of the two would ever be reachable.
test("duplicate output name across plugins is an error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-other.yaml"),
    "plugin:\n  name: deniz-other\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n",
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("duplicate skill name across plugins: alpha")),
    `expected a duplicate-name error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// marketplace.json is what a harness reads to find the plugins; a build that half-failed, or a
// hand-deleted plugin dir, leaves it advertising directories that are not there.
test("marketplace.json listing a plugin that is not built is an error", () => {
  const root = makeRepo();
  buildAll(root);
  rmSync(join(root, "plugins", "deniz-process"), { recursive: true });
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) => f.level === "error" && f.message.includes("marketplace lists deniz-process but plugins/deniz-process"),
    ),
    `expected a marketplace mismatch error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// A symlink can only reach plugins/ by hand or from a future build regression; either way the
// committed output stops being portable, so validate must fail rather than warn.
test("a symlink in built output is an error", (t) => {
  const root = makeRepo();
  buildAll(root);
  const link = join(root, "plugins", "deniz-process", "skills", "alpha", "fixtures");
  try {
    symlinkSync(join(root, "external", "sp", "skills", "beta"), link, "dir");
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) =>
        f.level === "error" &&
        f.message.includes("must not contain symlinks") &&
        f.message.includes("plugins/deniz-process/skills/alpha/fixtures"),
    ),
    `expected a symlink error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// The failure ADR-0001's guardrails exist to prevent, arriving through the one door they do not
// watch: the build only consults an overlay when the item says `body:`, so dropping that line
// ships pristine upstream with every hash check and patch check simply never running.
test("an overlay directory with no body: on its item is an error", () => {
  const root = makeRepo();
  buildAll(root);
  // delta is curated as a plain passthrough — no body: line anywhere in the manifest
  mkdirSync(join(root, "overlays", "deniz-process", "delta"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "delta", "SKILL.md"),
    "---\nname: delta\ndescription: Delta edited\n---\n\nEdited body.\n",
  );
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) => f.level === "error" && f.message.includes("deniz-process/delta") && f.message.includes("body:"),
    ),
    `expected an ignored-overlay error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// A renamed item, or a hand-made directory, leaves an overlay no item can ever reach.
test("an overlay directory matching no item is an error", () => {
  const root = makeRepo();
  buildAll(root);
  mkdirSync(join(root, "overlays", "deniz-process", "ghost"), { recursive: true });
  writeFileSync(join(root, "overlays", "deniz-process", "ghost", "SKILL.md"), "---\nname: ghost\n---\n\nBody.\n");
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("deniz-process/ghost")),
    `expected an unmatched-overlay error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// Bookkeeping rot rather than a bypass: nothing reads the entry, but it claims a guard exists.
test("a lock entry with no overlay directory is a warning", () => {
  const root = makeRepo();
  buildAll(root);
  const lock = loadLock(root);
  lock[lockKey("deniz-process", "vanished")] = { source: "sp/skills/delta", files: { "SKILL.md": "deadbeef" } };
  saveLock(root, lock);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "warn" && f.message.includes("deniz-process/vanished")),
    `expected a stale-lock warning, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// `eject --patch` cuts the patch and then deletes the working copy it was cut from. Anything left
// beside overlay.patch is dead weight the build never reads — and the next person to edit it will
// not be told their edit does nothing.
test("a patch overlay holding a stranded working copy is a warning", () => {
  const root = makeRepo();
  buildAll(root);
  writeFileSync(
    join(root, "overlays", "deniz-process", "gamma", "SKILL.md"),
    "---\nname: gamma\ndescription: stranded\n---\n\nNever read.\n",
  );
  const findings = validateRepo(root);
  assert.ok(
    findings.some(
      (f) => f.level === "warn" && f.message.includes("deniz-process/gamma") && f.message.includes("SKILL.md"),
    ),
    `expected a stranded-working-copy warning, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// A conversion reads exactly one file out of the source and drops the rest anyway, so an omit
// list there is describing work the conversion already did.
test("omit on a converted item is a warning", () => {
  const root = makeRepo();
  const manifest = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    manifest,
    readFileSync(manifest, "utf8").replace("    as: agent", '    as: agent\n    omit:\n      - "references/**"'),
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "warn" && f.message.includes("beta-agent") && f.message.includes("omit")),
    `expected an omit-on-conversion warning, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// A typo'd pattern removes nothing and says nothing — the file you meant to drop ships.
test("an omit pattern that matches nothing is a warning", () => {
  const root = makeRepo();
  const manifest = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    manifest,
    readFileSync(manifest, "utf8").replace(
      "  - source: sp/skills/delta",
      '  - source: sp/skills/delta\n    omit:\n      - "refrences/**"',
    ),
  );
  buildAll(root);
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "warn" && f.message.includes("refrences/**")),
    `expected a dead-omit-pattern warning, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// The exec bit cannot survive a Windows checkout, so a script curated there is committed 100644
// while a Linux rebuild produces 0755 — CI's freshness gate fails on the mode diff, and worse, the
// shipped script is not executable for anyone who installs the plugin. git's index is the only
// place the bit still exists on such a checkout, so both trees have to be real repositories here.
test("a built file whose upstream is executable must be recorded executable", () => {
  const root = makeRepo();
  const sp = join(root, "external", "sp");
  writeFileSync(join(sp, "skills", "delta", "run.sh"), "#!/bin/sh\necho hi\n");
  execFileSync("git", ["init", "-q", "."], { cwd: sp });
  execFileSync("git", ["add", "-A"], { cwd: sp });
  // marks the bit in the index only, so the fixture behaves identically on Windows and Linux
  execFileSync("git", ["update-index", "--chmod=+x", "skills/delta/run.sh"], { cwd: sp });
  execFileSync("git", ["init", "-q", "."], { cwd: root });
  buildAll(root);
  execFileSync("git", ["add", "-A", "--", "plugins", "opencode"], { cwd: root });
  const findings = validateRepo(root);
  assert.ok(
    findings.some((f) => f.level === "error" && f.message.includes("run.sh") && f.message.includes("executable")),
    `expected an exec-bit error, got ${JSON.stringify(findings, null, 2)}`,
  );
});

// The rule must stay quiet when nothing upstream is executable, and when there is no index to read.
test("no exec-bit finding on a clean fixture", () => {
  const root = makeRepo();
  buildAll(root);
  assert.ok(!validateRepo(root).some((f) => f.message.includes("executable")));
});

// The fixture curates sp/skills/beta twice (command + agent) and gives alpha a dead
// frontmatter name, so a clean build is warn-worthy without being error-worthy.
test("curation footguns are warnings on a clean build", () => {
  const root = makeRepo();
  buildAll(root);
  const warnings = validateRepo(root)
    .filter((f) => f.level === "warn")
    .map((f) => f.message);
  assert.ok(
    warnings.some(
      (msg) =>
        msg.includes("sp/skills/beta") &&
        msg.includes("deniz-process:deniz-beta") &&
        msg.includes("deniz-process:beta-agent"),
    ),
    `expected a duplicate-source warning, got ${JSON.stringify(warnings, null, 2)}`,
  );
  assert.ok(
    warnings.some((msg) => msg.includes("deniz-process/alpha") && msg.includes("sneaky")),
    `expected a dead frontmatter.name warning, got ${JSON.stringify(warnings, null, 2)}`,
  );
});
