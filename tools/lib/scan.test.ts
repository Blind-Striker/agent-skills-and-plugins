import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
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
