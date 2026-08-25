import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";

const root = join(import.meta.dirname, "..");

test("README build command names every committed generated tree", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  assert.match(readme, /`npm run build`[^\n]*`plugins\/`[^\n]*`opencode\/`[^\n]*`dist\/`/);
});

test("README verifies the current Release digest before package execution", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  assert.doesNotMatch(readme, /installer-v0\.1\.0|deniz-agent-skills-0\.1\.0\.tgz/);
  const section = readme.slice(readme.indexOf("### OpenCode from a Release Package"));
  const download = "gh release download installer-v0.2.0 --repo Blind-Striker/agent-skills-and-plugins";
  const asset = '"deniz-agent-skills-0.2.0.tgz"';
  const digest = "4ce23817052317b80926a6cd0aed7063364e9625c012f22080bfb887727286be";
  const compute = "Get-FileHash -LiteralPath $package -Algorithm SHA256";
  const compare = "if ($actual -ne $expected) { throw";
  const execute = "npm exec --yes --package $package -- deniz-skills install --all";
  const positions = [download, asset, digest, compute, compare, execute].map((value) => section.indexOf(value));

  assert.ok(
    positions.every((position) => position >= 0),
    "Release recipe lost a required identity or safety step",
  );
  assert.deepEqual(
    positions,
    [...positions].sort((left, right) => left - right),
  );
  assert.equal(section.indexOf("npm exec"), positions.at(-1), "no package execution may precede digest verification");
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
