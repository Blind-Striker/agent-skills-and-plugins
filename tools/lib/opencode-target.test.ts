import assert from "node:assert/strict";
import { test } from "node:test";
import { PORTABLE_NAME } from "./resolve.ts";
import {
  claudeCounterpartPath,
  OPENCODE_AGENT_COLOR,
  OPENCODE_AGENT_KEYS,
  OPENCODE_HIDE_KEY,
  OPENCODE_SKILL_KEYS,
  openCodeBundlePath,
  openCodeId,
  splitOpenCodeId,
} from "./opencode-target.ts";

test("OpenCode IDs join plugin and name with a dot", () => {
  assert.equal(openCodeId("deniz-process", "brainstorming"), "deniz-process.brainstorming");
  assert.deepEqual(splitOpenCodeId("deniz-process.handoff", ["deniz-process", "deniz-dotnet-general"]), {
    plugin: "deniz-process",
    name: "handoff",
  });
  assert.equal(splitOpenCodeId("other.handoff", ["deniz-process"]), undefined);
  assert.equal(splitOpenCodeId("deniz-process.", ["deniz-process"]), undefined);
});

test("Bundle paths place every kind under its ID", () => {
  assert.equal(openCodeBundlePath("skill", "deniz-process", "x"), "skills/deniz-process.x");
  assert.equal(openCodeBundlePath("command", "deniz-process", "x"), "commands/deniz-process.x.md");
  assert.equal(openCodeBundlePath("agent", "deniz-dotnet-akka", "y"), "agents/deniz-dotnet-akka.y.md");
});

test("a skill file maps to its Claude counterpart; generated documents have none", () => {
  assert.equal(
    claudeCounterpartPath("deniz-process", "skills/deniz-process.brainstorming/scripts/start-server.sh"),
    "plugins/deniz-process/skills/brainstorming/scripts/start-server.sh",
  );
  assert.equal(claudeCounterpartPath("deniz-process", "commands/deniz-process.x.md"), undefined);
  assert.equal(claudeCounterpartPath("deniz-process", "agents/deniz-process.x.md"), undefined);
  assert.equal(claudeCounterpartPath("deniz-process", "LICENSE"), undefined);
  assert.throws(() => claudeCounterpartPath("deniz-process", "skills/brainstorming/SKILL.md"), /not an OpenCode ID/);
});

test("key sets and patterns match canon", () => {
  assert.deepEqual([...OPENCODE_SKILL_KEYS].sort(), ["compatibility", "description", "license", "metadata", "name"]);
  assert.equal(OPENCODE_HIDE_KEY, "opencode/autoinvoke");
  assert.deepEqual([...OPENCODE_AGENT_KEYS].sort(), [
    "color",
    "description",
    "disabled",
    "hidden",
    "mode",
    "model",
    "permissions",
    "request",
    "steps",
    "system",
    "variant",
  ]);
  assert.ok(OPENCODE_AGENT_COLOR.test("#a1B2c3"));
  assert.ok(!OPENCODE_AGENT_COLOR.test("info"));
  assert.ok(PORTABLE_NAME.test("deniz-process"));
  for (const bad of ["a.b", "a_b", "a:b", "a--b", "-a", "A"]) {
    assert.ok(!PORTABLE_NAME.test(bad), bad);
  }
});
