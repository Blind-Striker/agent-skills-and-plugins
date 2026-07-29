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

// Claude Code addresses a plugin skill by its DIRECTORY name, not by the frontmatter name — and
// those diverge for 32 of the 223 upstream components, so keying the map on the frontmatter name
// rewrote references nobody writes and left the real ones (`dotnet-skills:akka-best-practices`)
// pointing at upstream.
test("map keys on the upstream address, not the frontmatter name", () => {
  const map = buildRewriteMap(
    [
      {
        plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
        items: [{ source: "sp/skills/dir-name" }],
      },
    ],
    [comp({ name: "fancy-name", sourcePath: "sp/skills/dir-name" })],
  );
  assert.equal(map.get("superpowers:dir-name"), "deniz-process:fancy-name");
  assert.equal(map.has("superpowers:fancy-name"), false);
});

test("commands and agents are addressed by file name without the extension", () => {
  const map = buildRewriteMap(
    [
      {
        plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
        items: [{ source: "sp/commands/do-it.md" }],
      },
    ],
    [comp({ type: "command", name: "fancy-name", sourcePath: "sp/commands/do-it.md" })],
  );
  assert.equal(map.get("superpowers:do-it"), "deniz-process:fancy-name");
});

test("rewriteRefs replaces longest keys first", () => {
  const map = new Map([
    ["sp:foo", "p:foo"],
    ["sp:foo-bar", "p:foo-bar"],
  ]);
  assert.equal(rewriteRefs("use sp:foo-bar then sp:foo", map), "use p:foo-bar then p:foo");
});

// `sp:foo-bar` is a different component that happens to start with a curated one's name. Rewriting
// its prefix produced `p:foo-bar`, a reference to a skill that was never curated.
test("rewriteRefs stops at ref-token boundaries", () => {
  const map = new Map([["sp:foo", "p:foo"]]);
  assert.equal(rewriteRefs("sp:foo-bar and sp:foobar and sp:foo", map), "sp:foo-bar and sp:foobar and p:foo");
  assert.equal(rewriteRefs("xsp:foo", map), "xsp:foo");
  assert.equal(rewriteRefs("(sp:foo).", map), "(p:foo).");
});
