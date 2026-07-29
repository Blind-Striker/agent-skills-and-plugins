import assert from "node:assert/strict";
import { rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "./build.ts";
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
