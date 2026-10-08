import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import type { AssembledItem } from "./assemble.ts";
import { PORTABLE_NAME } from "./resolve.ts";
import {
  adaptOpenCodeAgentDocument,
  adaptOpenCodeCommandDocument,
  adaptOpenCodeSkillDocument,
  claudeCounterpartPath,
  collectOpenCodeEmissionProblems,
  OPENCODE_AGENT_COLOR,
  OPENCODE_AGENT_KEYS,
  OPENCODE_HIDE_KEY,
  OPENCODE_SKILL_KEYS,
  openCodeBundlePath,
  openCodeId,
  openCodeWouldHide,
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
  assert.deepEqual([...OPENCODE_SKILL_KEYS].sort(), ["description", "metadata", "name"]);
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

const doc = (frontmatter: Record<string, unknown>) => ({ frontmatter, body: "Body.\n" });
const ID = "deniz-process.x";

test("skill adaptation: name is the ID and unknown keys are reported", () => {
  const a = adaptOpenCodeSkillDocument(
    ID,
    doc({ name: "x", description: "D", "allowed-tools": "Read", license: "MIT" }),
    "auto",
  );
  assert.deepEqual(a.document.frontmatter, { name: ID, description: "D" });
  assert.deepEqual(a.dropped, ["allowed-tools", "license"]);
  assert.equal(a.advertised, true);
  assert.equal(a.document.body, "Body.\n");
});

test("manual adds the hide key, merging into existing metadata", () => {
  const none = adaptOpenCodeSkillDocument(ID, doc({ name: "x", description: "D" }), "manual");
  assert.deepEqual(none.document.frontmatter.metadata, { "opencode/autoinvoke": false });
  assert.equal(none.advertised, false);
  const merged = adaptOpenCodeSkillDocument(
    ID,
    doc({ name: "x", description: "D", metadata: { a: "1", "opencode/autoinvoke": true } }),
    "manual",
  );
  assert.deepEqual(merged.document.frontmatter.metadata, { a: "1", "opencode/autoinvoke": false });
  assert.deepEqual(Object.keys(merged.document.frontmatter.metadata as object), ["a", "opencode/autoinvoke"]);
});

test("auto and both remove an upstream hide key; an emptied mapping disappears", () => {
  for (const inv of ["auto", "both"] as const) {
    const a = adaptOpenCodeSkillDocument(
      ID,
      doc({ name: "x", description: "D", metadata: { "opencode/autoinvoke": false } }),
      inv,
    );
    assert.equal("metadata" in a.document.frontmatter, false, inv);
    assert.equal(a.advertised, true);
  }
  const kept = adaptOpenCodeSkillDocument(
    ID,
    doc({ name: "x", description: "D", metadata: { a: "1", "opencode/autoinvoke": false } }),
    "both",
  );
  assert.deepEqual(kept.document.frontmatter.metadata, { a: "1" });
});

test("absent passes upstream posture through and renders an upstream DMI", () => {
  const plain = adaptOpenCodeSkillDocument(
    ID,
    doc({ name: "x", description: "D", metadata: { "opencode/autoinvoke": false } }),
    undefined,
  );
  assert.deepEqual(plain.document.frontmatter.metadata, { "opencode/autoinvoke": false }, "upstream key kept");
  assert.equal(plain.advertised, false);
  const dmi = adaptOpenCodeSkillDocument(
    ID,
    doc({ name: "x", description: "D", "disable-model-invocation": true, "user-invocable": false }),
    undefined,
  );
  assert.deepEqual(dmi.document.frontmatter.metadata, { "opencode/autoinvoke": false });
  assert.deepEqual(dmi.dropped, ["user-invocable"], "a consumed DMI is not a drop");
  assert.deepEqual(dmi.transformations, ["disable-model-invocation: true -> metadata.opencode/autoinvoke: false"]);
});

test("a stated invocation drops upstream Claude invocation keys", () => {
  const a = adaptOpenCodeSkillDocument(
    ID,
    doc({ name: "x", description: "D", "disable-model-invocation": true }),
    "auto",
  );
  assert.deepEqual(a.dropped, ["disable-model-invocation"]);
  assert.equal(a.advertised, true);
});

test("command keeps description; agent keeps description and mode, never name", () => {
  const src = doc({ name: "x", description: "D", model: "opus", color: "info", tools: "Read" });
  assert.deepEqual(adaptOpenCodeCommandDocument(src), {
    document: { frontmatter: { description: "D" }, body: "Body.\n" },
    dropped: ["color", "model", "tools"],
  });
  const agent = adaptOpenCodeAgentDocument(src);
  assert.deepEqual(agent.document.frontmatter, { description: "D", mode: "subagent" });
  assert.deepEqual(agent.dropped, ["color", "model", "tools"]);
});

test("a hidden item whose metadata is not a mapping is a preflight problem", () => {
  assert.equal(openCodeWouldHide(doc({ metadata: "x" }), "manual"), true);
  assert.equal(openCodeWouldHide(doc({ "disable-model-invocation": true }), undefined), true);
  assert.equal(openCodeWouldHide(doc({}), "both"), false);
});

test("collectOpenCodeEmissionProblems reports a non-mapping metadata on a hidden skill only", () => {
  const dir = mkdtempSync(join(tmpdir(), "oc-target-"));
  writeFileSync(join(dir, "SKILL.md"), "---\nname: x\ndescription: D\nmetadata: plain\n---\n\nBody.\n");
  const item = (invocation?: "manual" | "auto"): AssembledItem => ({
    plugin: "deniz-process",
    source: "sp/skills/x",
    sourceType: "skill",
    outName: "x",
    outType: "skill",
    dir,
    own: false,
    item: { source: "sp/skills/x", ...(invocation ? { invocation } : {}) },
  });
  const manifests = [{ plugin: { name: "deniz-process", description: "d", version: "0.1.0" }, items: [] }];
  assert.deepEqual(collectOpenCodeEmissionProblems(manifests, [item("manual")]), [
    "deniz-process/x: metadata must be a mapping to carry opencode/autoinvoke",
  ]);
  assert.deepEqual(collectOpenCodeEmissionProblems(manifests, [item("auto")]), []);
});
