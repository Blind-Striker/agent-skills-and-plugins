import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";

const root = join(import.meta.dirname, "..");

test("README build command names every committed generated tree", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  assert.match(readme, /`npm run build`[^\n]*`plugins\/`[^\n]*`opencode\/`[^\n]*`dist\/`/);
});

test("OpenCode lab describes installer composition rather than a mounted build tree", () => {
  const lab = readFileSync(join(root, "experiments", "harness-invocation", "lab.ps1"), "utf8");
  assert.match(lab, /installer composition/i);
  assert.doesNotMatch(lab, /built tree mounted as the global config/i);
});

test("package research lead question describes per-Module Bundles and installer", () => {
  const research = readFileSync(
    join(root, "docs", "research", "2026-08-07-opencode-plugin-package-artifacts.md"),
    "utf8",
  );
  const lead = research.split("## Direct answer", 1)[0] ?? "";
  assert.match(lead, /per-Module Bundles/i);
  assert.match(lead, /installer/i);
  assert.doesNotMatch(lead, /stages its generated `opencode\/` tree/i);
});
