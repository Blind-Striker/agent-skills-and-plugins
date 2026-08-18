import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";

const root = join(import.meta.dirname, "..");

test("README build command names every committed generated tree", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  assert.match(readme, /`npm run build`[^\n]*`plugins\/`[^\n]*`opencode\/`[^\n]*`dist\/`/);
});

test("README Release recipe verifies the downloaded digest before npm exec", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  const digest = "69532caf101f5626ea652cdb7e1046783b3d64fe79613e4d59f21e95eccb9460";
  const compute = "Get-FileHash -LiteralPath $package -Algorithm SHA256";
  const compare = "if ($actual -ne $expected) { throw";
  const exec = "npm exec --yes --package $package -- deniz-skills install --all";
  const digestAt = readme.indexOf(digest);
  const computeAt = readme.indexOf(compute);
  const compareAt = readme.indexOf(compare);
  const execAt = readme.indexOf(exec);
  assert.ok(digestAt >= 0, "README lost the expected release digest");
  assert.ok(computeAt >= 0, "README lost the hash computation");
  assert.ok(compareAt >= 0, "README lost the digest comparison and failure");
  assert.ok(execAt >= 0, "README lost the packed npm exec");
  assert.ok(
    digestAt < computeAt && computeAt < compareAt && compareAt < execAt,
    "README must compute, compare, and fail before npm exec",
  );
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
