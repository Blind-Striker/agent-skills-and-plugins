import assert from "node:assert/strict";
import { test } from "node:test";
import type { CurationManifest } from "./manifest.ts";
import {
  buildRewriteMap,
  claudeOnlyVocabulary,
  isBundledText,
  localize,
  type RewriteTarget,
  renderHarnessPhrasing,
  rewriteOpenCodePaths,
  rewriteRefs,
} from "./rewrite.ts";
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

test("one map renders all three styles", () => {
  const map = buildRewriteMap([manifest], components, own);
  const text = "Use superpowers:brainstorming, then /deniz-process:my-own.";
  assert.equal(rewriteRefs(text, map, "claude"), "Use deniz-process:brainstorming, then /deniz-process:my-own.");
  assert.equal(rewriteRefs(text, map, "codex"), "Use $deniz-process:brainstorming, then $deniz-process:my-own.");
  assert.equal(rewriteRefs(text, map, "opencode"), "Use deniz-process.brainstorming, then @deniz-process.my-own.");
});

test("OpenCode renders dotted IDs and picks the pointer prefix by target kind", () => {
  const m: CurationManifest = {
    plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
    items: [{ source: "sp/skills/brainstorming" }, { source: "sp/skills/tdd", as: "command", name: "handoff" }],
  };
  const map = buildRewriteMap([m], components, own);
  assert.equal(
    rewriteRefs(
      "Use superpowers:brainstorming. Open /superpowers:brainstorming or /superpowers:tdd or /deniz-process:my-own.",
      map,
      "opencode",
    ),
    "Use deniz-process.brainstorming. Open @deniz-process.brainstorming or /deniz-process.handoff or @deniz-process.my-own.",
  );
});

test("sibling climbs are respelled only when they land on the shared skills directory", () => {
  const ids = new Map([
    ["aspireify", "deniz-dotnet-aspire.aspireify"],
    ["requesting-code-review", "deniz-process.requesting-code-review"],
  ]);
  assert.equal(
    rewriteOpenCodePaths("[a](../aspireify/SKILL.md)", 0, ids),
    "[a](../deniz-dotnet-aspire.aspireify/SKILL.md)",
  );
  assert.equal(
    rewriteOpenCodePaths("[a](../../aspireify/SKILL.md)", 1, ids),
    "[a](../../deniz-dotnet-aspire.aspireify/SKILL.md)",
  );
  assert.equal(
    rewriteOpenCodePaths("[a](../../aspireify/SKILL.md)", 0, ids),
    "[a](../../aspireify/SKILL.md)",
    "wrong depth",
  );
  assert.equal(
    rewriteOpenCodePaths('"Dispatch (../requesting-code-review/code-reviewer.md)"', 0, ids),
    '"Dispatch (../deniz-process.requesting-code-review/code-reviewer.md)"',
    "prose climb",
  );
  assert.equal(
    rewriteOpenCodePaths('AddCSharpApp("api", "../src/Api")', 1, ids),
    'AddCSharpApp("api", "../src/Api")',
    "not an emitted skill",
  );
  assert.equal(rewriteOpenCodePaths("x/../aspireify/y", 0, ids), "x/../aspireify/y", "inside a longer path");
});

test("OpenCode path respelling covers item-root paths and climbs in any text", () => {
  const ids = new Map([
    ["brainstorming", "deniz-process.brainstorming"],
    ["beta", "deniz-process.beta"],
  ]);
  assert.equal(
    rewriteOpenCodePaths("`skills/brainstorming/visual-companion.md`", 0, ids),
    "`skills/deniz-process.brainstorming/visual-companion.md`",
  );
  assert.equal(rewriteOpenCodePaths("cat ../../beta/notes.md", 1, ids), "cat ../../deniz-process.beta/notes.md");
  assert.equal(
    rewriteOpenCodePaths('cat "$(dirname "$0")/../../beta/notes.md"', 1, ids),
    'cat "$(dirname "$0")/../../beta/notes.md"',
    "inside a longer path",
  );
  assert.equal(rewriteOpenCodePaths("skills/testing/x.md", 0, ids), "skills/testing/x.md", "not an emitted skill");
  const respelled = "`skills/deniz-process.beta/x.md` and ../deniz-process.beta/y.md";
  assert.equal(rewriteOpenCodePaths(respelled, 0, ids), respelled, "an ID segment is already respelled");
});

test("bundled text has no NUL byte and survives a UTF-8 round trip", () => {
  assert.equal(isBundledText(Buffer.from("#!/bin/sh\necho ok\n")), true);
  assert.equal(isBundledText(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x00])), false);
  assert.equal(isBundledText(Buffer.from([0x63, 0xff, 0x0a])), false);
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

test("original skill targets resolve to their Plugin and output name", () => {
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

const PHRASES = [
  'Call the Skill tool with "sp:a".',
  'Always call the Skill tool twice, for "sp:a" and "sp:b".',
  'a subagent that calls the Skill tool with "sp:a". By calling the Skill tool with "sp:b".',
  "naming which skills the next agent should call the Skill tool for.",
  "Subagent (general-purpose):",
  "dispatch a `general-purpose`\nsubagent. Then send one message with two `Agent` calls, one `Agent` call",
].join("\n");

test("harness phrasing: Claude Code is identity", () => {
  assert.equal(renderHarnessPhrasing(PHRASES, "claude"), PHRASES);
});

test("harness phrasing: OpenCode names its skill and subagent tools", () => {
  assert.equal(
    renderHarnessPhrasing(PHRASES, "opencode"),
    [
      'Call the `skill` tool with "sp:a".',
      'Always call the `skill` tool twice, for "sp:a" and "sp:b".',
      'a subagent that calls the `skill` tool with "sp:a". By calling the `skill` tool with "sp:b".',
      "naming which skills the next agent should call the `skill` tool for.",
      "Subagent (general):",
      "dispatch a `general`\nsubagent. Then send one message with two `subagent` calls, one `subagent` call",
    ].join("\n"),
  );
});

test("harness phrasing: Codex invokes and names no subagent tool or type", () => {
  assert.equal(
    renderHarnessPhrasing(PHRASES, "codex"),
    [
      "Invoke `sp:a`.",
      "Always invoke `sp:a` and `sp:b`.",
      "a subagent that invokes `sp:a`. By invoking `sp:b`.",
      "naming which skills the next agent should invoke.",
      "Subagent:",
      "dispatch a subagent. Then send one message with two subagent calls, one subagent call",
    ].join("\n"),
  );
});

test("localize renders the facts after the phrasing", () => {
  const map = buildRewriteMap([manifest], components);
  const line = 'Call the Skill tool with "superpowers:brainstorming".';
  assert.equal(localize(line, map, "claude"), 'Call the Skill tool with "deniz-process:brainstorming".');
  assert.equal(localize(line, map, "opencode"), 'Call the `skill` tool with "deniz-process.brainstorming".');
  assert.equal(localize(line, map, "codex"), "Invoke `$deniz-process:brainstorming`.");
});

test("every non-Claude rendering is free of Claude-only vocabulary", () => {
  assert.deepEqual(
    claudeOnlyVocabulary(PHRASES)
      .map((hit) => hit.match)
      .sort(),
    [
      "Skill tool",
      "Skill tool",
      "Skill tool",
      "Skill tool",
      "Skill tool",
      "Subagent (general-purpose)",
      "`Agent`",
      "`Agent`",
      "`general-purpose`",
    ].sort(),
  );
  for (const style of ["opencode", "codex"] as const) {
    assert.deepEqual(claudeOnlyVocabulary(renderHarnessPhrasing(PHRASES, style)), [], style);
  }
});
