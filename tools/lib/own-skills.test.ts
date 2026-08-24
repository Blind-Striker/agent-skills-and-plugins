import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import type { CurationManifest } from "./manifest.ts";
import { ownSkillIdentities } from "./own-skills.ts";

test("original skill identities come from Plugin names and top-level directories", () => {
  const root = mkdtempSync(join(tmpdir(), "own-skills-"));
  mkdirSync(join(root, "skills", "deniz-general", "writing-tests"), { recursive: true });
  mkdirSync(join(root, "skills", "deniz-process", "grilling"), { recursive: true });
  writeFileSync(join(root, "skills", "deniz-general", "NOTICE.md"), "not a skill\n");
  const manifests: CurationManifest[] = [
    { plugin: { name: "deniz-general", description: "d", version: "0.1.0" }, items: [] },
    { plugin: { name: "deniz-process", description: "d", version: "0.1.0" }, items: [] },
  ];

  assert.deepEqual(ownSkillIdentities(root, manifests), [
    { plugin: "deniz-general", name: "writing-tests", address: "deniz-general:writing-tests" },
    { plugin: "deniz-process", name: "grilling", address: "deniz-process:grilling" },
  ]);
});
