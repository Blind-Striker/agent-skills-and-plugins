import assert from "node:assert/strict";
import { writeFileSync } from "node:fs";
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
