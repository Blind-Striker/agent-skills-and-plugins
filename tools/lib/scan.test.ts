import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { scanSubmodule } from "./scan.ts";

function makeFixture(): string {
  const ext = mkdtempSync(join(tmpdir(), "scan-"));
  mkdirSync(join(ext, "sp", ".claude-plugin"), { recursive: true });
  writeFileSync(join(ext, "sp", ".claude-plugin", "plugin.json"), JSON.stringify({ name: "superpowers" }));
  mkdirSync(join(ext, "sp", "skills", "foo", "references"), { recursive: true });
  writeFileSync(join(ext, "sp", "skills", "foo", "SKILL.md"), "---\nname: foo\ndescription: Foo skill\n---\n\nBody");
  writeFileSync(join(ext, "sp", "skills", "foo", "references", "extra.md"), "ref");
  mkdirSync(join(ext, "sp", "commands"), { recursive: true });
  writeFileSync(join(ext, "sp", "commands", "bar.md"), "---\ndescription: Bar cmd\n---\n\nDo bar");
  mkdirSync(join(ext, "sp", "agents"), { recursive: true });
  writeFileSync(join(ext, "sp", "agents", "helper.md"), "---\nname: helper\ndescription: Helps\n---\n\nYou help.");
  return ext;
}

test("finds skills, commands, agents with namespace from plugin.json", () => {
  const comps = scanSubmodule(makeFixture(), "sp");
  const skill = comps.find((c) => c.type === "skill");
  assert.ok(skill);
  assert.equal(skill.name, "foo");
  assert.equal(skill.namespace, "superpowers");
  assert.equal(skill.sourcePath, "sp/skills/foo");
  assert.equal(skill.files, 2);
  assert.ok(skill.bytes > 0);
  const cmd = comps.find((c) => c.type === "command");
  assert.ok(cmd);
  assert.equal(cmd.name, "bar");
  assert.equal(cmd.sourcePath, "sp/commands/bar.md");
  const agent = comps.find((c) => c.type === "agent");
  assert.ok(agent);
  assert.equal(agent.name, "helper");
});

test("falls back to submodule dir name when no plugin.json", () => {
  const ext = mkdtempSync(join(tmpdir(), "scan-"));
  mkdirSync(join(ext, "raw", "skills", "x"), { recursive: true });
  writeFileSync(join(ext, "raw", "skills", "x", "SKILL.md"), "---\nname: x\ndescription: X\n---\n\nB");
  const comps = scanSubmodule(ext, "raw");
  assert.equal(comps[0].namespace, "raw");
});

test("tolerates trailing separator on submodule name", () => {
  const ext = mkdtempSync(join(tmpdir(), "scan-"));
  mkdirSync(join(ext, "raw", "skills", "x"), { recursive: true });
  writeFileSync(join(ext, "raw", "skills", "x", "SKILL.md"), "---\nname: x\ndescription: X\n---\n\nB");
  const comps = scanSubmodule(ext, "raw/");
  assert.equal(comps.length, 1);
  assert.equal(comps[0].namespace, "raw");
  assert.equal(comps[0].sourcePath, "raw/skills/x");
});

test("reads namespace from a bare plugin.json in a marketplace monorepo", () => {
  const ext = mkdtempSync(join(tmpdir(), "scan-"));
  const plugin = join(ext, "mono", "plugins", "toolx");
  mkdirSync(join(plugin, "skills", "s1"), { recursive: true });
  writeFileSync(join(plugin, "plugin.json"), JSON.stringify({ name: "toolx" }));
  writeFileSync(join(plugin, "skills", "s1", "SKILL.md"), "---\nname: s1\ndescription: S1\n---\n\nB");
  const comps = scanSubmodule(ext, "mono");
  assert.equal(comps.length, 1);
  assert.equal(comps[0].namespace, "toolx");
});

