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
  const [alpha, beta] = lines;
  assert.ok(alpha);
  assert.ok(beta);
  assert.match(alpha, /alpha/);
  assert.match(alpha, /auto-updated/);
  assert.match(beta, /beta/);
  assert.match(beta, /OVERLAY/);
});

test("no changes yields empty report", () => {
  assert.deepEqual(syncReport("sp", [], manifests), []);
});

test("excluded item reports no action", () => {
  const lines = syncReport("sp", ["skills/gone/SKILL.md"], manifests);
  assert.equal(lines.length, 1);
  const only = lines[0];
  assert.ok(only);
  assert.match(only, /excluded — no action/);
});
