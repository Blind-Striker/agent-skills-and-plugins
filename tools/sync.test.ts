import assert from "node:assert/strict";
import { test } from "node:test";
import type { CurationManifest } from "./lib/manifest.ts";
import { syncReport } from "./sync.ts";

const manifests: CurationManifest[] = [
  {
    plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
    items: [
      { source: "sp/skills/alpha" },
      { source: "sp/skills/beta", body: "overlay" },
      { source: "sp/skills/gone", exclude: true },
      { source: "other/skills/x" },
    ],
  },
];

test("reports only changed curated items with overlay flag", () => {
  const lines = syncReport(
    "sp",
    ["skills/alpha/SKILL.md", "skills/beta/SKILL.md", "skills/unrelated/SKILL.md"],
    manifests,
  );
  assert.equal(lines.length, 2);
  assert.match(lines[0], /alpha/);
  assert.match(lines[0], /auto-updated/);
  assert.match(lines[1], /beta/);
  assert.match(lines[1], /OVERLAY/);
});

test("no changes yields empty report", () => {
  assert.deepEqual(syncReport("sp", [], manifests), []);
});