test("prefers .claude-plugin/plugin.json over a sibling bare plugin.json", () => {
  const ext = mkdtempSync(join(tmpdir(), "scan-"));
  const plugin = join(ext, "mono", "plugins", "toolx");
  mkdirSync(join(plugin, ".claude-plugin"), { recursive: true });
  mkdirSync(join(plugin, "skills", "s1"), { recursive: true });
  writeFileSync(join(plugin, ".claude-plugin", "plugin.json"), JSON.stringify({ name: "canonical" }));
  writeFileSync(join(plugin, "plugin.json"), JSON.stringify({ name: "bare" }));
  writeFileSync(join(plugin, "skills", "s1", "SKILL.md"), "---\nname: s1\ndescription: S1\n---\n\nB");
  const comps = scanSubmodule(ext, "mono");
  assert.equal(comps[0].namespace, "canonical");
});

test("falls back past a malformed plugin.json instead of throwing", () => {
  const ext = mkdtempSync(join(tmpdir(), "scan-"));
  mkdirSync(join(ext, "bad", ".claude-plugin"), { recursive: true });
  writeFileSync(join(ext, "bad", ".claude-plugin", "plugin.json"), "not json");
  mkdirSync(join(ext, "bad", "skills", "x"), { recursive: true });
  writeFileSync(join(ext, "bad", "skills", "x", "SKILL.md"), "---\nname: x\ndescription: X\n---\n\nB");
  const comps = scanSubmodule(ext, "bad");
  assert.equal(comps.length, 1);
  assert.equal(comps[0].namespace, "bad");
});

test("falls back past a plugin.json with no string name", () => {
  const ext = mkdtempSync(join(tmpdir(), "scan-"));
  const plugin = join(ext, "noname", "plugins", "p");
  mkdirSync(join(plugin, "skills", "x"), { recursive: true });
  writeFileSync(join(plugin, "plugin.json"), JSON.stringify({ version: "1.0.0" }));
  writeFileSync(join(plugin, "skills", "x", "SKILL.md"), "---\nname: x\ndescription: X\n---\n\nB");
  const comps = scanSubmodule(ext, "noname");
  assert.equal(comps[0].namespace, "noname");
});

test("skips symlinked mirrors so a skill is not counted twice", (t) => {
  const ext = mkdtempSync(join(tmpdir(), "scan-"));
  const real = join(ext, "mirror", "skills", "one");
  mkdirSync(real, { recursive: true });
  writeFileSync(join(real, "SKILL.md"), "---\nname: one\ndescription: One\n---\n\nB");
  // aspire-skills mirrors its canonical skills/ as per-file symlinks inside real directories,
  // so both a symlinked SKILL.md and a symlinked skill directory must be ignored.
  const fileMirror = join(ext, "mirror", ".github", "plugins", "mirror", "skills", "one");
  const dirMirror = join(ext, "mirror", ".github", "plugins", "mirror", "copies");
  mkdirSync(fileMirror, { recursive: true });
  mkdirSync(dirMirror, { recursive: true });
  try {
    symlinkSync(join(real, "SKILL.md"), join(fileMirror, "SKILL.md"), "file");
    symlinkSync(real, join(dirMirror, "one"), "dir");
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }
  const comps = scanSubmodule(ext, "mirror");
  assert.equal(comps.length, 1);
  assert.equal(comps[0].sourcePath, "mirror/skills/one");
});

test("still finds real files under .github", () => {
  const ext = mkdtempSync(join(tmpdir(), "scan-"));
  mkdirSync(join(ext, "gh", ".github", "skills", "pr-review"), { recursive: true });
  writeFileSync(
    join(ext, "gh", ".github", "skills", "pr-review", "SKILL.md"),
    "---\nname: pr-review\ndescription: Reviews\n---\n\nB",
  );
  const comps = scanSubmodule(ext, "gh");
  assert.equal(comps.length, 1);
  assert.equal(comps[0].name, "pr-review");
});
