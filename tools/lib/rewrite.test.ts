import assert from "node:assert/strict";
import { test } from "node:test";
import type { CurationManifest } from "./manifest.ts";
import { buildRewriteMap, rewriteRefs } from "./rewrite.ts";
import type { ComponentInfo } from "./scan.ts";

const comp = (over: Partial<ComponentInfo>): ComponentInfo => ({
  submodule: "sp",
  namespace: "superpowers",
  type: "skill",
  name: "x",
  description: "",
  sourcePath: "sp/skills/x",
  files: 1,
  bytes: 1,
  ...over,
});
const manifest: CurationManifest = {
  plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
  items: [
    { source: "sp/skills/brainstorming" },
    { source: "sp/skills/tdd", name: "deniz-tdd" },
    { source: "sp/skills/dropped", exclude: true },
  ],
};
const components = [
  comp({ name: "brainstorming", sourcePath: "sp/skills/brainstorming" }),
  comp({ name: "tdd", sourcePath: "sp/skills/tdd" }),
  comp({ name: "dropped", sourcePath: "sp/skills/dropped" }),
];

test("map covers included items with renames, skips excluded", () => {
  const map = buildRewriteMap([manifest], components);
  assert.equal(map.get("superpowers:brainstorming"), "deniz-process:brainstorming");
  assert.equal(map.get("superpowers:tdd"), "deniz-process:deniz-tdd");
  assert.equal(map.has("superpowers:dropped"), false);
});

test("rewriteRefs replaces longest keys first", () => {
  const map = new Map([
    ["sp:foo", "p:foo"],
    ["sp:foo-bar", "p:foo-bar"],
  ]);
  assert.equal(rewriteRefs("use sp:foo-bar then sp:foo", map), "use p:foo-bar then p:foo");
});
