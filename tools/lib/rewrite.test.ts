import assert from "node:assert/strict";
import { test } from "node:test";
import type { CurationManifest } from "./manifest.ts";
import { buildRewriteMap, type RewriteTarget, rewriteRefs } from "./rewrite.ts";
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
const own = [{ plugin: "deniz-process", name: "my-own", address: "deniz-process:my-own" }];

test("map covers included items with renames, skips excluded", () => {
  const map = buildRewriteMap([manifest], components);
  assert.deepEqual(map.get("superpowers:brainstorming"), {
    plugin: "deniz-process",
    name: "brainstorming",
    kind: "skill",
  });
  assert.deepEqual(map.get("superpowers:tdd"), { plugin: "deniz-process", name: "deniz-tdd", kind: "skill" });
  assert.equal(map.has("superpowers:dropped"), false);
});

test("map records the resolved target kind", () => {
  const m: CurationManifest = {
    plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
    items: [{ source: "sp/skills/brainstorming", as: "command" }],
  };
  assert.equal(buildRewriteMap([m], components).get("superpowers:brainstorming")?.kind, "command");
});

test("one map renders all three styles; OpenCode stays bare in this checkpoint", () => {
  const map = buildRewriteMap([manifest], components, own);
  const text = "Use superpowers:brainstorming, then /deniz-process:my-own.";
  assert.equal(rewriteRefs(text, map, "claude"), "Use deniz-process:brainstorming, then /deniz-process:my-own.");
  assert.equal(rewriteRefs(text, map, "codex"), "Use $deniz-process:brainstorming, then $deniz-process:my-own.");
  assert.equal(rewriteRefs(text, map, "opencode"), "Use brainstorming, then /my-own.");
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
  assert.deepEqual(map.get("superpowers:dir-name"), { plugin: "deniz-process", name: "fancy-name", kind: "skill" });
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
  assert.deepEqual(map.get("superpowers:do-it"), { plugin: "deniz-process", name: "fancy-name", kind: "command" });
});

test("original skill targets use Plugin-qualified Claude and bare OpenCode spellings", () => {
  assert.deepEqual(buildRewriteMap([manifest], components, own).get("deniz-process:my-own"), {
    plugin: "deniz-process",
    name: "my-own",
    kind: "skill",
  });
});

test("an original skill rewrite key cannot overwrite another target", () => {
  const colliding = [comp({ namespace: "deniz-process", name: "upstream-name", sourcePath: "sp/skills/my-own" })];
  const collisionManifest: CurationManifest = {
    plugin: manifest.plugin,
    items: [{ source: "sp/skills/my-own", name: "other-target" }],
  };
  assert.throws(
    () => buildRewriteMap([collisionManifest], colliding, own),
    /reference identity deniz-process:my-own.*deniz-process:other-target.*deniz-process:my-own/,
  );
});

test("rewriteRefs replaces longest keys first", () => {
  const map = new Map<string, RewriteTarget>([
    ["sp:foo", { plugin: "p", name: "foo", kind: "skill" }],
    ["sp:foo-bar", { plugin: "p", name: "foo-bar", kind: "skill" }],
  ]);
  assert.equal(rewriteRefs("use sp:foo-bar then sp:foo", map), "use p:foo-bar then p:foo");
});

// `sp:foo-bar` is a different component that happens to start with a curated one's name. Rewriting
// its prefix produced `p:foo-bar`, a reference to a skill that was never curated.
test("rewriteRefs stops at ref-token boundaries", () => {
  const map = new Map<string, RewriteTarget>([["sp:foo", { plugin: "p", name: "foo", kind: "skill" }]]);
  assert.equal(rewriteRefs("sp:foo-bar and sp:foobar and sp:foo", map), "sp:foo-bar and sp:foobar and p:foo");
  assert.equal(rewriteRefs("xsp:foo", map), "xsp:foo");
  assert.equal(rewriteRefs("(sp:foo).", map), "(p:foo).");
});

test("Codex renders model edges and user pointers as valid namespaced dollar references", () => {
  const map = new Map<string, RewriteTarget>([
    ["superpowers:tdd", { plugin: "deniz-process", name: "test-driven-development", kind: "skill" }],
    ["superpowers:brainstorming", { plugin: "deniz-process", name: "brainstorming", kind: "skill" }],
  ]);
  assert.equal(
    rewriteRefs("Use superpowers:tdd, or tell the user to open /superpowers:brainstorming.", map, "codex"),
    "Use $deniz-process:test-driven-development, or tell the user to open $deniz-process:brainstorming.",
  );
});

test("Codex rewriting keeps token boundaries and literal dollar amounts", () => {
  const map = new Map<string, RewriteTarget>([["sp:foo", { plugin: "plugin", name: "renamed", kind: "skill" }]]);
  assert.equal(
    rewriteRefs("$25, sp:foo-bar, xsp:foo, and sp:foo.", map, "codex"),
    "$25, sp:foo-bar, xsp:foo, and $plugin:renamed.",
  );
});

test("Codex map preserves the owning plugin across cross-plugin targets and renames", () => {
  const other: CurationManifest = {
    plugin: { name: "deniz-other", description: "Other", version: "0.1.0" },
    items: [{ source: "sp/skills/tdd", name: "renamed-tdd" }],
  };
  const map = buildRewriteMap([other], components);
  assert.equal(rewriteRefs("Use superpowers:tdd.", map, "codex"), "Use $deniz-other:renamed-tdd.");
});

test("Codex leaves a dangling authored address visible for validation", () => {
  assert.equal(rewriteRefs("Use upstream:missing.", new Map(), "codex"), "Use upstream:missing.");
});
