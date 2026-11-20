import assert from "node:assert/strict";
import { test } from "node:test";
import { makeRepo } from "../testutil.ts";
import {
  adaptAntigravityDocument,
  collectAntigravityPathProblems,
  createAntigravityPluginManifest,
  splitAntigravitySkillBody,
} from "./antigravity-plugin.ts";
import { loadCodexPublisherMetadata } from "./codex-plugin.ts";

test("Antigravity emits only supported manifest fields with a neutral display name and a clean repository URL", () => {
  const publisher = loadCodexPublisherMetadata(makeRepo());
  const plugin = {
    name: "deniz-dotnet-mcp-plugin-claude-gemini-cursor-antigravity",
    version: "1.2.3",
    description: "Work on .NET projects",
  };
  assert.deepEqual(createAntigravityPluginManifest({ plugin, items: [] }, publisher), {
    $schema: "https://antigravity.google/schemas/v1/plugin.json",
    name: plugin.name,
    displayName: "Deniz .NET",
    version: "1.2.3",
    description: "Work on .NET projects",
    author: { name: "Deniz İrgin", email: "deniz@example.test", url: "https://example.test/deniz" },
    homepage: "https://example.test/project",
    repository: "https://example.test/repository",
    license: "MIT",
    keywords: ["antigravity", "skills"],
  });
});

test("Antigravity skill invocation derives from intent or preserves a true upstream block", () => {
  const frontmatter = {
    name: "upstream",
    description: "Description",
    license: "MIT",
    compatibility: "Requires Node",
    icon: "tools",
    "user-invocable": false,
  };
  for (const [invocation, upstream, blocked] of [
    ["manual", false, true],
    ["auto", true, false],
    ["both", true, false],
    [undefined, true, true],
    [undefined, false, false],
    [undefined, "true", false],
  ] as const) {
    const result = adaptAntigravityDocument(
      "skill",
      "curated",
      {
        frontmatter: { ...frontmatter, "disable-model-invocation": upstream },
        body: "Body\n",
      },
      invocation,
    );
    assert.deepEqual(result.document, {
      frontmatter: {
        name: "curated",
        description: "Description",
        license: "MIT",
        compatibility: "Requires Node",
        icon: "tools",
        ...(blocked ? { "disable-model-invocation": true } : {}),
      },
      body: "Body\n",
    });
    assert.deepEqual(result.dropped, blocked ? ["user-invocable"] : ["disable-model-invocation", "user-invocable"]);
  }
});

test("Antigravity agents inherit their model and drop foreign upstream keys", () => {
  const result = adaptAntigravityDocument(
    "agent",
    "curated",
    {
      frontmatter: { name: "upstream", description: "Agent", model: "other", color: "blue", tools: ["read"] },
      body: "Body\n",
    },
    undefined,
  );
  assert.deepEqual(result.document.frontmatter, {
    name: "curated",
    description: "Agent",
    mainAgent: false,
    subagent: true,
    model: "inherit",
  });
  assert.deepEqual(result.dropped, ["color", "model", "tools"]);
});

test("Antigravity checks Windows reserved names and case aliases in bundled paths", () => {
  assert.deepEqual(
    collectAntigravityPathProblems("plugin", [
      "skills/valid/notes.md",
      "skills/Valid/other.md",
      "agents/CON.md",
      "skills/valid/AUX/file.md",
      "skills/valid/a:stream",
    ]),
    [
      "plugin/skills/Valid: case alias of skills/valid",
      "plugin/agents/CON.md: path is not portable on Windows",
      "plugin/skills/valid/AUX/file.md: path is not portable on Windows",
      "plugin/skills/valid/a:stream: path is not portable on Windows",
    ],
  );
});

test("Antigravity continuation links resolve across the split", () => {
  const document = `# Start\n\n[Details](#details)\n\n${"x".repeat(22_000)}\n\n## Details\n\n[Start](#start)\n\n${"y".repeat(4000)}`;
  const split = splitAntigravitySkillBody(document);
  assert.ok(split);
  assert.match(split.body, /\[Details\]\(SKILL-CONTINUATION\.md#details\)/);
  assert.match(split.continuation, /\[Start\]\(SKILL\.md#start\)/);
});
