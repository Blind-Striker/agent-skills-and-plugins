import assert from "node:assert/strict";
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { makeRepo } from "../testutil.ts";
import { assembleItems } from "./assemble.ts";
import { parseDoc } from "./frontmatter.ts";
import { loadManifest } from "./manifest.ts";
import { listFiles } from "./overlay.ts";
import { scanSubmodule } from "./scan.ts";

function loadFixture(root: string) {
  return {
    manifests: [loadManifest(join(root, "curation", "deniz-process.yaml"))],
    components: scanSubmodule(join(root, "external"), "sp"),
  };
}

test("common assembly resolves each item once and retains a converted skill's dependency closure", (t) => {
  const root = makeRepo();
  const stagingRoot = mkdtempSync(join(tmpdir(), "assembly-test-"));
  t.after(() => {
    rmSync(root, { recursive: true, force: true });
    rmSync(stagingRoot, { recursive: true, force: true });
  });

  const manifestPath = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    manifestPath,
    readFileSync(manifestPath, "utf8").replace(
      "  - source: sp/skills/alpha\n    frontmatter:",
      "  - source: sp/skills/alpha\n    invocation: manual\n    frontmatter:",
    ),
  );
  const { manifests, components } = loadFixture(root);
  const report: string[] = [];
  const assembled = assembleItems(root, stagingRoot, manifests, components, report);

  assert.equal(assembled.length, 6, "five curated items and one original skill are assembled");
  assert.equal(new Set(assembled.map((item) => item.dir)).size, assembled.length);
  assert.ok(!existsSync(join(root, "plugins")), "assembly does not create a target tree");
  assert.ok(!existsSync(join(root, "opencode")), "assembly does not create a target tree");

  const alpha = assembled.find((item) => item.outName === "alpha");
  assert.ok(alpha);
  const alphaDoc = parseDoc(readFileSync(join(alpha.dir, "SKILL.md"), "utf8"));
  assert.equal(alphaDoc.frontmatter.name, "alpha", "resolved identity wins over frontmatter override");
  assert.equal(alphaDoc.frontmatter.description, "Alpha curated");
  assert.equal("user-invocable" in alphaDoc.frontmatter, false);
  assert.equal("disable-model-invocation" in alphaDoc.frontmatter, false, "invocation remains emitter policy");

  const command = assembled.find((item) => item.outName === "deniz-beta");
  assert.ok(command);
  assert.equal(command.outType, "command");
  assert.deepEqual(listFiles(command.dir), ["SKILL.md", "references/notes.md"]);
  const commandDoc = parseDoc(readFileSync(join(command.dir, "SKILL.md"), "utf8"));
  assert.equal(commandDoc.frontmatter.description, "Beta overlay");
  assert.equal(commandDoc.body, "Overlay body.\n");

  const agent = assembled.find((item) => item.outName === "beta-agent");
  assert.ok(agent);
  assert.deepEqual(listFiles(agent.dir), ["SKILL.md", "references/notes.md"]);
  assert.notEqual(agent.dir, command.dir, "repeated source use gets independent assembly state");

  const own = assembled.find((item) => item.own);
  assert.ok(own);
  assert.equal(
    readFileSync(join(own.dir, "SKILL.md"), "utf8"),
    readFileSync(join(root, "skills", "deniz-process", "my-own", "SKILL.md"), "utf8"),
  );
  assert.ok(report.includes("WARN deniz-process/deniz-beta: dropped in skill->command conversion: references"));
});

test("an assembly failure does not touch existing generated target trees", (t) => {
  const root = makeRepo();
  const stagingRoot = mkdtempSync(join(tmpdir(), "assembly-test-"));
  t.after(() => {
    rmSync(root, { recursive: true, force: true });
    rmSync(stagingRoot, { recursive: true, force: true });
  });

  mkdirSync(join(root, "plugins", "keep"), { recursive: true });
  mkdirSync(join(root, "opencode", "keep"), { recursive: true });
  const pluginSentinel = join(root, "plugins", "keep", "sentinel.txt");
  const opencodeSentinel = join(root, "opencode", "keep", "sentinel.txt");
  writeFileSync(pluginSentinel, "plugin\n");
  writeFileSync(opencodeSentinel, "opencode\n");
  rmSync(join(root, "overlays", "deniz-process", "deniz-beta"), { recursive: true, force: true });

  const { manifests, components } = loadFixture(root);
  assert.throws(() => assembleItems(root, stagingRoot, manifests, components, []), /body is overlay.*missing/s);
  assert.equal(readFileSync(pluginSentinel, "utf8"), "plugin\n");
  assert.equal(readFileSync(opencodeSentinel, "utf8"), "opencode\n");
});
