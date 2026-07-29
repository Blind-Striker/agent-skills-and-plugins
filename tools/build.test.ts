import assert from "node:assert/strict";
import { existsSync, lstatSync, mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
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

  // skill copied with frontmatter override + reference rewrite. beta is curated twice (command +
  // agent) and buildRewriteMap is last-write-wins per source, so alpha's ref maps to the agent name.
  const alpha = parseDoc(readFileSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md"), "utf8"));
  assert.equal(alpha.frontmatter.description, "Alpha curated");
  // forced name wins over item.frontmatter's name: sneaky — dir name and rewrite map both use outName
  assert.equal(alpha.frontmatter.name, "alpha");
  assert.match(alpha.body, /deniz-process:beta-agent/);
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
  assert.match(alpha, /deniz-process:beta-agent/);

  // skill -> agent conversion, plugins side: forced name, item.frontmatter carried, description from source
  const agentPath = join(root, "plugins", "deniz-process", "agents", "beta-agent.md");
  assert.ok(existsSync(agentPath));
  const agent = parseDoc(readFileSync(agentPath, "utf8"));
  assert.equal(agent.frontmatter.name, "beta-agent");
  assert.equal(agent.frontmatter.model, "opus");
  assert.equal(agent.frontmatter.description, "Beta upstream");

  // opencode side keeps description + mode only; model is dropped and reported, never silently lost
  const ocAgent = parseDoc(readFileSync(join(root, "opencode", "agent", "beta-agent.md"), "utf8"));
  assert.equal(ocAgent.frontmatter.mode, "subagent");
  assert.equal(ocAgent.frontmatter.description, "Beta upstream");
  assert.equal("model" in ocAgent.frontmatter, false);
  assert.ok(report.includes("opencode agent beta-agent.md: dropped frontmatter keys: model"));
});

// aspire-skills links evals/fixtures out of its skill dir; cpSync would copy that link with its
// target resolved to an absolute local path — a dangling, machine-specific artifact once committed.
test("symlinks inside a curated skill are skipped, not copied", (t) => {
  const root = makeRepo();
  mkdirSync(join(root, "external", "sp", "shared"), { recursive: true });
  writeFileSync(join(root, "external", "sp", "shared", "fixture.txt"), "outside the skill dir\n");
  try {
    symlinkSync(join("..", "..", "shared"), join(root, "external", "sp", "skills", "alpha", "fixtures"), "dir");
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }
  const report = buildAll(root);
  const copied = join(root, "plugins", "deniz-process", "skills", "alpha", "fixtures");
  // nothing at all at that path: not a real dir, not a dangling link
  assert.throws(() => lstatSync(copied), /ENOENT/);
  // the rest of the skill still copied — the filter must reject only the link
  assert.ok(existsSync(join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md")));
  assert.ok(
    report.includes("WARN deniz-process/alpha: skipped symlink external/sp/skills/alpha/fixtures"),
    `expected a skipped-symlink warning, got ${JSON.stringify(report, null, 2)}`,
  );
});

test("missing overlay throws a helpful error", () => {
  const root = makeRepo();
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    "plugin:\n  name: deniz-process\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/alpha\n    body: overlay\n",
  );
  assert.throws(() => buildAll(root), /overlay/);
});

// The build wipes plugins/ and opencode/ before re-emitting them, so any failure it discovers
// mid-emit destroys committed output and leaves a stale marketplace.json behind. Every
// per-item failure has to be found by the pre-pass, i.e. before the first rmSync.
test("unresolvable items abort the build before any output is deleted, and report all of them", () => {
  const root = makeRepo();
  buildAll(root);
  const alpha = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  assert.ok(existsSync(alpha));
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    [
      "plugin:",
      "  name: deniz-process",
      "  description: d",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/typo",
      "  - source: sp/skills/alpha",
      "    body: overlay",
      "",
    ].join("\n"),
  );
  assert.throws(() => buildAll(root), /sp\/skills\/typo/);
  // both problems in one message: a build must not have to be re-run once per typo
  assert.throws(() => buildAll(root), /overlays\/deniz-process\/alpha\/ is missing/);
  assert.ok(existsSync(alpha), "previous build output must survive an aborted build");
});

test("an overlay directory missing the file the build reads aborts before deleting output", () => {
  const root = makeRepo();
  buildAll(root);
  const alpha = join(root, "plugins", "deniz-process", "skills", "alpha", "SKILL.md");
  // deniz-beta converts sp/skills/beta to a command, so the build reads overlays/.../SKILL.md
  rmSync(join(root, "overlays", "deniz-process", "deniz-beta", "SKILL.md"));
  assert.throws(() => buildAll(root), /deniz-beta\/SKILL\.md is missing/);
  assert.ok(existsSync(alpha), "previous build output must survive an aborted build");
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
