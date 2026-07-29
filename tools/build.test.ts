import assert from "node:assert/strict";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "./build.ts";
import { parseDoc } from "./lib/frontmatter.ts";
import { makeRepo } from "./testutil.ts";

test("buildAll compiles plugins with overrides, overlays, conversions, rewrites", () => {
  const root = makeRepo();
  const report = buildAll(root);
  assert.ok(report.length > 0);
  assert.ok(report.includes("deniz-process: skill alpha <- sp/skills/alpha"));
  assert.ok(report.includes("WARN deniz-process/deniz-beta: dropped in skill->command conversion: references"));

  // skill copied with frontmatter override + reference rewrite (beta excluded from skills, so alpha's ref maps to the command name)
  const alpha = parseDoc(readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8"));
  assert.equal(alpha.frontmatter.description, "Alpha curated");
  // forced name wins over item.frontmatter's name: sneaky — dir name and rewrite map both use outName
  assert.equal(alpha.frontmatter.name, "alpha");
  assert.match(alpha.body, /deniz-process:deniz-beta/);
  assert.doesNotMatch(alpha.body, /superpowers:beta/);

  // skill -> command conversion with overlay body
  const cmd = parseDoc(readFileSync(join(root, "plugins", "deniz-process", "commands", "deniz-beta.md"), "utf8"));
  assert.equal(cmd.frontmatter.description, "Beta overlay");
  assert.match(cmd.body, /Overlay body/);

  // own skill copied
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "skills", "my-own", "SKILL.md")));

  // plugin.json + marketplace.json
  const pj = JSON.parse(readFileSync(join(root, "plugins", "deniz-process", ".claude-plugin", "plugin.json"), "utf8"));
  assert.equal(pj.name, "deniz-process");
  const mp = JSON.parse(readFileSync(join(root, ".claude-plugin", "marketplace.json"), "utf8"));
  assert.equal(mp.plugins[0].source, "./plugins/deniz-process");
});

test("buildAll emits opencode tree and reports dropped keys", () => {
  const root = makeRepo();
  const report = buildAll(root);
  assert.ok(existsSync(join(root, "opencode", "skill", "alpha", "SKILL.md")));
  assert.ok(existsSync(join(root, "opencode", "skill", "my-own", "SKILL.md")));
  const cmd = parseDoc(readFileSync(join(root, "opencode", "command", "deniz-beta.md"), "utf8"));
  assert.equal(cmd.frontmatter.description, "Beta overlay");
  // references were rewritten before opencode emission
  const alpha = readFileSync(join(root, "opencode", "skill", "alpha", "SKILL.md"), "utf8");
  assert.match(alpha, /deniz-process:deniz-beta/);
  assert.ok(Array.isArray(report));
});

test("missing overlay throws a helpful error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n    body: overlay\n",
  );
  assert.throws(() => buildAll(root), /overlay/);
});

test("non-empty hooks.include throws not-implemented and preserves existing output", () => {
  const root = makeRepo();
  buildAll(root);
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md")));
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: d\n  version: 0.1.0\nitems: []\nhooks:\n  include: [x]\n",
  );
  assert.throws(() => buildAll(root), /not implemented/);
  // guard fires before rmSync — previous build output must survive
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md")));
});
